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
    /// En la casa, los libros del usuario aparecen en la estantería real de la biblioteca (filas a la altura
    /// de los ojos, delante de los libros pintados). En passthrough, una estantería propia aparece frente a
    /// la persona desde el menú. Se toma un libro pellizcando su lomo (manos) o con el grip cerca (mandos):
    /// sale del estante y se abre. El libro abierto falta del estante, como en una biblioteca de verdad.
    /// Usa la misma vista que el menú (colección, formato y orden de <see cref="BookCollections"/>).
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
        }

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
        }

        private void OnDisable()
        {
            BookLibrary.StateChanged -= MarkDirty;
            BookCollections.Changed -= MarkDirty;
            FolioAccessibility.Changed -= OnAccessibility;
        }

        private void MarkDirty() => _dirty = true;

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
            if (_dirty || current != _shownCurrent) Rebuild(library, house ? rows : null, current);
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
            _atlas = Atlas();
            _material = new Material(page.sharedMaterial) { name = "FolioXR estantería", mainTexture = _atlas, color = Color.white };
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
            return true;
        }

        // ───────────── Construcción ─────────────

        // Atlas 16 × 4: columnas 0-6 cueros, 7 madera, 8 papel, 9 resaltado; filas = sombreado (frente, lado, arriba, abajo).
        private static readonly float[] Shade = { 1f, 0.72f, 1.18f, 0.5f };

        private static Texture2D Atlas()
        {
            var texture = new Texture2D(16, 4, TextureFormat.RGBA32, false)
            { name = "FolioXR colores de estantería", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var colors = new List<Color>(BookCover.LeatherColors)
            {
                new Color(0.42f, 0.29f, 0.18f), // madera
                new Color(0.86f, 0.82f, 0.72f), // papel (cantos)
                new Color(0.95f, 0.78f, 0.35f)  // resaltado
            };
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 16; x++)
                {
                    Color c = x < colors.Count ? colors[x] : Color.gray;
                    c *= Shade[y];
                    c.a = 1f;
                    texture.SetPixel(x, y, c);
                }
            texture.Apply(false, true);
            return texture;
        }

        private static Vector2 Texel(int column, int shade) => new Vector2((column + 0.5f) / 16f, (shade + 0.5f) / 4f);

        private void Rebuild(BookLibrary library, IReadOnlyList<ShelfRow> rows, string current)
        {
            _dirty = false;
            _shownCurrent = current;
            _vertices.Clear(); _uv.Clear(); _triangles.Clear();
            foreach (Slot s in _slots) if (s.Title != null) Destroy(s.Title.gameObject);
            _slots.Clear();
            _hover = _taking = -1;

            List<BookEntry> books = BookCollections.View(library.Books);
            // Filas como (izquierda, derecha, hacia la sala), en coordenadas del mundo.
            var lines = new List<(Vector3 left, Vector3 right, Vector3 facing)>();
            if (rows != null)
            {
                Transform root = _environment.HouseRoot;
                foreach (ShelfRow row in rows)
                    lines.Add((root.TransformPoint(row.Left), root.TransformPoint(row.Right), root.TransformDirection(row.Facing).normalized));
            }
            else
            {
                BuildFrame();
                lines.AddRange(FreeRows());
            }
            int index = 0, placed = 0;
            foreach (var line in lines)
            {
                Vector3 along = (line.right - line.left).normalized;
                float length = Vector3.Distance(line.left, line.right);
                Quaternion rotation = Quaternion.LookRotation(line.facing, Vector3.up);
                float x = 0f;
                while (index < books.Count)
                {
                    BookEntry entry = books[index];
                    float width = SpineWidth(entry), height = SpineHeight(entry);
                    if (x + width > length) break;
                    index++;
                    if (current != null && entry.id == current) { x += width + 0.004f; continue; } // Hueco del libro abierto.
                    // El lomo queda en el plano de la fila; el libro se extiende hacia atrás.
                    Vector3 center = line.left + along * (x + width * 0.5f) + Vector3.up * (height * 0.5f) + line.facing * 0.017f;
                    AddBook(entry, center, rotation, new Vector3(width, height, 0.034f));
                    x += width + 0.004f;
                    placed++;
                }
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uv);
            _mesh.SetTriangles(_triangles, 0, true);
            if (index < books.Count)
                Debug.Log("[FolioXR] Estantería: caben " + placed + " de " + books.Count + " libros; el resto sigue en el menú.");
        }

        private static float SpineWidth(BookEntry entry)
        {
            // Grosor según páginas: entre 1,6 y 6 cm.
            float pages = Mathf.Max(1, entry.pages);
            return Mathf.Clamp(0.016f + pages * 0.00009f, 0.016f, 0.06f);
        }

        private static float SpineHeight(BookEntry entry)
        {
            string f = (entry.format ?? string.Empty).ToLowerInvariant();
            // Cómics un poco más altos; documentos tamaño carta.
            return f == "cbz" || f == "cbr" || f == "zip" ? 0.27f : f == "docx" || f == "odt" || f == "rtf" ? 0.28f : 0.235f;
        }

        private void AddBook(BookEntry entry, Vector3 center, Quaternion rotation, Vector3 size)
        {
            int leather = Mathf.Clamp(BookLibrary.Leather(entry), 0, BookCover.LeatherColors.Length - 1);
            int first = _vertices.Count;
            // Lomo (frente) y tapas en cuero; arriba se ve el canto de las hojas.
            Box(center - rotation * Vector3.forward * (size.z * 0.5f), rotation, size, leather, 8);
            var slot = new Slot { Entry = entry, Center = center, Rotation = rotation, Size = size, FirstVertex = first };
            // Título a lo largo del lomo, de abajo hacia arriba.
            var go = new GameObject("Lomo · " + entry.title);
            go.transform.SetParent(_titles, false);
            go.transform.SetPositionAndRotation(center + rotation * Vector3.forward * 0.0012f,
                rotation * Quaternion.Euler(0f, 180f, 90f));
            var text = go.AddComponent<TextMesh>();
            text.font = _font;
            text.fontSize = 48;
            // Un píxel de la fuente mide characterSize / 10 m: letras de ~65 % del ancho del lomo.
            text.characterSize = Mathf.Clamp(size.x * 0.65f * 10f / 48f, 0.0018f, 0.003f);
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = leather == 6 ? new Color(0.25f, 0.16f, 0.08f) : new Color(0.93f, 0.8f, 0.45f);
            text.text = Spine(entry.title, size.y);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _textMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            slot.Title = text;
            _slots.Add(slot);
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
            Vector2 t = Texel(column, shade);
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
            int hover = -1;
            float best = float.MaxValue;
            for (int role = 0; role < 2; role++)
            {
                if (!Probe(role, out Vector3 point, out bool pressed, out bool wasPressed, out float reach)) continue;
                int near = Nearest(point, reach, out float distance);
                if (near < 0) continue;
                if (pressed && !wasPressed)
                {
                    Take(near);
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

        private void Take(int index)
        {
            _taking = index;
            _takeTime = Time.unscaledTime;
            Debug.Log("[FolioXR] Estantería: tomar «" + _slots[index].Entry.title + "».");
        }

        /// <summary>Saca el libro «amount» metros del estante (hacia la sala).</summary>
        private void Pull(int index, float amount)
        {
            Slot s = _slots[index];
            Vector3 offset = s.Rotation * Vector3.forward * amount;
            int count = 24; // Seis caras de cuatro vértices.
            Vector3 boxCenter = s.Center - s.Rotation * Vector3.forward * (s.Size.z * 0.5f);
            var vertices = new List<Vector3>(count);
            Vector3 h = s.Size * 0.5f;
            Quaternion r = s.Rotation;
            Vector3 X = r * Vector3.right * h.x, Y = r * Vector3.up * h.y, Z = r * Vector3.forward * h.z;
            void F(Vector3 c, Vector3 u, Vector3 v) { vertices.Add(c - u - v); vertices.Add(c + u - v); vertices.Add(c + u + v); vertices.Add(c - u + v); }
            Vector3 b = boxCenter + offset;
            F(b + Z, X, Y); F(b - Z, -X, Y); F(b + X, -Z, Y); F(b - X, Z, Y); F(b + Y, X, -Z); F(b - Y, X, Z);
            for (int i = 0; i < count; i++) _vertices[s.FirstVertex + i] = vertices[i];
            _mesh.SetVertices(_vertices);
            _mesh.RecalculateBounds();
            if (s.Title != null) s.Title.transform.position = s.Center + offset + r * Vector3.forward * 0.0012f;
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
        }
    }
}
