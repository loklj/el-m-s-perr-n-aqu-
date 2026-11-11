using System;
using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Tools.Ink
{
    /// <summary>Colores de los lápices (Tanda D). El índice se guarda en el JSON: no reordenar.</summary>
    public static class InkPalette
    {
        public const int Count = 6;
        public const int Highlighter = 5;
        public static readonly string[] Names = { "negro", "azul", "rojo", "verde", "morado", "resaltador" };
        public static readonly Color[] Colors =
        {
            new Color(0.05f, 0.05f, 0.06f),
            new Color(0.1f, 0.3f, 0.95f),
            new Color(0.9f, 0.12f, 0.12f),
            new Color(0.1f, 0.62f, 0.25f),
            new Color(0.55f, 0.2f, 0.85f),
            new Color(1f, 0.9f, 0.1f, 0.38f)
        };
        /// <summary>Tinta invisible revelada por la luz ultravioleta.</summary>
        public static readonly Color Revealed = new Color(0.78f, 0.55f, 1f);

        /// <summary>Ancho en metros.</summary>
        public static float Width(int color, bool invisible) => invisible ? 0.0025f : color == Highlighter ? 0.007f : 0.002f;
        /// <summary>Separación sobre la superficie: el resaltador queda debajo de la tinta.</summary>
        public static float Offset(int color, bool invisible) => invisible ? 0.0012f : color == Highlighter ? 0.0008f : 0.001f;
    }

    /// <summary>Trazo en coordenadas locales de su lienzo (página, libreta o casa).</summary>
    internal sealed class InkStroke
    {
        internal int Color;
        internal bool Invisible;
        internal float Width;           // Unidades locales del lienzo.
        internal Vector3 Normal;        // Normal local de la superficie (constante en todo el trazo).
        internal readonly List<Vector3> Points = new List<Vector3>(64);
        internal Bounds Bounds;
        internal bool Live;             // Aún se está dibujando.

        internal void Add(Vector3 point)
        {
            if (Points.Count == 0)
                Bounds = new Bounds(point, Vector3.one * Width);
            else
                Bounds.Encapsulate(point);
            Points.Add(point);
        }

        internal void RecalculateBounds()
        {
            if (Points.Count == 0)
                return;
            Bounds = new Bounds(Points[0], Vector3.zero);
            for (int i = 1; i < Points.Count; i++)
                Bounds.Encapsulate(Points[i]);
            Bounds.Expand(Width);
        }
    }

    [Serializable]
    internal sealed class InkStrokeData
    {
        public int color;
        public bool invisible;
        public float width;
        public Vector3 normal;
        public float[] points; // x, y, z consecutivos.
    }

    [Serializable]
    internal sealed class InkFile
    {
        public int version = 1;
        public List<InkStrokeData> strokes = new List<InkStrokeData>();
    }

    /// <summary>Cintas planas pegadas a la superficie. Un par de vértices por punto.</summary>
    internal static class InkGeometry
    {
        internal const int MaxPoints = 256;

        internal static int VertexCount(InkStroke stroke) => Mathf.Max(2, stroke.Points.Count) * 2;

        internal static void Append(InkStroke stroke, List<Vector3> vertices, List<int> triangles) =>
            Append(stroke.Points, 0, stroke.Points.Count, stroke.Normal, stroke.Width, vertices, triangles);

        /// <summary>Agrega los puntos [start, start+count) como una cinta continua.</summary>
        internal static void Append(List<Vector3> points, int start, int count, Vector3 normal, float width,
                                    List<Vector3> vertices, List<int> triangles)
        {
            if (count <= 0)
                return;
            float half = width * 0.5f;
            if (count == 1)
            {
                // Un toque deja un punto cuadrado.
                Vector3 p = points[start];
                Vector3 t = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized * half;
                Vector3 s = Vector3.Cross(normal, t).normalized * half;
                int k = vertices.Count;
                vertices.Add(p - t - s);
                vertices.Add(p - t + s);
                vertices.Add(p + t - s);
                vertices.Add(p + t + s);
                AddQuad(triangles, k);
                return;
            }
            Vector3 side = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = points[start + i];
                Vector3 tangent = i == 0 ? points[start + 1] - p : p - points[start + i - 1];
                Vector3 candidate = Vector3.Cross(normal, tangent);
                if (candidate.sqrMagnitude > 1e-14f)
                    side = candidate.normalized * half;
                else if (side == Vector3.zero)
                    side = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized * half;
                int k = vertices.Count;
                vertices.Add(p - side);
                vertices.Add(p + side);
                if (i > 0)
                    AddQuad(triangles, k - 2);
            }
        }

        private static void AddQuad(List<int> triangles, int k)
        {
            triangles.Add(k);
            triangles.Add(k + 1);
            triangles.Add(k + 3);
            triangles.Add(k);
            triangles.Add(k + 3);
            triangles.Add(k + 2);
        }

        internal static InkStrokeData ToData(InkStroke stroke)
        {
            var data = new InkStrokeData { color = stroke.Color,
                                           invisible = stroke.Invisible,
                                           width = stroke.Width,
                                           normal = stroke.Normal,
                                           points = new float[stroke.Points.Count * 3] };
            for (int i = 0; i < stroke.Points.Count; i++)
            {
                Vector3 p = stroke.Points[i];
                data.points[i * 3] = p.x;
                data.points[i * 3 + 1] = p.y;
                data.points[i * 3 + 2] = p.z;
            }
            return data;
        }

        internal static InkStroke FromData(InkStrokeData data)
        {
            if (data == null || data.points == null || data.points.Length < 3 || data.width <= 0f ||
                float.IsNaN(data.width) || data.normal.sqrMagnitude < 0.25f)
                return null;
            var stroke = new InkStroke { Color = Mathf.Clamp(data.color, 0, InkPalette.Count - 1),
                                         Invisible = data.invisible,
                                         Width = data.width,
                                         Normal = data.normal.normalized };
            int count = Mathf.Min(data.points.Length / 3, MaxPoints);
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3(data.points[i * 3], data.points[i * 3 + 1], data.points[i * 3 + 2]);
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.sqrMagnitude))
                    return null;
                stroke.Points.Add(p);
            }
            stroke.RecalculateBounds();
            return stroke;
        }
    }

    /// <summary>Borde recto (la regla de la Tanda E). El lápiz traza recto a menos de 1 cm.</summary>
    public interface IStraightEdge
    {
        /// <summary>
        /// Borde más cercano a <paramref name="point"/> si está a menos de <paramref name="maxDistance"/>.
        /// origin/direction describen la recta del borde (mundo); outward apunta hacia fuera de la regla.
        /// </summary>
        bool TryGetEdge(Vector3 point, float maxDistance, out Vector3 origin, out Vector3 direction, out Vector3 outward);
    }

    public static class StraightEdges
    {
        private static readonly List<IStraightEdge> All = new List<IStraightEdge>(2);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            All.Clear();
        }
        public static void Register(IStraightEdge edge)
        {
            if (edge != null && !All.Contains(edge))
                All.Add(edge);
        }
        public static void Unregister(IStraightEdge edge)
        {
            All.Remove(edge);
        }

        internal static bool TryFind(Vector3 point, float maxDistance, out IStraightEdge found, out Vector3 origin,
                                     out Vector3 direction, out Vector3 outward)
        {
            found = null;
            origin = direction = outward = Vector3.zero;
            float best = float.PositiveInfinity;
            foreach (IStraightEdge edge in All)
            {
                if (edge is Behaviour behaviour && (behaviour == null || !behaviour.isActiveAndEnabled))
                    continue;
                if (!edge.TryGetEdge(point, maxDistance, out Vector3 o, out Vector3 d, out Vector3 n))
                    continue;
                Vector3 offset = point - o;
                float distance = (offset - d * Vector3.Dot(offset, d)).magnitude;
                if (distance >= best)
                    continue;
                best = distance;
                found = edge;
                origin = o;
                direction = d;
                outward = n;
            }
            return found != null;
        }
    }

    /// <summary>Superficie con reglas propias (páginas del libro y de la libreta).</summary>
    internal sealed class InkSurface : MonoBehaviour
    {
        /// <summary>Devuelve el lienzo y el plano real de escritura, o false si ahí no se escribe.</summary>
        internal delegate bool Resolver(Vector3 tip, Vector3 hitPoint, Vector3 hitNormal, out InkCanvas canvas,
                                        out Vector3 surfacePoint, out Vector3 surfaceNormal);
        internal Resolver Resolve;
    }
}
