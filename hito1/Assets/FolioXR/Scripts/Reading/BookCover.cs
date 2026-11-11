using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Reading
{
    /// <summary>
    /// Pedido del usuario (11 nov 2026): tapas de cuero del color elegido con el título en la portada y
    /// en el lomo, o las tapas propias del libro (primera y última página) si las tiene.
    /// El libro está abierto: las tapas se ven por fuera (+Z local). Libro normal: la portada queda detrás
    /// de las páginas izquierdas; en manga, detrás de las derechas.
    /// </summary>
    public sealed class BookCover : MonoBehaviour
    {
        public static readonly string[] LeatherNames = { "marrón", "negro", "rojo vino", "verde bosque", "azul marino", "morado", "miel" };
        public static readonly Color[] LeatherColors =
        {
            new Color(0.36f, 0.22f, 0.13f), new Color(0.09f, 0.08f, 0.08f), new Color(0.42f, 0.08f, 0.1f),
            new Color(0.12f, 0.27f, 0.16f), new Color(0.1f, 0.15f, 0.33f), new Color(0.3f, 0.14f, 0.38f),
            new Color(0.66f, 0.45f, 0.2f)
        };
        private static readonly Color Gold = new Color(0.93f, 0.8f, 0.45f);

        private FolioBook _book;
        private Renderer _coverRenderer;
        private Texture2D _leather;
        private TextMesh _title, _spine;
        private Renderer _frontImage, _backImage;
        private Material _frontMaterial, _backMaterial;
        private Texture2D _frontTexture, _backTexture;
        private bool _own, _rtl;
        private string _titleText = string.Empty;
        private Font _font;
        private Material _textMaterial;

        internal static BookCover Attach(FolioBook book)
        {
            var cover = book.gameObject.AddComponent<BookCover>();
            cover._book = book;
            cover.Build();
            return cover;
        }

        private void Build()
        {
            _coverRenderer = _book.cover != null ? _book.cover.GetComponent<Renderer>() : null;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _leather = LeatherTexture();
            _title = Label("Título de la portada");
            _spine = Label("Título del lomo");
            _frontImage = ImageQuad("Portada propia", out _frontMaterial);
            _backImage = ImageQuad("Contraportada propia", out _backMaterial);
        }

        private TextMesh Label(string name)
        {
            var item = new GameObject(name);
            item.transform.SetParent(transform, false);
            TextMesh text = item.AddComponent<TextMesh>();
            text.font = _font;
            text.fontSize = 64;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Gold;
            text.richText = false;
            var renderer = item.GetComponent<MeshRenderer>();
            if (_font != null)
            {
                // El material propio de la fuente dibuja encima de todo (ZTest Always) y el título se veía
                // a través de las páginas y el lomo. FolioXR/Text3D es igual pero respeta la profundidad.
                if (_textMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>("FolioXRText3D");
                    if (shader != null && shader.isSupported)
                        _textMaterial = new Material(shader) { name = "Título de tapa", mainTexture = _font.material.mainTexture };
                    else Debug.LogWarning("[FolioXR] Falta el sombreador FolioXR/Text3D; el título usará el de la fuente.");
                }
                renderer.sharedMaterial = _textMaterial != null ? _textMaterial : _font.material;
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return text;
        }

        private Renderer ImageQuad(string name, out Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            var renderer = quad.GetComponent<Renderer>();
            Material source = _book.leftPage != null ? _book.leftPage.sharedMaterial : null;
            material = source != null ? new Material(source) : null;
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.enabled = false;
            return renderer;
        }

        /// <summary>Grano de cuero en gris claro: el color lo pone el material, así sirve para todos.</summary>
        private static Texture2D LeatherTexture()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Cuero", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[size * size];
            var random = new System.Random(11);
            var cells = new Vector2[64];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = new Vector2((float)random.NextDouble() * size, (float)random.NextDouble() * size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Celdas de Voronoi (poros) + ruido fino.
                    float best = float.MaxValue, second = float.MaxValue;
                    foreach (Vector2 c in cells)
                    {
                        float dx = Mathf.Abs(x - c.x), dy = Mathf.Abs(y - c.y);
                        dx = Mathf.Min(dx, size - dx);
                        dy = Mathf.Min(dy, size - dy);
                        float d = dx * dx + dy * dy;
                        if (d < best) { second = best; best = d; }
                        else if (d < second) second = d;
                    }
                    float crease = Mathf.Clamp01((Mathf.Sqrt(second) - Mathf.Sqrt(best)) / 6f);
                    float grain = (float)random.NextDouble() * 0.06f;
                    float value = 0.78f + 0.18f * crease + grain;
                    pixels[y * size + x] = new Color(value, value, value, 1f);
                }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>Aplica el estilo del libro abierto. Con tapas propias, pide la primera y la última página.</summary>
        public void Apply(bool ownCovers, int leatherIndex, string title, bool rightToLeft)
        {
            _own = ownCovers;
            _rtl = rightToLeft;
            _titleText = string.IsNullOrEmpty(title) ? "Folio XR" : title;
            Color color = LeatherColors[Mathf.Clamp(leatherIndex, 0, LeatherColors.Length - 1)];
            if (_coverRenderer != null)
            {
                Material material = _coverRenderer.material;
                material.mainTexture = _leather;
                material.mainTextureScale = new Vector2(3f, 3f);
                material.color = color;
            }
            _title.gameObject.SetActive(!ownCovers);
            _spine.gameObject.SetActive(!ownCovers);
            _frontImage.enabled = ownCovers && _frontTexture != null;
            _backImage.enabled = ownCovers && _backTexture != null;
            Layout();
            if (ownCovers) _book.RequestCoverPages(this);
        }

        internal void SetRightToLeft(bool value)
        {
            _rtl = value;
            Layout();
        }

        internal void SetCoverPages(Texture2D front, Texture2D back)
        {
            if (_frontTexture != null && _frontTexture != front) Destroy(_frontTexture);
            if (_backTexture != null && _backTexture != back) Destroy(_backTexture);
            _frontTexture = front;
            _backTexture = back;
            if (_frontMaterial != null) _frontMaterial.mainTexture = front;
            if (_backMaterial != null) _backMaterial.mainTexture = back;
            _frontImage.enabled = _own && front != null;
            _backImage.enabled = _own && back != null;
            Layout();
        }

        /// <summary>Recoloca títulos e imágenes según el tamaño actual de las páginas.</summary>
        internal void Layout()
        {
            if (_book == null || _book.leftPage == null) return;
            Vector3 page = _book.leftPage.transform.localScale;
            float width = page.x, height = page.y;
            float half = width * 0.5f + _book.gutter * 0.5f;
            float outside = 0.012f + 0.0015f; // Cara exterior de la tapa (grosor 12 mm).
            // Visto desde fuera (+Z) la izquierda y la derecha se invierten: la portada de un libro normal
            // (detrás de las páginas izquierdas, x < 0) queda a la derecha de quien mira por fuera.
            float front = _rtl ? half : -half;
            Quaternion facingOut = Quaternion.Euler(0f, 180f, 0f);

            _frontImage.transform.localPosition = new Vector3(front, 0f, outside);
            _frontImage.transform.localRotation = facingOut;
            _frontImage.transform.localScale = new Vector3(width, height, 1f);
            _backImage.transform.localPosition = new Vector3(-front, 0f, outside);
            _backImage.transform.localRotation = facingOut;
            _backImage.transform.localScale = new Vector3(width, height, 1f);

            if (_textMaterial != null && _font != null) _textMaterial.mainTexture = _font.material.mainTexture;
            _title.text = Wrap(_titleText, 16, 3);
            _title.transform.localPosition = new Vector3(front, height * 0.12f, outside);
            _title.transform.localRotation = facingOut;
            _title.characterSize = Fit(_title.text, width * 0.8f, height * 0.35f);

            _spine.text = Wrap(_titleText, 40, 1);
            _spine.transform.localPosition = new Vector3(0f, 0f, outside);
            // Lomo: el texto corre de arriba hacia abajo, como en los libros en español.
            _spine.transform.localRotation = facingOut * Quaternion.Euler(0f, 0f, -90f);
            _spine.characterSize = Fit(_spine.text, height * 0.85f, Mathf.Max(0.006f, _book.gutter + 0.014f));
        }

        private float Fit(string text, float maxWidth, float maxHeight)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return 0.001f;
            _font.RequestCharactersInTexture(text, 64);
            float widest = 0f;
            int lines = 0;
            foreach (string line in text.Split('\n'))
            {
                lines++;
                float advance = 0f;
                foreach (char c in line)
                    if (_font.GetCharacterInfo(c, out CharacterInfo info, 64)) advance += info.advance;
                widest = Mathf.Max(widest, advance);
            }
            // TextMesh: un píxel de la fuente mide characterSize / 10 metros.
            float byWidth = maxWidth / Mathf.Max(1f, widest * 0.1f);
            float byHeight = maxHeight / Mathf.Max(1f, lines * 64f * 1.15f * 0.1f);
            return Mathf.Min(byWidth, byHeight);
        }

        private static string Wrap(string text, int perLine, int maxLines)
        {
            var words = text.Split(' ');
            var lines = new List<string>();
            string line = string.Empty;
            foreach (string word in words)
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > perLine)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = line.Length == 0 ? word : line + " " + word;
            }
            if (line.Length > 0) lines.Add(line);
            if (lines.Count > maxLines)
            {
                lines.RemoveRange(maxLines, lines.Count - maxLines);
                lines[maxLines - 1] = lines[maxLines - 1].TrimEnd('.') + "…";
            }
            return string.Join("\n", lines);
        }

        private void OnDestroy()
        {
            if (_leather != null) Destroy(_leather);
            if (_frontTexture != null) Destroy(_frontTexture);
            if (_backTexture != null) Destroy(_backTexture);
            if (_frontMaterial != null) Destroy(_frontMaterial);
            if (_backMaterial != null) Destroy(_backMaterial);
            if (_textMaterial != null) Destroy(_textMaterial);
        }
    }
}
