// Folio XR - Hito 1
// Puente entre Unity y el plugin Java FolioPdfRenderer (Assets/Plugins/Android).
// En el Quest dibuja paginas reales del PDF. En el Editor de Unity no existe
// PdfRenderer de Android, asi que devuelve una pagina de muestra generada.

using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace FolioXR.Reading
{
    /// <summary>Resultado de una peticion al renderizador.</summary>
    public sealed class PdfResult
    {
        public bool Ok;
        public string Data;      // "OK:<datos>" sin el prefijo
        public string ErrorKind; // PASSWORD, IO, MEMORY, PAGE, OTHER, TIMEOUT, PLATFORM
        public string ErrorDetail;
    }

    public sealed class PdfPageRenderer : IDisposable
    {
        private const string JavaClass = "com.loklj.folioxr.FolioPdfRenderer";
        // Cómics CBZ (ZIP de imágenes): mismo protocolo, otra clase Java (11 nov 2026).
        private const string ComicJavaClass = "com.loklj.folioxr.FolioComicRenderer";

        public static bool IsComic(string path)
        {
            string extension = string.IsNullOrEmpty(path) ? string.Empty : Path.GetExtension(path).ToLowerInvariant();
            return extension == ".cbz" || extension == ".zip" || extension == ".cbr";
        }
        public static bool IsEpub(string path) => string.Equals(Path.GetExtension(path), ".epub", StringComparison.OrdinalIgnoreCase);
        public static bool IsReflow(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".epub" || ext == ".txt" || ext == ".mobi" || ext == ".azw" || ext == ".azw3" || ext == ".docx" || ext == ".odt" || ext == ".rtf";
        }
        public const string TxtEncodingPrefsKey = "folioxr.txtEncoding";
        public static string TxtEncoding => PlayerPrefs.GetString(TxtEncodingPrefsKey, "auto");
        public static string FormatFailurePath { get; private set; }
        public static string FormatFailureDetail { get; private set; }
        public const string EpubFontPrefsKey = "folioxr.epubFontScale";
        public static float EpubFontScale => Mathf.Clamp(PlayerPrefs.GetFloat(EpubFontPrefsKey, 1f), 0.8f, 1.25f);
        public static string DrmFailurePath { get; private set; }
        public static event Action<string> PageCounted;
        public static event Action<string> RenderFailed;
        private const float TimeoutSeconds = 300f; // Apertura PDF grande cancelable; no truncar trabajos a los 30 s.

        private readonly string _pdfPath;
        private readonly string _cacheDir;
        private int _requestCounter;
        private string _contentsKey;
        private string _searchKey;
        public static bool IsSearchable(string path) => IsReflow(path);
        private readonly float _openedAt = Time.realtimeSinceStartup;
        private bool _firstPage = true, _lastCacheHit;
        private readonly string _instanceKey = Guid.NewGuid().ToString("N");
        private bool _disposed;
        private static Task _cleanup;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass _java;
#endif

        public PdfPageRenderer(string pdfPath)
        {
            _pdfPath = pdfPath;
            _cacheDir = Path.Combine(Application.temporaryCachePath, "folio-paginas");
            Directory.CreateDirectory(_cacheDir);
            if (_cleanup == null)
            {
                string folder = _cacheDir;
                _cleanup = Task.Run(() =>
                {
                    foreach (string pattern in new[] { "folio-*", "precarga-*" })
                        foreach (string stale in Directory.EnumerateFiles(folder, pattern)) TryDelete(stale);
                });
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            _java = new AndroidJavaClass(IsEpub(pdfPath) ? "com.loklj.folioxr.FolioEpubRenderer" :
                IsReflow(pdfPath) ? "com.loklj.folioxr.FolioTextRenderer" :
                string.Equals(Path.GetExtension(pdfPath), ".cbr", StringComparison.OrdinalIgnoreCase) ? "com.loklj.folioxr.FolioCbrRenderer" :
                IsComic(pdfPath) ? ComicJavaClass : JavaClass);
            if (IsReflow(pdfPath))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _java.CallStatic("init", activity, EpubFontScale);
                    _java.CallStatic("setTxtEncoding", TxtEncoding);
                    _java.CallStatic("setLayout", ReadingLayout.Font, ReadingLayout.LineSpacing, ReadingLayout.Margin);
                }
#endif
        }

        /// <summary>Ruta del PDF que abre este renderizador.</summary>
        public string PdfPath
        {
            get { return _pdfPath; }
        }

        public static bool IsRealRendererAvailable
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Pide el numero de paginas. Llama a done con el resultado.</summary>
        public IEnumerator GetPageCount(Action<int, PdfResult> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_disposed) { done(0, Fail("CANCEL", "Lectura cancelada")); yield break; }
            string key = NextKey("count");
            _java.CallStatic("requestPageCount", _pdfPath, key);
            PdfResult result = null;
            yield return WaitFor(key, r => result = r);
            int count = 0;
            if (result.Ok && !int.TryParse(result.Data, out count))
            {
                result = Fail("OTHER", "respuesta inesperada: " + result.Data);
            }
            FormatFailurePath = result.Ok ? null : _pdfPath;
            FormatFailureDetail = result.ErrorDetail;
            DrmFailurePath = result.ErrorKind == "DRM" ? _pdfPath : null;
            if (result.Ok) PageCounted?.Invoke(_pdfPath);
            done(count, result);
#else
            yield return null;
            PageCounted?.Invoke(_pdfPath);
            done(EditorPreviewPages, new PdfResult { Ok = true, Data = EditorPreviewPages.ToString() });
#endif
        }

        /// <summary>
        /// Dibuja una pagina (indice desde 0) y la entrega como Texture2D.
        /// Quien recibe la textura es responsable de destruirla (Object.Destroy).
        /// </summary>
        public IEnumerator RenderPage(int pageIndex, int widthPixels, Action<Texture2D, PdfResult> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            while (!_cleanup.IsCompleted) yield return null;
            if (_disposed) { done(null, Fail("CANCEL", "Lectura cancelada")); yield break; }
            float renderStart = Time.realtimeSinceStartup;
            string key = NextKey("page" + pageIndex);
            string pngPath = Path.Combine(_cacheDir, key + ".png");
            _java.CallStatic("requestRenderPage", _pdfPath, pageIndex, widthPixels, pngPath, key);
            PdfResult result = null;
            yield return WaitFor(key, r => result = r);
            if (!result.Ok)
            {
                RenderFailed?.Invoke(_pdfPath);
                done(null, result);
                yield break;
            }

            // Disco fuera del hilo de Unity; solo se trae una página acotada, nunca el libro.
            Task<byte[]> readPage = Task.Run(() => ReadPageImage(pngPath));
            while (!readPage.IsCompleted) yield return null;
            Texture2D texture = null;
            try
            {
                byte[] image = readPage.GetAwaiter().GetResult();
                if (_disposed) throw new IOException("Lectura cancelada");
                bool night = PageTheme.NeedsRemap(_pdfPath);
                texture = LoadRawBytes(image, night);
                if (texture == null)
                {
                    // PNG (EPUB) o JPEG (cómics).
                    byte[] png = image;
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    // Las páginas solo se muestrean en GPU; evitar la copia CPU a 4096 px
                    // salvo en modo noche, que necesita leer los píxeles una vez.
                    if (!texture.LoadImage(png, !night))
                    {
                        UnityEngine.Object.Destroy(texture);
                        texture = null;
                        result = Fail("IO", "no se pudo leer la imagen de la pagina");
                    }
                    else if (night)
                    {
                        PageTheme.RemapNight(texture.GetPixelData<byte>(0).AsSpan(), texture.format);
                        texture.Apply(true, true);
                    }
                }
            }
            catch (Exception e)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                texture = null;
                result = Fail("IO", e.Message);
            }
            finally
            {
                TryDelete(pngPath);
            }

            if (!result.Ok) RenderFailed?.Invoke(_pdfPath);
            if (texture != null)
            {
                ConfigureTexture(texture); ReadingDiagnostics.Track(texture);
                ReadingDiagnostics.Record(_lastCacheHit ? "page_precached_ms" : "page_uncached_ms", (Time.realtimeSinceStartup - renderStart) * 1000.0);
                if (_firstPage) { ReadingDiagnostics.Record("first_page_ms", (Time.realtimeSinceStartup - _openedAt) * 1000.0); _firstPage = false; }
            }
            done(texture, result);
#else
            yield return null;
            Texture2D preview = BuildEditorPreview(pageIndex, widthPixels);
            done(preview, new PdfResult { Ok = true, Data = preview.width + "x" + preview.height });
#endif
        }

        public IEnumerator GetContents(Action<ContentsList, string> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            CancelContents();
            if (IsComic(_pdfPath)) { done(new ContentsList(), null); yield break; }
            string key = _contentsKey = NextKey("contents");
            _java.CallStatic("requestContents", _pdfPath, key);
            PdfResult result = null;
            yield return WaitFor(key, r => result = r);
            if (_contentsKey != key) yield break;
            _contentsKey = null;
            if (result == null || !result.Ok) { done(null, result?.ErrorDetail ?? "No se pudo leer el índice."); yield break; }
            ContentsList entries = null;
            string error = null;
            try { entries = JsonUtility.FromJson<ContentsList>(result.Data); }
            catch (Exception) { error = "Respuesta de índice no válida."; }
            done(entries ?? new ContentsList(), error);
#else
            done(new ContentsList(), "Índice disponible en el visor.");
            yield break;
#endif
        }

        public void CancelContents()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_java != null && _contentsKey != null) _java.CallStatic("clearStatus", _contentsKey);
#endif
            _contentsKey = null;
        }

        /// <summary>Busca texto en el libro abierto desde el cursor dado ("" = principio).</summary>
        public IEnumerator Search(string query, string cursor, int limit, Action<SearchList, string> done)
        {
            CancelSearch();
            if (!IsSearchable(_pdfPath))
            {
                done(null, "Los PDF y cómics se muestran como imagen: no tienen texto que buscar aquí.");
                yield break;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            string key = _searchKey = NextKey("search");
            _java.CallStatic("requestSearch", _pdfPath, query ?? string.Empty, cursor ?? string.Empty, limit, key);
            PdfResult result = null;
            yield return WaitFor(key, r => result = r);
            if (_searchKey != key) yield break;
            _searchKey = null;
            if (result == null || !result.Ok) { done(null, result?.ErrorDetail ?? "No se pudo buscar en el libro."); yield break; }
            SearchList list = null;
            string error = null;
            try { list = JsonUtility.FromJson<SearchList>(result.Data); }
            catch (Exception) { error = "Respuesta de búsqueda no válida."; }
            done(list ?? new SearchList(), error);
#else
            done(new SearchList(), "La búsqueda funciona en el visor.");
            yield break;
#endif
        }

        public void CancelSearch()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_java != null && _searchKey != null) _java.CallStatic("clearStatus", _searchKey);
#endif
            _searchKey = null;
        }

        public void Dispose()
        {
            _disposed = true;
            CancelContents();
            CancelSearch();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_java != null)
            {
                _java.CallStatic("close");
                _java.Dispose();
                _java = null;
            }
#endif
        }

        // ------------------------------------------------------------------

        private string NextKey(string label)
        {
            _requestCounter++;
            return "folio-" + _instanceKey + "-" + label + "-" + _requestCounter + "-" + Time.frameCount;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator WaitFor(string key, Action<PdfResult> done)
        {
            float start = Time.realtimeSinceStartup;
            while (true)
            {
                if (key.Contains("-contents-") && _contentsKey != key) yield break;
                if (key.Contains("-search-") && _searchKey != key) yield break;
                if (_disposed) { done(Fail("CANCEL", "Lectura cancelada")); yield break; }
                string status = _java.CallStatic<string>("getStatus", key);
                if (status != null && status != "PENDING")
                {
                    _lastCacheHit = !IsReflow(_pdfPath) && !IsComic(_pdfPath) && key.Contains("-page") &&
                        _java.CallStatic<bool>("wasCached", key);
                    _java.CallStatic("clearStatus", key);
                    done(Parse(status));
                    yield break;
                }
                if (Time.realtimeSinceStartup - start > ((IsReflow(_pdfPath) || string.Equals(Path.GetExtension(_pdfPath), ".cbr", StringComparison.OrdinalIgnoreCase)) ? 300f : TimeoutSeconds))
                {
                    _java.CallStatic("clearStatus", key);
                    done(Fail("TIMEOUT", "El lector tardó demasiado en responder"));
                    yield break;
                }
                yield return null;
            }
        }
#endif

        private static PdfResult Parse(string status)
        {
            if (status.StartsWith("OK:", StringComparison.Ordinal))
            {
                return new PdfResult { Ok = true, Data = status.Substring(3) };
            }
            // Formato: ERROR:<tipo>:<detalle>
            string rest = status.StartsWith("ERROR:", StringComparison.Ordinal) ? status.Substring(6) : status;
            int colon = rest.IndexOf(':');
            string kind = colon > 0 ? rest.Substring(0, colon) : "OTHER";
            string detail = colon > 0 ? rest.Substring(colon + 1) : rest;
            return Fail(kind, detail);
        }

        private static PdfResult Fail(string kind, string detail)
        {
            return new PdfResult { Ok = false, ErrorKind = kind, ErrorDetail = detail };
        }

        /// <summary>
        /// Encargo 6: píxeles crudos del PDF (cabecera "FXR1", ancho, alto, filas de abajo hacia arriba).
        /// Lectura acotada fuera del hilo de Unity; luego se copia a la textura sin decodificar PNG.
        /// Devuelve null si el archivo no tiene esa cabecera.
        /// </summary>
        private static byte[] ReadPageImage(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                if (stream.Length > (40L << 20)) throw new IOException("La imagen supera el presupuesto de memoria");
                byte[] image = new byte[checked((int)stream.Length)];
                int read = 0;
                while (read < image.Length)
                {
                    int n = stream.Read(image, read, image.Length - read);
                    if (n <= 0) throw new IOException("Imagen truncada");
                    read += n;
                }
                return image;
            }
        }

        private static Texture2D LoadRaw(string path, bool night) => LoadRawBytes(ReadPageImage(path), night);

        private static Texture2D LoadRawBytes(byte[] image, bool night)
        {
            if (image.Length < 4 || image[0] != 'F' || image[1] != 'X' || image[2] != 'R' || image[3] != '1') return null;
            if (image.Length < 16) throw new IOException("Cabecera FXR1 truncada");
            int width = BitConverter.ToInt32(image, 4), height = BitConverter.ToInt32(image, 8);
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 8000000L ||
                image.LongLength - 16 != (long)width * height * 4) throw new IOException("Página cruda incompleta o demasiado grande");
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
            try
            {
                Span<byte> target = texture.GetPixelData<byte>(0).AsSpan();
                image.AsSpan(16).CopyTo(target);
                if (night) PageTheme.RemapNight(target, TextureFormat.RGBA32);
                texture.Apply(true, true);
                return texture;
            }
            catch { UnityEngine.Object.Destroy(texture); throw; }
        }

        private static void ConfigureTexture(Texture2D texture)
        {
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;        // texto mas nitido al mirar la pagina en angulo
            texture.mipMapBias = -0.5f;    // usa un mipmap mas detallado: letras menos borrosas
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // Es solo cache; si no se puede borrar se reemplaza en la proxima peticion.
            }
        }

        // ------------------------------------------------------------------
        // Vista previa para el Editor (no hay PdfRenderer fuera de Android).

        private const int EditorPreviewPages = 8;

        private static Texture2D BuildEditorPreview(int pageIndex, int widthPixels)
        {
            int width = Mathf.Clamp(widthPixels / 4, 128, 512);
            int height = Mathf.RoundToInt(width * 1.414f);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            var paper = new Color32(250, 248, 240, 255);
            var ink = new Color32(70, 70, 80, 255);
            var accent = pageIndex % 2 == 0 ? new Color32(40, 90, 160, 255) : new Color32(160, 70, 40, 255);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 c = paper;
                    int fromTop = height - 1 - y;
                    bool margin = x < width / 10 || x > width * 9 / 10;
                    // Barra de titulo
                    if (fromTop > height / 12 && fromTop < height / 12 + height / 20 && !margin) c = accent;
                    // Lineas de "texto"
                    else if (!margin && fromTop > height / 5 && fromTop < height * 9 / 10 && (fromTop / 6) % 3 == 0) c = ink;
                    // Marcas del numero de pagina abajo
                    else if (fromTop > height * 93 / 100 && fromTop < height * 95 / 100 &&
                             x > width / 2 - (pageIndex + 1) * 4 && x < width / 2 + (pageIndex + 1) * 4) c = accent;
                    pixels[y * width + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            ConfigureTexture(texture);
            return texture;
        }
    }
}
