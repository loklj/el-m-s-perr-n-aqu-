using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Reading
{
    /// <summary>
    /// Cuerpo de libro de verdad (pedido del usuario, 13 nov 2026: «parece una tabla para cortar verduras»).
    /// Reemplaza la caja de la tapa por: dos tapas duras de cartón forradas que sobresalen 3 mm, lomo
    /// redondeado, bloques de hojas con canto de papel, hojas que se curvan hacia el pliegue y sombra en
    /// el pliegue. Las páginas siguen siendo los mismos objetos (tinta, marcadores y búsqueda no cambian):
    /// solo cambia su malla, que se hunde unos milímetros cerca del lomo.
    /// Ejes locales del libro: las páginas miran hacia -Z (z = 0); la tapa queda detrás (+Z).
    /// </summary>
    public sealed class BookBody : MonoBehaviour
    {
        public const float Block = 0.011f;      // Grosor del bloque de hojas.
        public const float Board = 0.0025f;     // Grosor de cada tapa.
        public const float Overhang = 0.003f;   // Lo que la tapa sobresale de las hojas.
        public const float SpineBulge = 0.0045f;
        public const float CurlDepth = 0.009f;  // Cuánto se hunde la hoja junto al lomo.
        public const float CurlReach = 0.2f;   // Fracción del ancho de página que se curva.
        public static float BackFace => Block + Board;

        private FolioBook _book;
        private Renderer _coverBox;
        private Mesh _spine, _closedSpine, _leftCurl, _rightCurl, _leftShade, _rightShade;
        private readonly Mesh[] _boards = new Mesh[2], _blocks = new Mesh[2];
        private readonly Transform[] _halves = new Transform[2];   // 0 izquierda, 1 derecha.
        private Renderer _spineRenderer, _closedSpineRenderer;
        private float _fold, _nextSweep;                           // 0 abierto, 1 cerrado.
        private bool _contentsHidden;
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private const float FoldSeconds = 0.4f;

        /// <summary>Libro cerrado: la mitad de la portada se pliega sobre la otra y queda la tapa a la vista.</summary>
        public bool Closed { get; private set; }
        public bool Folding => _fold > 0.001f && _fold < 0.999f;
        /// <summary>Lado que no se mueve al cerrar (+1 derecha, -1 izquierda): ahí queda el libro cerrado.</summary>
        public int RestSide => _book != null && _book.RightToLeft ? -1 : 1;
        private Material _paperEdge, _shade;
        private Texture2D _edgeTexture, _shadeTexture;
        private readonly List<Object> _owned = new List<Object>();
        private Renderer _leftOverlay, _rightOverlay;

        internal static BookBody Attach(FolioBook book)
        {
            var body = book.gameObject.AddComponent<BookBody>();
            body._book = book;
            body.Build();
            return body;
        }

        private void Build()
        {
            _coverBox = _book.cover != null ? _book.cover.GetComponent<Renderer>() : null;
            // La misma instancia de material que BookCover pinta de cuero (color y grano por libro).
            Material leather = _coverBox != null ? _coverBox.material : null;
            if (_coverBox != null) _coverBox.enabled = false;

            _edgeTexture = Own(EdgeTexture());
            if (leather != null)
            {
                _paperEdge = Own(new Material(leather) { name = "Canto de las hojas" });
                _paperEdge.mainTexture = _edgeTexture;
                _paperEdge.mainTextureScale = Vector2.one;
                _paperEdge.color = new Color(0.96f, 0.93f, 0.86f);
                if (_paperEdge.HasProperty("_Smoothness")) _paperEdge.SetFloat("_Smoothness", 0.15f);
            }
            _spine = Own(new Mesh { name = "Lomo" });
            _closedSpine = Own(new Mesh { name = "Lomo cerrado" });
            _spineRenderer = Part("Lomo", _spine, leather, transform);
            _closedSpineRenderer = Part("Lomo cerrado", _closedSpine, leather, transform);
            for (int i = 0; i < 2; i++)
            {
                _halves[i] = new GameObject(i == 0 ? "Mitad izquierda" : "Mitad derecha").transform;
                _halves[i].SetParent(transform, false);
                _boards[i] = Own(new Mesh { name = "Tapa dura" });
                _blocks[i] = Own(new Mesh { name = "Bloque de hojas" });
                Part("Tapa dura", _boards[i], leather, _halves[i]);
                Part("Bloque de hojas", _blocks[i], _paperEdge, _halves[i]);
            }

            // Hojas curvadas: la malla vive en el espacio de la página (escala ancho × alto × 1).
            _leftCurl = Own(PageMesh(true, 0f, 1f, 0f));
            _rightCurl = Own(PageMesh(false, 0f, 1f, 0f));
            SetPageMesh(_book.leftPage, _leftCurl);
            SetPageMesh(_book.rightPage, _rightCurl);

            Shader text = Resources.Load<Shader>("FolioXRText3D");
            if (text != null && text.isSupported)
            {
                _shadeTexture = Own(ShadeTexture());
                _shade = Own(new Material(text) { name = "Sombra del pliegue", mainTexture = _shadeTexture });
                _shade.color = new Color(0.06f, 0.045f, 0.03f, 0.62f);
                _leftShade = Own(PageMesh(true, 0f, 0.3f, -0.0004f, true));
                _rightShade = Own(PageMesh(false, 0f, 0.3f, -0.0004f, true));
                _leftOverlay = Overlay(_book.leftPage, _leftShade);
                _rightOverlay = Overlay(_book.rightPage, _rightShade);
            }
            Layout();
        }

        private T Own<T>(T item) where T : Object { _owned.Add(item); return item; }

        private Renderer Part(string name, Mesh mesh, Material material, Transform parent)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return renderer;
        }

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

        private void ApplyFold()
        {
            if (_halves[0] == null) return;
            int rest = RestSide;
            float angle = Mathf.SmoothStep(0f, 1f, _fold) * 180f;
            // La mitad de la portada gira sobre el lomo pasando por delante (hacia quien lee).
            _halves[0].localRotation = rest > 0 ? Quaternion.Euler(0f, -angle, 0f) : Quaternion.identity;
            _halves[1].localRotation = rest < 0 ? Quaternion.Euler(0f, angle, 0f) : Quaternion.identity;
            bool shut = _fold >= 0.5f;
            if (_spineRenderer != null) _spineRenderer.enabled = !shut;
            if (_closedSpineRenderer != null) _closedSpineRenderer.enabled = shut;
            if (_book.Cover != null) _book.Cover.PlaceSpine(shut, rest);
            HideContents(_fold > 0.001f);
        }

        /// <summary>
        /// Con el libro plegado no se ven páginas, trazos ni marcadores (todo lo que cuelga del libro y no
        /// es tapa ni cuerpo). forceRenderingOff no toca «enabled», que sigue siendo de quien dibuja la página.
        /// </summary>
        private void HideContents(bool hide)
        {
            _nextSweep = Time.unscaledTime + 0.5f;
            foreach (Renderer r in _hidden) if (r != null) r.forceRenderingOff = false;
            _hidden.Clear();
            _contentsHidden = hide;
            if (!hide) return;
            Transform root = _book.leftPage != null ? _book.leftPage.transform.parent : transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == _halves[0] || child == _halves[1] || child == _spineRenderer.transform ||
                    child == _closedSpineRenderer.transform || (_book.Cover != null && _book.Cover.Owns(child)) ||
                    (_book.statusText != null && child == _book.statusText.transform)) continue;
                foreach (Renderer r in child.GetComponentsInChildren<Renderer>(true))
                {
                    r.forceRenderingOff = true;
                    _hidden.Add(r);
                }
            }
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

        // La sombra del pliegue aparece y desaparece con su hoja.
        private void LateUpdate()
        {
            if (_leftOverlay != null) _leftOverlay.enabled = _book.leftPage != null && _book.leftPage.enabled && !_contentsHidden;
            if (_rightOverlay != null) _rightOverlay.enabled = _book.rightPage != null && _book.rightPage.enabled && !_contentsHidden;
        }

        /// <summary>Hundimiento (en metros, hacia +Z) a una distancia del lomo dada como fracción del ancho.</summary>
        public static float Curl(float fromGutter)
        {
            if (fromGutter >= CurlReach) return 0f;
            float t = 1f - Mathf.Max(0f, fromGutter) / CurlReach;
            return CurlDepth * t * t;
        }

        /// <summary>
        /// Malla de página en [-0,5; 0,5]² con el mismo sentido que el Quad de Unity (mira hacia -Z).
        /// Solo cubre la franja [f0, f1] medida desde el lomo; shade = u recorre esa franja (sombra).
        /// </summary>
        internal static Mesh PageMesh(bool left, float f0, float f1, float lift, bool shade = false)
        {
            const int columns = 40;
            var vertices = new Vector3[(columns + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[columns * 6];
            for (int i = 0; i <= columns; i++)
            {
                float f = Mathf.Lerp(f0, f1, i / (float)columns);   // Distancia al lomo (fracción del ancho).
                float x = left ? 0.5f - f : f - 0.5f;
                float z = Curl(f) + lift;
                vertices[i * 2] = new Vector3(x, -0.5f, z);
                vertices[i * 2 + 1] = new Vector3(x, 0.5f, z);
                float u = shade ? (f - f0) / Mathf.Max(0.0001f, f1 - f0) : x + 0.5f;
                uv[i * 2] = new Vector2(u, 0f);
                uv[i * 2 + 1] = new Vector2(u, 1f);
            }
            for (int i = 0; i < columns; i++)
            {
                // a/b: abajo, c/d: arriba; a está a la izquierda (x menor) de b.
                int p = i * 2, q = (i + 1) * 2;
                int a = left ? q : p, b = left ? p : q;
                int c = a + 1, d = b + 1;
                int k = i * 6;
                triangles[k] = a; triangles[k + 1] = d; triangles[k + 2] = b;
                triangles[k + 3] = d; triangles[k + 4] = a; triangles[k + 5] = c;
            }
            var mesh = new Mesh { name = left ? "Hoja izquierda curvada" : "Hoja derecha curvada" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1.02f, 1.02f, 0.04f));
            return mesh;
        }

        /// <summary>Vuelve a armar tapas, lomo y bloques con el tamaño actual de las páginas.</summary>
        internal void Layout()
        {
            if (_book == null || _book.leftPage == null) return;
            Vector3 page = _book.leftPage.transform.localScale;
            float w = page.x, h = page.y, g = _book.gutter;
            BuildBoards(w, h, g);
            BuildSpine(h, g);
            BuildBlocks(w, h, g);
            BuildClosedSpine(h, g);
            // La portada (imagen o título) viaja con la mitad que se pliega.
            if (_book.Cover != null) _book.Cover.AttachFront(_halves[RestSide > 0 ? 0 : 1], _halves[RestSide > 0 ? 1 : 0]);
            ApplyFold();
        }

        /// <summary>Lomo del libro cerrado: une las dos tapas, una sobre otra, por el lado del pliegue.</summary>
        private void BuildClosedSpine(float h, float g)
        {
            var mesh = new MeshBuilder();
            float y = h * 0.5f + Overhang, edge = g * 0.5f + 0.0015f, depth = Block + Board;
            int rest = RestSide;
            float x0 = rest > 0 ? edge - Board - 0.001f : -edge, x1 = rest > 0 ? edge : -edge + Board + 0.001f;
            mesh.Box(x0, x1, -y, y, -depth, depth, 2.5f);
            mesh.Apply(_closedSpine);
        }

        private void BuildBoards(float w, float h, float g)
        {
            float y = h * 0.5f + Overhang;
            for (int side = -1; side <= 1; side += 2)
            {
                var mesh = new MeshBuilder();
                float inner = side * (g * 0.5f + 0.0015f), outer = side * (g * 0.5f + w + Overhang);
                mesh.Box(Mathf.Min(inner, outer), Mathf.Max(inner, outer), -y, y, Block, Block + Board, 2.5f);
                mesh.Apply(_boards[side < 0 ? 0 : 1]);
            }
        }

        private void BuildSpine(float h, float g)
        {
            // Arco que une las dos tapas por detrás del pliegue, abombado hacia afuera.
            var mesh = new MeshBuilder();
            const int steps = 12;
            float half = g * 0.5f + 0.0025f, y = h * 0.5f + Overhang;
            var outer = new Vector2[steps + 1];
            var inner = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.PI * i / steps;
                float x = -Mathf.Cos(a) * half;
                float bulge = Mathf.Sin(a) * SpineBulge;
                outer[i] = new Vector2(x, Block + Board + bulge);
                inner[i] = new Vector2(x * 0.92f, Block + bulge * 0.6f);
            }
            for (int i = 0; i < steps; i++)
            {
                float u0 = i / (float)steps, u1 = (i + 1) / (float)steps;
                mesh.Quad(new Vector3(outer[i].x, -y, outer[i].y), new Vector3(outer[i + 1].x, -y, outer[i + 1].y),
                          new Vector3(outer[i + 1].x, y, outer[i + 1].y), new Vector3(outer[i].x, y, outer[i].y),
                          new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 3), new Vector2(u0, 3));
                // Tapas de los extremos del lomo (arriba y abajo).
                for (int end = -1; end <= 1; end += 2)
                {
                    float ye = end * y;
                    var p = new[] { new Vector3(inner[i].x, ye, inner[i].y), new Vector3(inner[i + 1].x, ye, inner[i + 1].y),
                                    new Vector3(outer[i + 1].x, ye, outer[i + 1].y), new Vector3(outer[i].x, ye, outer[i].y) };
                    if (end < 0) mesh.Quad(p[0], p[1], p[2], p[3], Vector2.zero, Vector2.right * 0.1f, Vector2.one * 0.1f, Vector2.up * 0.1f);
                    else mesh.Quad(p[3], p[2], p[1], p[0], Vector2.zero, Vector2.right * 0.1f, Vector2.one * 0.1f, Vector2.up * 0.1f);
                }
            }
            // Fondo del pliegue entre las dos hojas (en sombra): sin él se vería a través del libro.
            float gh = g * 0.5f + 0.002f, crease = CurlDepth + 0.0004f;
            mesh.Quad(new Vector3(-gh, -y, crease), new Vector3(-gh, y, crease), new Vector3(gh, y, crease), new Vector3(gh, -y, crease),
                      Vector2.zero, Vector2.up * 0.1f, Vector2.one * 0.1f, Vector2.right * 0.1f);
            mesh.Apply(_spine);
        }

        private void BuildBlocks(float w, float h, float g)
        {
            float y = h * 0.5f;
            const int steps = 16;
            float lines = Block / 0.00035f; // Una línea cada ~0,35 mm, como el canto de un libro impreso.
            for (int side = -1; side <= 1; side += 2)
            {
                var mesh = new MeshBuilder();
                float gx = side * g * 0.5f, ox = side * (g * 0.5f + w);
                // Canto delantero (lado opuesto al lomo): plano, de z = 0 a z = Block.
                Vector3 a = new Vector3(ox, -y, 0f), b = new Vector3(ox, y, 0f), c = new Vector3(ox, y, Block), d = new Vector3(ox, -y, Block);
                if (side > 0) mesh.Quad(a, b, c, d, new Vector2(0, 0), new Vector2(h * 40f, 0), new Vector2(h * 40f, lines), new Vector2(0, lines));
                else mesh.Quad(d, c, b, a, new Vector2(0, lines), new Vector2(h * 40f, lines), new Vector2(h * 40f, 0), new Vector2(0, 0));
                // Cantos de arriba y abajo: siguen la curva de las hojas junto al lomo.
                for (int i = 0; i < steps; i++)
                {
                    float f0 = i / (float)steps, f1 = (i + 1) / (float)steps;
                    float x0 = Mathf.Lerp(gx, ox, f0), x1 = Mathf.Lerp(gx, ox, f1);
                    float z0 = Curl(f0), z1 = Curl(f1);
                    for (int end = -1; end <= 1; end += 2)
                    {
                        float ye = end * y;
                        var p0 = new Vector3(x0, ye, z0); var p1 = new Vector3(x1, ye, z1);
                        var p2 = new Vector3(x1, ye, Block); var p3 = new Vector3(x0, ye, Block);
                        Vector2 t0 = new Vector2(f0 * w * 40f, lines * z0 / Block), t1 = new Vector2(f1 * w * 40f, lines * z1 / Block);
                        Vector2 t2 = new Vector2(f1 * w * 40f, lines), t3 = new Vector2(f0 * w * 40f, lines);
                        bool flip = (side > 0) != (end > 0);
                        if (flip) mesh.Quad(p0, p1, p2, p3, t0, t1, t2, t3);
                        else mesh.Quad(p3, p2, p1, p0, t3, t2, t1, t0);
                    }
                }
                // Fondo del pliegue: del borde hundido de la hoja hasta la tapa.
                Vector3 e = new Vector3(gx, -y, CurlDepth), f = new Vector3(gx, y, CurlDepth), k = new Vector3(gx, y, Block), m = new Vector3(gx, -y, Block);
                if (side < 0) mesh.Quad(e, f, k, m, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                else mesh.Quad(m, k, f, e, Vector2.up, Vector2.one, Vector2.right, Vector2.zero);
                // Hoja de papel bajo la página (misma curva): es lo que se ve mientras el libro se pliega.
                Vector2 plain = new Vector2(0.5f, 0.6f);
                for (int i = 0; i < steps; i++)
                {
                    float f0 = i / (float)steps, f1 = (i + 1) / (float)steps;
                    float x0 = Mathf.Lerp(gx, ox, f0), x1 = Mathf.Lerp(gx, ox, f1);
                    var p0 = new Vector3(x0, -y, Curl(f0) + 0.0007f); var p1 = new Vector3(x0, y, Curl(f0) + 0.0007f);
                    var p2 = new Vector3(x1, y, Curl(f1) + 0.0007f); var p3 = new Vector3(x1, -y, Curl(f1) + 0.0007f);
                    if (side > 0) mesh.Quad(p0, p1, p2, p3, plain, plain, plain, plain);
                    else mesh.Quad(p3, p2, p1, p0, plain, plain, plain, plain);
                }
                mesh.Apply(_blocks[side < 0 ? 0 : 1]);
            }
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
            for (int y = 0; y < height; y++) row[y] = 0.9f + (float)random.NextDouble() * 0.1f - ((y % 4 == 0) ? 0.12f : 0f);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float v = row[y] - (float)random.NextDouble() * 0.03f;
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                    pixels[y * width + x] = new Color32(b, b, b, 255);
                }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>Degradado de la sombra del pliegue: oscuro junto al lomo, nada a un 30 % del ancho.</summary>
        private static Texture2D ShadeTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, 1, TextureFormat.RGBA32, false)
            { name = "Sombra del pliegue", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size];
            for (int i = 0; i < size; i++)
            {
                float t = i / (float)(size - 1);
                float a = Mathf.Pow(1f - t, 2.6f);
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            foreach (Renderer r in _hidden) if (r != null) r.forceRenderingOff = false;
            foreach (Object item in _owned) if (item != null) Destroy(item);
            if (_coverBox != null) _coverBox.enabled = true;
        }

        /// <summary>Acumula cuadriláteros y los vuelca en una malla.</summary>
        private sealed class MeshBuilder
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int> _t = new List<int>();

            /// <summary>Cuadrilátero a, b, c, d en sentido horario visto desde el lado que se ve.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
                _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }

            /// <summary>Caja con las seis caras hacia afuera; tiling = repeticiones del grano por cara.</summary>
            public void Box(float x0, float x1, float y0, float y1, float z0, float z1, float tiling)
            {
                Vector3 p000 = new Vector3(x0, y0, z0), p100 = new Vector3(x1, y0, z0), p110 = new Vector3(x1, y1, z0), p010 = new Vector3(x0, y1, z0);
                Vector3 p001 = new Vector3(x0, y0, z1), p101 = new Vector3(x1, y0, z1), p111 = new Vector3(x1, y1, z1), p011 = new Vector3(x0, y1, z1);
                Vector2 o = Vector2.zero, r = new Vector2(tiling, 0), u = new Vector2(0, tiling), ru = new Vector2(tiling, tiling);
                Quad(p000, p010, p110, p100, o, u, ru, r);   // -Z (cara interior)
                Quad(p101, p111, p011, p001, o, u, ru, r);   // +Z (cara exterior)
                Quad(p001, p011, p010, p000, o, u, ru, r);   // -X
                Quad(p100, p110, p111, p101, o, u, ru, r);   // +X
                Quad(p010, p011, p111, p110, o, u, ru, r);   // +Y
                Quad(p001, p000, p100, p101, o, u, ru, r);   // -Y
            }

            public void Apply(Mesh mesh)
            {
                mesh.Clear();
                mesh.SetVertices(_v);
                mesh.SetUVs(0, _uv);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }
    }
}
