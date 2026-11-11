using System;
using System.Collections.Generic;
using System.Text;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Menu
{
    /// <summary>Lo que el controlador le entrega a la esfera en cada cuadro mientras está abierta.</summary>
    internal struct SphereInput
    {
        internal Transform Head, Space;
        internal bool AnchorValid;
        internal Vector3 Anchor;   // Mando izquierdo o centro de la palma izquierda.
        internal bool TipValid;
        internal Vector3 Tip;      // Punta del índice derecho.
        internal float StickX;     // Stick de cualquier mando libre.
        internal bool Select;      // Flanco del gatillo.
    }

    /// <summary>
    /// Menú esférico (docs/MENU_ESFERICO.md), escrito por Claude sobre el mismo MenuModel del panel.
    /// Frente = -Z de la raíz, que siempre mira a la cabeza. Dos anillos rígidos: categorías y opciones.
    /// Al elegir una categoría toda la esfera gira 52° hacia arriba: las categorías quedan arriba,
    /// atenuadas, y el anillo de opciones sube al ecuador.
    /// 6 draw calls: cuerpo, casillas y textos de cada anillo, y el marco del frente.
    /// Las mallas solo se rehacen cuando cambia su contenido; al girar se mueven transformaciones.
    /// </summary>
    public sealed class SphereMenuView : MonoBehaviour, IMenuView
    {
        private const float Radius = 0.07f, TileRadius = 0.0715f, IconRadius = 0.0719f, LabelRadius = 0.0722f,
                            FrameRadius = 0.0726f;
        private const float Step = 72f, HalfWidth = 31f, HalfHeight = 21f, Tilt = 52f;
        private const float AnchorHeight = 0.13f, OpenSeconds = 0.2f, LevelSeconds = 0.3f;
        private const int FontSize = 64;
        private const int Normal = 0, Marked = 1, Disabled = 2, White = 3, Frame = 4, BackTile = 5;
        private static readonly char[] Separators = { ' ', '\n', '\r' };
        private static readonly Color32 TextColor = new Color32(255, 255, 255, 255);
        private static readonly Color32 DisabledText = new Color32(150, 160, 185, 255);

        private sealed class Ring
        {
            internal Transform Tilt, Spin;
            internal Mesh Tiles, Labels;
            internal MeshRenderer TilesRenderer, LabelsRenderer;
            internal int Slots = 5;                 // Posiciones lógicas de 72° (5 o más).
            internal int[] SlotTile = new int[5];   // Casilla de cada posición, o -1 si está vacía.
            internal float Scroll, Velocity, Target; // Grados; Scroll = posición lógica que está al frente.
            internal bool Snapping;
            internal int BaseSlot, Front;
            internal readonly List<int> BuiltSlots = new List<int>(5);
        }

        /// <summary>Clic del menú; true si vino de un mando (para vibrar).</summary>
        internal Action<bool> Feedback;
        public bool ShowingOptions => _level == 1;

        private MenuModel _model;
        private Font _font;
        private Transform _root, _body, _frame;
        private readonly Ring _categories = new Ring(), _options = new Ring();
        private Material _bodyMaterial, _categoryMaterial, _categoryTextMaterial, _optionMaterial;
        private Texture2D _bodyTexture, _palette;
        private Mesh _bodyMesh, _frameMesh;
        private readonly List<Vector3> _vertices = new List<Vector3>(), _textVertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>(), _textUv = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>(), _textTriangles = new List<int>();
        private readonly List<Color32> _textColors = new List<Color32>();
        private readonly StringBuilder _builder = new StringBuilder(256);
        private string _characters = string.Empty;
        private float _capPixels = 1f, _linePixels = 1f;
        private int _optionCount, _optionSignature, _level;
        private float _scale, _blend, _pulseUntil, _noticeUntil;
        private bool _open, _placed, _labelsDirty, _refreshPending, _stickArmed, _controllerDriven;
        private bool _touching, _touchArmed, _dragging;
        private float _touchStart, _touchAzimuth, _lastAzimuth;
        private Vector3 _touchLocal, _spaceLocal;
        private long _minute = -1;
        private TextMesh _notice;
        private Transform _noticeHead;

        internal bool Initialize(FolioBook book)
        {
            Renderer page = book.leftPage != null ? book.leftPage : book.rightPage;
            Material source = page != null ? page.sharedMaterial : null;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (source == null || source.shader == null || source.shader.name != "Universal Render Pipeline/Unlit" ||
                _font == null || _font.material == null)
            {
                Debug.LogWarning("[FolioXR] Esfera: falta el material URP Unlit de la página o la fuente integrada.");
                return false;
            }
            _palette = new Texture2D(8, 1, TextureFormat.RGBA32, false)
            { name = "Colores de la esfera", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _palette.SetPixels(new[] {
                new Color(0.07f, 0.13f, 0.30f, 0.94f), // casilla
                new Color(0.08f, 0.42f, 0.92f, 0.96f), // marcada
                new Color(0.09f, 0.10f, 0.14f, 0.80f), // deshabilitada
                Color.white,                           // íconos
                new Color(0.55f, 0.95f, 1f, 1f),       // marco del frente
                new Color(0.26f, 0.18f, 0.42f, 0.94f), // «Volver»
                Color.white, Color.white });
            _palette.Apply(false, true);
            _bodyTexture = BodyTexture();

            // Orden de dibujo fijo: cuerpo → categorías → sus textos → opciones y marco → textos de opciones.
            // El cuerpo escribe profundidad: lo que queda en la cara de atrás no se ve a través de la esfera.
            _bodyMaterial = PanelMenuView.Clone(source, "Cuerpo de la esfera", _bodyTexture);
            _bodyMaterial.SetFloat("_ZWrite", 1f);
            _bodyMaterial.SetFloat("_Cull", (float)CullMode.Back);
            _bodyMaterial.renderQueue = (int)RenderQueue.Transparent - 4;
            _categoryMaterial = PanelMenuView.Clone(source, "Casillas de categorías", _palette);
            _categoryMaterial.SetFloat("_Cull", (float)CullMode.Back);
            _categoryMaterial.renderQueue = (int)RenderQueue.Transparent - 3;
            _categoryTextMaterial = new Material(_font.material) { name = "FolioXR Textos de categorías" };
            _categoryTextMaterial.renderQueue = (int)RenderQueue.Transparent - 2;
            _optionMaterial = PanelMenuView.Clone(source, "Casillas de opciones", _palette);
            _optionMaterial.SetFloat("_Cull", (float)CullMode.Back);
            _optionMaterial.renderQueue = (int)RenderQueue.Transparent - 1;

            _root = new GameObject("Esfera").transform;
            _root.SetParent(transform, false);
            _bodyMesh = BodyMesh();
            _body = MeshObject(_root, "Cuerpo azul", _bodyMesh, _bodyMaterial).transform;
            CreateRing(_categories, "Categorías", _categoryMaterial, _categoryTextMaterial);
            CreateRing(_options, "Opciones", _optionMaterial, _font.material);
            for (int i = 0; i < 5; i++) _categories.SlotTile[i] = i;
            _frameMesh = FrameMesh();
            _frame = MeshObject(_root, "Marco del frente", _frameMesh, _optionMaterial).transform;
            _root.gameObject.SetActive(false);
            ApplyLevel();
            return true;
        }

        private void CreateRing(Ring ring, string name, Material tiles, Material labels)
        {
            ring.Tilt = new GameObject("Inclinación · " + name).transform;
            ring.Tilt.SetParent(_root, false);
            ring.Spin = new GameObject("Giro · " + name).transform;
            ring.Spin.SetParent(ring.Tilt, false);
            ring.Tiles = new Mesh { name = "Casillas · " + name };
            ring.Tiles.MarkDynamic();
            ring.Labels = new Mesh { name = "Textos · " + name };
            ring.Labels.MarkDynamic();
            ring.TilesRenderer = MeshObject(ring.Spin, "Casillas", ring.Tiles, tiles);
            ring.LabelsRenderer = MeshObject(ring.Spin, "Textos", ring.Labels, labels);
        }

        private static MeshRenderer MeshObject(Transform parent, string name, Mesh mesh, Material material)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return renderer;
        }

        // ───────────────────────── Recursos fijos ─────────────────────────

        /// <summary>Degradado radial: centro azul translúcido y borde claro, como un brillo.</summary>
        private static Texture2D BodyTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "Brillo de la esfera", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            var deep = new Color(0.04f, 0.2f, 0.72f);
            var glow = new Color(0.55f, 0.86f, 1f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float rim = Mathf.SmoothStep(0.5f, 1f, Mathf.Sqrt(dx * dx + dy * dy));
                    Color color = Color.Lerp(deep, glow, rim * rim);
                    color.a = Mathf.Lerp(0.32f, 0.9f, rim);
                    pixels[y * size + x] = color;
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Esfera cuyo -Z mira al lector; la textura se proyecta a lo largo de Z.</summary>
        private static Mesh BodyMesh()
        {
            const int rows = 12, columns = 24;
            var vertices = new Vector3[(rows + 1) * (columns + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[rows * columns * 6];
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    Vector3 p = OnSphere(x * 360f / columns - 180f, y * 180f / rows - 90f, Radius);
                    int i = y * (columns + 1) + x;
                    vertices[i] = p;
                    uv[i] = new Vector2(0.5f + 0.49f * p.x / Radius, 0.5f + 0.49f * p.y / Radius);
                }
            int t = 0;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int a = y * (columns + 1) + x, b = a + 1, c = a + columns + 1, d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = c; triangles[t++] = d; triangles[t++] = b;
                }
            var mesh = new Mesh { name = "Cuerpo de la esfera" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Marco fijo alrededor de la casilla del frente: no gira con los anillos.</summary>
        private Mesh FrameMesh()
        {
            _vertices.Clear(); _uv.Clear(); _triangles.Clear();
            float w = HalfWidth + 2.6f, h = HalfHeight + 1.2f, t = 1.5f;
            AddPatch(0f, -w, w, h, h + t, FrameRadius, Frame, 8, 1);
            AddPatch(0f, -w, w, -h - t, -h, FrameRadius, Frame, 8, 1);
            AddPatch(0f, -w, -w + t, -h, h, FrameRadius, Frame, 1, 4);
            AddPatch(0f, w - t, w, -h, h, FrameRadius, Frame, 1, 4);
            var mesh = new Mesh { name = "Marco del frente" };
            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uv);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ───────────────────────── Geometría sobre la esfera ─────────────────────────

        /// <summary>Longitud 0 = frente (-Z); positiva hacia la derecha del lector (+X).</summary>
        private static Vector3 OnSphere(float longitude, float latitude, float radius)
        {
            float lo = longitude * Mathf.Deg2Rad, la = latitude * Mathf.Deg2Rad, c = Mathf.Cos(la);
            return new Vector3(Mathf.Sin(lo) * c, Mathf.Sin(la), -Mathf.Cos(lo) * c) * radius;
        }

        /// <summary>Punto de una casilla: x e y en metros desde su centro, curvados sobre la esfera.</summary>
        private static Vector3 Map(float angle, float x, float y, float radius) =>
            OnSphere(angle + x / radius * Mathf.Rad2Deg, y / radius * Mathf.Rad2Deg, radius);

        private static Vector2 PaletteUv(int index) => new Vector2((index + 0.5f) / 8f, 0.5f);

        private static int Mod(int value, int length) => (value % length + length) % length;

        /// <summary>Ángulo con signo en [-length/2, length/2).</summary>
        private static float Wrap(float angle, float length) => Mathf.Repeat(angle + length * 0.5f, length) - length * 0.5f;

        private void AddPatch(float angle, float lon0, float lon1, float lat0, float lat1, float radius, int palette,
            int columns, int rows)
        {
            int start = _vertices.Count;
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    _vertices.Add(OnSphere(angle + Mathf.Lerp(lon0, lon1, x / (float)columns),
                        Mathf.Lerp(lat0, lat1, y / (float)rows), radius));
                    _uv.Add(PaletteUv(palette));
                }
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int a = start + y * (columns + 1) + x, b = a + 1, c = a + columns + 1, d = c + 1;
                    _triangles.Add(a); _triangles.Add(c); _triangles.Add(b);
                    _triangles.Add(c); _triangles.Add(d); _triangles.Add(b);
                }
        }

        /// <summary>Trazo de ícono en metros de la casilla, partido en tramos de 6 mm para seguir la curva.</summary>
        private void Stroke(float angle, Vector2 a, Vector2 b, float width)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.00001f) return;
            Vector2 n = new Vector2(-d.y, d.x) / length * (width * 0.5f);
            int parts = Mathf.Max(1, Mathf.CeilToInt(length / 0.006f));
            for (int i = 0; i < parts; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)parts), q = Vector2.Lerp(a, b, (i + 1) / (float)parts);
                int k = _vertices.Count;
                _vertices.Add(Map(angle, p.x - n.x, p.y - n.y, IconRadius));
                _vertices.Add(Map(angle, p.x + n.x, p.y + n.y, IconRadius));
                _vertices.Add(Map(angle, q.x + n.x, q.y + n.y, IconRadius));
                _vertices.Add(Map(angle, q.x - n.x, q.y - n.y, IconRadius));
                for (int j = 0; j < 4; j++) _uv.Add(PaletteUv(White));
                _triangles.Add(k); _triangles.Add(k + 1); _triangles.Add(k + 2);
                _triangles.Add(k); _triangles.Add(k + 2); _triangles.Add(k + 3);
            }
        }

        private void Arc(float angle, Vector2 center, float radius, float from, float to, float width)
        {
            int parts = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(to - from) / 30f));
            for (int i = 0; i < parts; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)parts) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(from, to, (i + 1) / (float)parts) * Mathf.Deg2Rad;
                Stroke(angle, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                    center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, width);
            }
        }

        private void Icon(float angle, MenuCategory category)
        {
            const float u = 0.0085f, w = 0.0011f;
            var o = new Vector2(0f, 0.009f);
            void Line(float ax, float ay, float bx, float by) =>
                Stroke(angle, o + new Vector2(ax, ay) * u, o + new Vector2(bx, by) * u, w);
            void Box(float x, float y, float width, float height)
            {
                Line(x, y, x + width, y); Line(x + width, y, x + width, y + height);
                Line(x + width, y + height, x, y + height); Line(x, y + height, x, y);
            }
            switch (category)
            {
                case MenuCategory.Biblioteca: // Tres lomos sobre un estante.
                    Box(-0.95f, -0.85f, 0.5f, 1.6f);
                    Box(-0.35f, -0.85f, 0.5f, 1.85f);
                    Line(0.35f, -0.85f, 0.75f, 0.8f); Line(0.75f, 0.8f, 1.15f, 0.7f);
                    Line(1.15f, 0.7f, 0.8f, -0.85f);
                    Line(-1.2f, -0.85f, 1.3f, -0.85f);
                    break;
                case MenuCategory.Entornos: // Casa.
                    Line(-1.15f, 0.05f, 0f, 1f); Line(0f, 1f, 1.15f, 0.05f);
                    Line(-0.8f, 0.3f, -0.8f, -0.9f); Line(-0.8f, -0.9f, 0.8f, -0.9f); Line(0.8f, -0.9f, 0.8f, 0.3f);
                    Box(-0.2f, -0.9f, 0.4f, 0.7f);
                    break;
                case MenuCategory.Ajustes: // Engranaje.
                    Arc(angle, o, 0.34f * u, 0f, 360f, w);
                    Arc(angle, o, 0.7f * u, 0f, 360f, w);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * 45f * Mathf.Deg2Rad;
                        Stroke(angle, o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (0.7f * u),
                            o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (1.02f * u), w * 2.2f);
                    }
                    break;
                case MenuCategory.ImportarYNubes: // Nube.
                    Arc(angle, o + new Vector2(-0.5f, -0.15f) * u, 0.45f * u, 90f, 270f, w);
                    Arc(angle, o + new Vector2(0f, 0.2f) * u, 0.6f * u, 15f, 165f, w);
                    Arc(angle, o + new Vector2(0.6f, -0.15f) * u, 0.45f * u, -90f, 90f, w);
                    Line(-0.5f, -0.6f, 0.6f, -0.6f);
                    break;
            }
        }

        // ───────────────────────── Texto propio (un solo mesh por anillo) ─────────────────────────

        private float LinePixels(string line)
        {
            float pixels = 0f;
            foreach (char c in line)
                if (_font.GetCharacterInfo(c, out CharacterInfo glyph, FontSize)) pixels += glyph.advance;
            return pixels;
        }

        /// <summary>
        /// Texto centrado en una caja (metros de la casilla), en una o dos líneas. Se achica lo necesario
        /// para caber siempre; <paramref name="maxCap"/> es el alto máximo de las mayúsculas.
        /// </summary>
        private void AddLabel(float angle, string label, float centerY, float width, float height, float maxCap,
            Color32 color)
        {
            string flat = string.Join(" ", label.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
            if (flat.Length == 0) return;
            float lineStep = _linePixels * 0.92f / _capPixels; // Paso entre líneas, en altos de mayúscula.
            string first = flat, second = null;
            float best = 0f;
            for (int split = -1; split < flat.Length; split++)
            {
                if (split >= 0 && flat[split] != ' ') continue;
                string a = split < 0 ? flat : flat.Substring(0, split);
                string b = split < 0 ? null : flat.Substring(split + 1);
                float pixels = Mathf.Max(1f, LinePixels(a), b != null ? LinePixels(b) : 0f);
                float cap = Mathf.Min(maxCap, width * _capPixels / pixels, height / (b != null ? 1f + lineStep : 1f));
                if (cap <= best) continue;
                best = cap; first = a; second = b;
            }
            float baseline = centerY - best * 0.5f + (second != null ? lineStep * best * 0.5f : 0f);
            AddLine(angle, first, baseline, best, color);
            if (second != null) AddLine(angle, second, baseline - lineStep * best, best, color);
        }

        private void AddLine(float angle, string line, float baseline, float cap, Color32 color)
        {
            float k = cap / _capPixels; // Metros por píxel de la fuente.
            float x = -LinePixels(line) * k * 0.5f;
            foreach (char c in line)
            {
                if (!_font.GetCharacterInfo(c, out CharacterInfo glyph, FontSize)) continue;
                if (glyph.maxX > glyph.minX && glyph.maxY > glyph.minY)
                {
                    int i = _textVertices.Count;
                    float x0 = x + glyph.minX * k, x1 = x + glyph.maxX * k;
                    float y0 = baseline + glyph.minY * k, y1 = baseline + glyph.maxY * k;
                    _textVertices.Add(Map(angle, x0, y0, LabelRadius)); _textUv.Add(glyph.uvBottomLeft);
                    _textVertices.Add(Map(angle, x0, y1, LabelRadius)); _textUv.Add(glyph.uvTopLeft);
                    _textVertices.Add(Map(angle, x1, y1, LabelRadius)); _textUv.Add(glyph.uvTopRight);
                    _textVertices.Add(Map(angle, x1, y0, LabelRadius)); _textUv.Add(glyph.uvBottomRight);
                    for (int j = 0; j < 4; j++) _textColors.Add(color);
                    _textTriangles.Add(i); _textTriangles.Add(i + 1); _textTriangles.Add(i + 2);
                    _textTriangles.Add(i); _textTriangles.Add(i + 2); _textTriangles.Add(i + 3);
                }
                x += glyph.advance * k;
            }
        }

        private static string BatteryText()
        {
            float battery = SystemInfo.batteryLevel;
            return battery < 0f ? "Batería —" : "Batería " + Mathf.RoundToInt(battery * 100f) + "%";
        }

        /// <summary>Todo el texto visible: se pide a la fuente en cada cuadro para que no lo descarte del atlas.</summary>
        private void CollectCharacters()
        {
            _builder.Length = 0;
            _builder.Append("H0123456789:%—Batería Volver");
            foreach (string name in MenuModel.CategoryNames) _builder.Append(name);
            if (_model != null)
                foreach (MenuOption option in _model.Options) _builder.Append(option.Text);
            _characters = _builder.ToString();
        }

        private void RequestCharacters()
        {
            _font.RequestCharactersInTexture(_characters, FontSize);
            _font.GetCharacterInfo('H', out CharacterInfo glyph, FontSize);
            _capPixels = Mathf.Max(1, glyph.glyphHeight);
            _linePixels = _font.lineHeight * (FontSize / (float)Mathf.Max(1, _font.fontSize));
        }

        // ───────────────────────── Contenido de los anillos ─────────────────────────

        private void BuildRing(Ring ring)
        {
            ring.BuiltSlots.Clear();
            if (ring.Slots <= 5)
            {
                ring.BaseSlot = 0;
                for (int k = 0; k < ring.Slots; k++) ring.BuiltSlots.Add(k);
            }
            else
                // Más de cinco casillas: solo existe la ventana de cinco alrededor del frente.
                // Las que entran y salen lo hacen por detrás, tapadas por el cuerpo.
                for (int d = -2; d <= 2; d++) ring.BuiltSlots.Add(Mod(ring.BaseSlot + d, ring.Slots));
            _vertices.Clear(); _uv.Clear(); _triangles.Clear();
            _textVertices.Clear(); _textUv.Clear(); _textTriangles.Clear(); _textColors.Clear();
            CollectCharacters();
            RequestCharacters();
            foreach (int slot in ring.BuiltSlots)
            {
                int tile = ring.SlotTile[slot];
                if (tile < 0) continue;
                float angle = Wrap((slot - ring.BaseSlot) * Step, ring.Slots * Step);
                if (ring == _categories) AddCategory(angle, (MenuCategory)tile);
                else AddOption(angle, tile);
            }
            ring.Tiles.Clear();
            ring.Tiles.SetVertices(_vertices);
            ring.Tiles.SetUVs(0, _uv);
            ring.Tiles.SetTriangles(_triangles, 0);
            ring.Tiles.RecalculateBounds();
            ring.Labels.Clear();
            ring.Labels.SetVertices(_textVertices);
            ring.Labels.SetUVs(0, _textUv);
            ring.Labels.SetColors(_textColors);
            ring.Labels.SetTriangles(_textTriangles, 0);
            ring.Labels.RecalculateBounds();
        }

        private void AddCategory(float angle, MenuCategory category)
        {
            AddPatch(angle, -HalfWidth, HalfWidth, -HalfHeight, HalfHeight, TileRadius, Normal, 8, 4);
            if (category == MenuCategory.Hora)
            {
                // La casilla es el reloj: hora grande y batería debajo.
                AddLabel(angle, DateTime.Now.ToString("HH:mm"), 0.008f, 0.064f, 0.02f, 0.015f, TextColor);
                AddLabel(angle, BatteryText(), -0.013f, 0.064f, 0.012f, 0.0075f, TextColor);
                return;
            }
            Icon(angle, category);
            AddLabel(angle, MenuModel.CategoryNames[(int)category], -0.0125f, 0.066f, 0.019f, 0.01f, TextColor);
        }

        private void AddOption(float angle, int tile)
        {
            if (tile >= _optionCount)
            {
                AddPatch(angle, -HalfWidth, HalfWidth, -HalfHeight, HalfHeight, TileRadius, BackTile, 8, 4);
                var o = new Vector2(0f, 0.01f);
                Stroke(angle, o + new Vector2(0.008f, 0f), o + new Vector2(-0.008f, 0f), 0.0012f);
                Stroke(angle, o + new Vector2(-0.008f, 0f), o + new Vector2(-0.003f, 0.0045f), 0.0012f);
                Stroke(angle, o + new Vector2(-0.008f, 0f), o + new Vector2(-0.003f, -0.0045f), 0.0012f);
                AddLabel(angle, "Volver", -0.01f, 0.066f, 0.016f, 0.01f, TextColor);
                return;
            }
            if (tile >= _model.Options.Count) return; // El modelo cambió; SetupOptions lo rehace en este cuadro.
            MenuOption option = _model.Options[tile];
            bool enabled = option.Enabled();
            AddPatch(angle, -HalfWidth, HalfWidth, -HalfHeight, HalfHeight, TileRadius,
                !enabled ? Disabled : option.Marked() ? Marked : Normal, 8, 4);
            AddLabel(angle, option.Text, 0f, 0.066f, 0.042f, 0.011f, enabled ? TextColor : DisabledText);
        }

        private int Signature()
        {
            unchecked
            {
                int signature = 17 + (int)_model.SelectedCategory;
                foreach (MenuOption option in _model.Options)
                {
                    signature = signature * 31 + option.Text.GetHashCode();
                    signature = signature * 31 + (option.Enabled() ? 1 : 0) + (option.Marked() ? 2 : 0);
                }
                return signature;
            }
        }

        /// <summary>Opciones de la categoría elegida + «Volver», que queda justo a la izquierda de la primera.</summary>
        private void SetupOptions(bool reset)
        {
            int count = _model.Options.Count;
            int slots = Mathf.Max(count + 1, 5);
            int previous = _options.SlotTile[Mathf.Clamp(_options.Front, 0, _options.SlotTile.Length - 1)];
            bool wasBack = previous >= _optionCount;
            bool sameShape = !reset && slots == _options.Slots && count == _optionCount;
            _optionCount = count;
            _optionSignature = Signature();
            if (!sameShape)
            {
                if (_options.SlotTile.Length != slots) _options.SlotTile = new int[slots];
                _options.Slots = slots;
                for (int k = 0; k < slots; k++) _options.SlotTile[k] = k < count ? k : -1;
                _options.SlotTile[slots - 1] = count;
                int front = count == 0 || (!reset && wasBack) ? slots - 1
                    : reset ? 0 : Mathf.Clamp(previous, 0, count - 1);
                _options.Scroll = _options.Target = front * Step;
                _options.Velocity = 0f;
                _options.Snapping = false;
                _options.Front = _options.BaseSlot = front;
            }
            BuildRing(_options);
            _options.Spin.localRotation = Quaternion.Euler(0f, Offset(_options), 0f);
        }

        // ───────────────────────── Vista (IMenuView) ─────────────────────────

        public void Bind(MenuModel model, Action close)
        {
            Unbind();
            _model = model;
            _model.Changed += Refresh;
            _refreshPending = true;
        }

        public void Unbind()
        {
            if (_model != null) _model.Changed -= Refresh;
            _model = null;
        }

        public void Refresh()
        {
            // Llega desde dentro de Invoke/Select: se atiende en el siguiente Update.
            _refreshPending = true;
        }

        public void SetOpen(bool open, Transform head)
        {
            _open = open;
            _touching = _dragging = _touchArmed = _stickArmed = false;
            if (!open) return;
            _level = 0;
            _blend = 0f;
            _placed = false;
            _categories.Velocity = 0f;
            _labelsDirty = true; // El atlas pudo cambiar mientras estaba cerrada.
            // Parte del tamaño actual (0 la primera vez): nunca aparece un cuadro a tamaño completo.
            _root.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, _scale);
            _root.gameObject.SetActive(true);
            ApplyLevel();
        }

        public void ShowNotice(string message, Transform head)
        {
            if (_notice == null)
            {
                var item = new GameObject("Aviso del temporizador (esfera)");
                item.transform.SetParent(transform, false);
                _notice = item.AddComponent<TextMesh>();
                _notice.font = _font;
                _notice.fontSize = FontSize;
                _font.RequestCharactersInTexture("H", FontSize);
                _font.GetCharacterInfo('H', out CharacterInfo glyph, FontSize);
                _notice.characterSize = 0.012f * 10f / Mathf.Max(1, glyph.glyphHeight);
                _notice.anchor = TextAnchor.MiddleCenter;
                _notice.alignment = TextAlignment.Center;
                _notice.richText = false;
                var renderer = item.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = _font.material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            _noticeHead = head;
            _notice.text = message;
            _noticeUntil = Time.unscaledTime + 5f;
            _notice.gameObject.SetActive(head != null);
        }

        /// <summary>B/Y o la casilla «Volver»: del anillo de opciones al de categorías.</summary>
        internal bool Back()
        {
            if (_level == 0) return false;
            _level = 0;
            _touching = false;
            return true;
        }

        // ───────────────────────── Ciclo ─────────────────────────

        private void OnEnable()
        {
            Font.textureRebuilt += OnFontRebuilt;
        }

        private void OnDisable()
        {
            Font.textureRebuilt -= OnFontRebuilt;
        }

        private void OnFontRebuilt(Font font)
        {
            // Las coordenadas de todos los glifos cambiaron: rehacer los textos en el próximo Update.
            if (font != _font) return;
            _labelsDirty = true;
            if (_categoryTextMaterial != null) _categoryTextMaterial.mainTexture = _font.material.mainTexture;
        }

        private void Update()
        {
            if (_root == null) return;
            if (_notice != null && _notice.gameObject.activeSelf && Time.unscaledTime >= _noticeUntil)
                _notice.gameObject.SetActive(false);
            float dt = Time.unscaledDeltaTime;
            _scale = Mathf.MoveTowards(_scale, _open ? 1f : 0f, dt / OpenSeconds);
            if (_scale <= 0f)
            {
                if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
                return;
            }
            _root.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, _scale);
            if (_model == null) return;

            float blend = Mathf.MoveTowards(_blend, _level, dt / LevelSeconds);
            if (!Mathf.Approximately(blend, _blend))
            {
                _blend = blend;
                ApplyLevel();
            }
            _frame.localScale = Vector3.one * (Time.unscaledTime < _pulseUntil ? 1.035f : 1f);

            _font.RequestCharactersInTexture(_characters, FontSize);
            long minute = DateTime.Now.Ticks / TimeSpan.TicksPerMinute;
            if (minute != _minute)
            {
                _minute = minute;
                _labelsDirty = true;
            }
            if (_refreshPending)
            {
                _refreshPending = false;
                // También durante la animación de vuelta: el anillo de opciones sigue visible.
                if ((_level == 1 || _blend > 0f) && Signature() != _optionSignature) SetupOptions(false);
            }
            if (_labelsDirty)
            {
                _labelsDirty = false;
                BuildRing(_categories);
                if (_blend > 0f) BuildRing(_options);
            }
        }

        private void LateUpdate()
        {
            if (_notice != null && _notice.gameObject.activeSelf && _noticeHead != null)
                _notice.transform.SetPositionAndRotation(_noticeHead.position + _noticeHead.forward * 0.55f +
                    _noticeHead.up * 0.17f, _noticeHead.rotation);
        }

        /// <summary>Giro rígido de toda la esfera entre los dos niveles y atenuado de las categorías.</summary>
        private void ApplyLevel()
        {
            float t = Mathf.SmoothStep(0f, 1f, _blend);
            _categories.Tilt.localRotation = Quaternion.Euler(Tilt * t, 0f, 0f);
            _options.Tilt.localRotation = Quaternion.Euler(Tilt * (t - 1f), 0f, 0f);
            bool options = _blend > 0f;
            _options.TilesRenderer.enabled = options;
            _options.LabelsRenderer.enabled = options;
            float alpha = Mathf.Lerp(1f, 0.45f, t);
            _categoryMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            _categoryTextMaterial.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
        }

        private static float Offset(Ring ring) => Wrap(ring.Scroll - ring.BaseSlot * Step, ring.Slots * Step);

        /// <summary>Entradas y movimiento; lo llama el controlador en cada cuadro con el menú abierto.</summary>
        internal void Drive(SphereInput input)
        {
            if (!_open || _root == null || _model == null || input.Head == null) return;
            float dt = Time.unscaledDeltaTime;
            Place(input, dt);
            Ring ring = _level == 0 ? _categories : _options;
            bool ready = _scale >= 1f && Mathf.Approximately(_blend, _level);
            UpdateTouch(input, ring, ready, dt);
            if (ready && !_touching)
            {
                if (Mathf.Abs(input.StickX) < 0.3f) _stickArmed = true;
                else if (_stickArmed && Mathf.Abs(input.StickX) > 0.7f)
                {
                    _stickArmed = false;
                    _controllerDriven = true;
                    StepRing(ring, input.StickX > 0f ? 1 : -1);
                }
                if (input.Select && Mathf.Abs(Wrap(ring.Front * Step - ring.Scroll, ring.Slots * Step)) < 20f)
                {
                    _controllerDriven = true;
                    Activate(ring, ring.SlotTile[ring.Front]);
                    return; // El anillo activo pudo cambiar.
                }
            }
            Advance(ring, dt);
        }

        private void Place(SphereInput input, float dt)
        {
            Vector3 position = _root.position;
            if (input.AnchorValid)
            {
                Vector3 target = input.Anchor + Vector3.up * AnchorHeight;
                // Sigue a la mano en tiempo real; un salto del rig no la arrastra por el aire.
                position = _placed && (target - position).sqrMagnitude < 0.25f
                    ? Vector3.Lerp(position, target, 1f - Mathf.Exp(-30f * dt)) : target;
            }
            else if (_placed && input.Space != null)
                position = input.Space.TransformPoint(_spaceLocal); // Mano fuera de vista: queda donde estaba.
            else if (!_placed)
            {
                Vector3 forward = Vector3.ProjectOnPlane(input.Head.forward, Vector3.up);
                forward = forward.sqrMagnitude > 0.01f ? forward.normalized : input.Head.forward;
                position = input.Head.position + forward * 0.4f - Vector3.up * 0.18f -
                    Vector3.Cross(Vector3.up, forward) * 0.12f;
            }
            _placed = true;
            if (input.Space != null) _spaceLocal = input.Space.InverseTransformPoint(position);
            Vector3 away = position - input.Head.position;
            Vector3 flat = Vector3.ProjectOnPlane(away, Vector3.up);
            _root.SetPositionAndRotation(position, flat.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(flat.normalized, Vector3.up) : _root.rotation);
            if (away.sqrMagnitude > 0.0001f) _body.rotation = Quaternion.LookRotation(away.normalized, input.Head.up);
        }

        private static float Azimuth(Vector3 local) => Mathf.Atan2(local.x, -local.z) * Mathf.Rad2Deg;

        /// <summary>
        /// Índice derecho: deslizar gira el anillo; tocar y retirar sin deslizar elige (como en una pantalla táctil).
        /// </summary>
        private void UpdateTouch(SphereInput input, Ring ring, bool ready, float dt)
        {
            if (!input.TipValid || _scale < 1f)
            {
                // Sin seguimiento (o mientras aparece) no hay toque ni elección; la inercia que hubiera continúa.
                _touching = _touchArmed = false;
                return;
            }
            Vector3 local = _root.InverseTransformPoint(input.Tip);
            float distance = local.magnitude;
            if (!_touching)
            {
                // Hay que llegar desde fuera: aparecer sobre el dedo no cuenta como toque.
                if (distance > Radius + 0.025f) _touchArmed = true;
                if (!ready || !_touchArmed || distance > Radius + 0.008f || distance < Radius - 0.035f) return;
                _touching = true;
                _touchArmed = _dragging = _controllerDriven = false;
                _touchStart = Time.unscaledTime;
                _touchLocal = local;
                _touchAzimuth = _lastAzimuth = Azimuth(local);
                ring.Velocity = 0f;
                ring.Snapping = false;
                return;
            }
            if (distance > Radius + 0.02f || distance < Radius - 0.055f)
            {
                _touching = false;
                if (!_dragging && distance > Radius && Time.unscaledTime - _touchStart < 1f) Tap(_touchLocal);
                return;
            }
            if (Mathf.Abs(local.y) > Radius * 0.85f) return; // Cerca de los polos el ángulo no es fiable.
            float azimuth = Azimuth(local);
            float delta = Mathf.DeltaAngle(_lastAzimuth, azimuth);
            _lastAzimuth = azimuth;
            if (!_dragging && Mathf.Abs(Mathf.DeltaAngle(_touchAzimuth, azimuth)) > 7f)
            {
                _dragging = true;
                delta = 0f;
            }
            if (!_dragging) return;
            ring.Scroll -= delta; // La casilla sigue al dedo.
            ring.Velocity = Mathf.Lerp(ring.Velocity, Mathf.Clamp(-delta / Mathf.Max(dt, 0.001f), -720f, 720f), 0.4f);
        }

        private void Tap(Vector3 local)
        {
            Vector3 world = _root.TransformPoint(local);
            if (_level == 1)
            {
                if (TryHit(_options, world, out int option))
                {
                    if (option == _options.Front) Activate(_options, _options.SlotTile[option]);
                    else SnapTo(_options, option);
                }
                else if (TryHit(_categories, world, out _) && Back()) Feedback?.Invoke(false);
            }
            else if (TryHit(_categories, world, out int category))
            {
                if (category == _categories.Front) Activate(_categories, _categories.SlotTile[category]);
                else SnapTo(_categories, category);
            }
        }

        private static bool TryHit(Ring ring, Vector3 world, out int slot)
        {
            slot = -1;
            Vector3 p = ring.Spin.InverseTransformPoint(world);
            float length = p.magnitude;
            if (length < 0.0001f) return false;
            float latitude = Mathf.Asin(Mathf.Clamp(p.y / length, -1f, 1f)) * Mathf.Rad2Deg;
            if (Mathf.Abs(latitude) > HalfHeight + 3f) return false;
            float longitude = Azimuth(p);
            foreach (int k in ring.BuiltSlots)
            {
                if (ring.SlotTile[k] < 0) continue;
                float angle = Wrap((k - ring.BaseSlot) * Step, ring.Slots * Step);
                if (Mathf.Abs(Mathf.DeltaAngle(longitude, angle)) > HalfWidth + 3f) continue;
                slot = k;
                return true;
            }
            return false;
        }

        private void Activate(Ring ring, int tile)
        {
            if (tile < 0) return;
            if (ring == _categories)
            {
                _model.Select((MenuCategory)tile);
                _level = 1;
                SetupOptions(true);
            }
            else if (tile >= _optionCount) Back();
            else if (tile < _model.Options.Count && _model.Options[tile].Enabled()) _model.Invoke(tile);
            else return; // Casilla informativa: sin clic.
            _pulseUntil = Time.unscaledTime + 0.12f;
            Feedback?.Invoke(_controllerDriven);
        }

        private static void SnapTo(Ring ring, int slot)
        {
            ring.Target = ring.Scroll + Wrap(slot * Step - ring.Scroll, ring.Slots * Step);
            ring.Snapping = true;
            ring.Velocity = 0f;
        }

        /// <summary>Stick: una casilla por movimiento, siempre en el sentido pedido y saltando huecos.</summary>
        private static void StepRing(Ring ring, int direction)
        {
            float length = ring.Slots * Step;
            float from = ring.Snapping ? ring.Target : ring.Scroll;
            int slot = Mod(Mathf.RoundToInt(from / Step), ring.Slots);
            for (int i = 0; i < ring.Slots; i++)
            {
                slot = Mod(slot + direction, ring.Slots);
                if (ring.SlotTile[slot] >= 0) break;
            }
            float delta = Wrap(slot * Step - from, length);
            if (direction > 0 && delta < -0.5f) delta += length;
            if (direction < 0 && delta > 0.5f) delta -= length;
            ring.Target = from + delta;
            ring.Snapping = true;
            ring.Velocity = 0f;
        }

        /// <summary>Inercia, frenado y ajuste a la casilla más cercana; clic al pasar cada una por el frente.</summary>
        private void Advance(Ring ring, float dt)
        {
            float length = ring.Slots * Step;
            if (!_touching)
            {
                if (ring.Snapping)
                {
                    float gap = Mathf.Abs(ring.Target - ring.Scroll);
                    ring.Scroll = Mathf.MoveTowards(ring.Scroll, ring.Target, Mathf.Max(90f, gap * 12f) * dt);
                    if (Mathf.Abs(ring.Target - ring.Scroll) < 0.05f)
                    {
                        ring.Scroll = ring.Target = Mathf.Repeat(ring.Target, length);
                        ring.Snapping = false;
                    }
                }
                else if (Mathf.Abs(ring.Velocity) > 40f)
                {
                    ring.Scroll += ring.Velocity * dt;
                    ring.Velocity *= Mathf.Exp(-2.5f * dt);
                }
                else
                {
                    ring.Velocity = 0f;
                    int nearest = -1;
                    float best = float.PositiveInfinity;
                    for (int k = 0; k < ring.Slots; k++)
                    {
                        if (ring.SlotTile[k] < 0) continue;
                        float gap = Mathf.Abs(Wrap(k * Step - ring.Scroll, length));
                        if (gap >= best) continue;
                        best = gap;
                        nearest = k;
                    }
                    if (nearest >= 0 && best > 0.05f) SnapTo(ring, nearest);
                }
            }
            int front = Mod(Mathf.RoundToInt(ring.Scroll / Step), ring.Slots);
            if (front != ring.Front)
            {
                ring.Front = front;
                if (ring.Slots > 5)
                {
                    ring.BaseSlot = front;
                    BuildRing(ring);
                }
                if (ring.SlotTile[front] >= 0) Feedback?.Invoke(_controllerDriven);
            }
            ring.Spin.localRotation = Quaternion.Euler(0f, Offset(ring), 0f);
        }

        private void OnDestroy()
        {
            Unbind();
            foreach (UnityEngine.Object item in new UnityEngine.Object[] {
                _bodyMesh, _frameMesh, _categories.Tiles, _categories.Labels, _options.Tiles, _options.Labels,
                _bodyMaterial, _categoryMaterial, _categoryTextMaterial, _optionMaterial, _bodyTexture, _palette })
                if (item != null) Destroy(item);
        }
    }
}
