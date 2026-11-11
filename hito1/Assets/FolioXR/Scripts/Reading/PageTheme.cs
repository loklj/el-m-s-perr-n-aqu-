using System;
using UnityEngine;

namespace FolioXR.Reading
{
    /// <summary>Modo de página: Día (papel blanco), Sepia o Noche.</summary>
    public enum PageThemeMode { Day = 0, Sepia = 1, Night = 2 }

    /// <summary>
    /// Modos de lectura (encargo 17, parte 1; Claude, 11 nov 2026).
    /// Sepia: tinte del material de la página (sin costo). Noche en texto (PDF, EPUB, TXT, MOBI, DOCX):
    /// los píxeles se remapean al cargar la página, papel gris muy oscuro y letra crema. Noche en cómics:
    /// solo se oscurece el tinte, porque invertir dibujos y fotos se ve mal.
    /// </summary>
    public static class PageTheme
    {
        private const string PrefsKey = "folioxr.pageTheme";
        public static event Action Changed;

        public static PageThemeMode Current =>
            (PageThemeMode)Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, 0), 0, 2);

        public static string Name(PageThemeMode mode) =>
            mode == PageThemeMode.Sepia ? "sepia" : mode == PageThemeMode.Night ? "noche" : "día";

        public static void Set(PageThemeMode mode)
        {
            if (mode == Current) return;
            PlayerPrefs.SetInt(PrefsKey, (int)mode);
            PlayerPrefs.Save();
            Debug.Log("[FolioXR] Modo de página: " + Name(mode));
            Changed?.Invoke();
        }

        /// <summary>Color del material de la página para el documento indicado.</summary>
        public static Color Tint(string path)
        {
            switch (Current)
            {
                case PageThemeMode.Sepia: return new Color(1f, 0.92f, 0.78f);
                case PageThemeMode.Night:
                    return PdfPageRenderer.IsComic(path) ? new Color(0.62f, 0.60f, 0.56f) : Color.white;
                default: return Color.white;
            }
        }

        /// <summary>True si la página debe remapearse a colores de noche al cargarla.</summary>
        public static bool NeedsRemap(string path) => Current == PageThemeMode.Night && !PdfPageRenderer.IsComic(path);

        // Blanco → (30, 29, 28); negro → (222, 214, 198): fondo casi negro y letra crema, poco brillo.
        private static readonly byte[] R = Ramp(30, 222), G = Ramp(29, 214), B = Ramp(28, 198);

        private static byte[] Ramp(int paper, int ink)
        {
            var table = new byte[256];
            for (int i = 0; i < 256; i++) table[i] = (byte)(ink + (paper - ink) * i / 255);
            return table;
        }

        /// <summary>Remapea en sitio píxeles de 3 o 4 bytes (RGB24, RGBA32 o ARGB32).</summary>
        public static void RemapNight(Span<byte> pixels, TextureFormat format)
        {
            int stride, r;
            switch (format)
            {
                case TextureFormat.RGB24: stride = 3; r = 0; break;
                case TextureFormat.RGBA32: stride = 4; r = 0; break;
                case TextureFormat.ARGB32: stride = 4; r = 1; break;
                default: return; // Formato inesperado: se deja como está.
            }
            for (int i = r; i + 2 < pixels.Length; i += stride)
            {
                pixels[i] = R[pixels[i]];
                pixels[i + 1] = G[pixels[i + 1]];
                pixels[i + 2] = B[pixels[i + 2]];
            }
        }
    }
}
