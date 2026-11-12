using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Environments
{
    // Exterior realista (Claude, 11 nov): terreno con alturas, agua, vegetación por bioma,
    // ciudad nocturna y un cielo pintado con sol/luna, nubes y estrellas. Todo procedural:
    // sin assets, sin luces extra y dentro del presupuesto de ocho lotes.
    public static partial class HouseGenerator
    {
        /// <summary>Dirección hacia el sol (o la luna) en coordenadas de la casa; la misma del cielo pintado.</summary>
        public static Vector3 SunDirection(EnvironmentTheme theme)
        {
            float a = theme.SunAzimuth * Mathf.Deg2Rad, e = theme.SunElevation * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(e) * Mathf.Cos(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(a));
        }

        private sealed partial class Builder
        {
            private const float GroundY = -0.28f;
            private const float TerrainRadius = 86f;
            private const float LeafV = 0.97f, RockV = 0.45f;
            private static readonly Vector2 WindowUv = new Vector2(0.5f, 0.008f);

            private Mesh _trunk, _rock, _glow, _building;
            private readonly Mesh[] _leaves = new Mesh[8];
            private Color[] _leafTones;
            private float _flat;
            private float _waterLevel = float.NegativeInfinity;
            private bool HasWater => _theme.Exterior == ExteriorKind.Lake ||
                _theme.Exterior == ExteriorKind.Beach || _theme.Exterior == ExteriorKind.Island;

            private void BuildExterior()
            {
                _flat = 15f * Mathf.Max(_theme.HouseWidth / 12f, _theme.HouseDepth / 10f);
                _leafTones = LeafTones();
                _trunk = Template(ProceduralMeshes.Cylinder(7)); // Copia propia: sin colisionadores.
                _building = Template(ProceduralMeshes.Box());
                _rock = Uniform(ProceduralMeshes.Octahedron(), new Vector2(0.5f, RockV));
                _glow = Uniform(ProceduralMeshes.Box(), WindowUv);
                for (int i = 0; i < _leaves.Length; i++)
                    _leaves[i] = Uniform(ProceduralMeshes.Octahedron(), new Vector2((i + 0.5f) / 8f, LeafV));
                if (HasWater) _waterLevel = GroundY - 0.55f;

                // Suelo físico invisible (el mismo de antes); el terreno visible no tiene colisión.
                AddCollider(Matrix4x4.TRS(new Vector3(0f, -0.34f, 0f), Quaternion.identity, new Vector3(120f, 0.12f, 120f)), true);
                for (int i = 0; i < 8; i++)
                    B(Cream, new Vector3(1.7f, -0.22f, -6f - i * 0.78f), new Vector3(1.25f, 0.13f, 0.55f), true);
                if (_baked)
                {
                    // Entorno horneado: el paisaje llega dibujado; aquí solo quedan los suelos caminables.
                    if (_theme.Exterior == ExteriorKind.Lake) Dock();
                    return;
                }
                AddWorld(TerrainMesh(), Terrain, Matrix4x4.identity);
                if (HasWater) AddWorld(WaterMesh(), Sky, Matrix4x4.identity);

                switch (_theme.Exterior)
                {
                    case ExteriorKind.Mountains:
                        Scatter(75, 16f, 70f, Pine); Scatter(24, 16f, 60f, Rock); Scatter(14, 14f, 30f, Bush);
                        break;
                    case ExteriorKind.Lake:
                        Scatter(26, 17f, 70f, Tree); Scatter(30, 20f, 75f, Pine); Scatter(16, 15f, 40f, Rock);
                        Scatter(16, 13f, 30f, Bush); Dock();
                        break;
                    case ExteriorKind.Beach:
                        Scatter(16, 13f, 34f, Palm); Scatter(18, 14f, 40f, Bush); Scatter(10, 18f, 50f, Rock);
                        break;
                    case ExteriorKind.Snow:
                        Scatter(80, 15f, 72f, Pine); Scatter(18, 16f, 60f, Rock);
                        break;
                    case ExteriorKind.Desert:
                        Scatter(30, 14f, 55f, Cactus); Scatter(40, 14f, 70f, Rock); Scatter(14, 14f, 40f, Bush);
                        break;
                    case ExteriorKind.Forest:
                        Scatter(90, 14f, 60f, Tree); Scatter(60, 16f, 72f, Pine); Scatter(34, 13f, 40f, Bush);
                        Scatter(12, 14f, 50f, Rock);
                        break;
                    case ExteriorKind.Autumn:
                        Scatter(70, 15f, 65f, Tree); Scatter(14, 20f, 70f, Pine); Scatter(24, 13f, 40f, Bush);
                        Scatter(10, 16f, 50f, Rock);
                        break;
                    case ExteriorKind.City:
                        Scatter(14, 14f, 30f, Tree); Scatter(16, 13f, 30f, Bush); City();
                        break;
                    case ExteriorKind.Island:
                        Scatter(18, 12f, 24f, Palm); Scatter(16, 12f, 24f, Bush); Scatter(14, 18f, 26f, Rock);
                        Scatter(10, 55f, 82f, Palm);
                        break;
                    default:
                        Scatter(24, 18f, 45f, Tree); Scatter(10, 25f, 65f, Pine); Scatter(26, 13f, 32f, Bush);
                        Scatter(10, 16f, 50f, Rock);
                        break;
                }
            }

            // ───────────── Terreno ─────────────

            /// <summary>Altura sobre GroundY en (x, z). Cero dentro del radio libre de la casa.</summary>
            private float Height(float x, float z)
            {
                float r = Mathf.Sqrt(x * x + z * z);
                if (r < _flat) return 0f;
                float dx = x / r, dz = z / r;
                float H = _theme.MountainHeight;
                float near = Smooth(_flat, _flat + 10f, r);
                float roll = (Fbm(x * 0.045f, z * 0.045f, 3) - 0.5f) * 1.6f * near;
                float ring = Fbm(dx * 2.2f + 13f, dz * 2.2f - 7f, 4);
                switch (_theme.Exterior)
                {
                    case ExteriorKind.Mountains:
                    case ExteriorKind.Snow:
                    {
                        float ridge = 1f - Mathf.Abs(2f * Fbm(dx * 3f + 5f, dz * 3f + 9f, 5) - 1f);
                        float peaks = H * (0.22f + 0.78f * ridge * ridge) * Smooth(40f, 82f, r);
                        float foot = 0.14f * H * Fbm(x * 0.05f, z * 0.05f, 4) * Smooth(_flat + 4f, 45f, r);
                        return peaks + foot + roll;
                    }
                    case ExteriorKind.Lake:
                    {
                        float front = Smooth(0.1f, 0.6f, -dz);
                        float basin = front * Smooth(_flat + 2f, _flat + 9f, r) * (1f - Smooth(48f, 64f, r)) * 3.5f;
                        float hills = H * (0.3f + 0.7f * ring) * Smooth(50f, 82f, r);
                        return hills + roll * (1f - front) - basin;
                    }
                    case ExteriorKind.Beach:
                    {
                        float sea = Smooth(-0.2f, 0.4f, -dz);
                        float slope = sea * Smooth(_flat + 3f, _flat + 18f, r) * 4f;
                        float hills = H * (0.3f + 0.7f * ring) * Smooth(30f, 72f, r) * (1f - sea);
                        return hills + roll * 0.4f * (1f - sea) - slope;
                    }
                    case ExteriorKind.Island:
                    {
                        float sea = Smooth(_flat + 6f, _flat + 18f, r) * 4.5f;
                        float far = Smooth(0.55f, 0.75f, Fbm(dx * 3f + 2f, dz * 3f + 4f, 3)) *
                                    Smooth(50f, 60f, r) * (1f - Smooth(78f, 86f, r));
                        return roll * 0.3f - sea + far * (H + 4.5f);
                    }
                    case ExteriorKind.Desert:
                    {
                        float m = Fbm(x * 0.022f + 31f, z * 0.022f - 17f, 4);
                        float mesa = H * (Smooth(0.50f, 0.54f, m) + 0.45f * Smooth(0.62f, 0.65f, m)) * Smooth(32f, 55f, r);
                        float dunes = (0.9f * Mathf.Sin(x * 0.13f + Fbm(x * 0.03f, z * 0.03f, 2) * 6f) + 0.4f) * near;
                        return mesa + dunes * (1f - Smooth(40f, 60f, r)) + H * 0.25f * ring * Smooth(60f, 84f, r);
                    }
                    case ExteriorKind.City:
                        return 0f;
                    default:
                        return H * (0.35f + 0.65f * ring) * Smooth(38f, 82f, r) + roll;
                }
            }

            /// <summary>Banda vertical de la textura del terreno: suelo, roca o nieve según la altura.</summary>
            private float BandV(float x, float z, float h)
            {
                float jitter = (Fbm(x * 0.09f + 3f, z * 0.09f + 8f, 3) - 0.5f);
                if (HasWater && h < 0.25f && Mathf.Sqrt(x * x + z * z) > _flat + 1f) return 0.17f; // Orilla.
                float snowLine = Mathf.Max(1f, _theme.MountainHeight * 0.6f);
                float t = h / snowLine + jitter * 0.25f;
                if (t < 0.14f) return Mathf.Clamp(0.1f + jitter * 0.22f, 0.02f, 0.19f);
                if (t < 1f) return Mathf.Lerp(0.26f, 0.62f, (t - 0.14f) / 0.86f);
                return Mathf.Lerp(0.72f, 0.9f, Mathf.Clamp01(t - 1f));
            }

            private Mesh TerrainMesh()
            {
                const int rings = 56, segments = 144;
                var vertices = new List<Vector3>((rings + 1) * (segments + 1));
                var uv = new List<Vector2>(vertices.Capacity);
                var triangles = new List<int>(rings * segments * 6);
                for (int i = 0; i <= rings; i++)
                {
                    float r = TerrainRadius * Mathf.Pow(i / (float)rings, 1.45f);
                    for (int j = 0; j <= segments; j++)
                    {
                        float a = j * Mathf.PI * 2f / segments;
                        float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                        float h = Height(x, z);
                        // El borde baja un poco para no dejar ver la costura con el cielo.
                        if (i == rings) h = Mathf.Min(h, 0f) - 1f;
                        vertices.Add(new Vector3(x, GroundY + h, z));
                        uv.Add(new Vector2(x * 0.11f + z * 0.07f, BandV(x, z, h)));
                    }
                }
                Grid(triangles, vertices, rings, segments);
                return FinishMesh("FolioXR Terreno", vertices, uv, triangles);
            }

            private Mesh WaterMesh()
            {
                const int rings = 24, segments = 96;
                const float radius = 88f;
                var vertices = new List<Vector3>();
                var uv = new List<Vector2>();
                var triangles = new List<int>();
                for (int i = 0; i <= rings; i++)
                {
                    float f = Mathf.Pow(i / (float)rings, 1.3f);
                    for (int j = 0; j <= segments; j++)
                    {
                        float a = j * Mathf.PI * 2f / segments;
                        vertices.Add(new Vector3(Mathf.Cos(a) * f * radius, _waterLevel, Mathf.Sin(a) * f * radius));
                        // Muestra la franja de agua pintada en la mitad inferior de la textura del cielo.
                        uv.Add(new Vector2(j / (float)segments * 6f, Mathf.Lerp(0.04f, 0.40f, f)));
                    }
                }
                Grid(triangles, vertices, rings, segments);
                return FinishMesh("FolioXR Agua", vertices, uv, triangles);
            }

            /// <summary>Triángulos de una malla polar con las caras hacia arriba.</summary>
            private static void Grid(List<int> triangles, List<Vector3> vertices, int rings, int segments)
            {
                int stride = segments + 1;
                for (int i = 0; i < rings; i++)
                    for (int j = 0; j < segments; j++)
                    {
                        int a = i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                        UpTriangle(triangles, vertices, a, c, b);
                        UpTriangle(triangles, vertices, b, c, d);
                    }
            }

            private static void UpTriangle(List<int> triangles, List<Vector3> v, int a, int b, int c)
            {
                Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (n.sqrMagnitude < 1e-10f) return; // Centro del disco: triángulos degenerados.
                if (n.y < 0f) { int k = b; b = c; c = k; }
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            private Mesh FinishMesh(string name, List<Vector3> vertices, List<Vector2> uv, List<int> triangles)
            {
                var mesh = new Mesh
                {
                    name = name,
                    indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
                };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return Template(mesh);
            }

            /// <summary>Copia de una malla con todas sus UV en un solo texel (color plano de la textura).</summary>
            private Mesh Uniform(Mesh mesh, Vector2 texel)
            {
                Vector2[] uv = mesh.uv;
                for (int i = 0; i < uv.Length; i++) uv[i] = texel;
                mesh.uv = uv;
                return Template(mesh);
            }

            // ───────────── Vegetación y rocas ─────────────

            private delegate void Placer(Vector3 position, float size);

            private void Scatter(int count, float minRadius, float maxRadius, Placer place)
            {
                int placed = 0;
                for (int attempt = 0; attempt < count * 6 && placed < count; attempt++)
                {
                    float angle = Next() * Mathf.PI * 2f;
                    // Raíz cuadrada: densidad pareja por área, no amontonada cerca del centro.
                    float r = Mathf.Sqrt(Mathf.Lerp(minRadius * minRadius, maxRadius * maxRadius, Next()));
                    float x = Mathf.Cos(angle) * r, z = Mathf.Sin(angle) * r;
                    if (Mathf.Abs(x) < 8.5f * _theme.HouseWidth / 12f && z > -11.5f * _theme.HouseDepth / 10f &&
                        z < 7f * _theme.HouseDepth / 10f) continue;
                    if (Mathf.Abs(x - 1.7f) < 1.6f && z < -5f && z > -26f) continue; // Camino y muelle.
                    float y = GroundY + Height(x, z);
                    if (y < _waterLevel + 0.25f) continue;
                    // Nada sobre paredes casi verticales (mesetas, picos).
                    float slope = Mathf.Abs(Height(x + 1f, z) - Height(x - 1f, z)) + Mathf.Abs(Height(x, z + 1f) - Height(x, z - 1f));
                    if (slope > 2.6f) continue;
                    place(new Vector3(x, y - 0.05f, z), 0.75f + Next() * 0.6f);
                    placed++;
                }
            }

            private Mesh Leaf() { return _leaves[_random.Next(Mathf.Max(1, Mathf.Min(_leaves.Length, _leafTones.Length)))]; }

            private void Tree(Vector3 p, float size)
            {
                float h = 4.5f * size;
                AddWorld(_trunk, Wood, Matrix4x4.TRS(p + Vector3.up * h * 0.3f, Quaternion.identity, new Vector3(0.3f * size, h * 0.6f, 0.3f * size)));
                int clumps = 4 + _random.Next(3);
                for (int i = 0; i < clumps; i++)
                {
                    float a = Next() * Mathf.PI * 2f;
                    float spread = h * (0.12f + Next() * 0.14f);
                    Vector3 c = p + new Vector3(Mathf.Cos(a) * spread, h * (0.62f + Next() * 0.25f), Mathf.Sin(a) * spread);
                    float s = h * (0.32f + Next() * 0.16f);
                    AddWorld(Leaf(), Terrain, Matrix4x4.TRS(c, Quaternion.Euler(Next() * 30f, Next() * 360f, Next() * 30f),
                        new Vector3(s, s * 0.8f, s)));
                }
            }

            private void Pine(Vector3 p, float size)
            {
                float h = 7f * size;
                AddWorld(_trunk, Wood, Matrix4x4.TRS(p + Vector3.up * h * 0.25f, Quaternion.identity, new Vector3(0.28f, h * 0.5f, 0.28f)));
                Mesh leaf = Leaf();
                for (int k = 0; k < 4; k++)
                {
                    float y = h * (0.32f + 0.17f * k);
                    float w = h * (0.40f - 0.08f * k);
                    AddWorld(leaf, Terrain, Matrix4x4.TRS(p + Vector3.up * y, Quaternion.Euler(0f, Next() * 90f, 0f),
                        new Vector3(w, h * 0.34f, w)));
                }
            }

            private void Palm(Vector3 p, float size)
            {
                float h = 6f * size;
                float a = Next() * Mathf.PI * 2f;
                Vector3 lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * h * 0.28f;
                Vector3 previous = p;
                for (int i = 1; i <= 6; i++)
                {
                    float t = i / 6f;
                    Vector3 next = p + Vector3.up * h * t + lean * t * t;
                    Vector3 d = next - previous;
                    AddWorld(_trunk, Wood, Matrix4x4.TRS((previous + next) * 0.5f, Quaternion.FromToRotation(Vector3.up, d),
                        new Vector3(0.26f - 0.06f * t, d.magnitude * 1.05f, 0.26f - 0.06f * t)));
                    previous = next;
                }
                Mesh leaf = Leaf();
                for (int k = 0; k < 8; k++)
                {
                    Quaternion yaw = Quaternion.Euler(0f, k * 45f + Next() * 15f, 0f);
                    Quaternion droop = yaw * Quaternion.Euler(28f + Next() * 14f, 0f, 0f);
                    Vector3 center = previous + yaw * Vector3.forward * 1.25f * size - Vector3.up * 0.35f * size;
                    AddWorld(leaf, Terrain, Matrix4x4.TRS(center, droop, new Vector3(0.55f, 0.07f, 2.6f) * size));
                }
            }

            private void Cactus(Vector3 p, float size)
            {
                float h = 2.6f * size;
                AddWorld(_trunk, Green, Matrix4x4.TRS(p + Vector3.up * h * 0.5f, Quaternion.identity, new Vector3(0.38f, h, 0.38f)));
                for (int side = -1; side <= 1; side += 2)
                {
                    if (Next() < 0.3f) continue;
                    float y = h * (0.35f + Next() * 0.25f);
                    Quaternion yaw = Quaternion.Euler(0f, Next() * 360f, 0f);
                    Vector3 o = yaw * Vector3.right * side;
                    AddWorld(_trunk, Green, Matrix4x4.TRS(p + Vector3.up * y + o * 0.35f, yaw * Quaternion.Euler(0f, 0f, 90f),
                        new Vector3(0.24f, 0.55f, 0.24f)));
                    AddWorld(_trunk, Green, Matrix4x4.TRS(p + Vector3.up * (y + 0.45f) + o * 0.62f, Quaternion.identity,
                        new Vector3(0.24f, 0.95f, 0.24f)));
                }
            }

            private void Bush(Vector3 p, float size)
            {
                Mesh leaf = Leaf();
                for (int i = 0; i < 3; i++)
                {
                    float s = (0.7f + Next() * 0.5f) * size;
                    Vector3 o = new Vector3((Next() - 0.5f) * s, s * 0.3f, (Next() - 0.5f) * s);
                    AddWorld(leaf, Terrain, Matrix4x4.TRS(p + o, Quaternion.Euler(0f, Next() * 360f, 0f), new Vector3(s, s * 0.7f, s)));
                }
            }

            private void Rock(Vector3 p, float size)
            {
                float s = size * (0.6f + Next() * 1.4f);
                AddWorld(_rock, Terrain, Matrix4x4.TRS(p + Vector3.up * s * 0.12f, Quaternion.Euler(Next() * 25f, Next() * 360f, Next() * 25f),
                    new Vector3(s * (1f + Next()), s * (0.45f + Next() * 0.4f), s * (0.8f + Next() * 0.6f))));
            }

            private void Dock()
            {
                // Muelle de madera desde el final del camino hasta el lago; se puede caminar por él.
                float deck = _waterLevel + 0.45f;
                Vector3 c = new Vector3(1.7f, deck, -19f);
                AddWorld(_box, Wood, Matrix4x4.TRS(c, Quaternion.identity, new Vector3(1.9f, 0.1f, 12f)), true);
                for (int i = 0; i < 6; i++)
                    for (int side = -1; side <= 1; side += 2)
                        AddWorld(_trunk, Wood, Matrix4x4.TRS(new Vector3(1.7f + side * 0.85f, deck - 0.9f, -13.5f - i * 2.2f),
                            Quaternion.identity, new Vector3(0.16f, 2f, 0.16f)));
            }

            private void City()
            {
                // Edificios oscuros en anillo; ventanas iluminadas con el material sin luz del cielo.
                int count = 44;
                for (int i = 0; i < count; i++)
                {
                    float angle = (i + Next() * 0.6f) * Mathf.PI * 2f / count;
                    float r = 42f + Next() * 34f;
                    Vector3 toCenter = -new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Vector3 c = -toCenter * r;
                    float w = 9f + Next() * 9f, d = 9f + Next() * 8f, h = 14f + Mathf.Pow(Next(), 1.6f) * 52f;
                    Quaternion facing = Quaternion.LookRotation(toCenter, Vector3.up);
                    AddWorld(_building, Metal, Matrix4x4.TRS(c + Vector3.up * (GroundY + h * 0.5f), facing, new Vector3(w, h, d)));
                    float density = 0.25f + Next() * 0.35f;
                    for (float y = 2.2f; y < h - 1.5f; y += 3.3f)
                        for (float x = -w * 0.5f + 1.4f; x < w * 0.5f - 1.2f; x += 2.6f)
                        {
                            if (Next() > density) continue;
                            Vector3 local = new Vector3(x, GroundY + y, d * 0.5f + 0.06f); // +Z local mira a la casa.
                            AddWorld(_glow, Sky, Matrix4x4.TRS(c + facing * local, facing, new Vector3(1.3f, 1.6f, 0.1f)));
                        }
                    if (h > 50f) // Baliza en la azotea de los más altos.
                        AddWorld(_glow, Sky, Matrix4x4.TRS(c + Vector3.up * (GroundY + h + 0.6f), facing, Vector3.one * 0.7f));
                }
                for (int i = 0; i < 6; i++) // Farolas del camino.
                {
                    Vector3 p = new Vector3(i % 2 == 0 ? 0.6f : 2.8f, GroundY, -7f - i * 1.6f);
                    AddWorld(_trunk, Metal, Matrix4x4.TRS(p + Vector3.up * 1.6f, Quaternion.identity, new Vector3(0.08f, 3.2f, 0.08f)));
                    AddWorld(_glow, Sky, Matrix4x4.TRS(p + Vector3.up * 3.25f, Quaternion.identity, new Vector3(0.32f, 0.22f, 0.32f)));
                }
            }

            private Color[] LeafTones()
            {
                if (_theme.Leaves != null && _theme.Leaves.Length > 0) return _theme.Leaves;
                Color f = _theme.Foliage;
                return new[] { f, f * 0.82f, Color.Lerp(f, new Color(0.55f, 0.6f, 0.25f), 0.25f), f * 1.1f };
            }

            // ───────────── Texturas ─────────────

            private Texture2D TerrainTexture()
            {
                const int w = 256, hgt = 256;
                var pixels = new Color[w * hgt];
                Color[] tones = _leafTones ?? LeafTones();
                for (int y = 0; y < hgt; y++)
                {
                    float v = (y + 0.5f) / hgt;
                    for (int x = 0; x < w; x++)
                    {
                        float fine = Fbm(x * 0.21f, y * 0.21f, 3);
                        float coarse = Fbm(x * 0.035f + 40f, y * 0.05f, 3);
                        Color c;
                        if (v >= 0.94f)
                        {
                            // Tira de follaje: ocho celdas de 32 px, una por tono.
                            Color tone = tones[(x / 32) % tones.Length];
                            c = tone * (0.86f + 0.24f * fine);
                        }
                        else
                        {
                            Color ground = Color.Lerp(_theme.Ground, _theme.GroundAlt, Smooth(0.06f, 0.16f, v + (coarse - 0.5f) * 0.06f));
                            ground *= 0.84f + 0.3f * fine; // Matas de pasto, piedras, arena con grano.
                            Color rock = _theme.Rock * (0.78f + 0.32f * Fbm(x * 0.08f, y * 0.6f, 3) + 0.1f * fine);
                            Color snow = _theme.Snow * (0.93f + 0.08f * fine);
                            c = Color.Lerp(ground, rock, Smooth(0.19f, 0.27f, v));
                            c = Color.Lerp(c, snow, Smooth(0.64f, 0.72f, v));
                        }
                        c.a = 1f;
                        pixels[y * w + x] = c;
                    }
                }
                Texture2D texture = Texture("Terreno y follaje", w, hgt, pixels, true);
                texture.wrapModeV = TextureWrapMode.Clamp;
                return texture;
            }

            private Texture2D SkyTexture()
            {
                const int w = 512, hgt = 256;
                var pixels = new Color[w * hgt];
                bool night = _theme.Sky == SkyKind.Night, sunset = _theme.Sky == SkyKind.Sunset,
                     overcast = _theme.Sky == SkyKind.Overcast, city = _theme.Exterior == ExteriorKind.City;
                Vector3 sun = SunDirection(_theme);
                Vector3 sunFlat = new Vector3(sun.x, 0f, sun.z).normalized;
                Color water = _theme.Water;
                for (int y = 0; y < hgt; y++)
                {
                    float v = (y + 0.5f) / hgt;
                    float sy = v * 2f - 1f;
                    float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - sy * sy));
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w;
                        float a = u * Mathf.PI * 2f;
                        var dir = new Vector3(ring * Mathf.Cos(a), sy, ring * Mathf.Sin(a));
                        var flat = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        float toward = Mathf.Max(0f, Vector3.Dot(flat, sunFlat));
                        Color c;
                        if (v < 0.02f) c = city || night ? new Color(1f, 0.80f, 0.48f) : new Color(1f, 0.92f, 0.75f); // Ventanas.
                        else if (v < 0.44f)
                        {
                            // Agua: profunda cerca, refleja el horizonte lejos; destello bajo el sol o la luna.
                            float t = Mathf.Pow((v - 0.04f) / 0.36f, 1.4f);
                            Color deep = water * (night ? 0.25f : 0.62f);
                            Color far = Color.Lerp(water, _theme.SkyHorizon, night ? 0.35f : 0.55f);
                            float ripple = Fbm(u * 96f, v * 160f, 3);
                            c = Color.Lerp(deep, far, Mathf.Clamp01(t)) * (0.88f + 0.24f * ripple);
                            float glint = Mathf.Pow(toward, sunset ? 60f : 120f) * Mathf.Clamp01(t * 1.4f) *
                                          Smooth(0.55f, 0.85f, ripple);
                            if (!overcast) c += _theme.SunColor * glint * (night ? 0.35f : sunset ? 1.1f : 0.8f);
                        }
                        else if (v < 0.5f) c = Color.Lerp(_theme.SkyHorizon * 0.85f, _theme.SkyHorizon, (v - 0.44f) / 0.06f);
                        else c = SkyColor(dir, sun, toward, u, night, sunset, overcast, city);
                        c.a = 1f;
                        pixels[y * w + x] = c;
                    }
                }
                Texture2D texture = Texture("Cielo " + _theme.Name, w, hgt, pixels, false, false);
                texture.wrapModeU = TextureWrapMode.Repeat;
                texture.wrapModeV = TextureWrapMode.Clamp;
                return texture;
            }

            private Color SkyColor(Vector3 dir, Vector3 sun, float toward, float u, bool night, bool sunset, bool overcast, bool city)
            {
                float elevation = Mathf.Max(0f, dir.y);
                Color c = Color.Lerp(_theme.SkyHorizon, _theme.SkyZenith, Mathf.Pow(elevation, 0.45f));
                float d = Vector3.Dot(dir, sun);
                if (sunset)
                {
                    // Resplandor cálido sobre el horizonte del lado del sol.
                    float band = Mathf.Pow(1f - elevation, 5f);
                    c = Color.Lerp(c, _theme.SunColor, band * (0.25f + 0.65f * Mathf.Pow(toward, 2f)));
                }
                if (!night)
                {
                    float halo = Mathf.Pow(Mathf.Max(0f, d), overcast ? 6f : 48f) * (overcast ? 0.25f : 0.5f);
                    c += _theme.SunColor * halo;
                }
                // Nubes proyectadas sobre un plano: se aplanan hacia el horizonte.
                float cloud = 0f;
                if (dir.y > 0.005f)
                {
                    float k = 1.4f / (dir.y + 0.1f);
                    float n = Fbm(dir.x * k + 11f, dir.z * k - 5f, 5);
                    float cover = Mathf.Clamp01(_theme.CloudCover);
                    cloud = Smooth(1f - cover - 0.08f, 1f - cover + 0.22f, n) * Smooth(0.005f, 0.12f, dir.y);
                    if (overcast) cloud = Mathf.Max(cloud, 0.75f * Smooth(0.0f, 0.2f, dir.y));
                    float shade = Fbm(dir.x * k + 3f, dir.z * k + 1f, 3);
                    Color lit = _theme.CloudColor * (0.72f + 0.28f * shade + (night ? 0f : 0.25f * Mathf.Pow(Mathf.Max(0f, d), 4f)));
                    if (sunset) lit = Color.Lerp(lit, _theme.SunColor, 0.35f * Mathf.Pow(toward, 1.5f) * (1f - elevation));
                    c = Color.Lerp(c, lit, cloud * (overcast ? 0.9f : 0.85f));
                }
                if (night)
                {
                    // Estrellas con brillo variable, más densas en una franja (vía láctea), y luna.
                    float milky = Mathf.Exp(-Mathf.Pow(Vector3.Dot(dir, new Vector3(0.45f, 0.35f, 0.82f).normalized) / 0.22f, 2f));
                    float hash = Fbm(u * 900f, dir.y * 700f, 1);
                    float star = Smooth(0.945f - milky * 0.05f, 0.995f, hash) * Smooth(0.02f, 0.25f, elevation) *
                                 (0.3f + 0.7f * Fbm(u * 517f + 3f, dir.y * 333f, 1));
                    c += new Color(0.9f, 0.92f, 1f) * star * (1f - cloud) * (city ? 0.35f : 1f);
                    c += new Color(0.10f, 0.11f, 0.16f) * milky * Fbm(dir.x * 9f, dir.z * 9f, 3) * (city ? 0.2f : 1f) * (1f - cloud);
                    float moon = Smooth(0.99965f, 0.99975f, d);
                    float craters = 0.82f + 0.18f * Fbm(dir.x * 900f, dir.z * 900f, 2);
                    c = Color.Lerp(c, new Color(0.93f, 0.94f, 0.88f) * craters, moon);
                    c += new Color(0.25f, 0.28f, 0.35f) * Mathf.Pow(Mathf.Max(0f, d), 400f) * 0.6f;
                }
                else if (!overcast)
                {
                    float disc = Smooth(0.99975f, 0.99985f, d);
                    c = Color.Lerp(c, Color.Lerp(_theme.SunColor, Color.white, 0.6f) * 1.2f, disc * (1f - cloud * 0.8f));
                }
                if (city)
                {
                    // Resplandor de la ciudad y silueta lejana con ventanas encendidas.
                    c += new Color(0.35f, 0.22f, 0.12f) * Mathf.Pow(1f - elevation, 14f) * 0.6f;
                    int building = Mathf.FloorToInt(u * 180f);
                    float top = 0.012f + 0.06f * Fbm(building * 7.13f, 3.1f, 1) * Fbm(building * 1.7f, 9.4f, 1);
                    if (elevation < top)
                    {
                        Color wall = new Color(0.035f, 0.04f, 0.06f);
                        float lit = Fbm(u * 1400f, elevation * 1800f, 1);
                        c = lit > 0.74f ? Color.Lerp(wall, new Color(1f, 0.78f, 0.45f), 0.55f) : wall;
                    }
                }
                return c;
            }

            // ───────────── Ruido ─────────────

            private static float Smooth(float a, float b, float x)
            {
                float t = Mathf.Clamp01((x - a) / (b - a));
                return t * t * (3f - 2f * t);
            }

            private float Hash(int x, int y)
            {
                unchecked
                {
                    int h = x * 374761393 + y * 668265263 + _theme.Seed * 144665;
                    h = (h ^ (h >> 13)) * 1274126177;
                    return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
                }
            }

            private float ValueNoise(float x, float y)
            {
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
                float fx = x - xi, fy = y - yi;
                fx = fx * fx * (3f - 2f * fx);
                fy = fy * fy * (3f - 2f * fy);
                float a = Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx);
                float b = Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx);
                return Mathf.Lerp(a, b, fy);
            }

            /// <summary>Ruido fractal en [0, 1].</summary>
            private float Fbm(float x, float y, int octaves)
            {
                float sum = 0f, amplitude = 0.5f, total = 0f;
                for (int i = 0; i < octaves; i++)
                {
                    sum += ValueNoise(x, y) * amplitude;
                    total += amplitude;
                    x = x * 2.03f + 17.1f;
                    y = y * 2.03f - 9.7f;
                    amplitude *= 0.5f;
                }
                return sum / total;
            }
        }
    }
}
