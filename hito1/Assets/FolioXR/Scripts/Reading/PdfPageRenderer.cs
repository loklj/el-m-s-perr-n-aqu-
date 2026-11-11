// Folio XR - Hito 1
// Puente entre Unity y el plugin Java FolioPdfRenderer (Assets/Plugins/Android).
// En el Quest dibuja paginas reales del PDF. En el Editor de Unity no existe
// PdfRenderer de Android, asi que devuelve una pagina de muestra generada.

using System;
using System.Collections;
using System.IO;
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
            return ext == ".epub" || ext == ".txt" || ext == ".mobi" || ext == ".azw" || ext == ".azw3";
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
        private const float TimeoutSeconds = 30f;

        private readonly string _pdfPath;
        private readonly string _cacheDir;
        private int _requestCounter;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass _java;
#endif

        public PdfPageRenderer(string pdfPath)
        {
            _pdfPath = pdfPath;
            _cacheDir = Path.Combine(Application.temporaryCachePath, "folio-paginas");
            Directory.CreateDirectory(_cacheDir);
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

            Texture2D texture = null;
            try
            {
                bool night = PageTheme.NeedsRemap(_pdfPath);
                texture = LoadRaw(pngPath, night);
                if (texture == null)
                {
                    // PNG (EPUB) o JPEG (cómics).
                    byte[] png = File.ReadAllBytes(pngPath);
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
            if (texture != null) ConfigureTexture(texture);
            done(texture, result);
#else
            yield return null;
            Texture2D preview = BuildEditorPreview(pageIndex, widthPixels);
            done(preview, new PdfResult { Ok = true, Data = preview.width + "x" + preview.height });
#endif
        }

        public void Dispose()
        {
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
            return "folio-" + label + "-" + _requestCounter + "-" + Time.frameCount;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator WaitFor(string key, Action<PdfResult> done)
        {
            float start = Time.realtimeSinceStartup;
            while (true)
            {
                string status = _java.CallStatic<string>("getStatus", key);
                if (status != null && status != "PENDING")
                {
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
        /// Se leen directo a la memoria de la textura, sin PNG y sin un arreglo intermedio de 24 MB.
        /// Devuelve null si el archivo no tiene esa cabecera.
        /// </summary>
        private static Texture2D LoadRaw(string path, bool night)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                var header = new byte[16];
                if (stream.Read(header, 0, 16) != 16 || header[0] != 'F' || header[1] != 'X' || header[2] != 'R' ||
                    header[3] != '1')
                    return null;
                int width = BitConverter.ToInt32(header, 4), height = BitConverter.ToInt32(header, 8);
                if (width <= 0 || height <= 0 || width > 8192 || height > 16384 ||
                    stream.Length - 16 < (long)width * height * 4)
                    throw new IOException("pagina cruda incompleta");
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
                try
                {
                    Unity.Collections.NativeArray<byte> pixels = texture.GetPixelData<byte>(0);
                    Span<byte> target = pixels.AsSpan();
                    int read = 0;
                    while (read < target.Length)
                    {
                        int n = stream.Read(target.Slice(read));
                        if (n <= 0) throw new IOException("pagina cruda truncada");
                        read += n;
                    }
                    if (night) PageTheme.RemapNight(target, TextureFormat.RGBA32);
                    texture.Apply(true, true); // Mipmaps y fuera de la RAM de CPU.
                    return texture;
                }
                catch
                {
                    UnityEngine.Object.Destroy(texture);
                    throw;
                }
            }
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
