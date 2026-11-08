using System;
using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Environments
{
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
    }

    public static class EnvironmentThemes
    {
        // Los otros diecinueve estilos se añaden aquí como datos, cuando estén autorizados.
        public static IReadOnlyList<EnvironmentTheme> All { get; } = Array.AsReadOnly(new[]
        {
            new EnvironmentTheme
            {
                Name = "Moderna cálida",
                Seed = 58241,
                HouseWidth = 12f,
                HouseDepth = 10f,
                StoreyHeight = 3.15f,
                WallThickness = 0.18f,
                FurnitureDensity = 1f,
                Wall = new Color(0.94f, 0.89f, 0.80f),
                Wood = new Color(0.80f, 0.64f, 0.43f),
                Fabric = new Color(0.68f, 0.42f, 0.29f),
                Metal = new Color(0.20f, 0.23f, 0.22f),
                Foliage = new Color(0.38f, 0.56f, 0.32f),
                BookPalette = new[]
                {
                    new Color(0.27f, 0.40f, 0.47f), new Color(0.66f, 0.33f, 0.23f),
                    new Color(0.72f, 0.60f, 0.37f), new Color(0.35f, 0.48f, 0.35f),
                    new Color(0.47f, 0.35f, 0.43f), new Color(0.84f, 0.76f, 0.61f),
                    new Color(0.32f, 0.48f, 0.53f), new Color(0.57f, 0.44f, 0.29f)
                },
                SkyHorizon = new Color(0.83f, 0.91f, 0.93f),
                SkyZenith = new Color(0.36f, 0.65f, 0.89f),
                AmbientSky = new Color(0.72f, 0.78f, 0.83f),
                AmbientEquator = new Color(0.66f, 0.62f, 0.53f),
                AmbientGround = new Color(0.37f, 0.34f, 0.29f)
            }
        });
    }
}
