using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FolioXR.Reading
{
    /// <summary>
    /// Miniaturas de portada para la estantería (pedido del usuario, 16 nov 2026: «que se vea la portada de los
    /// libros»). El libro guarda una miniatura de su primera página la primera vez que se abre (FolioBook) y la
    /// estantería la muestra de frente (BookShelf). Quedan en la carpeta privada de la app, una por libro.
    /// </summary>
    internal static class CoverThumbs
    {
        private const int Height = 384;
        public static event Action Changed;
        private static readonly Dictionary<string, Texture2D> Loaded = new Dictionary<string, Texture2D>();
        private static string _folder;

        private static string Folder
        {
            get
            {
                if (_folder == null)
                {
                    _folder = Path.Combine(Application.persistentDataPath, "portadas");
                    try { Directory.CreateDirectory(_folder); } catch (Exception e) { Debug.LogWarning("[FolioXR] Portadas: " + e.Message); }
                }
                return _folder;
            }
        }

        private static string FileFor(string id)
        {
            // FNV-1a de 64 bits: nombre de archivo estable sin los caracteres raros de un identificador o ruta.
            ulong hash = 14695981039346656037UL;
            foreach (char c in id) { hash ^= c; hash *= 1099511628211UL; }
            return Path.Combine(Folder, hash.ToString("x16") + ".jpg");
        }

        public static bool Has(string id) => !string.IsNullOrEmpty(id) && (Loaded.ContainsKey(id) || File.Exists(FileFor(id)));

        /// <summary>Miniatura de ese libro o null si todavía no se abrió nunca.</summary>
        public static Texture2D Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Loaded.TryGetValue(id, out Texture2D texture) && texture != null) return texture;
            string file = FileFor(id);
            if (!File.Exists(file)) return null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGB24, true) { name = "FolioXR portada", wrapMode = TextureWrapMode.Clamp };
                if (!texture.LoadImage(File.ReadAllBytes(file), true)) { UnityEngine.Object.Destroy(texture); return null; }
                Loaded[id] = texture;
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Portadas: no se pudo leer una miniatura: " + e.Message);
                return null;
            }
        }

        /// <summary>Guarda como miniatura una copia reducida de esa página (cualquier textura, legible o no).</summary>
        public static void Save(string id, Texture page)
        {
            if (string.IsNullOrEmpty(id) || page == null || page.width <= 0 || page.height <= 0) return;
            int width = Mathf.Clamp(Mathf.RoundToInt(Height * page.width / (float)page.height), 64, 1024);
            RenderTexture target = RenderTexture.GetTemporary(width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            Texture2D small = null;
            try
            {
                Graphics.Blit(page, target);
                RenderTexture.active = target;
                small = new Texture2D(width, Height, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0, 0, width, Height), 0, 0);
                small.Apply(false, false);
                File.WriteAllBytes(FileFor(id), small.EncodeToJPG(86));
                // Si la tapa todavía no dio su color (BookCover), sirve el borde de la miniatura.
                if (!TryGetColor(id, out _)) SetColor(id, Edge(small.GetPixels32(), width, Height), false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Portadas: no se pudo guardar una miniatura: " + e.Message);
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(target);
                if (small != null) UnityEngine.Object.Destroy(small);
            }
            if (Loaded.TryGetValue(id, out Texture2D old) && old != null) UnityEngine.Object.Destroy(old);
            Loaded.Remove(id);
            Changed?.Invoke();
        }

        // ───────────── Color del libro (17 nov 2026) ─────────────
        // Pedido del usuario: en la estantería cada libro con su color, el mismo del libro en la mano (un
        // Condorito gris, otro amarillo…). Con tapas propias, la tapa toma el color del borde de la portada
        // (BookCover); ese color se guarda aquí y la estantería lo usa para el lomo.
        private const string ColorPrefix = "folioxr.book.color.";

        public static bool TryGetColor(string id, out Color color)
        {
            color = Color.gray;
            if (string.IsNullOrEmpty(id)) return false;
            string text = PlayerPrefs.GetString(ColorPrefix + id, string.Empty);
            if (text.Length != 6) return false;
            try
            {
                int v = Convert.ToInt32(text, 16);
                color = new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Guarda el color del libro; avisa a la estantería si cambió de verdad.</summary>
        public static void SetColor(string id, Color color, bool notify = true)
        {
            if (string.IsNullOrEmpty(id)) return;
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255), g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255),
                b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            string text = ((r << 16) | (g << 8) | b).ToString("x6");
            if (PlayerPrefs.GetString(ColorPrefix + id, string.Empty) == text) return;
            PlayerPrefs.SetString(ColorPrefix + id, text);
            PlayerPrefs.Save();
            if (notify) Changed?.Invoke();
        }

        /// <summary>Promedio del borde de una imagen (3 % de cada lado), un poco más oscuro como el canto de la tapa.</summary>
        internal static Color Edge(Color32[] pixels, int width, int height)
        {
            int band = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(width, height) * 0.03f));
            long r = 0, g = 0, b = 0, count = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (x >= band && x < width - band && y >= band && y < height - band) { x = width - band - 1; continue; }
                    Color32 p = pixels[y * width + x];
                    r += p.r; g += p.g; b += p.b; count++;
                }
            if (count == 0) return new Color(0.22f, 0.21f, 0.2f);
            const float shade = 0.9f / 255f;
            return new Color(r * shade / count, g * shade / count, b * shade / count, 1f);
        }

        /// <summary>El libro salió de la biblioteca: su miniatura ya no hace falta.</summary>
        public static void Forget(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            PlayerPrefs.DeleteKey(ColorPrefix + id);
            if (Loaded.TryGetValue(id, out Texture2D old) && old != null) UnityEngine.Object.Destroy(old);
            Loaded.Remove(id);
            try { File.Delete(FileFor(id)); } catch (Exception) { }
        }
    }
}
