using System;
using System.Collections.Generic;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Menu
{
    /// <summary>
    /// Panel en metros, frente -Z. Un TextMesh por botón, ajustado a su rectángulo.
    /// Hasta 21 renderizadores con diez opciones; presupuesto: 30 draw calls.
    /// </summary>
    public sealed class PanelMenuView : MonoBehaviour, IMenuView
    {
        private const float FadeSeconds = 0.15f;
        private const float RowPitch = 0.044f;
        private const float Top = 0.086f;
        private const float ButtonHeight = 0.04f;
        private MenuModel _model;
        private Action _close;
        private Material _surfaceMaterial;
        private Texture2D _palette;
        private Font _font;
        private Mesh _surface, _rays;
        private MeshRenderer _surfaceRenderer, _rayRenderer;
        private TextMesh _header, _clock, _notice;
        private TextMesh[] _panelTexts;
        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _indices = new List<int>();
        private readonly HashSet<int> _hover = new HashSet<int>();
        private readonly Vector3[] _rayVertices = new Vector3[8];
        private readonly Vector2[] _rayUv = new Vector2[8];
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
            public TextMesh Text;
            public string Label;
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
            // Las superficies van antes que el texto (cola 3000), aunque el orden por distancia diga otra cosa.
            _surfaceMaterial.renderQueue = (int)RenderQueue.Transparent - 1;
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
            _header = Label("Hora y batería", new Vector3(-0.184f, 0.123f, -0.004f), 0.01f);
            _clock = Label("Reloj grande", new Vector3(-0.027f, 0.083f, -0.004f), 0.018f);
            _notice = Label("Aviso del temporizador", Vector3.zero, 0.012f);
            _notice.anchor = TextAnchor.MiddleCenter;
            _notice.alignment = TextAlignment.Center;
            _notice.gameObject.SetActive(false);
            _panelTexts = new[] { _header, _clock };
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

        private TextMesh Label(string name, Vector3 position, float height)
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
            text.color = Color.white;
            // Revisión de Claude: el material propio de la fuente (GUI/Text Shader) es el mismo
            // que ya funciona en el aviso del libro. URP Unlit con el atlas Alpha8 podría
            // muestrear el color como negro y dejar el texto invisible sobre el panel oscuro.
            var renderer = item.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _font.material;
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
            bool layoutChanged = _drawnCategory != _model.SelectedCategory || _drawnCount != _model.Options.Count;
            if (layoutChanged)
            {
                _drawnCategory = _model.SelectedCategory;
                _drawnCount = _model.Options.Count;
                _hover.Clear();
                _pressed = -1;
                BuildLayout();
                _clock.gameObject.SetActive(_drawnCategory == MenuCategory.Hora);
            }
            RefreshButtonTexts();
            UpdateButtons();
        }

        private void BuildLayout()
        {
            foreach (Button button in _buttons)
            {
                button.Text.gameObject.SetActive(false);
                Destroy(button.Text.gameObject);
            }
            _buttons.Clear(); _vertices.Clear(); _uv.Clear(); _indices.Clear();
            RoundedRect(new Rect(-0.2f, -0.14f, 0.4f, 0.28f), 0f, 0, 0.012f);
            for (int i = 0; i < 5; i++)
                AddButton(new Rect(-0.192f, Top - i * RowPitch - ButtonHeight, 0.148f, ButtonHeight), i, -1, false);
            AddButton(new Rect(0.148f, 0.094f, 0.044f, 0.04f), -1, -1, true);
            bool wide = _drawnCategory == MenuCategory.Biblioteca || _drawnCategory == MenuCategory.ImportarYNubes;
            float top = _drawnCategory == MenuCategory.Hora ? Top - 0.056f : Top;
            for (int i = 0; i < _model.Options.Count; i++)
            {
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
            TextMesh text = Label("Texto de botón", new Vector3(rect.center.x, rect.center.y, -0.004f),
                category >= 0 ? 0.008f : 0.007f);
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.lineSpacing = 1f;
            text.color = new Color(1f, 1f, 1f, _alpha);
            text.GetComponent<MeshRenderer>().enabled = _alpha > 0f;
            _buttons.Add(new Button { Rect = rect, Category = category, Option = option, Close = close,
                Text = text, Start = start, Count = _vertices.Count - start, Depth = -0.002f });
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

        private static Vector2 PaletteUv(int index) => new Vector2((index + 0.5f) / 8f, 0.5f);

        private void RefreshButtonTexts()
        {
            foreach (Button button in _buttons)
            {
                string label = button.Close ? "×" : button.Category >= 0
                    ? MenuModel.CategoryNames[button.Category] : _model.Options[button.Option].Text;
                if (button.Label == label) continue;
                button.Label = label;
                FitButtonText(button, label);
            }
        }

        private void FitButtonText(Button button, string label)
        {
            TextMesh text = button.Text;
            string flat = string.Join(" ", label.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
            _font.RequestCharactersInTexture(flat + "H", 64);
            _font.GetCharacterInfo('H', out CharacterInfo glyph, 64);
            float initialSize = (button.Category >= 0 ? 0.008f : 0.007f) * 10f / Mathf.Max(1, glyph.glyphHeight);
            float width = button.Rect.width - 0.006f;
            float height = button.Rect.height - 0.006f;
            Renderer renderer = text.GetComponent<Renderer>();
            string best = flat;
            float bestSize = 0f;
            // Probar una línea y cada corte entre palabras: nunca más de dos líneas.
            // Las métricas de la fuente cubren también textos recién creados con el
            // renderer apagado; localBounds añade los límites de la geometría generada.
            for (int split = -1; split < flat.Length; split++)
            {
                if (split >= 0 && flat[split] != ' ') continue;
                text.text = split < 0 ? flat : flat.Substring(0, split) + "\n" + flat.Substring(split + 1);
                text.characterSize = initialSize;
                Bounds bounds = renderer.localBounds;
                Vector2 measured = MeasureText(text.text, initialSize);
                float fit = Mathf.Min(1f, width / Mathf.Max(0.00001f, bounds.size.x, measured.x),
                    height / Mathf.Max(0.00001f, bounds.size.y, measured.y));
                if (fit * initialSize > bestSize)
                {
                    bestSize = fit * initialSize;
                    best = text.text;
                }
            }
            text.text = best;
            text.characterSize = bestSize * 0.99f; // Margen para redondeos del atlas dinámico.
            Vector3 center = renderer.localBounds.center;
            text.transform.localPosition = new Vector3(button.Rect.center.x - center.x,
                button.Rect.center.y - center.y, -0.004f);
        }

        private Vector2 MeasureText(string value, float characterSize)
        {
            float width = 0f, advance = 0f, overhang = 0f;
            int lines = 1;
            foreach (char c in value)
            {
                if (c == '\n')
                {
                    width = Mathf.Max(width, advance + overhang * 2f);
                    advance = overhang = 0f;
                    lines++;
                    continue;
                }
                if (!_font.GetCharacterInfo(c, out CharacterInfo glyph, 64)) continue;
                advance += glyph.advance;
                overhang = Mathf.Max(overhang, -glyph.minX, glyph.maxX - glyph.advance);
            }
            width = Mathf.Max(width, advance + overhang * 2f);
            float lineHeight = _font.lineHeight * (64f / Mathf.Max(1, _font.fontSize));
            return new Vector2(width, lineHeight * lines) * (characterSize * 0.1f);
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
            // Sin el giro lateral (roll) de la cabeza: el panel queda derecho aunque la cabeza esté inclinada.
            Quaternion facing = Quaternion.LookRotation(head.forward, Vector3.up);
            transform.SetPositionAndRotation(head.position + head.forward * 0.45f - (facing * Vector3.up) * 0.10f,
                facing * Quaternion.Euler(15f, 0f, 0f));
            _placed = true; // Nunca se parenta al rig: permanece fijo en el mundo.
        }

        /// <summary>Recoloca el panel abierto frente a la vista (por ejemplo, después de un salto del rig).</summary>
        internal void PlaceInFront(Transform head)
        {
            if (_open && head != null) Place(head);
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
            _surfaceRenderer.enabled = alpha > 0f;
            foreach (TextMesh text in _panelTexts)
            {
                text.color = new Color(1f, 1f, 1f, alpha);
                text.GetComponent<MeshRenderer>().enabled = alpha > 0f;
            }
            foreach (Button button in _buttons)
            {
                button.Text.color = new Color(1f, 1f, 1f, alpha);
                button.Text.GetComponent<MeshRenderer>().enabled = alpha > 0f;
            }
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

        private void OnDestroy()
        {
            if (_model != null) _model.Changed -= Refresh;
            if (_surface != null) Destroy(_surface);
            if (_rays != null) Destroy(_rays);
            if (_surfaceMaterial != null) Destroy(_surfaceMaterial);
            if (_palette != null) Destroy(_palette);
        }
    }
}
