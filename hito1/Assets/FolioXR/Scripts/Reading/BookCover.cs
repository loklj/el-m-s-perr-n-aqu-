using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Reading
{
    /// <summary>
    /// Pedido del usuario (11 nov 2026): tapas de cuero del color elegido con el título en la portada y
    /// en el lomo, o las tapas propias del libro (primera y última página) si las tiene. Con tapas propias
    /// (14 nov 2026) el arte va de borde a borde y la tapa toma el color del borde de la portada.
    /// El libro está abierto: las tapas se ven por fuera (+Z local). Libro normal: la portada queda detrás
    /// de las páginas izquierdas; en manga, detrás de las derechas. La forma y la ubicación las pone BookBody.
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
        private int _leatherIndex;
        private Color? _artColor;
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
            _leatherIndex = leatherIndex;
            _titleText = string.IsNullOrEmpty(title) ? "Folio XR" : title;
            UpdateMaterial();
            _title.gameObject.SetActive(!ownCovers);
            _spine.gameObject.SetActive(!ownCovers);
            _frontImage.enabled = ownCovers && _frontTexture != null;
            _backImage.enabled = ownCovers && _backTexture != null;
            Layout();
            if (ownCovers) _book.RequestCoverPages(this);
        }

        /// <summary>
        /// Cuero del color elegido, o (con tapas propias) el color del borde de la portada: así los cantos de la
        /// tapa y el lomo combinan con el arte, como en un cómic de papel.
        /// </summary>
        private void UpdateMaterial()
        {
            if (_coverRenderer == null) return;
            Material material = _coverRenderer.material;
            material.mainTexture = _leather;
            if (_own)
            {
                material.mainTextureScale = new Vector2(8f, 8f);
                material.color = _artColor ?? new Color(0.22f, 0.21f, 0.2f);
            }
            else
            {
                material.mainTextureScale = new Vector2(3f, 3f);
                material.color = LeatherColors[Mathf.Clamp(_leatherIndex, 0, LeatherColors.Length - 1)];
            }
        }

        /// <summary>Color promedio del borde de una imagen (en la GPU: las páginas no se pueden leer desde la CPU).</summary>
        private static Color? EdgeColor(Texture texture)
        {
            if (texture == null) return null;
            const int n = 32, band = 3;
            RenderTexture rt = null;
            Texture2D small = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(n, n, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                small = new Texture2D(n, n, TextureFormat.RGBA32, false, false);
                small.ReadPixels(new Rect(0, 0, n, n), 0, 0, false);
                small.Apply(false, false);
                Color32[] pixels = small.GetPixels32();
                long r = 0, g = 0, b = 0;
                int count = 0;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        if (x >= band && x < n - band && y >= band && y < n - band) continue;
                        Color32 p = pixels[y * n + x];
                        r += p.r; g += p.g; b += p.b; count++;
                    }
                if (count == 0) return null;
                // Un poco más oscuro: el canto de la tapa recibe menos luz que la cara.
                const float shade = 0.9f / 255f;
                return new Color(r * shade / count, g * shade / count, b * shade / count, 1f);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[FolioXR] Tapa: no se pudo tomar el color de la portada: " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (small != null) Destroy(small);
            }
        }

        // BookBody: la portada cuelga de la mitad que se pliega y la contraportada de la que queda quieta.
        private Transform _frontHalf, _backHalf;

        internal void AttachFront(Transform frontHalf, Transform backHalf)
        {
            if (_frontHalf == frontHalf && _backHalf == backHalf) return;
            _frontHalf = frontHalf; _backHalf = backHalf;
            _frontImage.transform.SetParent(frontHalf, false);
            _title.transform.SetParent(frontHalf, false);
            _backImage.transform.SetParent(backHalf, false);
        }

        internal bool Owns(Transform item) => item == _title.transform || item == _spine.transform ||
            item == _frontImage.transform || item == _backImage.transform;

        /// <summary>Mallas del arte (de borde a borde, con esquinas redondeadas) que arma BookBody.</summary>
        internal void SetArtMeshes(Mesh front, Mesh back)
        {
            SetMesh(_frontImage, front);
            SetMesh(_backImage, back);
        }

        private static void SetMesh(Renderer renderer, Mesh mesh)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != mesh) filter.sharedMesh = mesh;
            renderer.transform.localPosition = Vector3.zero;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
        }

        /// <summary>Título de la portada, sobre la cara de afuera de la tapa (espacio de su mitad).</summary>
        internal void PlaceTitle(Vector3 center, float width, float height)
        {
            if (_textMaterial != null && _font != null) _textMaterial.mainTexture = _font.material.mainTexture;
            string text = Wrap(_titleText, 16, 3);
            if (_title.text != text) _title.text = text;
            _title.transform.localPosition = center + new Vector3(0f, height * 0.12f, 0.0004f);
            _title.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _title.characterSize = Fit(_title.text, width * 0.8f, height * 0.35f);
        }

        /// <summary>
        /// Título del lomo, de arriba hacia abajo como en los libros en español. Con el libro abierto queda
        /// detrás; cerrado, en el canto. En libros muy finos (cómics) no hay lomo donde escribir.
        /// </summary>
        internal void PlaceSpineLabel(Vector3 position, Vector3 outward, float length, float thickness)
        {
            string text = Wrap(_titleText, 40, 1);
            if (_spine.text != text) _spine.text = text;
            bool visible = !_own && thickness >= 0.004f;
            if (_spine.gameObject.activeSelf != visible) _spine.gameObject.SetActive(visible);
            if (!visible) return;
            _spine.transform.localPosition = position;
            _spine.transform.localRotation = Quaternion.LookRotation(-outward, Vector3.up) * Quaternion.Euler(0f, 0f, -90f);
            _spine.characterSize = Fit(_spine.text, length, thickness * 0.7f);
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
            _artColor = EdgeColor(front);
            UpdateMaterial();
            _frontImage.enabled = _own && front != null;
            _backImage.enabled = _own && back != null;
            Layout();
        }

        /// <summary>Recoloca títulos e imágenes: la forma del libro (y dónde va cada cosa) la decide BookBody.</summary>
        internal void Layout()
        {
            if (_book != null && _book.Body != null) _book.Body.Layout();
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
