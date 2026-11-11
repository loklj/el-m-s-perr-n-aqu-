using FolioXR.Tools.Ink;
using UnityEngine;

namespace FolioXR.Tools
{
    /// <summary>
    /// Tanda E (escrita por Claude). Regla transparente de 30 cm con marcas cada 1 mm y números cada 1 cm.
    /// Largo en X, ancho en Z, cara superior +Y. Sus dos bordes largos guían al lápiz (IStraightEdge).
    /// </summary>
    public sealed class RulerTool : FolioTool, IStraightEdge
    {
        internal static readonly Vector3 ColliderSize = new Vector3(0.30f, 0.003f, 0.035f);
        private const float Length = 0.30f, Width = 0.035f, Thickness = 0.003f;

        internal void Build(ToolVisuals visuals)
        {
            Body.mass = 0.03f;
            GripCollider.size = ColliderSize;
            visuals.Box(transform, "Cuerpo transparente", Vector3.zero, ColliderSize * 0.999f,
                        visuals.Transparent("acrílico de la regla", new Color(0.75f, 0.9f, 1f, 0.22f)));
            Texture2D marks = visuals.Own(MarksTexture());
            Material face = visuals.Textured("escala de la regla", marks);
            ToolVisuals.MakeTransparent(face);
            float x = Length * 0.5f, z = Width * 0.5f;
            Mesh top = visuals.Own(new Mesh { name = "FolioXR cara de la regla" });
            top.vertices = new[] { new Vector3(-x, 0f, -z), new Vector3(x, 0f, -z), new Vector3(-x, 0f, z), new Vector3(x, 0f, z) };
            top.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            top.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            top.RecalculateBounds();
            ToolVisuals.MeshObject(transform, "Escala", top, face).transform.localPosition =
                new Vector3(0f, Thickness * 0.5f + 0.0003f, 0f);
            StraightEdges.Register(this);
        }

        /// <summary>Bordes largos de la cara inferior, la que apoya sobre la página.</summary>
        public bool TryGetEdge(Vector3 point, float maxDistance, out Vector3 origin, out Vector3 direction,
                               out Vector3 outward)
        {
            origin = direction = outward = Vector3.zero;
            if (State == ToolState.Stored || !isActiveAndEnabled)
                return false;
            Vector3 local = transform.InverseTransformPoint(point);
            float half = Width * 0.5f, bottom = -Thickness * 0.5f;
            if (Mathf.Abs(local.x) > Length * 0.5f + 0.005f || Mathf.Abs(local.y - bottom) > 0.012f)
                return false;
            float side = local.z >= 0f ? 1f : -1f;
            float gap = Mathf.Abs(local.z) - half; // Positivo fuera de la regla.
            if (gap < -0.003f || gap > maxDistance)
                return false;
            origin = transform.TransformPoint(new Vector3(0f, bottom, side * half));
            direction = transform.right;
            outward = transform.forward * side;
            return true;
        }

        private void OnDestroy()
        {
            StraightEdges.Unregister(this);
        }

        // Dígitos de 3×5 píxeles, filas de arriba hacia abajo.
        private static readonly string[] Digits =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111"
        };

        /// <summary>Marcas en el borde -Z (el que queda hacia el lector): 1 mm, 5 mm y 1 cm con su número.</summary>
        private static Texture2D MarksTexture()
        {
            const int width = 2048, height = 256;
            float pxPerMm = width / (Length * 1000f);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = "FolioXR escala de la regla",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            var pixels = new Color32[width * height];
            var clear = new Color32(220, 240, 255, 40);
            var ink = new Color32(20, 30, 45, 255);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;
            for (int mm = 0; mm <= 300; mm++)
            {
                float lengthMm = mm % 10 == 0 ? 6f : mm % 5 == 0 ? 4f : 2.5f;
                int x = Mathf.Clamp(Mathf.RoundToInt(mm * pxPerMm), 1, width - 2);
                int top = Mathf.RoundToInt(lengthMm * height / (Width * 1000f)); // Píxeles por mm a lo ancho.
                int thickness = mm % 10 == 0 ? 2 : 1;
                for (int y = 0; y < top && y < height; y++)
                    for (int t = 0; t < thickness; t++)
                        pixels[y * width + Mathf.Min(width - 1, x + t - (thickness > 1 ? 1 : 0))] = ink;
                if (mm % 10 != 0 || mm == 0 || mm == 300)
                    continue;
                string label = (mm / 10).ToString();
                const int scale = 4;
                int labelWidth = label.Length * 4 * scale - scale;
                int startX = x - labelWidth / 2, baseY = top + 6;
                for (int c = 0; c < label.Length; c++)
                {
                    string glyph = Digits[label[c] - '0'];
                    for (int row = 0; row < 5; row++)
                        for (int col = 0; col < 3; col++)
                        {
                            if (glyph[row * 3 + col] != '1')
                                continue;
                            for (int dy = 0; dy < scale; dy++)
                                for (int dx = 0; dx < scale; dx++)
                                {
                                    int px = startX + c * 4 * scale + col * scale + dx;
                                    int py = baseY + (4 - row) * scale + dy;
                                    if (px >= 0 && px < width && py >= 0 && py < height)
                                        pixels[py * width + px] = ink;
                                }
                        }
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
