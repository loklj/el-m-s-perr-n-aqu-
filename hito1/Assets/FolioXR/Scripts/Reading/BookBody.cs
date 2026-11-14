using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Reading
{
    /// <summary>
    /// Cuerpo del libro (pedidos del usuario: 13 nov 2026 «parece una tabla para cortar verduras»; 14 nov 2026
    /// «mira videos de cómo se ven los libros en Livro y replícalo»). Geometría propia en BookGeometry:
    /// las dos mitades abren en una V leve hacia quien lee, las hojas bajan curvas hasta juntarse en el lomo,
    /// el grosor sale de la cantidad de páginas (lo leído queda a un lado), la tapa es blanda (se dobla con las
    /// hojas, como un cómic) o dura, con esquinas redondeadas y el arte de la portada de borde a borde.
    /// Las páginas siguen siendo los mismos objetos (tinta, marcadores y búsqueda no cambian): solo cambia su
    /// malla y se giran con su mitad.
    /// Ejes locales del libro: lomo en x = 0, las páginas miran hacia -Z (z = 0); la tapa queda detrás (+Z).
    /// </summary>
    public sealed class BookBody : MonoBehaviour
    {
        public const float TiltDegrees = BookGeometry.TiltDegrees;
        public const float MinGutter = 0.002f;        // Las hojas casi se tocan en el lomo.
        private const float FoldSeconds = 0.45f;
        public const string BindingPrefsPrefix = "folioxr.book.binding.";   // 0 automática, 1 dura, 2 blanda.

        private FolioBook _book;
        private Renderer _coverBox;
        private Material _leather, _paperEdge, _shade, _crease;
        private Texture2D _edgeTexture, _shadeTexture;
        private readonly Transform[] _halves = new Transform[2];   // 0 izquierda, 1 derecha.
        private readonly Mesh[] _boards = new Mesh[2], _blocks = new Mesh[2], _art = new Mesh[2];
        private Mesh _spine, _creaseMesh, _leftPage, _rightPage, _leftShadeMesh, _rightShadeMesh;
        private Renderer _spineRenderer, _creaseRenderer, _leftOverlay, _rightOverlay;
        private readonly MeshData _data = new MeshData();
        private readonly List<Object> _owned = new List<Object>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private BookShape _shape;
        private float _baseCurlL, _baseCurlR, _fold, _builtCurl = -1f, _nextSweep;
        private bool _contentsHidden, _built;

        /// <summary>Libro cerrado: la mitad de la portada se pliega sobre la otra y queda la tapa a la vista.</summary>
        public bool Closed { get; private set; }
        public bool Folding => _fold > 0.001f && _fold < 0.999f;
        /// <summary>Lado que no se mueve al cerrar (+1 derecha, -1 izquierda): ahí queda el libro cerrado.</summary>
        public int RestSide => _book != null && _book.RightToLeft ? -1 : 1;
        /// <summary>Inclinación de cada mitad con el libro abierto (para la hoja que pasa).</summary>
        public float TiltRadians => TiltDegrees * Mathf.Deg2Rad;
        public bool HardCover => _shape.Hard;

        internal static BookBody Attach(FolioBook book)
        {
            var body = book.gameObject.AddComponent<BookBody>();
            body._book = book;
            body.Build();
            return body;
        }

        /// <summary>Tapa elegida para un libro: 0 automática, 1 dura, 2 blanda.</summary>
        public static int BindingFor(string documentId) =>
            string.IsNullOrEmpty(documentId) ? 0 : PlayerPrefs.GetInt(BindingPrefsPrefix + documentId, 0);

        private void Build()
        {
            _book.gutter = Mathf.Min(_book.gutter, MinGutter);
            _coverBox = _book.cover != null ? _book.cover.GetComponent<Renderer>() : null;
            // La misma instancia de material que BookCover pinta (cuero o el color de la portada).
            _leather = _coverBox != null ? _coverBox.material : null;
            if (_coverBox != null) _coverBox.enabled = false;

            _edgeTexture = Own(EdgeTexture());
            if (_leather != null)
            {
                _paperEdge = Own(new Material(_leather) { name = "Canto de las hojas" });
                _paperEdge.mainTexture = _edgeTexture;
                _paperEdge.mainTextureScale = Vector2.one;
                _paperEdge.color = new Color(0.96f, 0.94f, 0.88f);
                if (_paperEdge.HasProperty("_Smoothness")) _paperEdge.SetFloat("_Smoothness", 0.1f);
            }
            for (int i = 0; i < 2; i++)
            {
                _halves[i] = new GameObject(i == 0 ? "Mitad izquierda" : "Mitad derecha").transform;
                _halves[i].SetParent(transform, false);
                _boards[i] = Own(NewMesh("Tapa"));
                _blocks[i] = Own(NewMesh("Bloque de hojas"));
                _art[i] = Own(NewMesh("Arte de la tapa"));
                Part("Tapa", _boards[i], _leather, _halves[i], true);
                Part("Bloque de hojas", _blocks[i], _paperEdge, _halves[i], true);
            }
            _spine = Own(NewMesh("Lomo"));
            _spineRenderer = Part("Lomo", _spine, _leather, transform, true);

            // Hojas curvadas: la malla vive en el espacio de la página (escala ancho × alto × 1).
            _leftPage = Own(NewMesh("Hoja izquierda curvada"));
            _rightPage = Own(NewMesh("Hoja derecha curvada"));
            SetPageMesh(_book.leftPage, _leftPage);
            SetPageMesh(_book.rightPage, _rightPage);

            Shader text = Resources.Load<Shader>("FolioXRText3D");
            if (text != null && text.isSupported)
            {
                _shadeTexture = Own(ShadeTexture());
                _shade = Own(new Material(text) { name = "Sombra del pliegue", mainTexture = _shadeTexture });
                _shade.color = new Color(0.05f, 0.04f, 0.03f, 0.6f);
                _leftShadeMesh = Own(NewMesh("Sombra izquierda"));
                _rightShadeMesh = Own(NewMesh("Sombra derecha"));
                _leftOverlay = Overlay(_book.leftPage, _leftShadeMesh);
                _rightOverlay = Overlay(_book.rightPage, _rightShadeMesh);
                _crease = Own(new Material(text) { name = "Fondo del pliegue", mainTexture = Texture2D.whiteTexture });
                _crease.color = new Color(0.07f, 0.06f, 0.05f, 0.9f);
                _creaseMesh = Own(NewMesh("Fondo del pliegue"));
                _creaseRenderer = Part("Fondo del pliegue", _creaseMesh, _crease, transform, false);
            }
            _book.SpreadShown += OnSpreadShown;
            _built = true;
            Layout();
        }

        private void OnSpreadShown(int left, int count) => Layout();

        private static Mesh NewMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            return mesh;
        }

        private T Own<T>(T item) where T : Object { _owned.Add(item); return item; }

        private Renderer Part(string name, Mesh mesh, Material material, Transform parent, bool shadows)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
            return renderer;
        }

        private static void SetPageMesh(Renderer page, Mesh mesh)
        {
            if (page == null) return;
            MeshFilter filter = page.GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = mesh;
        }

        private Renderer Overlay(Renderer page, Mesh mesh)
        {
            if (page == null) return null;
            var item = new GameObject("Sombra del pliegue");
            item.transform.SetParent(page.transform, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _shade;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        // ───────────────────────── Medidas ─────────────────────────

        /// <summary>Vuelve a medir el libro (tamaño de página, cantidad de páginas, avance, tapa) y lo arma.</summary>
        internal void Layout()
        {
            if (!_built || _book == null || _book.leftPage == null) return;
            if (_book.gutter > MinGutter) _book.gutter = MinGutter;
            Vector3 page = _book.leftPage.transform.localScale;
            int pages = Mathf.Max(2, _book.PageCount);
            int binding = BindingFor(_book.DocumentId);
            bool hard = binding == 1 || (binding == 0 && !PdfPageRenderer.IsComic(_book.DocumentPath) && pages >= 150);
            _shape = BookGeometry.Measure(page.x, page.y, _book.gutter, pages, _book.LeftPageIndex, _book.RightToLeft, hard);
            _baseCurlL = BookGeometry.OpenCurl(_shape.BlockL, hard);
            _baseCurlR = BookGeometry.OpenCurl(_shape.BlockR, hard);
            _builtCurl = -1f;   // Fuerza a rearmar todo.
            if (_book.Cover != null) _book.Cover.AttachFront(_halves[RestSide > 0 ? 0 : 1], _halves[RestSide > 0 ? 1 : 0]);
            ApplyFold();
        }

        // ───────────────────────── Abrir y cerrar ─────────────────────────

        /// <summary>Cierra o abre el libro (animado salvo instant o «reducir movimiento»).</summary>
        public void SetClosed(bool closed, bool instant = false)
        {
            Closed = closed;
            if (instant || FolioXR.Accessibility.FolioAccessibility.ReduceMotion) _fold = closed ? 1f : 0f;
            ApplyFold();
        }

        private void Update()
        {
            float target = Closed ? 1f : 0f;
            if (!Mathf.Approximately(_fold, target))
            {
                _fold = Mathf.MoveTowards(_fold, target, Time.deltaTime / FoldSeconds);
                ApplyFold();
            }
            else if (_contentsHidden && Time.unscaledTime >= _nextSweep) HideContents(true);
        }

        /// <summary>Hundimiento actual de la hoja (metros a escala 1) a una distancia f del lomo.</summary>
        public float PageSurface(int side, float f) => BookGeometry.PageZ(_shape, side, f);

        /// <summary>Con el libro cerrado: profundidad de la cara de afuera de la portada, desde el plano de las hojas.</summary>
        public float CoverOuterDepth
        {
            get
            {
                int front = -RestSide;
                return (_shape.Hard ? _shape.HardBack(front) : _shape.Block(front)) + _shape.Board;
            }
        }

        private void ApplyFold()
        {
            if (!_built || _halves[0] == null) return;
            int rest = RestSide;
            float s = Mathf.SmoothStep(0f, 1f, _fold);
            float flat = 1f - s;
            // Las hojas se aplanan al cerrar; con el libro abierto vuelven a curvarse.
            if (Mathf.Abs(flat - _builtCurl) > 0.004f || _builtCurl < 0f)
            {
                _shape.CurlL = _baseCurlL * flat;
                _shape.CurlR = _baseCurlR * flat;
                BuildMeshes();
                _builtCurl = flat;
            }
            // Abierto: cada mitad sube TiltDegrees. Cerrado: la mitad de la portada da la vuelta sobre la otra,
            // que queda plana mirando a quien lee.
            BookGeometry.HalfAngles(_fold, rest, out float leftAngle, out float rightAngle);
            _halves[0].localRotation = BookGeometry.HalfRotation(-1, leftAngle);
            _halves[1].localRotation = BookGeometry.HalfRotation(1, rightAngle);
            PlacePage(_book.leftPage, -1);
            PlacePage(_book.rightPage, 1);
            BuildSpine();
            PlaceCrease(flat);
            PlaceCollider(s);
            if (_book.Cover != null) _book.Cover.PlaceTitle(CoverCenterFor(-rest, out float w, out float h), w, h);
            HideContents(_fold >= 0.999f);
        }

        private void PlacePage(Renderer page, int side)
        {
            if (page == null) return;
            Quaternion r = _halves[side < 0 ? 0 : 1].localRotation;
            page.transform.localRotation = r;
            page.transform.localPosition = r * new Vector3(side * (_shape.Gutter * 0.5f + _shape.W * 0.5f), 0f, 0f);
        }

        private void BuildMeshes()
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                BookGeometry.Board(_data, _shape, side); Apply(_data, _boards[i]);
                BookGeometry.Block(_data, _shape, side); Apply(_data, _blocks[i]);
            }
            BookGeometry.Page(_data, true, 0f, 1f, 0f, _shape.CurlL, _shape.Reach, false); Apply(_data, _leftPage);
            BookGeometry.Page(_data, false, 0f, 1f, 0f, _shape.CurlR, _shape.Reach, false); Apply(_data, _rightPage);
            if (_leftShadeMesh != null)
            {
                BookGeometry.Page(_data, true, 0f, 0.36f, -0.0004f, _shape.CurlL, _shape.Reach, true); Apply(_data, _leftShadeMesh, true);
                BookGeometry.Page(_data, false, 0f, 0.36f, -0.0004f, _shape.CurlR, _shape.Reach, true); Apply(_data, _rightShadeMesh, true);
            }
            // Arte de las tapas: la portada en la mitad que se pliega, la contraportada en la otra.
            if (_book.Cover != null)
            {
                int front = -RestSide;
                bool rtl = _book.RightToLeft;
                BookGeometry.CoverArt(_data, _shape, front, !rtl); Apply(_data, _art[front < 0 ? 0 : 1]);
                BookGeometry.CoverArt(_data, _shape, -front, rtl); Apply(_data, _art[front < 0 ? 1 : 0]);
                _book.Cover.SetArtMeshes(_art[front < 0 ? 0 : 1], _art[front < 0 ? 1 : 0]);
            }
        }

        private static readonly List<Color32> White = new List<Color32>();

        /// <summary>colors: blanco por vértice para el sombreador de texto (la sombra y el pliegue lo multiplican).</summary>
        private static void Apply(MeshData data, Mesh mesh, bool colors = false)
        {
            if (mesh == null) return;
            mesh.Clear();
            mesh.SetVertices(data.V);
            mesh.SetUVs(0, data.UV);
            if (colors)
            {
                while (White.Count < data.V.Count) White.Add(new Color32(255, 255, 255, 255));
                if (White.Count > data.V.Count) White.RemoveRange(data.V.Count, White.Count - data.V.Count);
                mesh.SetColors(White);
            }
            mesh.SetTriangles(data.T, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>Lomo: une los bordes de las dos tapas por detrás (abierto) o por el canto (cerrado).</summary>
        private void BuildSpine()
        {
            BookGeometry.SpineFor(_data, _shape, _halves[0].localRotation, _halves[1].localRotation,
                                  out Vector3 middle, out Vector3 outward, out float thickness);
            Apply(_data, _spine);
            if (_book.Cover != null)
                _book.Cover.PlaceSpineLabel(middle + outward * 0.0004f, outward, _shape.H * 0.85f, thickness);
        }

        private void PlaceCrease(float flat)
        {
            if (_creaseRenderer == null) return;
            float g = _shape.Gutter * 0.5f + 0.0006f, y = _shape.H * 0.5f;
            float z = Mathf.Min(_shape.CurlL, _shape.CurlR) + 0.0002f;
            _data.Clear();
            _data.Quad(new Vector3(-g, -y, z), new Vector3(g, -y, z), new Vector3(g, y, z), new Vector3(-g, y, z),
                       Vector2.zero, Vector2.right, Vector2.one, Vector2.up, new Vector3(0f, 0f, -1f));
            Apply(_data, _creaseMesh, true);
            _creaseRenderer.enabled = flat > 0.05f;
        }

        /// <summary>Colisionador del libro: abierto cubre las dos mitades (con la V); cerrado, solo el libro cerrado.</summary>
        private void PlaceCollider(float s)
        {
            BoxCollider box = _book.bookCollider;
            if (box == null) return;
            float side = _shape.Gutter * 0.5f + _shape.W + _shape.Overhang, height = _shape.H + 2f * _shape.Overhang;
            float deepest = Mathf.Max(_shape.Hard ? _shape.HardBack(-1) : _shape.BlockL, _shape.Hard ? _shape.HardBack(1) : _shape.BlockR)
                            + _shape.Board + 0.006f;
            if (s < 0.5f)
            {
                float tilt = TiltDegrees * Mathf.Deg2Rad;
                float front = -Mathf.Sin(tilt) * side - 0.004f;
                box.center = new Vector3(0f, 0f, (front + deepest) * 0.5f);
                box.size = new Vector3(2f * side * Mathf.Cos(tilt) + 0.01f, height, deepest - front);
            }
            else
            {
                int rest = RestSide;
                float frontDepth = CoverOuterDepth;
                float restDepth = (_shape.Hard ? _shape.HardBack(rest) : _shape.Block(rest)) + _shape.Board;
                float width = _shape.W + _shape.Overhang;
                box.center = new Vector3(rest * (_shape.Gutter * 0.5f + width * 0.5f), 0f, (restDepth - frontDepth) * 0.5f);
                box.size = new Vector3(width + 0.004f, height, frontDepth + restDepth + 0.006f);
            }
        }

        /// <summary>Centro de la cara de afuera de una tapa (espacio de su mitad) y su tamaño.</summary>
        internal Vector3 CoverCenterFor(int side, out float width, out float height) =>
            BookGeometry.CoverCenter(_shape, side, out width, out height);

        /// <summary>
        /// Con el libro del todo cerrado no se ven páginas, trazos ni marcadores (quedan adentro).
        /// forceRenderingOff no toca «enabled», que sigue siendo de quien dibuja la página.
        /// </summary>
        private void HideContents(bool hide)
        {
            _nextSweep = Time.unscaledTime + 0.5f;
            if (!hide && !_contentsHidden) return;
            foreach (Renderer r in _hidden) if (r != null) r.forceRenderingOff = false;
            _hidden.Clear();
            _contentsHidden = hide;
            if (!hide) return;
            Transform root = _book.leftPage != null ? _book.leftPage.transform.parent : transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == _halves[0] || child == _halves[1] || child == _spineRenderer.transform ||
                    (_creaseRenderer != null && child == _creaseRenderer.transform) ||
                    (_book.Cover != null && _book.Cover.Owns(child)) ||
                    (_book.statusText != null && child == _book.statusText.transform)) continue;
                foreach (Renderer r in child.GetComponentsInChildren<Renderer>(true))
                {
                    r.forceRenderingOff = true;
                    _hidden.Add(r);
                }
            }
        }

        // La sombra del pliegue aparece y desaparece con su hoja.
        private void LateUpdate()
        {
            if (_leftOverlay != null) _leftOverlay.enabled = _book.leftPage != null && _book.leftPage.enabled && !_contentsHidden;
            if (_rightOverlay != null) _rightOverlay.enabled = _book.rightPage != null && _book.rightPage.enabled && !_contentsHidden;
        }

        /// <summary>Canto del bloque: líneas finas de papel (u a lo largo, v a través de las hojas).</summary>
        private static Texture2D EdgeTexture()
        {
            const int width = 64, height = 64;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            { name = "Canto de papel", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var pixels = new Color32[width * height];
            var random = new System.Random(5);
            var row = new float[height];
            for (int y = 0; y < height; y++) row[y] = 0.91f + (float)random.NextDouble() * 0.08f - ((y % 4 == 0) ? 0.1f : 0f);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float v = row[y] - (float)random.NextDouble() * 0.025f;
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                    pixels[y * width + x] = new Color32(b, b, b, 255);
                }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>Degradado de la sombra del pliegue: oscuro junto al lomo, nada a un tercio del ancho.</summary>
        private static Texture2D ShadeTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, 1, TextureFormat.RGBA32, false)
            { name = "Sombra del pliegue", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size];
            for (int i = 0; i < size; i++)
            {
                float t = i / (float)(size - 1);
                // Pliegue muy oscuro en el primer milímetro y luego una caída suave, como luz rasante.
                float a = 0.75f * Mathf.Pow(1f - t, 2.2f) + 0.25f * Mathf.Pow(1f - t, 12f);
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            if (_book != null) _book.SpreadShown -= OnSpreadShown;
            foreach (Renderer r in _hidden) if (r != null) r.forceRenderingOff = false;
            foreach (Object item in _owned) if (item != null) Destroy(item);
            if (_coverBox != null) _coverBox.enabled = true;
        }
    }
}
