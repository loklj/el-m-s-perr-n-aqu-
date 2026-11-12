using System;
using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Environments
{
    /// <summary>Paisaje alrededor de la casa (docs/ENTORNOS.md).</summary>
    public enum ExteriorKind { Garden, Mountains, Lake, Beach, Snow, Desert, Forest, Autumn, City, Island }

    /// <summary>Hora y tiempo del cielo pintado.</summary>
    public enum SkyKind { Day, Sunset, Night, Overcast }

    /// <summary>Datos del estilo. Las medidas son metros; no contiene lógica de construcción.</summary>
    [Serializable]
    public sealed class EnvironmentTheme
    {
        public string Name;
        public int Seed;
        public float HouseWidth = 12f;
        public float HouseDepth = 10f;
        public float StoreyHeight = 3.15f;
        public float WallThickness = 0.18f;
        public float FurnitureDensity = 1f;

        public Color Wall;
        public Color Wood;
        public Color Fabric;
        public Color Metal;
        public Color Foliage;
        public Color[] BookPalette;
        public Color SkyHorizon;
        public Color SkyZenith;
        public Color AmbientSky;
        public Color AmbientEquator;
        public Color AmbientGround;

        // ── Exterior y cielo (Claude, 11 nov) ──
        public ExteriorKind Exterior = ExteriorKind.Garden;
        public SkyKind Sky = SkyKind.Day;
        /// <summary>Grados. 270 = frente de la casa (la terraza y los puntos de lectura miran a −Z).</summary>
        public float SunAzimuth = 230f;
        public float SunElevation = 40f;
        public Color SunColor = new Color(1f, 0.96f, 0.88f);
        public float SunIntensity = 1.1f;
        public float CloudCover = 0.35f;
        public Color CloudColor = Color.white;
        public Color Ground = new Color(0.33f, 0.47f, 0.24f);
        public Color GroundAlt = new Color(0.45f, 0.50f, 0.28f);
        public Color Rock = new Color(0.47f, 0.48f, 0.50f);
        public Color Snow = new Color(0.93f, 0.95f, 0.98f);
        public Color Water = new Color(0.12f, 0.33f, 0.42f);
        public float MountainHeight = 10f;
        /// <summary>Tonos de las copas de los árboles (hasta 8); vacío = variaciones de Foliage.</summary>
        public Color[] Leaves;
        /// <summary>
        /// Carpeta de Resources/Entornos con la versión horneada en Blender (herramientas/entornos).
        /// Si los archivos no están, se dibuja la versión procedural.
        /// </summary>
        public string Baked;
    }

    public static class EnvironmentThemes
    {
        private static readonly Color[] WarmBooks =
        {
            new Color(0.27f, 0.40f, 0.47f), new Color(0.66f, 0.33f, 0.23f),
            new Color(0.72f, 0.60f, 0.37f), new Color(0.35f, 0.48f, 0.35f),
            new Color(0.47f, 0.35f, 0.43f), new Color(0.84f, 0.76f, 0.61f),
            new Color(0.32f, 0.48f, 0.53f), new Color(0.57f, 0.44f, 0.29f)
        };

        private static readonly Color[] DarkBooks =
        {
            new Color(0.20f, 0.24f, 0.32f), new Color(0.48f, 0.18f, 0.16f),
            new Color(0.55f, 0.45f, 0.25f), new Color(0.19f, 0.32f, 0.24f),
            new Color(0.33f, 0.22f, 0.30f), new Color(0.70f, 0.64f, 0.52f),
            new Color(0.14f, 0.15f, 0.17f), new Color(0.42f, 0.30f, 0.20f)
        };

        private static Color C(int hex)
        {
            return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);
        }

        /// <summary>
        /// Once estilos originales (inspirados en el realismo de los entornos de Meta, sin copiar
        /// nombres ni assets). Misma planta de casa; cambian materiales, paisaje, cielo y luz.
        /// </summary>
        public static IReadOnlyList<EnvironmentTheme> All { get; } = Array.AsReadOnly(new[]
        {
            new EnvironmentTheme
            {
                Name = "Moderna cálida", Baked = "moderna-calida", Seed = 58241,
                Wall = new Color(0.94f, 0.89f, 0.80f), Wood = new Color(0.80f, 0.64f, 0.43f),
                Fabric = new Color(0.68f, 0.42f, 0.29f), Metal = new Color(0.20f, 0.23f, 0.22f),
                Foliage = new Color(0.38f, 0.56f, 0.32f), BookPalette = WarmBooks,
                SkyHorizon = new Color(0.83f, 0.91f, 0.93f), SkyZenith = new Color(0.30f, 0.58f, 0.88f),
                AmbientSky = new Color(0.72f, 0.78f, 0.83f), AmbientEquator = new Color(0.66f, 0.62f, 0.53f),
                AmbientGround = new Color(0.37f, 0.34f, 0.29f),
                Exterior = ExteriorKind.Garden, Sky = SkyKind.Day, SunAzimuth = 235f, SunElevation = 42f,
                CloudCover = 0.35f, MountainHeight = 9f
            },
            new EnvironmentTheme
            {
                Name = "Refugio alpino", Baked = "refugio-alpino", Seed = 7713, HouseWidth = 12.5f, StoreyHeight = 3.3f,
                Wall = C(0xE8DCC8), Wood = C(0x9A6B43), Fabric = C(0x8E3B2E), Metal = C(0x2B2B2D),
                Foliage = C(0x2F5A35), BookPalette = DarkBooks,
                SkyHorizon = C(0xDCE8F0), SkyZenith = C(0x2A62B8),
                AmbientSky = C(0xB8C8DC), AmbientEquator = C(0x9AA0A0), AmbientGround = C(0x50524E),
                Exterior = ExteriorKind.Mountains, Sky = SkyKind.Day, SunAzimuth = 300f, SunElevation = 35f,
                SunColor = C(0xFFF6E6), SunIntensity = 1.2f, CloudCover = 0.25f,
                Ground = C(0x55703A), GroundAlt = C(0x7A8048), Rock = C(0x6E7178), Snow = C(0xF4F7FB),
                MountainHeight = 42f,
                Leaves = new[] { C(0x1F4429), C(0x264D2E), C(0x2D5733), C(0x1C3B26) }
            },
            new EnvironmentTheme
            {
                Name = "Casa del lago", Baked = "casa-del-lago", Seed = 4402,
                Wall = C(0xEFE6D6), Wood = C(0xB9895A), Fabric = C(0x3F5E6E), Metal = C(0x272A2C),
                Foliage = C(0x4C6B34), BookPalette = WarmBooks,
                SkyHorizon = C(0xF7B47A), SkyZenith = C(0x4A5C92),
                AmbientSky = C(0xB49AA0), AmbientEquator = C(0xC08A64), AmbientGround = C(0x4A3A30),
                Exterior = ExteriorKind.Lake, Sky = SkyKind.Sunset, SunAzimuth = 262f, SunElevation = 5f,
                SunColor = C(0xFFB070), SunIntensity = 0.9f, CloudCover = 0.4f, CloudColor = C(0xFFC9A0),
                Ground = C(0x4D6630), GroundAlt = C(0x6B7238), Rock = C(0x5A5866), Water = C(0x23455A),
                MountainHeight = 20f,
                Leaves = new[] { C(0x2E4A24), C(0x3A5A2A), C(0x23402A), C(0x445E2C) }
            },
            new EnvironmentTheme
            {
                Name = "Casa de playa", Baked = "casa-de-playa", Seed = 9120, HouseWidth = 13f,
                Wall = C(0xF6F2EA), Wood = C(0xD8C3A0), Fabric = C(0x5C8FA8), Metal = C(0x7E8488),
                Foliage = C(0x4F8A3E), BookPalette = WarmBooks,
                SkyHorizon = C(0xD6EEF6), SkyZenith = C(0x2F86D6),
                AmbientSky = C(0xBCD8EA), AmbientEquator = C(0xD8CDB4), AmbientGround = C(0x8C7C60),
                Exterior = ExteriorKind.Beach, Sky = SkyKind.Day, SunAzimuth = 210f, SunElevation = 55f,
                SunIntensity = 1.25f, CloudCover = 0.2f,
                Ground = C(0xE2D2AC), GroundAlt = C(0xD3C095), Rock = C(0x8F8577), Water = C(0x1D7F98),
                MountainHeight = 12f,
                Leaves = new[] { C(0x3F7A32), C(0x4E8A3A), C(0x5E9640), C(0x36692C) }
            },
            new EnvironmentTheme
            {
                Name = "Cabaña nevada", Baked = "cabana-nevada", Seed = 3317, StoreyHeight = 3f,
                Wall = C(0xD9C7A8), Wood = C(0x7A4E2E), Fabric = C(0x7E2A26), Metal = C(0x1F1F21),
                Foliage = C(0x2E4F37), BookPalette = DarkBooks,
                SkyHorizon = C(0xD8DEE6), SkyZenith = C(0x9AA8BC),
                AmbientSky = C(0xC6CEDA), AmbientEquator = C(0xB0B4BA), AmbientGround = C(0x8C9098),
                Exterior = ExteriorKind.Snow, Sky = SkyKind.Overcast, SunAzimuth = 250f, SunElevation = 25f,
                SunColor = C(0xE6ECF5), SunIntensity = 0.65f, CloudCover = 0.85f, CloudColor = C(0xE4E8EE),
                Ground = C(0xECF0F5), GroundAlt = C(0xDCE2EA), Rock = C(0x5C6068), Snow = C(0xF6F8FB),
                MountainHeight = 30f,
                Leaves = new[] { C(0x1E3A2A), C(0x24422E), C(0x8E9EA0), C(0x1A3326) }
            },
            new EnvironmentTheme
            {
                Name = "Casa del desierto", Baked = "casa-del-desierto", Seed = 6608, HouseWidth = 14f, HouseDepth = 11f,
                Wall = C(0xE9D5B8), Wood = C(0xA9774A), Fabric = C(0xB45A35), Metal = C(0x3A302A),
                Foliage = C(0x6E8A4A), BookPalette = WarmBooks,
                SkyHorizon = C(0xF4D2A2), SkyZenith = C(0x5C8CC4),
                AmbientSky = C(0xC8B8A8), AmbientEquator = C(0xD49A6A), AmbientGround = C(0x7A5038),
                Exterior = ExteriorKind.Desert, Sky = SkyKind.Sunset, SunAzimuth = 285f, SunElevation = 12f,
                SunColor = C(0xFFC488), SunIntensity = 1.05f, CloudCover = 0.12f, CloudColor = C(0xFFD8B0),
                Ground = C(0xC99A68), GroundAlt = C(0xB98856), Rock = C(0xA0583A), Snow = C(0xC8805A),
                MountainHeight = 26f,
                Leaves = new[] { C(0x5E7A40), C(0x6E8A4A), C(0x56703C), C(0x7A9454) }
            },
            new EnvironmentTheme
            {
                Name = "Casa del bosque", Baked = "casa-del-bosque", Seed = 1189, FurnitureDensity = 1.2f,
                Wall = C(0xE3D7C2), Wood = C(0x8C6239), Fabric = C(0x5B6B3A), Metal = C(0x232521),
                Foliage = C(0x3C6B30), BookPalette = DarkBooks,
                SkyHorizon = C(0xCFE2E4), SkyZenith = C(0x3E78B4),
                AmbientSky = C(0x9EB8A8), AmbientEquator = C(0x7E8E66), AmbientGround = C(0x3A3A2A),
                Exterior = ExteriorKind.Forest, Sky = SkyKind.Day, SunAzimuth = 200f, SunElevation = 48f,
                SunIntensity = 1.05f, CloudCover = 0.45f,
                Ground = C(0x3F5A2A), GroundAlt = C(0x5A5230), Rock = C(0x5A5E58), MountainHeight = 16f,
                Leaves = new[] { C(0x2C5A26), C(0x3A6A2E), C(0x24482A), C(0x4A7434), C(0x1E3F25), C(0x355F2A) }
            },
            new EnvironmentTheme
            {
                Name = "Otoño en el valle", Baked = "otono-en-el-valle", Seed = 2471,
                Wall = C(0xF0E4D0), Wood = C(0xA06A3C), Fabric = C(0x9C4A28), Metal = C(0x2E2A26),
                Foliage = C(0xB0602A), BookPalette = WarmBooks,
                SkyHorizon = C(0xE8E6DC), SkyZenith = C(0x5A8CC0),
                AmbientSky = C(0xC0C4C4), AmbientEquator = C(0xC09068), AmbientGround = C(0x5A4430),
                Exterior = ExteriorKind.Autumn, Sky = SkyKind.Day, SunAzimuth = 240f, SunElevation = 28f,
                SunColor = C(0xFFE6C0), SunIntensity = 1.05f, CloudCover = 0.4f,
                Ground = C(0x6E6A34), GroundAlt = C(0x9A6A30), Rock = C(0x6A6460), MountainHeight = 18f,
                Leaves = new[] { C(0xC4501E), C(0xD9822A), C(0xE0B03A), C(0x9C3A1C), C(0xB8662A), C(0x6E7A30) }
            },
            new EnvironmentTheme
            {
                Name = "Loft nocturno", Baked = "loft-nocturno", Seed = 8034, HouseWidth = 13f,
                Wall = C(0xCFC8BE), Wood = C(0x5C4030), Fabric = C(0x34405A), Metal = C(0x18191C),
                Foliage = C(0x3A5A34), BookPalette = DarkBooks,
                SkyHorizon = C(0x2C3254), SkyZenith = C(0x070A18),
                AmbientSky = C(0x4A5070), AmbientEquator = C(0x5A4A48), AmbientGround = C(0x2A2626),
                Exterior = ExteriorKind.City, Sky = SkyKind.Night, SunAzimuth = 300f, SunElevation = 35f,
                SunColor = C(0x9AAAD0), SunIntensity = 0.35f, CloudCover = 0.15f, CloudColor = C(0x4A4A60),
                Ground = C(0x2E3A2A), GroundAlt = C(0x3A3A36), Rock = C(0x303236), MountainHeight = 0f,
                Leaves = new[] { C(0x1E3020), C(0x253A26), C(0x1A2A1C) }
            },
            new EnvironmentTheme
            {
                Name = "Isla tropical", Baked = "isla-tropical", Seed = 5150, HouseWidth = 12f,
                Wall = C(0xF4EEE2), Wood = C(0xC49A66), Fabric = C(0x2E8C80), Metal = C(0x585C5E),
                Foliage = C(0x3E8A3A), BookPalette = WarmBooks,
                SkyHorizon = C(0xDFF3F7), SkyZenith = C(0x1F7ED8),
                AmbientSky = C(0xB8DCEC), AmbientEquator = C(0xCCD2B4), AmbientGround = C(0x6A7A58),
                Exterior = ExteriorKind.Island, Sky = SkyKind.Day, SunAzimuth = 250f, SunElevation = 60f,
                SunIntensity = 1.3f, CloudCover = 0.3f,
                Ground = C(0x5A8A3A), GroundAlt = C(0xE6D6AE), Rock = C(0x6A6A5E), Water = C(0x1690A8),
                MountainHeight = 14f,
                Leaves = new[] { C(0x3A8034), C(0x4C9238), C(0x2E6E2C), C(0x5AA040) }
            },
            new EnvironmentTheme
            {
                Name = "Noche estrellada", Baked = "noche-estrellada", Seed = 9931,
                Wall = C(0xE6DCCB), Wood = C(0x8E6844), Fabric = C(0x4A3A5C), Metal = C(0x202024),
                Foliage = C(0x2E4A2A), BookPalette = DarkBooks,
                SkyHorizon = C(0x1C2A48), SkyZenith = C(0x03050E),
                AmbientSky = C(0x445070), AmbientEquator = C(0x3A3E50), AmbientGround = C(0x1E2022),
                Exterior = ExteriorKind.Garden, Sky = SkyKind.Night, SunAzimuth = 255f, SunElevation = 30f,
                SunColor = C(0xA8B8E0), SunIntensity = 0.4f, CloudCover = 0.08f, CloudColor = C(0x3A4058),
                Ground = C(0x24361E), GroundAlt = C(0x2E3A22), Rock = C(0x2A2C34), MountainHeight = 12f,
                Leaves = new[] { C(0x182A18), C(0x1E321C), C(0x142416) }
            }
        });
    }
}
