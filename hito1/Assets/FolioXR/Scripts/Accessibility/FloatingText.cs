using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using FolioXR.Books;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace FolioXR.Accessibility
{
    /// <summary>Panel de texto flotante (fondo oscuro + texto 3D) que usan los subtítulos y el visor de licencias.</summary>
    internal sealed class TextPanel : IDisposable
    {
        internal readonly Transform Root;
        private readonly TextMesh _text;
        private readonly Material _background, _textMaterial;
        private readonly Transform _quad;

        internal TextPanel(string name, Material source, Font font, float characterSize)
        {
            Root = new GameObject(name).transform;
            UnityEngine.Object.DontDestroyOnLoad(Root.gameObject);
            _background = new Material(source) { name = "FolioXR fondo · " + name, mainTexture = Texture2D.whiteTexture };
            FolioXR.Tools.ToolVisuals.MakeTransparent(_background);
            _background.renderQueue = (int)RenderQueue.Transparent + 10;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(quad.GetComponent<Collider>());
            quad.name = "Fondo";
            quad.transform.SetParent(Root, false);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _background;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _quad = quad.transform;

            var textObject = new GameObject("Texto");
            textObject.transform.SetParent(Root, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            _text = textObject.AddComponent<TextMesh>();
            _text.font = font;
            _text.fontSize = 64;
            _text.characterSize = characterSize;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            var textRenderer = textObject.GetComponent<MeshRenderer>();
            Shader shader = Resources.Load<Shader>("FolioXRText3D");
            _textMaterial = shader != null && shader.isSupported
                ? new Material(shader) { name = "FolioXR texto · " + name, mainTexture = font.material.mainTexture }
                : new Material(font.material);
            _textMaterial.renderQueue = (int)RenderQueue.Transparent + 11;
            textRenderer.sharedMaterial = _textMaterial;
            textRenderer.shadowCastingMode = ShadowCastingMode.Off;
            Root.gameObject.SetActive(false);
        }

        internal void Show(string text, TextAlignment alignment, Color background, Color color, float padding = 0.03f)
        {
            _text.alignment = alignment;
            _text.anchor = alignment == TextAlignment.Left ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            _text.text = text;
            _text.color = color;
            _background.color = background;
            if (_textMaterial.HasProperty("_MainTex")) _textMaterial.mainTexture = _text.font.material.mainTexture;
            Root.gameObject.SetActive(true);
            // Medir el texto para ajustar el fondo: los límites del renderer están en coordenadas del mundo.
            Root.rotation = Quaternion.identity;
            Vector3 scale = Root.localScale;
            Root.localScale = Vector3.one;
            Bounds bounds = _text.GetComponent<MeshRenderer>().bounds;
            Root.localScale = scale;
            Vector3 size = bounds.size;
            _quad.localScale = new Vector3(size.x + padding * 2f, size.y + padding * 2f, 1f);
            Vector3 center = bounds.center - Root.position;
            _quad.localPosition = new Vector3(center.x, center.y, 0f);
            if (alignment == TextAlignment.Left) _text.transform.localPosition = new Vector3(-size.x * 0.5f, 0f, -0.002f);
            else _text.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            _quad.localPosition = new Vector3(0f, 0f, 0f);
        }

        internal void Hide() => Root.gameObject.SetActive(false);

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            if (_background != null) UnityEngine.Object.Destroy(_background);
            if (_textMaterial != null) UnityEngine.Object.Destroy(_textMaterial);
        }

        /// <summary>Material URP Unlit de una página del libro (el mismo origen que usan los menús).</summary>
        internal static Material PageMaterial()
        {
            FolioBook book = UnityEngine.Object.FindAnyObjectByType<FolioBook>();
            Renderer page = book != null ? (book.leftPage != null ? book.leftPage : book.rightPage) : null;
            return page != null ? page.sharedMaterial : null;
        }

        /// <summary>Corta en líneas de hasta <paramref name="width"/> caracteres, respetando palabras.</summary>
        internal static List<string> Wrap(string text, int width)
        {
            var lines = new List<string>();
            foreach (string raw in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                string line = raw.Replace('\t', ' ');
                while (line.Length > width)
                {
                    int cut = line.LastIndexOf(' ', width);
                    if (cut <= width / 3) cut = width;
                    lines.Add(line.Substring(0, cut).TrimEnd());
                    line = line.Substring(cut).TrimStart();
                }
                lines.Add(line);
            }
            return lines;
        }
    }

    /// <summary>
    /// Subtítulos de los avisos (encargo 18): cada aviso de la biblioteca o del menú aparece como texto
    /// bajo la vista durante unos segundos, aunque el menú esté cerrado. Se apaga en Ajustes → Accesibilidad.
    /// </summary>
    public sealed class CaptionView : MonoBehaviour
    {
        public static CaptionView Instance { get; private set; }
        private TextPanel _panel;
        private float _hideAt;
        private bool _placed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("FolioXR Subtítulos");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CaptionView>();
        }

        /// <summary>Muestra un subtítulo (si los subtítulos están activados).</summary>
        public static void Show(string message)
        {
            if (Instance != null && FolioAccessibility.Captions) Instance.Present(message);
        }

        private void OnEnable() => BookLibrary.Notice += Show;
        private void OnDisable() => BookLibrary.Notice -= Show;

        private void Present(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (_panel == null)
            {
                Material source = TextPanel.PageMaterial();
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (source == null || font == null) return;
                _panel = new TextPanel("Subtítulo", source, font, 0.0032f);
            }
            string text = string.Join("\n", TextPanel.Wrap(message, 46));
            bool contrast = FolioAccessibility.HighContrast;
            _panel.Show(text, TextAlignment.Center, contrast ? Color.black : new Color(0f, 0f, 0f, 0.78f),
                        contrast ? new Color(1f, 0.92f, 0.2f) : Color.white);
            _hideAt = Time.unscaledTime + 3.5f + 0.045f * message.Length;
            _placed = false;
            Debug.Log("[FolioXR] Subtítulo: " + message.Replace('\n', ' '));
        }

        private void LateUpdate()
        {
            if (_panel == null || !_panel.Root.gameObject.activeSelf) return;
            if (Time.unscaledTime > _hideAt || !FolioAccessibility.Captions) { _panel.Hide(); return; }
            Camera camera = Camera.main;
            if (camera == null) return;
            Transform head = camera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = head.up;
            forward.Normalize();
            Vector3 target = head.position + forward * 1.1f + Vector3.down * 0.32f;
            Quaternion rotation = Quaternion.LookRotation(target - head.position, Vector3.up);
            // Sigue a la cabeza con calma (o al instante con «reducir movimiento»).
            float k = !_placed || FolioAccessibility.ReduceMotion ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f);
            _panel.Root.SetPositionAndRotation(Vector3.Lerp(_panel.Root.position, target, k),
                                               Quaternion.Slerp(_panel.Root.rotation, rotation, k));
            _placed = true;
        }

        private void OnDestroy() => _panel?.Dispose();
    }

    /// <summary>
    /// «Acerca de» y «Licencias de terceros» (encargo 18, parte 4). Lee StreamingAssets/Licencias/indice.txt
    /// (lo regenera FolioXRLicenseIndex al compilar) y muestra el texto completo de cada licencia por páginas.
    /// </summary>
    public sealed class LicenseViewer : MonoBehaviour
    {
        public static LicenseViewer Instance { get; private set; }
        public const int LinesPerPage = 30;

        public IReadOnlyList<string> Files => _files;
        public bool Loading { get; private set; }
        public bool Showing => _panel != null && _panel.Root.gameObject.activeSelf;
        public string Title { get; private set; } = string.Empty;
        public int Page { get; private set; }
        public int PageCount => Mathf.Max(1, (_lines.Count + LinesPerPage - 1) / LinesPerPage);
        public static event Action Changed;

        private readonly List<string> _files = new List<string>();
        private List<string> _lines = new List<string>();
        private TextPanel _panel;
        private bool _indexLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("FolioXR Licencias");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<LicenseViewer>();
        }

        public static string Version => Application.version + (Debug.isDebugBuild ? " (desarrollo)" : string.Empty);

        public void LoadIndex()
        {
            if (_indexLoaded || Loading) return;
            StartCoroutine(Read("indice.txt", text =>
            {
                _files.Clear();
                foreach (string line in (text ?? string.Empty).Split('\n'))
                {
                    string name = line.Trim();
                    if (name.Length > 0 && !name.StartsWith("#")) _files.Add(name);
                }
                _indexLoaded = true;
            }));
        }

        public void Open(string file)
        {
            StartCoroutine(Read(file, text =>
            {
                Title = System.IO.Path.GetFileNameWithoutExtension(file);
                _lines = TextPanel.Wrap(text ?? "(No se pudo leer la licencia.)", 64);
                Page = 0;
                Draw(true);
            }));
        }

        public void NextPage() { if (Page + 1 < PageCount) { Page++; Draw(false); } }
        public void PreviousPage() { if (Page > 0) { Page--; Draw(false); } }

        public void Close()
        {
            _panel?.Hide();
            Changed?.Invoke();
        }

        private void Draw(bool place)
        {
            if (_panel == null)
            {
                Material source = TextPanel.PageMaterial();
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (source == null || font == null) return;
                _panel = new TextPanel("Licencia", source, font, 0.0018f);
            }
            var builder = new StringBuilder();
            builder.Append(Title).Append("  ·  página ").Append(Page + 1).Append(" de ").Append(PageCount).Append("\n\n");
            for (int i = Page * LinesPerPage; i < Mathf.Min(_lines.Count, (Page + 1) * LinesPerPage); i++)
                builder.Append(_lines[i]).Append('\n');
            bool contrast = FolioAccessibility.HighContrast;
            _panel.Show(builder.ToString(), TextAlignment.Left, contrast ? Color.black : new Color(0.06f, 0.07f, 0.09f, 0.94f),
                        contrast ? Color.white : new Color(0.92f, 0.93f, 0.95f), 0.04f);
            Camera camera = Camera.main;
            if (place && camera != null)
            {
                Transform head = camera.transform;
                Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                // A la izquierda del frente, para que el menú quede libre a la derecha.
                Vector3 position = head.position + forward * 0.95f - Vector3.Cross(Vector3.up, forward) * 0.25f + Vector3.down * 0.05f;
                _panel.Root.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
            }
            Changed?.Invoke();
        }

        private static IEnumerator Read(string file, Action<string> done)
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "Licencias", file);
            if (!path.Contains("://")) path = "file://" + path;
            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[FolioXR] No se pudo leer " + file + ": " + request.error);
                    done(null);
                }
                else done(request.downloadHandler.text);
            }
            Changed?.Invoke();
        }

        private void OnDestroy() => _panel?.Dispose();
    }
}
