using System.Collections.Generic;
using FolioXR.Accessibility;
using FolioXR.Environments;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace FolioXR.Books
{
    /// <summary>
    /// Estantería 3D de la biblioteca (encargo 18, parte 1; Claude, 12 nov 2026).
    /// En la casa, las estanterías están vacías y solo tienen los libros de la persona (pedido del usuario,
    /// 15 nov 2026): cada libro queda en la fila y el lugar donde se lo deja, y eso se guarda. Un libro nuevo
    /// aparece en el primer hueco libre, empezando por las filas a la altura de los ojos. En passthrough,
    /// una estantería propia aparece frente a la persona desde el menú, con sus propios lugares.
    /// Se toma un libro pellizcando su lomo (manos) o con el grip cerca (mandos): sale del estante y queda en
    /// la mano. Soltarlo sobre cualquier fila lo deja ahí (los vecinos se corren si hace falta); soltarlo
    /// lejos del estante lo abre. El libro abierto deja su hueco, y soltarlo sobre una fila lo cierra y lo
    /// guarda en ese lugar (pedido del usuario, 13 nov 2026).
    /// La estantería muestra todos los libros: los filtros del menú (colección, formato) no esconden ninguno.
    /// </summary>
    public sealed class BookShelf : MonoBehaviour
    {
        public static BookShelf Instance { get; private set; }
        private const string VisibleKey = "folioxr.shelf.visible";

        /// <summary>Estantería propia en passthrough (en la casa siempre se usa la de la biblioteca).</summary>
        public static bool FreeStanding => PlayerPrefs.GetInt(VisibleKey, 0) != 0;

        private struct Slot
        {
            public BookEntry Entry;
            public Vector3 Center;      // Centro del lomo en el mundo (cara delantera).
            public Quaternion Rotation; // +Z local apunta hacia la sala.
            public Vector3 Size;        // Ancho (lomo), alto y fondo.
            public int FirstVertex;
            public TextMesh Title;
            public int Row;             // Fila donde quedó.
            public float Along;         // Distancia desde el inicio de la fila hasta el centro del lomo.
            public int Leather;         // Color de la tapa (columna del atlas).
            public Transform Cover;     // Portada sobre la tapa (si el libro ya tiene miniatura).
            public Material CoverMaterial;
        }

        private struct Line { public Vector3 Left, Right, Facing; public float Length; public Color Light; }
        private int _blocks = 1, _block;    // Bloques de luz del atlas (0 = sin teñir) y el que usa lo que se está armando.
        private readonly List<Line> _lines = new List<Line>();
        private readonly List<List<ShelfLayout.Item>> _rows = new List<List<ShelfLayout.Item>>();   // Libros de cada fila.
        private readonly List<ShelfLayout.Item> _trial = new List<ShelfLayout.Item>();
        private bool _houseShown;
        private int _held = -1, _heldRole;
        private Vector3 _heldOffset;
        private bool _bookWasGrabbed;
        private GameObject _marker;
        private Mesh _markerMesh, _coverQuad;

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();
        private Mesh _mesh;
        private Material _material, _textMaterial;
        private Texture2D _atlas;
        private Font _font;
        private GameObject _meshObject;
        private Transform _titles;
        private FolioEnvironmentController _environment;
        private OVRCameraRig _rig;
        private FolioBook _book;
        private object _rowsSource;
        private bool _dirty = true, _freePlaced;
        private Pose _freePose;
        private int _hover = -1, _taking = -1;
        private float _takeTime;
        private readonly bool[] _pinchWas = new bool[2], _gripWas = new bool[2];
        private readonly OVRHand[] _hands = new OVRHand[2];
        private readonly OVRSkeleton[] _skeletons = new OVRSkeleton[2];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("FolioXR Estantería");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<BookShelf>();
        }

        /// <summary>Muestra u oculta la estantería propia de passthrough frente a la persona.</summary>
        public static void SetFreeStanding(bool visible)
        {
            PlayerPrefs.SetInt(VisibleKey, visible ? 1 : 0);
            PlayerPrefs.Save();
            if (Instance != null) { Instance._freePlaced = false; Instance._dirty = true; }
        }

        private void OnEnable()
        {
            BookLibrary.StateChanged += MarkDirty;
            BookCollections.Changed += MarkDirty;
            FolioAccessibility.Changed += OnAccessibility;
            Font.textureRebuilt += OnFontRebuilt;
            CoverThumbs.Changed += MarkDirty;
        }

        /// <summary>La fuente rehízo su textura: el material de los títulos de los lomos apunta a la nueva.</summary>
        private void OnFontRebuilt(Font font)
        {
            if (font == _font && _textMaterial != null) _textMaterial.mainTexture = _font.material.mainTexture;
        }

        private void OnDisable()
        {
            BookLibrary.StateChanged -= MarkDirty;
            BookCollections.Changed -= MarkDirty;
            FolioAccessibility.Changed -= OnAccessibility;
            Font.textureRebuilt -= OnFontRebuilt;
            CoverThumbs.Changed -= MarkDirty;
        }

        private void MarkDirty() => _dirty = true;

        // ───────────── Lugares guardados ─────────────

        private const string PlacesKey = "folioxr.shelf.lugares";
        [System.Serializable] private sealed class Place { public string id; public int row; public float x; }
        [System.Serializable]
        private sealed class Places
        {
            public List<Place> house = new List<Place>();   // Estanterías de la casa (las mismas en todos los entornos).
            public List<Place> free = new List<Place>();    // Estantería propia de passthrough.
        }
        private Places _places;
        private string _savedPlaces;
        private readonly HashSet<string> _known = new HashSet<string>();   // Libros de la biblioteca en el último reparto.

        private Places Stored
        {
            get
            {
                if (_places != null) return _places;
                _savedPlaces = PlayerPrefs.GetString(PlacesKey, "{}");
                try { _places = JsonUtility.FromJson<Places>(_savedPlaces) ?? new Places(); }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[FolioXR] Estantería: lugares ilegibles, se empieza de cero: " + e.Message);
                    _places = new Places();
                }
                if (_places.house == null) _places.house = new List<Place>();
                if (_places.free == null) _places.free = new List<Place>();
                return _places;
            }
        }

        /// <summary>
        /// Guarda dónde quedó cada libro (lo que está en _rows) si cambió algo. El lugar de un libro que ahora
        /// no está en la lista (la biblioteca todavía cargando, por ejemplo) se conserva: solo se olvida al
        /// quitar el libro (Forget).
        /// </summary>
        private void SavePlaces(bool house)
        {
            List<Place> list = house ? Stored.house : Stored.free;
            var kept = new List<Place>();
            foreach (Place place in list)
                if (place != null && place.id != null && !_known.Contains(place.id)) kept.Add(place);
            list.Clear();
            for (int r = 0; r < _rows.Count; r++)
                foreach (ShelfLayout.Item item in _rows[r])
                    list.Add(new Place { id = item.Id, row = r, x = Mathf.Round(item.X * 10000f) / 10000f });
            list.AddRange(kept);
            string json = JsonUtility.ToJson(Stored);
            if (json == _savedPlaces) return;
            _savedPlaces = json;
            PlayerPrefs.SetString(PlacesKey, json);
            PlayerPrefs.Save();
        }

        /// <summary>El libro salió de la biblioteca: su lugar en las estanterías queda libre.</summary>
        public static void Forget(string id)
        {
            if (Instance == null || string.IsNullOrEmpty(id)) return;
            Places stored = Instance.Stored;
            int removed = stored.house.RemoveAll(p => p == null || p.id == id) + stored.free.RemoveAll(p => p == null || p.id == id);
            if (removed == 0) return;
            Instance._savedPlaces = JsonUtility.ToJson(stored);
            PlayerPrefs.SetString(PlacesKey, Instance._savedPlaces);
            PlayerPrefs.Save();
            Instance._dirty = true;
        }

        /// <summary>
        /// Vuelve a acomodar todos los libros de la estantería que se ve, juntos y en el orden elegido en el
        /// menú (recientes, título o autor). Para empezar de nuevo después de moverlos a gusto.
        /// </summary>
        public static void Arrange()
        {
            if (Instance == null) return;
            // Solo los libros que están a la vista: los lugares de libros que no cargaron se conservan.
            (Instance._houseShown ? Instance.Stored.house : Instance.Stored.free).RemoveAll(p => p == null || p.id == null || Instance._known.Contains(p.id));
            Instance._dirty = true;
            Debug.Log("[FolioXR] Estantería: libros acomodados de nuevo.");
        }

        private void OnAccessibility()
        {
            _hands[0] = _hands[1] = null; // Las manos cambian de papel con el modo zurdo.
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (!EnsureResources()) return;
            BookLibrary library = BookLibrary.Instance;
            IReadOnlyList<ShelfRow> rows = _environment != null ? _environment.ShelfRows : null;
            bool house = rows != null && rows.Count > 0;
            bool show = library != null && library.Available && (house || FreeStanding);
            if (!show)
            {
                if (_meshObject.activeSelf) { _meshObject.SetActive(false); _titles.gameObject.SetActive(false); }
                return;
            }
            if (!ReferenceEquals(rows, _rowsSource)) { _rowsSource = rows; _dirty = true; }
            if (!house && !_freePlaced) PlaceFree();
            string current = library.Current != null ? library.Current.id : null;
            // Con un libro en la mano no se rearma (se soltaría solo); el cambio espera a que lo suelte.
            if ((_dirty && _held < 0) || current != _shownCurrent) Rebuild(library, house ? rows : null, current);
            if (!_meshObject.activeSelf) { _meshObject.SetActive(true); _titles.gameObject.SetActive(true); }
            UpdateInteraction(library);
        }

        private string _shownCurrent;

        private bool EnsureResources()
        {
            if (_environment == null) _environment = FindAnyObjectByType<FolioEnvironmentController>();
            if (_rig == null) _rig = FindAnyObjectByType<OVRCameraRig>();
            if (_book == null) _book = FindAnyObjectByType<FolioBook>();
            if (_material != null) return true;
            Renderer page = _book != null ? (_book.leftPage != null ? _book.leftPage : _book.rightPage) : null;
            if (page == null || page.sharedMaterial == null) return false;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) return false;
            _material = new Material(page.sharedMaterial) { name = "FolioXR estantería", color = Color.white };
            Shader text = Resources.Load<Shader>("FolioXRText3D");
            _textMaterial = text != null && text.isSupported
                ? new Material(text) { name = "FolioXR títulos de lomos", mainTexture = _font.material.mainTexture }
                : new Material(_font.material);
            _mesh = new Mesh { name = "FolioXR estantería", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _meshObject = new GameObject("Libros y estante");
            _meshObject.transform.SetParent(transform, false);
            _meshObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _meshObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _titles = new GameObject("Títulos").transform;
            _titles.SetParent(transform, false);
            _marker = MarkerObject();
            // Rectángulo de 1 × 1 para las portadas; mira a −Z como el Quad de Unity y lleva las dos caras.
            _coverQuad = new Mesh { name = "Portada" };
            _coverQuad.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            _coverQuad.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            _coverQuad.SetTriangles(new[] { 0, 3, 2, 0, 2, 1, 0, 2, 3, 0, 1, 2 }, 0);
            return true;
        }

        // ───────────── Construcción ─────────────

        // Atlas de 16 columnas (0-6 cueros, 7 madera, 8 papel, 9 resaltado) por 4 sombreados (frente, lado, arriba,
        // abajo), repetido en bloques: el bloque 0 va sin teñir (estantería propia, libro en la mano) y cada fila
        // de la casa tiene el suyo, teñido con la luz horneada que le llega.
        private static readonly float[] Shade = { 1f, 0.72f, 1.18f, 0.5f };

        private void BuildAtlas()
        {
            _blocks = _lines.Count + 1;
            if (_atlas == null || _atlas.height != 4 * _blocks)
            {
                if (_atlas != null) Destroy(_atlas);
                _atlas = new Texture2D(16, 4 * _blocks, TextureFormat.RGBA32, false)
                { name = "FolioXR colores de estantería", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                _material.mainTexture = _atlas;
            }
            var colors = new List<Color>(BookCover.LeatherColors)
            {
                new Color(0.42f, 0.29f, 0.18f), // madera
                new Color(0.86f, 0.82f, 0.72f), // papel (cantos)
                new Color(0.95f, 0.78f, 0.35f)  // resaltado
            };
            for (int y = 0; y < 4 * _blocks; y++)
            {
                Color light = BlockLight(y / 4);
                for (int x = 0; x < 16; x++)
                {
                    Color c = (x < colors.Count ? colors[x] : Color.gray) * Shade[y % 4] * light;
                    c.a = 1f;
                    _atlas.SetPixel(x, y, c);
                }
            }
            _atlas.Apply(false, false);
            // La barrita del hueco usa el bloque sin teñir, que cambia de lugar con el tamaño del atlas.
            if (_markerMesh != null)
            {
                var uv = new List<Vector2>();
                for (int i = 0; i < _markerMesh.vertexCount; i++) uv.Add(Texel(9, 2, 0));
                _markerMesh.SetUVs(0, uv);
            }
        }

        /// <summary>
        /// Luz del bloque. Tiene un piso: en una estantería casi a oscuras los libros se oscurecen, pero se
        /// siguen distinguiendo (floor) y sus títulos se siguen leyendo.
        /// </summary>
        private Color BlockLight(int block, float floor = 0.3f)
        {
            if (block <= 0 || block > _lines.Count) return Color.white;
            Color c = _lines[block - 1].Light;
            float brightest = Mathf.Max(0.01f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            float k = brightest < floor ? floor / brightest : brightest > 1.15f ? 1.15f / brightest : 1f;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        private Vector2 Texel(int column, int shade, int block) =>
            new Vector2((column + 0.5f) / 16f, (block * 4 + shade + 0.5f) / (4f * _blocks));

        private Color TitleColor(int leather, int block)
        {
            Color c = (leather == 6 ? new Color(0.25f, 0.16f, 0.08f) : new Color(0.93f, 0.8f, 0.45f)) * BlockLight(block, 0.6f);
            c.a = 1f;
            return c;
        }

        /// <summary>Pasa un libro a otro bloque de luz (0 al tomarlo: en la mano se ve con sus colores).</summary>
        private void Relight(int index, int block)
        {
            Slot s = _slots[index];
            // Mismo orden de caras que Box: frente, atrás, dos lados, arriba (canto de papel) y abajo.
            for (int face = 0; face < 6; face++)
            {
                Vector2 t = Texel(face == 4 ? 8 : s.Leather, face == 0 ? 0 : face == 4 ? 2 : face == 5 ? 3 : 1, block);
                for (int k = 0; k < 4; k++) _uv[s.FirstVertex + face * 4 + k] = t;
            }
            _mesh.SetUVs(0, _uv);
            if (s.Title != null) s.Title.color = TitleColor(s.Leather, block);
            if (s.CoverMaterial != null) s.CoverMaterial.color = BlockLight(block);
        }

        private void Rebuild(BookLibrary library, IReadOnlyList<ShelfRow> rows, string current)
        {
            _dirty = false;
            _shownCurrent = current;
            _vertices.Clear(); _uv.Clear(); _triangles.Clear();
            foreach (Slot s in _slots)
            {
                if (s.Title != null) Destroy(s.Title.gameObject);
                if (s.Cover != null) Destroy(s.Cover.gameObject);
                if (s.CoverMaterial != null) Destroy(s.CoverMaterial);
            }
            _slots.Clear();
            _hover = _taking = _held = -1;
            if (_marker != null) _marker.SetActive(false);

            // Todos los libros, en el orden del menú: así se reparten los que todavía no tienen lugar.
            List<BookEntry> books = BookCollections.View(library.Books, true);
            _lines.Clear();
            _houseShown = rows != null;
            // Filas como (izquierda, derecha, hacia la sala), en coordenadas del mundo.
            if (rows != null)
            {
                Transform root = _environment.HouseRoot;
                foreach (ShelfRow row in rows)
                    AddLine(root.TransformPoint(row.Left), root.TransformPoint(row.Right), root.TransformDirection(row.Facing).normalized, row.Light);
                BuildAtlas();
            }
            else
            {
                foreach (var line in FreeRows()) AddLine(line.Item1, line.Item2, line.Item3, Color.white);
                BuildAtlas();
                _block = 0;
                BuildFrame();
            }
            int waiting = Distribute(books, _houseShown ? Stored.house : Stored.free);
            SavePlaces(_houseShown);
            var byId = new Dictionary<string, BookEntry>();
            foreach (BookEntry entry in books) if (entry != null && entry.id != null) byId[entry.id] = entry;
            int placed = 0;
            for (int r = 0; r < _rows.Count; r++)
            {
                Line line = _lines[r];
                Vector3 along = (line.Right - line.Left).normalized;
                Quaternion rotation = Quaternion.LookRotation(line.Facing, Vector3.up);
                foreach (ShelfLayout.Item item in _rows[r])
                {
                    placed++;
                    if (current != null && item.Id == current) continue;   // Hueco del libro abierto: su lugar lo espera.
                    BookEntry entry = byId[item.Id];
                    float height = SpineHeight(entry);
                    // El lomo queda en el plano de la fila; el libro se extiende hacia el fondo del estante.
                    Vector3 center = line.Left + along * (item.X + item.Width * 0.5f) + Vector3.up * (height * 0.5f + 0.0005f);
                    _block = r + 1;
                    AddBook(entry, center, rotation, new Vector3(item.Width, height, SpineDepth(entry)), r, item.X + item.Width * 0.5f);
                }
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uv);
            _mesh.SetTriangles(_triangles, 0, true);
            if (waiting > 0)
                Debug.Log("[FolioXR] Estantería: caben " + placed + " de " + (placed + waiting) + " libros; el resto sigue en el menú.");
        }

        private void AddLine(Vector3 left, Vector3 right, Vector3 facing, Color light)
        {
            _lines.Add(new Line { Left = left, Right = right, Facing = facing, Length = Vector3.Distance(left, right), Light = light });
        }

        /// <summary>
        /// Reparte los libros en las filas: cada uno donde lo dejaron (places) y los que no tienen lugar, en el
        /// primer hueco libre siguiendo el orden de las filas. Devuelve cuántos no entraron.
        /// </summary>
        private int Distribute(List<BookEntry> books, List<Place> places)
        {
            _rows.Clear();
            for (int r = 0; r < _lines.Count; r++) _rows.Add(new List<ShelfLayout.Item>());
            var byId = new Dictionary<string, BookEntry>();
            foreach (BookEntry entry in books) if (entry != null && entry.id != null) byId[entry.id] = entry;
            _known.Clear();
            foreach (string id in byId.Keys) _known.Add(id);
            var seated = new HashSet<string>();
            foreach (Place place in places)
            {
                // Lugares de libros que ya no están, de filas que no existen o repetidos: se olvidan.
                if (place == null || place.id == null || !byId.ContainsKey(place.id) || place.row < 0 ||
                    place.row >= _rows.Count || !seated.Add(place.id)) continue;
                _rows[place.row].Add(new ShelfLayout.Item { Id = place.id, X = place.x, Width = SpineWidth(byId[place.id]) });
            }
            for (int r = 0; r < _rows.Count; r++)
            {
                List<ShelfLayout.Item> row = _rows[r];
                // La fila es más corta en otro entorno o los libros engordaron: los últimos buscan otro hueco.
                while (!ShelfLayout.Resolve(row, _lines[r].Length))
                {
                    seated.Remove(row[row.Count - 1].Id);
                    row.RemoveAt(row.Count - 1);
                }
            }
            int waiting = 0;
            foreach (BookEntry entry in books)
            {
                if (entry == null || entry.id == null || seated.Contains(entry.id)) continue;
                float width = SpineWidth(entry);
                bool found = false;
                for (int r = 0; r < _rows.Count && !found; r++)
                {
                    float x = ShelfLayout.FirstGap(_rows[r], _lines[r].Length, width);
                    if (x < 0f) continue;
                    ShelfLayout.Insert(_rows[r], new ShelfLayout.Item { Id = entry.id, X = x, Width = width });
                    seated.Add(entry.id);
                    found = true;
                }
                if (!found) waiting++;
            }
            return waiting;
        }

        // Separación de la portada y del título respecto a la tapa: de lejos, menos que esto parpadea.
        private const float CoverGap = 0.0015f, TitleGap = 0.002f;

        private static float SpineWidth(BookEntry entry)
        {
            // Grosor según páginas: entre 1,6 y 6 cm.
            float pages = Mathf.Max(1, entry.pages);
            return Mathf.Clamp(0.016f + pages * 0.00009f, 0.016f, 0.06f);
        }

        /// <summary>
        /// Fondo del libro parado en el estante: el ancho de su portada si ya tiene miniatura (CoverThumbs);
        /// si no, el de una hoja corriente.
        /// </summary>
        private static float SpineDepth(BookEntry entry)
        {
            Texture2D cover = CoverThumbs.Get(entry.id);
            float aspect = cover != null && cover.height > 0 ? Mathf.Clamp(cover.width / (float)cover.height, 0.5f, 0.8f) : 0.68f;
            return Mathf.Min(0.2f, SpineHeight(entry) * aspect);
        }

        private static float SpineHeight(BookEntry entry)
        {
            string f = (entry.format ?? string.Empty).ToLowerInvariant();
            // Cómics un poco más altos; documentos tamaño carta.
            return f == "cbz" || f == "cbr" || f == "zip" ? 0.27f : f == "docx" || f == "odt" || f == "rtf" ? 0.28f : 0.235f;
        }

        private void AddBook(BookEntry entry, Vector3 center, Quaternion rotation, Vector3 size, int row, float along)
        {
            int leather = Mathf.Clamp(BookLibrary.Leather(entry), 0, BookCover.LeatherColors.Length - 1);
            int first = _vertices.Count;
            // Lomo (frente) y tapas en cuero; arriba se ve el canto de las hojas.
            Box(center - rotation * Vector3.forward * (size.z * 0.5f), rotation, size, leather, 8);
            var slot = new Slot { Entry = entry, Center = center, Rotation = rotation, Size = size, FirstVertex = first, Row = row, Along = along, Leather = leather };
            // Título a lo largo del lomo, de abajo hacia arriba.
            var go = new GameObject("Lomo · " + entry.title);
            go.transform.SetParent(_titles, false);
            var text = go.AddComponent<TextMesh>();
            text.font = _font;
            text.fontSize = 48;
            // Un píxel de la fuente mide characterSize / 10 m: letras de ~65 % del ancho del lomo.
            text.characterSize = Mathf.Clamp(size.x * 0.65f * 10f / 48f, 0.0018f, 0.003f);
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = TitleColor(leather, _block);
            text.text = Spine(entry.title, size.y);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _textMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            slot.Title = text;
            Texture2D cover = CoverThumbs.Get(entry.id);
            if (cover != null)
            {
                // Portada sobre la tapa de adelante (a la derecha del lomo, como en un libro de verdad): se ve
                // al sacar el libro, que en la mano gira para mostrarla (SetBox).
                var art = new GameObject("Portada · " + entry.title);
                art.transform.SetParent(_titles, false);
                art.transform.localScale = new Vector3(size.z, size.y, 1f);
                art.AddComponent<MeshFilter>().sharedMesh = _coverQuad;
                var renderer = art.AddComponent<MeshRenderer>();
                slot.CoverMaterial = new Material(_material) { name = "FolioXR portada en estantería", mainTexture = cover, color = BlockLight(_block) };
                renderer.sharedMaterial = slot.CoverMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                slot.Cover = art.transform;
            }
            _slots.Add(slot);
            PlaceLabels(slot, center - rotation * Vector3.forward * (size.z * 0.5f), rotation);
        }

        /// <summary>Lleva el título del lomo y la portada a su lugar sobre la caja del libro.</summary>
        private static void PlaceLabels(Slot s, Vector3 boxCenter, Quaternion r)
        {
            if (s.Title != null)
                s.Title.transform.SetPositionAndRotation(boxCenter + r * Vector3.forward * (s.Size.z * 0.5f + TitleGap), r * Quaternion.Euler(0f, 180f, 90f));
            if (s.Cover != null)
                s.Cover.SetPositionAndRotation(boxCenter - r * Vector3.right * (s.Size.x * 0.5f + CoverGap), r * Quaternion.Euler(0f, 90f, 0f));
        }

        private static string Spine(string title, float height)
        {
            title = string.IsNullOrWhiteSpace(title) ? "Sin título" : title.Trim();
            int max = Mathf.Max(8, Mathf.RoundToInt(height * 95f));
            return title.Length <= max ? title : title.Substring(0, max - 1).TrimEnd() + "…";
        }

        /// <summary>Caja centrada en center con la cara «frente» hacia +Z local.</summary>
        private void Box(Vector3 center, Quaternion rotation, Vector3 size, int column, int topColumn)
        {
            Vector3 h = size * 0.5f;
            Vector3 X = rotation * Vector3.right * h.x, Y = rotation * Vector3.up * h.y, Z = rotation * Vector3.forward * h.z;
            Face(center + Z, X, Y, column, 0);           // frente (lomo)
            Face(center - Z, -X, Y, column, 1);          // atrás
            Face(center + X, -Z, Y, column, 1);          // lados
            Face(center - X, Z, Y, column, 1);
            Face(center + Y, X, -Z, topColumn, 2);       // arriba
            Face(center - Y, X, Z, column, 3);           // abajo
        }

        private void Face(Vector3 c, Vector3 u, Vector3 v, int column, int shade)
        {
            int b = _vertices.Count;
            _vertices.Add(c - u - v); _vertices.Add(c + u - v); _vertices.Add(c + u + v); _vertices.Add(c - u + v);
            Vector2 t = Texel(column, shade, _block);
            for (int i = 0; i < 4; i++) _uv.Add(t);
            // Las dos caras: el material de página no descarta caras traseras en todos los casos.
            _triangles.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2, b, b + 1, b + 2, b, b + 2, b + 3 });
        }

        // ───────────── Estantería propia (passthrough) ─────────────

        private const float FreeWidth = 1.1f, FreeDepth = 0.26f;
        private static readonly float[] FreeShelves = { -0.55f, -0.18f, 0.19f }; // Altura de cada tabla respecto a los ojos.

        private void PlaceFree()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            Transform head = camera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 position = head.position + forward * 0.95f;
            _freePose = new Pose(position, Quaternion.LookRotation(-forward, Vector3.up)); // +Z mira a la persona.
            _freePlaced = true;
            _dirty = true;
        }

        private IEnumerable<(Vector3, Vector3, Vector3)> FreeRows()
        {
            Vector3 right = _freePose.rotation * Vector3.left; // Vista desde la persona: izquierda a derecha.
            Vector3 facing = _freePose.rotation * Vector3.forward;
            foreach (float y in new[] { FreeShelves[1], FreeShelves[2], FreeShelves[0] })
            {
                Vector3 board = _freePose.position + Vector3.up * (y + 0.0125f);
                yield return (board - right * (FreeWidth * 0.5f - 0.03f) + facing * (FreeDepth * 0.5f - 0.05f),
                              board + right * (FreeWidth * 0.5f - 0.03f) + facing * (FreeDepth * 0.5f - 0.05f), facing);
            }
        }

        private void BuildFrame()
        {
            const int wood = 7;
            Quaternion r = _freePose.rotation;
            Vector3 p = _freePose.position;
            float top = FreeShelves[2] + 0.36f, bottom = FreeShelves[0] - 0.02f;
            float height = top - bottom, middle = (top + bottom) * 0.5f;
            foreach (float y in FreeShelves)
                Box(p + Vector3.up * y, r, new Vector3(FreeWidth, 0.025f, FreeDepth), wood, wood);
            Box(p + Vector3.up * top, r, new Vector3(FreeWidth, 0.025f, FreeDepth), wood, wood);
            for (int side = -1; side <= 1; side += 2)
                Box(p + Vector3.up * middle + r * Vector3.right * (side * FreeWidth * 0.5f), r, new Vector3(0.025f, height, FreeDepth), wood, wood);
            Box(p + Vector3.up * middle - r * Vector3.forward * (FreeDepth * 0.5f), r, new Vector3(FreeWidth, height, 0.012f), wood, wood);
        }

        // ───────────── Tomar un libro ─────────────

        private void UpdateInteraction(BookLibrary library)
        {
            if (_taking >= 0)
            {
                // El libro sale del estante (0,25 s) y se abre.
                float t = Mathf.Clamp01((Time.unscaledTime - _takeTime) / (FolioAccessibility.ReduceMotion ? 0.01f : 0.25f));
                Pull(_taking, 0.12f * t);
                if (t >= 1f)
                {
                    BookEntry entry = _slots[_taking].Entry;
                    _taking = -1;
                    library.Open(entry);
                    if (_book != null) _book.PlaceInFrontOfView();
                    _dirty = true;
                }
                return;
            }
            if (_held >= 0) { UpdateHeld(library); return; }
            if (ReturnOpenBook(library)) return;
            int hover = -1;
            float best = float.MaxValue;
            for (int role = 0; role < 2; role++)
            {
                if (!Probe(role, out Vector3 point, out bool pressed, out bool wasPressed, out float reach)) continue;
                int near = Nearest(point, reach, out float distance);
                if (near < 0) continue;
                if (pressed && !wasPressed)
                {
                    // El libro queda en la mano: soltarlo decide si vuelve al estante o se abre.
                    _held = near;
                    _heldRole = role;
                    _heldOffset = point - _slots[near].Center;
                    _hover = -1;
                    Relight(near, 0);
                    Debug.Log("[FolioXR] Estantería: en la mano «" + _slots[near].Entry.title + "».");
                    return;
                }
                if (distance < best) { best = distance; hover = near; }
            }
            if (hover != _hover)
            {
                if (_hover >= 0 && _hover < _slots.Count) Pull(_hover, 0f);
                if (hover >= 0) Pull(hover, 0.02f);
                _hover = hover;
            }
        }

        private void UpdateHeld(BookLibrary library)
        {
            Slot s = _slots[_held];
            if (!Probe(_heldRole, out Vector3 point, out bool pressed, out _, out _))
            {
                // Se perdió la mano: el libro vuelve a su lugar.
                Move(_held, s.Center);
                Relight(_held, s.Row + 1);
                _held = -1;
                if (_marker != null) _marker.SetActive(false);
                return;
            }
            Vector3 center = point - _heldOffset;
            bool onShelf = DropTarget(center, s.Entry.id, s.Size.x, out int row, out Vector3 markerAt, out Quaternion markerRotation);
            if (pressed)
            {
                Move(_held, center, true);
                if (_marker != null)
                {
                    _marker.SetActive(onShelf);
                    if (onShelf) _marker.transform.SetPositionAndRotation(markerAt, markerRotation);
                }
                return;
            }
            if (_marker != null) _marker.SetActive(false);
            int index = _held;
            _held = -1;
            if (onShelf)
            {
                Debug.Log("[FolioXR] Estantería: «" + s.Entry.title + "» queda en la fila " + (row + 1) + ".");
                Settle(s.Entry.id, row);
            }
            else
            {
                Move(index, s.Center);
                Relight(index, s.Row + 1);
                Take(index);
            }
        }

        /// <summary>El libro abierto, soltado sobre una fila, se cierra y queda guardado en ese lugar.</summary>
        private bool ReturnOpenBook(BookLibrary library)
        {
            bool grabbed = _book != null && _book.IsGrabbed;
            bool released = _bookWasGrabbed && !grabbed;
            _bookWasGrabbed = grabbed;
            if (grabbed && library.Current != null)
            {
                bool over = DropTarget(_book.transform.position, library.Current.id, SpineWidth(library.Current), out _, out Vector3 at, out Quaternion rotation);
                if (_marker != null)
                {
                    _marker.SetActive(over);
                    if (over) _marker.transform.SetPositionAndRotation(at, rotation);
                }
                return true;
            }
            if (!released || library.Current == null) return false;
            if (_marker != null) _marker.SetActive(false);
            if (!DropTarget(_book.transform.position, library.Current.id, SpineWidth(library.Current), out int row, out _, out _)) return false;
            Debug.Log("[FolioXR] Estantería: «" + library.Current.title + "» cerrado y guardado en la fila " + (row + 1) + ".");
            Settle(library.Current.id, row);
            library.CloseCurrent();
            return true;
        }

        /// <summary>
        /// ¿El punto está sobre una fila donde el libro cabe? Si es así, row es la fila, _trial queda con los
        /// libros de esa fila ya acomodados (el que se suelta, donde lo dejan; los vecinos, corridos si hace
        /// falta) y at/rotation marcan con una barrita dorada dónde quedaría el lomo.
        /// </summary>
        private bool DropTarget(Vector3 point, string id, float width, out int row, out Vector3 at, out Quaternion rotation, float extra = 0f)
        {
            row = -1; at = Vector3.zero; rotation = Quaternion.identity;
            float bestScore = float.MaxValue, bestAlong = 0f;
            for (int r = 0; r < _lines.Count; r++)
            {
                Line line = _lines[r];
                if (line.Length < 0.01f) continue;
                Vector3 axis = (line.Right - line.Left) / line.Length;
                Vector3 d = point - line.Left;
                float along = Vector3.Dot(d, axis), up = Vector3.Dot(d, Vector3.up), depth = Vector3.Dot(d, line.Facing);
                if (along < -0.08f - extra || along > line.Length + 0.08f + extra) continue;
                if (up < -0.06f - extra || up > 0.34f + extra) continue;
                if (depth < -0.3f - extra || depth > 0.22f + extra) continue;
                float score = Mathf.Abs(up - 0.12f) + Mathf.Abs(depth) * 0.5f;
                if (score < bestScore) { bestScore = score; row = r; bestAlong = along; }
            }
            if (row < 0) return false;
            // Prueba: la fila sin este libro (si ya estaba en ella) más el libro donde lo sueltan.
            _trial.Clear();
            foreach (ShelfLayout.Item item in _rows[row])
                if (item.Id != id) _trial.Add(new ShelfLayout.Item { Id = item.Id, X = item.X, Width = item.Width });
            var dropped = new ShelfLayout.Item { Id = id, X = bestAlong - width * 0.5f, Width = width };
            _trial.Add(dropped);
            if (!ShelfLayout.Resolve(_trial, _lines[row].Length, dropped)) { row = -1; return false; }   // Fila llena.
            Line target = _lines[row];
            Vector3 dir = (target.Right - target.Left) / target.Length;
            at = target.Left + dir * (dropped.X + width * 0.5f) + Vector3.up * 0.12f + target.Facing * 0.02f;
            rotation = Quaternion.LookRotation(target.Facing, Vector3.up);
            return true;
        }

        /// <summary>Deja el libro en la fila con el acomodo que DropTarget acaba de calcular (_trial) y lo guarda.</summary>
        private void Settle(string id, int row)
        {
            foreach (List<ShelfLayout.Item> items in _rows) items.RemoveAll(item => item.Id == id);
            _rows[row].Clear();
            _rows[row].AddRange(_trial);
            SavePlaces(_houseShown);
            _dirty = true;
        }

        private void Move(int index, Vector3 center, bool turned = false) => SetBox(index, center, turned);

        private GameObject MarkerObject()
        {
            var go = new GameObject("Hueco en la estantería");
            go.transform.SetParent(transform, false);
            var mesh = _markerMesh = new Mesh { name = "Hueco" };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3 h = new Vector3(0.0025f, 0.12f, 0.015f);
            void F(Vector3 c, Vector3 a, Vector3 b)
            {
                int k = v.Count;
                v.Add(c - a - b); v.Add(c + a - b); v.Add(c + a + b); v.Add(c - a + b);
                for (int i = 0; i < 4; i++) uv.Add(Texel(9, 2, 0));
                t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2, k, k + 1, k + 2, k, k + 2, k + 3 });
            }
            F(Vector3.forward * h.z, Vector3.right * h.x, Vector3.up * h.y);
            F(-Vector3.forward * h.z, Vector3.right * h.x, Vector3.up * h.y);
            F(Vector3.right * h.x, Vector3.forward * h.z, Vector3.up * h.y);
            F(-Vector3.right * h.x, Vector3.forward * h.z, Vector3.up * h.y);
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }

        private void Take(int index)
        {
            _taking = index;
            _takeTime = Time.unscaledTime;
            Debug.Log("[FolioXR] Estantería: tomar «" + _slots[index].Entry.title + "».");
        }

        /// <summary>Saca el libro «amount» metros del estante (hacia la sala).</summary>
        private void Pull(int index, float amount) => SetBox(index, _slots[index].Center + _slots[index].Rotation * Vector3.forward * amount);

        /// <summary>
        /// Lleva el libro (caja, título y portada) a un centro de lomo nuevo. turned: en la mano, girado un
        /// cuarto de vuelta y centrado en ese punto, con la portada hacia quien lo sacó.
        /// </summary>
        private void SetBox(int index, Vector3 spineCenter, bool turned = false)
        {
            Slot s = _slots[index];
            Quaternion r = turned ? s.Rotation * Quaternion.Euler(0f, 90f, 0f) : s.Rotation;
            Vector3 b = turned ? spineCenter : spineCenter - r * Vector3.forward * (s.Size.z * 0.5f);
            Vector3 h = s.Size * 0.5f;
            Vector3 X = r * Vector3.right * h.x, Y = r * Vector3.up * h.y, Z = r * Vector3.forward * h.z;
            int k = s.FirstVertex;
            void F(Vector3 c, Vector3 u, Vector3 v) { _vertices[k++] = c - u - v; _vertices[k++] = c + u - v; _vertices[k++] = c + u + v; _vertices[k++] = c - u + v; }
            // Mismo orden de caras que Box.
            F(b + Z, X, Y); F(b - Z, -X, Y); F(b + X, -Z, Y); F(b - X, Z, Y); F(b + Y, X, -Z); F(b - Y, X, Z);
            _mesh.SetVertices(_vertices);
            _mesh.RecalculateBounds();
            PlaceLabels(s, b, r);
        }

        private int Nearest(Vector3 point, float reach, out float distance)
        {
            distance = float.MaxValue;
            int best = -1;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                // Distancia al lomo: punto en coordenadas del libro, recortado a su caja.
                Vector3 local = Quaternion.Inverse(s.Rotation) * (point - s.Center);
                Vector3 clamped = new Vector3(Mathf.Clamp(local.x, -s.Size.x * 0.5f, s.Size.x * 0.5f),
                                              Mathf.Clamp(local.y, -s.Size.y * 0.5f, s.Size.y * 0.5f),
                                              Mathf.Clamp(local.z, -s.Size.z, 0f));
                float d = Vector3.Distance(local, clamped);
                // Entre libros vecinos gana el que tiene el punto más centrado.
                d += Mathf.Abs(local.x) * 0.15f;
                if (d < reach && d < distance) { distance = d; best = i; }
            }
            return best;
        }

        /// <summary>Punto de contacto y botón de una mano (0 = apoyo, 1 = principal).</summary>
        private bool Probe(int role, out Vector3 point, out bool pressed, out bool wasPressed, out float reach)
        {
            point = Vector3.zero; pressed = wasPressed = false; reach = 0f;
            XRNode node = Hands.Node(role);
            if (FolioXR.Tools.ToolRegistry.HoldsHand(node) || FolioXR.Tools.ToolRegistry.HoldsController(node)) return false;
            if (_book != null && _book.IsGrabbed) return false;
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            bool controller = device.isValid && (device.characteristics & InputDeviceCharacteristics.Controller) != 0 &&
                              device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
            if (controller)
            {
                Transform anchor = role == 0 ? Hands.SupportAnchor(_rig) : Hands.MainAnchor(_rig);
                if (anchor == null) return false;
                point = anchor.position + anchor.forward * 0.04f;
                device.TryGetFeatureValue(CommonUsages.gripButton, out pressed);
                wasPressed = _gripWas[role];
                _gripWas[role] = pressed;
                reach = 0.07f;
                return true;
            }
            _gripWas[role] = false;
            OVRHand hand = HandFor(role);
            if (hand == null || !hand.IsTracked || !hand.IsDataHighConfidence) return false;
            OVRSkeleton skeleton = _skeletons[role];
            if (!TipPosition(skeleton, out point)) return false;
            pressed = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            wasPressed = _pinchWas[role];
            _pinchWas[role] = pressed;
            reach = 0.05f;
            return true;
        }

        private OVRHand HandFor(int role)
        {
            OVRPlugin.Hand side = role == 0 ? Hands.SupportHand : Hands.MainHand;
            if (_hands[role] != null && _hands[role].isActiveAndEnabled && _hands[role].GetHand() == side) return _hands[role];
            _hands[role] = null;
            foreach (OVRHand hand in FindObjectsByType<OVRHand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (hand.GetHand() == side)
                {
                    _hands[role] = hand;
                    _skeletons[role] = hand.GetComponent<OVRSkeleton>();
                    break;
                }
            return _hands[role];
        }

        private static bool TipPosition(OVRSkeleton skeleton, out Vector3 position)
        {
            position = Vector3.zero;
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null) return false;
            OVRSkeleton.SkeletonType type = skeleton.GetSkeletonType();
            bool xr = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight;
            OVRSkeleton.BoneId index = xr ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip;
            OVRSkeleton.BoneId thumb = xr ? OVRSkeleton.BoneId.XRHand_ThumbTip : OVRSkeleton.BoneId.Hand_ThumbTip;
            Vector3 a = Vector3.zero, b = Vector3.zero;
            bool hasA = false, hasB = false;
            foreach (OVRBone bone in skeleton.Bones)
            {
                if (bone == null || bone.Transform == null) continue;
                if (bone.Id == index) { a = bone.Transform.position; hasA = true; }
                else if (bone.Id == thumb) { b = bone.Transform.position; hasB = true; }
            }
            if (!hasA) return false;
            position = hasB ? (a + b) * 0.5f : a; // Punto del pellizco.
            return true;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
            if (_textMaterial != null) Destroy(_textMaterial);
            if (_atlas != null) Destroy(_atlas);
            if (_markerMesh != null) Destroy(_markerMesh);
            if (_coverQuad != null) Destroy(_coverQuad);
            foreach (Slot s in _slots) if (s.CoverMaterial != null) Destroy(s.CoverMaterial);
        }
    }
}
