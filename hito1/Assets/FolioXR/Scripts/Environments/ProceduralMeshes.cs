using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Environments
{
    /// <summary>Mallas propias, centradas en el origen; el generador posee su vida útil.</summary>
    internal static class ProceduralMeshes
    {
        internal static Mesh Box()
        {
            var vertices = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uv = new List<Vector2>(24);
            var triangles = new List<int>(36);
            AddFace(vertices, normals, uv, triangles, Vector3.forward * 0.5f, Vector3.right, Vector3.up);
            AddFace(vertices, normals, uv, triangles, Vector3.back * 0.5f, Vector3.left, Vector3.up);
            AddFace(vertices, normals, uv, triangles, Vector3.right * 0.5f, Vector3.back, Vector3.up);
            AddFace(vertices, normals, uv, triangles, Vector3.left * 0.5f, Vector3.forward, Vector3.up);
            AddFace(vertices, normals, uv, triangles, Vector3.up * 0.5f, Vector3.right, Vector3.back);
            AddFace(vertices, normals, uv, triangles, Vector3.down * 0.5f, Vector3.right, Vector3.forward);
            return Finish("FolioXR Caja", vertices, normals, uv, triangles);
        }

        internal static Mesh Cylinder(int sides = 10)
        {
            sides = Mathf.Clamp(sides, 3, 64);
            var vertices = new List<Vector3>(4 * sides + 6);
            var normals = new List<Vector3>(4 * sides + 6);
            var uv = new List<Vector2>(4 * sides + 6);
            var triangles = new List<int>(12 * sides);

            // Dos vértices por ángulo; se duplica la costura de UV y cada tapa.
            for (int i = 0; i <= sides; i++)
            {
                float u = i / (float)sides;
                float angle = u * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices.Add(radial * 0.5f + Vector3.down * 0.5f);
                vertices.Add(radial * 0.5f + Vector3.up * 0.5f);
                normals.Add(radial);
                normals.Add(radial);
                uv.Add(new Vector2(u, 0f));
                uv.Add(new Vector2(u, 1f));
                if (i == sides) continue;
                int a = 2 * i;
                int b = a + 2;
                AddTriangle(triangles, a, a + 1, b + 1, false);
                AddTriangle(triangles, a, b + 1, b, false);
            }

            for (int cap = 0; cap < 2; cap++)
            {
                Vector3 normal = cap == 0 ? Vector3.up : Vector3.down;
                int center = vertices.Count;
                vertices.Add(normal * 0.5f);
                normals.Add(normal);
                uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= sides; i++)
                {
                    float angle = i / (float)sides * Mathf.PI * 2f;
                    var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.5f;
                    vertices.Add(radial + normal * 0.5f);
                    normals.Add(normal);
                    uv.Add(new Vector2(radial.x + 0.5f, radial.z + 0.5f));
                    if (i < sides)
                        AddTriangle(triangles, center, center + i + 2, center + i + 1, cap == 1);
                }
            }

            return Finish("FolioXR Cilindro", vertices, normals, uv, triangles);
        }

        internal static Mesh Sphere(int longitude = 24, int latitude = 12, bool inward = false)
        {
            longitude = Mathf.Clamp(longitude, 3, 64);
            latitude = Mathf.Clamp(latitude, 2, 32);
            int stride = longitude + 1;
            var vertices = new List<Vector3>(stride * (latitude + 1));
            var normals = new List<Vector3>(stride * (latitude + 1));
            var uv = new List<Vector2>(stride * (latitude + 1));
            var triangles = new List<int>(6 * longitude * (latitude - 1));

            for (int row = 0; row <= latitude; row++)
            {
                float polar = row / (float)latitude * Mathf.PI;
                float y = Mathf.Cos(polar);
                float radius = row == 0 || row == latitude ? 0f : Mathf.Sin(polar);
                for (int column = 0; column <= longitude; column++)
                {
                    float u = column / (float)longitude;
                    float angle = u * Mathf.PI * 2f;
                    var position = new Vector3(radius * Mathf.Cos(angle), y, radius * Mathf.Sin(angle));
                    vertices.Add(position);
                    normals.Add(inward ? -position : position);
                    // Altura lineal: permite una textura vertical de cielo sin shader especial.
                    uv.Add(new Vector2(u, (y + 1f) * 0.5f));
                    if (row == latitude || column == longitude) continue;
                    int a = row * stride + column;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    // Los polos tienen un solo triángulo por segmento, sin caras degeneradas.
                    if (row > 0) AddTriangle(triangles, a, b, c, inward);
                    if (row < latitude - 1) AddTriangle(triangles, b, d, c, inward);
                }
            }

            return Finish(inward ? "FolioXR Cielo interior" : "FolioXR Esfera", vertices, normals, uv, triangles);
        }

        internal static Mesh Octahedron()
        {
            var vertices = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uv = new List<Vector2>(24);
            var triangles = new List<int>(24);
            Vector3[] ring = { Vector3.right, Vector3.forward, Vector3.left, Vector3.back };
            for (int i = 0; i < ring.Length; i++)
            {
                Vector3 current = ring[i];
                Vector3 next = ring[(i + 1) % ring.Length];
                AddFacet(vertices, normals, uv, triangles, Vector3.up, next, current);
                AddFacet(vertices, normals, uv, triangles, Vector3.down, current, next);
            }
            return Finish("FolioXR Octaedro", vertices, normals, uv, triangles);
        }

        private static void AddFace(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv,
            List<int> triangles, Vector3 center, Vector3 right, Vector3 up)
        {
            int start = vertices.Count;
            vertices.Add(center - right * 0.5f - up * 0.5f);
            vertices.Add(center + right * 0.5f - up * 0.5f);
            vertices.Add(center + right * 0.5f + up * 0.5f);
            vertices.Add(center - right * 0.5f + up * 0.5f);
            Vector3 normal = Vector3.Cross(right, up).normalized;
            for (int i = 0; i < 4; i++) normals.Add(normal);
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(1f, 0f));
            uv.Add(new Vector2(1f, 1f));
            uv.Add(new Vector2(0f, 1f));
            AddTriangle(triangles, start, start + 1, start + 2, false);
            AddTriangle(triangles, start, start + 2, start + 3, false);
        }

        private static void AddFacet(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv,
            List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
        {
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            uv.Add(new Vector2(0.5f, 1f));
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(1f, 0f));
            AddTriangle(triangles, start, start + 1, start + 2, false);
        }

        private static void AddTriangle(List<int> triangles, int a, int b, int c, bool reverse)
        {
            triangles.Add(a);
            triangles.Add(reverse ? c : b);
            triangles.Add(reverse ? b : c);
        }

        private static Mesh Finish(string name, List<Vector3> vertices, List<Vector3> normals,
            List<Vector2> uv, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
