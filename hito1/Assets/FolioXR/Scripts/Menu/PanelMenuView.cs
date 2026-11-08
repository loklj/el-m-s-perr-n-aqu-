using System;
using System.Collections.Generic;
using System.Text;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Menu
{
    /// <summary>
    /// Panel en metros, frente -Z. Ocho renderizadores activos como máximo, sin física:
    /// superficies, categorías, dos columnas, cabecera, reloj, aviso y ambos rayos.
    /// </summary>
    public sealed class PanelMenuView : MonoBehaviour, IMenuView
    {
        private const float FadeSeconds = 0.15f;
        private const float RowPitch = 0.044f;
        private const float Top = 0.086f;
        private const float ButtonHeight = 0.04f;
        private MenuModel _model;
        private Action _close;
        private Material _surfaceMaterial, _textMaterial, _noticeMaterial;
        private Texture2D _palette;
        private Font _font;
        private Mesh _surface, _rays;
        private MeshRenderer _surfaceRenderer, _rayRenderer;
        private TextMesh _categories, _optionsLeft, _optionsRight, _header, _clock, _notice;
        private TextMesh[] _panelTexts;
        private TextMesh _environmentLeft, _environmentRight;
        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _indices = new List<int>();
        private readonly HashSet<int> _hover = new HashSet<int>();
        private readonly Vector3[] _rayVertices = new Vector3[8];
        private readonly Vector2[] _rayUv = new Vector2[8];
        private readonly string[] _labels = new string[8];
        private MenuCategory _drawnCategory = (MenuCategory)(-1);
        private int _drawnCount = -1;
        private int _pressed = -1;
        private int _activationFrame = -1;
        private float _pressedUntil, _alpha, _noticeUntil;
        private bool _open, _placed, _surfaceDirty;
        private long _minute = -1;
        private Transform _noticeHead;

        private sealed class Button
        {
            public Rect Rect;
            public int Category = -1, Option = -1;
            public bool Close;
            public int Start, Count, Palette = -1;
            public float Depth;
        }

        internal bool Initialize(FolioBook book)
        {
            Renderer page = book.leftPage != null ? book.leftPage : book.rightPage;
            Material source = page != null ? page.sharedMaterial : null;
            if (source == null || source.shader == null || source.shader.name != "Universal Render Pipeline/Unlit")
            {
                Debug.LogWarning("[FolioXR] Menú: se necesita el material URP Unlit de una página del libro.");
                return false;
            }
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) return false;
            _palette = new Texture2D(8, 1, TextureFormat.RGBA32, false)
            { name = "Colores del menú", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _palette.SetPixels(new[] {
                new Color(0.075f, 0.085f, 0.10f, 0.9f), // panel
                new Color(0.15f, 0.18f, 0.23f, 1f),     // botón
                new Color(0.08f, 0.36f, 0.8f, 1f),      // marcado
                new Color(0.22f, 0.52f, 0.95f, 1f),     // hover
                new Color(0.10f, 0.11f, 0.13f, 1f),     // deshabilitado
                new Color(0.06f, 0.23f, 0.48f, 1f),     // pulsado
                Color.white, new Color(0.4f, 0.7f, 1f, 1f) });
            _palette.Apply(false, true);
            _surfaceMaterial = Clone(source, "Superficies del menú", _palette);
            _textMaterial = Clone(source, "Texto del menú", _font.material.mainTexture);
            _noticeMaterial = Clone(source, "Aviso del menú", _font.material.mainTexture);
            _surface = new Mesh { name = "Panel y botones" };
            _surface.MarkDynamic();
            _surfaceRenderer = MeshObject("Panel y botones", _surface, _surfaceMaterial);
            _rays = new Mesh { name = "Dos rayos del menú" };
            _rays.MarkDynamic();
            for (int i = 0; i < 8; i++) _rayUv[i] = PaletteUv(7);
            _rays.vertices = _rayVertices;
            _rays.uv = _rayUv;
            _rays.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            _rayRenderer = MeshObject("Rayos", _rays, _surfaceMaterial);
            _categories = Label("Categorías", new Vector3(-0.184f, Top - 0.003f, -0.004f), 0.012f, _textMaterial);
            _optionsLeft = Label("Opciones izquierda", new Vector3(-0.027f, Top - 0.003f, -0.004f), 0.01f, _textMaterial);
            _optionsRight = Label("Opciones derecha", new Vector3(0.089f, Top - 0.003f, -0.004f), 0.01f, _textMaterial);
            // Variante corta de Entornos: se cachea al cambiar de categoría.
            // Solo una pareja de columnas está activa en cada momento.
            _environmentLeft = Label("Entornos sin lugares izquierda", new Vector3(-0.027f, Top - 0.003f, -0.004f), 0.01f, _textMaterial);
            _environmentRight = Label("Entornos sin lugares derecha", new Vector3(0.089f, Top - 0.003f, -0.004f), 0.01f, _textMaterial);
            _environmentLeft.gameObject.SetActive(false);
            _environmentRight.gameObject.SetActive(false);
            _header = Label("Hora y batería", new Vector3(-0.184f, 0.123f, -0.004f), 0.01f, _textMaterial);
            _clock = Label("Reloj grande", new Vector3(-0.027f, 0.083f, -0.004f), 0.018f, _textMaterial);
            _notice = Label("Aviso del temporizador", Vector3.zero, 0.012f, _noticeMaterial);
            _notice.anchor = TextAnchor.MiddleCenter;
            _notice.alignment = TextAlignment.Center;
            _notice.gameObject.SetActive(false);
            _panelTexts = new[] { _categories, _optionsLeft, _optionsRight, _environmentLeft, _environmentRight, _header, _clock };
            _categories.text = "Biblioteca\n\nEntornos\n\nAjustes\n\nImportar y\nnubes\nHora";
            Font.textureRebuilt += OnFontRebuilt;
            OnFontRebuilt(_font);
            SetVisibility(0f);
            return true;
        }

        private Material Clone(Material source, string name, Texture texture)
        {
            var material = new Material(source) { name = "FolioXR " + name };
            material.mainTexture = texture;
            material.mainTextureScale = Vector2.one;
            material.mainTextureOffset = Vector2.zero;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            // URP 17 usa _Surface en OutputAlpha, sin necesitar otra variante de
            // shader que pudiera eliminarse del APK al partir de páginas opacas.
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("DepthNormalsOnly", false);
            material.SetShaderPassEnabled("MotionVectors", false);
            material.SetShaderPassEnabled("XRMotionVectors", false);
            return material;
        }

        private MeshRenderer MeshObject(string name, Mesh mesh, Material material)
        {
            var item = new GameObject(name);
            item.transform.SetParent(transform, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureRenderer(renderer);
            return renderer;
        }

        private static void ConfigureRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private TextMesh Label(string name, Vector3 position, float height, Material material)
        {
            var item = new GameObject(name);
            item.transform.SetParent(transform, false);
            item.transform.localPosition = position;
            var text = item.AddComponent<TextMesh>();
            text.font = _font;
            text.fontSize = 64;
            text.anchor = TextAnchor.UpperLeft;
            text.alignment = TextAlignment.Left;
            text.richText = false;
            _font.RequestCharactersInTexture("H", 64);
            _font.GetCharacterInfo('H', out CharacterInfo glyph, 64);
            // TextMesh expresa las métricas del glifo en characterSize / 10 metros.
            text.characterSize = height * 10f / Mathf.Max(1, glyph.glyphHeight);
            float lineHeight = _font.lineHeight * (64f / Mathf.Max(1, _font.fontSize));
            text.lineSpacing = (RowPitch * 0.5f) / (lineHeight * text.characterSize * 0.1f);
            var renderer = item.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ConfigureRenderer(renderer);
            return text;
        }

        public void Bind(MenuModel model, Action close)
        {
            if (_model != null) _model.Changed -= Refresh;
            _model = model;
            _close = close;
            _model.Changed += Refresh;
            Refresh();
        }

        public void Refresh()
        {
            if (_model == null) return;
            int visibleCount = 0;
            foreach (MenuOption option in _model.Options) if (option.Visible()) visibleCount++;
            bool categoryChanged = _drawnCategory != _model.SelectedCategory;
            bool layoutChanged = categoryChanged || _drawnCount != visibleCount;
            if (layoutChanged)
            {
                _drawnCategory = _model.SelectedCategory;
                _drawnCount = visibleCount;
                _hover.Clear();
                _pressed = -1;
                BuildLayout();
            }
            if (categoryChanged) BuildOptionTexts();
            bool shortEnvironment = _drawnCategory == MenuCategory.Entornos && visibleCount <= 2;
            _optionsLeft.gameObject.SetActive(!shortEnvironment);
            _optionsRight.gameObject.SetActive(!shortEnvironment);
            _environmentLeft.gameObject.SetActive(shortEnvironment);
            _environmentRight.gameObject.SetActive(shortEnvironment);
            _clock.gameObject.SetActive(_drawnCategory == MenuCategory.Hora);
            UpdateButtons(); // Solo UV/profundidad; las marcas no reconstruyen texto.
        }

        private void BuildLayout()
        {
            _buttons.Clear(); _vertices.Clear(); _uv.Clear(); _indices.Clear();
            RoundedRect(new Rect(-0.2f, -0.14f, 0.4f, 0.28f), 0f, 0, 0.012f);
            for (int i = 0; i < 5; i++)
                AddButton(new Rect(-0.192f, Top - i * RowPitch - ButtonHeight, 0.148f, ButtonHeight), i, -1, false);
            AddButton(new Rect(0.148f, 0.094f, 0.044f, 0.04f), -1, -1, true);
            // X geométrica: no añade otro TextMesh/draw call.
            CrossStroke(new Vector2(0.162f, 0.106f), new Vector2(0.178f, 0.122f));
            CrossStroke(new Vector2(0.162f, 0.122f), new Vector2(0.178f, 0.106f));
            bool wide = _drawnCategory == MenuCategory.Biblioteca || _drawnCategory == MenuCategory.ImportarYNubes;
            float top = _drawnCategory == MenuCategory.Hora ? Top - 0.056f : Top;
            for (int i = 0; i < _model.Options.Count; i++)
            {
                if (!_model.Options[i].Visible()) continue;
                int column = wide ? 0 : i % 2;
                int row = wide ? i : i / 2;
                AddButton(new Rect(-0.034f + column * 0.116f, top - row * RowPitch - ButtonHeight,
                    wide ? 0.224f : 0.108f, ButtonHeight), -1, i, false);
            }
            _surface.Clear();
            _surface.SetVertices(_vertices);
            _surface.SetUVs(0, _uv);
            _surface.SetTriangles(_indices, 0);
            _surface.RecalculateBounds();
        }

        private void AddButton(Rect rect, int category, int option, bool close)
        {
            int start = _vertices.Count;
            RoundedRect(rect, -0.002f, 1, 0.004f);
            _buttons.Add(new Button { Rect = rect, Category = category, Option = option, Close = close,
                Start = start, Count = _vertices.Count - start, Depth = -0.002f });
        }

        private void RoundedRect(Rect rect, float z, int palette, float radius)
        {
            int start = _vertices.Count;
            _vertices.Add(new Vector3(rect.center.x, rect.center.y, z));
            _uv.Add(PaletteUv(palette));
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
                    corner < 2 ? rect.yMax - radius : rect.yMin + radius);
                for (int j = 0; j <= 4; j++)
                {
                    float angle = (corner * 90f + j * 22.5f) * Mathf.Deg2Rad;
                    _vertices.Add(new Vector3(center.x + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius, z));
                    _uv.Add(PaletteUv(palette));
                }
            }
            for (int i = 0; i < 20; i++)
            { _indices.Add(start); _indices.Add(start + 1 + (i + 1) % 20); _indices.Add(start + 1 + i); }
        }

        private void CrossStroke(Vector2 a, Vector2 b)
        {
            Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * 0.001f;
            int start = _vertices.Count;
            foreach (Vector2 point in new[] { a - n, a + n, b + n, b - n })
            { _vertices.Add(new Vector3(point.x, point.y, -0.004f)); _uv.Add(PaletteUv(6)); }
            _indices.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        private static Vector2 PaletteUv(int index) => new Vector2((index + 0.5f) / 8f, 0.5f);

        private void BuildOptionTexts()
        {
            bool wide = _drawnCategory == MenuCategory.Biblioteca || _drawnCategory == MenuCategory.ImportarYNubes;
            for (int i = 0; i < _labels.Length; i++)
                _labels[i] = i < _model.Options.Count ? Wrap(_model.Options[i].Text, wide ? 0.210f : 0.094f) : string.Empty;
            float top = _drawnCategory == MenuCategory.Hora ? Top - 0.056f : Top;
            _optionsLeft.transform.localPosition = new Vector3(-0.027f, top - 0.003f, -0.004f);
            _optionsRight.transform.localPosition = new Vector3(0.089f, top - 0.003f, -0.004f);
            _optionsLeft.text = ColumnText(0, wide);
            _optionsRight.text = wide ? string.Empty : ColumnText(1, false);
            if (_drawnCategory == MenuCategory.Entornos)
            {
                _environmentLeft.text = _labels[0];
                _environmentRight.text = _labels[1];
            }
        }

        private string ColumnText(int column, bool wide)
        {
            var result = new StringBuilder();
            for (int i = column; i < _model.Options.Count; i += wide ? 1 : 2)
            {
                if (result.Length > 0) result.Append('\n');
                string label = _labels[i];
                result.Append(label);
                if (!label.Contains("\n")) result.Append('\n');
            }
            return result.ToString();
        }

        private string Wrap(string text, float width)
        {
            _font.RequestCharactersInTexture(text, 64);
            var result = new StringBuilder();
            float lineWidth = 0f;
            foreach (string line in text.Split('\n'))
            {
                if (result.Length > 0) { result.Append('\n'); lineWidth = 0f; }
                foreach (string word in line.Split(' '))
                {
                    float wordWidth = 0f;
                    foreach (char c in word)
                        if (_font.GetCharacterInfo(c, out CharacterInfo glyph, 64)) wordWidth += glyph.advance * _optionsLeft.characterSize * 0.1f;
                    _font.GetCharacterInfo(' ', out CharacterInfo space, 64);
                    float gap = lineWidth > 0f ? space.advance * _optionsLeft.characterSize * 0.1f : 0f;
                    if (lineWidth > 0f && lineWidth + gap + wordWidth > width)
                    { result.Append('\n'); lineWidth = 0f; gap = 0f; }
                    else if (gap > 0f) result.Append(' ');
                    result.Append(word);
                    lineWidth += gap + wordWidth;
                }
            }
            return result.ToString();
        }

        private bool Enabled(Button button) => button.Option < 0 || _model.Options[button.Option].Enabled();

        private void UpdateButtons()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                Button button = _buttons[i];
                bool enabled = Enabled(button);
                bool selected = button.Category == (int)_model.SelectedCategory ||
                    (button.Option >= 0 && _model.Options[button.Option].Marked());
                bool pressed = i == _pressed && Time.unscaledTime < _pressedUntil;
                int palette = !enabled ? 4 : pressed ? 5 : _hover.Contains(i) ? 3 : selected ? 2 : 1;
                float z = pressed ? -0.0005f : -0.002f;
                if (palette == button.Palette && Mathf.Approximately(z, button.Depth)) continue;
                button.Palette = palette; button.Depth = z;
                for (int j = button.Start; j < button.Start + button.Count; j++)
                {
                    _uv[j] = PaletteUv(palette);
                    Vector3 vertex = _vertices[j]; vertex.z = z; _vertices[j] = vertex;
                }
                _surfaceDirty = true;
            }
            if (!_surfaceDirty) return;
            _surface.SetVertices(_vertices);
            _surface.SetUVs(0, _uv);
            _surfaceDirty = false;
        }

        public void SetOpen(bool open, Transform head)
        {
            _open = open;
            if (open && head != null && (!_placed || NeedsPlacement(head))) Place(head);
            if (!open) { _hover.Clear(); _rayRenderer.enabled = false; }
            Refresh();
        }

        private bool NeedsPlacement(Transform head)
        {
            if (Vector3.Distance(head.position, transform.position) > 1.5f) return true;
            Camera camera = head.GetComponent<Camera>();
            if (camera == null) return Vector3.Dot(head.forward, transform.position - head.position) <= 0f;
            Vector3 p = camera.WorldToViewportPoint(transform.position);
            return p.z <= 0f || p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f ||
                Vector3.Dot(transform.forward, head.position - transform.position) >= 0f;
        }

        private void Place(Transform head)
        {
            transform.SetPositionAndRotation(head.position + head.forward * 0.45f - head.up * 0.10f,
                head.rotation * Quaternion.Euler(15f, 0f, 0f));
            _placed = true; // Nunca se parenta al rig: permanece fijo en el mundo.
        }

        private void Update()
        {
            if (_surfaceMaterial == null) return;
            float next = Mathf.MoveTowards(_alpha, _open ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            if (!Mathf.Approximately(next, _alpha)) SetVisibility(next);
            long minute = DateTime.Now.Ticks / TimeSpan.TicksPerMinute;
            if (_minute != minute)
            {
                _minute = minute;
                float battery = SystemInfo.batteryLevel;
                string level = battery < 0f ? "Batería: —" : "Batería: " + Mathf.RoundToInt(battery * 100f) + "%";
                string time = DateTime.Now.ToString("HH:mm");
                _header.text = time + "   " + level;
                _clock.text = time + "\n" + level;
            }
            if (_notice.gameObject.activeSelf && Time.unscaledTime >= _noticeUntil) _notice.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_notice != null && _notice.gameObject.activeSelf && _noticeHead != null)
                _notice.transform.SetPositionAndRotation(_noticeHead.position + _noticeHead.forward * 0.55f +
                    _noticeHead.up * 0.17f, _noticeHead.rotation);
        }

        private void SetVisibility(float alpha)
        {
            _alpha = alpha;
            _surfaceMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            _textMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            _surfaceRenderer.enabled = alpha > 0f;
            foreach (TextMesh text in _panelTexts) text.GetComponent<MeshRenderer>().enabled = alpha > 0f;
            if (alpha <= 0f) _rayRenderer.enabled = false;
        }

        public void ShowNotice(string message, Transform head)
        {
            _noticeHead = head;
            _notice.text = message;
            _noticeUntil = Time.unscaledTime + 5f;
            _notice.gameObject.SetActive(head != null);
        }

        internal void BeginPointers()
        {
            _hover.Clear();
            Array.Clear(_rayVertices, 0, _rayVertices.Length);
            _rayRenderer.enabled = false;
        }

        internal void EndPointers()
        {
            UpdateButtons();
            _rays.vertices = _rayVertices;
            _rays.RecalculateBounds();
        }

        internal bool Intersect(Ray ray, out Vector3 hit)
        {
            hit = default;
            Vector3 origin = transform.InverseTransformPoint(ray.origin);
            Vector3 direction = transform.InverseTransformDirection(ray.direction);
            if (origin.z >= 0f || direction.z <= 0.0001f) return false;
            float distance = -origin.z / direction.z;
            if (distance > 3f) return false;
            Vector3 local = origin + direction * distance;
            if (Mathf.Abs(local.x) > 0.2f || Mathf.Abs(local.y) > 0.14f) return false;
            hit = transform.TransformPoint(local);
            return true;
        }

        internal void SetRay(int slot, Vector3 start, Vector3 end)
        {
            Vector3 a = transform.InverseTransformPoint(start);
            Vector3 b = transform.InverseTransformPoint(end) - Vector3.forward * 0.005f;
            Vector3 side = Vector3.Cross(b - a, Vector3.up).normalized * 0.0007f;
            if (side.sqrMagnitude < 0.00000001f) side = Vector3.right * 0.0007f;
            int i = slot * 4;
            _rayVertices[i] = a - side; _rayVertices[i + 1] = a + side;
            _rayVertices[i + 2] = b + side; _rayVertices[i + 3] = b - side;
            _rayRenderer.enabled = _open && _alpha > 0f;
        }

        internal void Hover(Vector3 local)
        {
            int hit = Hit(local);
            if (hit >= 0 && Enabled(_buttons[hit])) _hover.Add(hit);
        }

        private int Hit(Vector3 local)
        {
            for (int i = 0; i < _buttons.Count; i++)
                if (_buttons[i].Rect.Contains(new Vector2(local.x, local.y))) return i;
            return -1;
        }

        internal bool Activate(Vector3 local)
        {
            if (!_open || _alpha < 0.99f || _activationFrame == Time.frameCount) return false;
            int hit = Hit(local);
            if (hit < 0 || !Enabled(_buttons[hit])) return false;
            Button button = _buttons[hit];
            _activationFrame = Time.frameCount;
            if (button.Close) _close?.Invoke();
            else if (button.Category >= 0) _model.Select((MenuCategory)button.Category);
            else _model.Invoke(button.Option);
            _pressed = hit;
            _pressedUntil = Time.unscaledTime + 0.12f;
            return true;
        }

        private void OnFontRebuilt(Font font)
        {
            if (font != _font) return;
            // La fuente dinámica puede cambiar de atlas al aparecer nuevos caracteres.
            _textMaterial.mainTexture = _font.material.mainTexture;
            _noticeMaterial.mainTexture = _font.material.mainTexture;
        }

        private void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            if (_model != null) _model.Changed -= Refresh;
            if (_surface != null) Destroy(_surface);
            if (_rays != null) Destroy(_rays);
            if (_surfaceMaterial != null) Destroy(_surfaceMaterial);
            if (_textMaterial != null) Destroy(_textMaterial);
            if (_noticeMaterial != null) Destroy(_noticeMaterial);
            if (_palette != null) Destroy(_palette);
        }
    }
}
