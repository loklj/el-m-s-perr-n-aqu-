using System;
using System.Collections.Generic;
using System.Globalization;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace FolioXR.Environments
{
    /// <summary>Casa completa sin assets: ocho lotes opacos y ninguna luz adicional.</summary>
    public static partial class HouseGenerator
    {
        public const float SkyRadius = 150f; // Por fuera de los picos más altos (r 84 m, 45 m de altura).

        public static HouseBuildResult Build(EnvironmentTheme theme, Transform parent)
        {
            Validate(theme);
            var timer = Stopwatch.StartNew();
            var result = new HouseBuildResult();
            Builder builder = null;
            try
            {
                result.Root = new GameObject("FolioXR · " + theme.Name);
                result.Root.SetActive(false);
                result.Root.transform.SetParent(parent, false);
                builder = new Builder(theme, result);
                builder.Construct();
                timer.Stop();
                Debug.Log("[FolioXR] Entorno: triángulos=" + result.TriangleCount +
                          ", mallas=" + result.MeshCount + ", materiales=" + result.MaterialCount +
                          ", generación=" + timer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms");
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
            finally
            {
                if (builder != null) builder.DisposeTemplates();
            }
        }

        private static void Validate(EnvironmentTheme theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            CheckRange(theme.HouseWidth, 10f, 18f, nameof(theme.HouseWidth));
            CheckRange(theme.HouseDepth, 9f, 16f, nameof(theme.HouseDepth));
            CheckRange(theme.StoreyHeight, 2.8f, 4f, nameof(theme.StoreyHeight));
            CheckRange(theme.WallThickness, 0.12f, 0.30f, nameof(theme.WallThickness));
            CheckRange(theme.FurnitureDensity, 0.5f, 1.5f, nameof(theme.FurnitureDensity));
            if (theme.BookPalette == null || theme.BookPalette.Length == 0)
                throw new ArgumentException("El tema necesita al menos un color de lomo.", nameof(theme));
        }

        private static void CheckRange(float value, float min, float max, string name)
        {
            if (float.IsNaN(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name, "Medida fuera del intervalo seguro del generador.");
        }

        private sealed partial class Builder
        {
            private const int Wood = 0, Cream = 1, Fabric = 2, Metal = 3, Green = 4, Books = 5, Sky = 6, Terrain = 7;
            private readonly EnvironmentTheme _theme;
            private readonly HouseBuildResult _result;
            private readonly System.Random _random;
            private readonly List<CombineInstance>[] _batches = new List<CombineInstance>[8];
            private readonly Material[] _materials = new Material[8];
            private readonly List<Mesh> _templates = new List<Mesh>();
            private readonly Matrix4x4 _planScale;
            private Mesh _box, _cylinder, _octahedron;
            private Transform _colliders;
            private int _colliderCount;
            private readonly Mesh[] _bookBoxes = new Mesh[8];
            // Con archivos horneados (BakedEnvironment) solo se crean colisionadores y puntos de lectura.
            private readonly bool _baked;
            private Material _unlit;

            internal Builder(EnvironmentTheme theme, HouseBuildResult result)
            {
                _theme = theme;
                _result = result;
                _random = new System.Random(theme.Seed);
                _baked = BakedEnvironment.Exists(theme);
                _planScale = Matrix4x4.Scale(new Vector3(theme.HouseWidth / 12f, 1f, theme.HouseDepth / 10f));
                for (int i = 0; i < _batches.Length; i++) _batches[i] = new List<CombineInstance>();
            }

            internal void Construct()
            {
                CreateMaterials();
                _box = Template(ProceduralMeshes.Box());
                _cylinder = Template(ProceduralMeshes.Cylinder());
                _octahedron = Template(ProceduralMeshes.Octahedron());
                for (int i = 0; i < _bookBoxes.Length; i++)
                {
                    Mesh mesh = Template(ProceduralMeshes.Box());
                    Vector2[] uv = mesh.uv;
                    // Medio texel de margen por banda; las ocho variantes comparten material.
                    for (int j = 0; j < uv.Length; j++)
                        uv[j] = new Vector2((i * 16f + 1.5f + uv[j].x * 13f) / 128f,
                                            (1.5f + uv[j].y * 125f) / 128f);
                    mesh.uv = uv;
                    _bookBoxes[i] = mesh;
                }

                _colliders = new GameObject("Colisionadores (sin renderers)").transform;
                _colliders.SetParent(_result.Root.transform, false);
                BuildShell();
                BuildStairs();
                BuildRooms();
                BuildExterior();
                SetReadingPoints();
                if (_baked)
                {
                    BakedEnvironment.Attach(_theme, _result, _unlit);
                }
                else
                {
                    Mesh sky = Template(ProceduralMeshes.Sphere(64, 32, true));
                    AddWorld(sky, Sky, Matrix4x4.Scale(Vector3.one * SkyRadius));
                    Combine();
                }
                Debug.Log("[FolioXR] Casa: " + _colliderCount + " BoxCollider independientes.");
            }

            private void BuildShell()
            {
                float h = _theme.StoreyHeight;
                float wall = _theme.WallThickness;
                B(Cream, new Vector3(0f, -0.22f, 0f), new Vector3(12.4f, 0.24f, 10.4f));
                B(Wood, new Vector3(0f, -0.07f, 0f), new Vector3(12f, 0.14f, 10f), true);
                // Planta alta con hueco real de escalera x[3.7,5.7], z[-1.4,4.3].
                B(Wood, new Vector3(-1.15f, h - 0.09f, 0f), new Vector3(9.7f, 0.18f, 10f), true);
                B(Wood, new Vector3(4.85f, h - 0.09f, -3.2f), new Vector3(2.3f, 0.18f, 3.6f), true);
                B(Wood, new Vector3(4.85f, h - 0.09f, 4.65f), new Vector3(2.3f, 0.18f, 0.7f), true);
                B(Wood, new Vector3(5.85f, h - 0.09f, 1.45f), new Vector3(0.3f, 0.18f, 5.7f), true);
                B(Cream, new Vector3(0f, 2f * h + 0.09f, 0f), new Vector3(12.6f, 0.18f, 10.6f));
                for (int floor = 0; floor < 2; floor++)
                {
                    float y = floor * h;
                    Facade(new Vector3(0f, y, -5f), 12f, 0f, 4, 1);
                    Facade(new Vector3(0f, y, 5f), 12f, 0f, 4, -1);
                    Facade(new Vector3(-6f, y, 0f), 10f, 90f, 4, -1);
                    Facade(new Vector3(6f, y, 0f), 10f, 90f, 4, -1);
                }
                // Separación abierta sala/biblioteca; dormitorio separado del rellano.
                B(Wood, new Vector3(-0.15f, 1.05f, 2.6f), new Vector3(0.14f, 2.1f, 2.6f));
                B(Cream, new Vector3(1.0f, h + h * 0.5f, 1.8f), new Vector3(wall, h, 6.4f));
                B(Wood, new Vector3(1.0f, h + h - 0.18f, -2.65f), new Vector3(0.24f, 0.36f, 2.5f));
                // Balcón al frente del dormitorio; la puerta ocupa el segundo vano.
                B(Wood, new Vector3(-3f, h - 0.09f, -6.15f), new Vector3(6f, 0.18f, 2.3f), true);
                Rail(new Vector3(-6f, h, -7.3f), new Vector3(0f, h, -7.3f));
                Rail(new Vector3(-6f, h, -7.3f), new Vector3(-6f, h, -5f));
                Rail(new Vector3(0f, h, -7.3f), new Vector3(0f, h, -5f));
                // Terraza junto al jardín, top=0.12 m. Postes sostienen una pérgola ligera.
                B(Wood, new Vector3(-3f, 0.02f, -6.95f), new Vector3(6f, 0.2f, 3.9f), true);
                for (int side = 0; side < 2; side++)
                    B(Wood, new Vector3(-5.75f + side * 5.5f, 1.46f, -8.65f), new Vector3(0.16f, 2.68f, 0.16f));
                B(Wood, new Vector3(-3f, 2.75f, -8.65f), new Vector3(5.7f, 0.18f, 0.18f));
                for (int i = 0; i < 10; i++)
                    B(Wood, new Vector3(-5.7f + i * 0.6f, 2.84f, -8f), new Vector3(0.09f, 0.12f, 1.6f));
            }

            private void Facade(Vector3 center, float length, float yaw, int bays, int doorBay)
            {
                Matrix4x4 group = Group(center, yaw);
                float h = _theme.StoreyHeight;
                float wall = _theme.WallThickness;
                float bayWidth = length / bays;
                GB(group, Cream, new Vector3(0f, h - 0.2f, 0f), new Vector3(length, 0.4f, wall));
                for (int i = 0; i <= bays; i++)
                    GB(group, Cream, new Vector3(-length * 0.5f + i * bayWidth, (h - 0.4f) * 0.5f, 0f),
                       new Vector3(0.2f, h - 0.4f, wall));
                for (int i = 0; i < bays; i++)
                {
                    float x = -length * 0.5f + (i + 0.5f) * bayWidth;
                    bool door = i == doorBay;
                    float bottom = door ? 0.03f : 0.65f;
                    if (!door) GB(group, Cream, new Vector3(x, 0.3f, 0f), new Vector3(bayWidth - 0.2f, 0.6f, wall));
                    // Vano abierto, sin planos transparentes: marcos y montantes definen el ventanal.
                    GB(group, Wood, new Vector3(x, bottom, 0f), new Vector3(bayWidth - 0.18f, 0.08f, wall + 0.04f));
                    GB(group, Wood, new Vector3(x, h - 0.43f, 0f), new Vector3(bayWidth - 0.18f, 0.08f, wall + 0.04f));
                    if (!door) GB(group, Metal, new Vector3(x, (bottom + h - 0.43f) * 0.5f, 0f),
                                  new Vector3(0.035f, h - 0.43f - bottom, 0.04f));
                }
            }

            private void BuildStairs()
            {
                const int steps = 18;
                float h = _theme.StoreyHeight;
                float rise = h / steps;
                float run = 5.7f / steps;
                for (int i = 0; i < steps; i++)
                {
                    float top = (i + 1) * rise;
                    float z = -1.4f + (i + 0.5f) * run;
                    B(Wood, new Vector3(4.7f, top * 0.5f, z), new Vector3(1.7f, top, run), true);
                    B(Metal, new Vector3(4.7f, top - 0.008f, z - run * 0.5f + 0.015f), new Vector3(1.7f, 0.016f, 0.03f), true);
                }
                Rail(new Vector3(3.78f, rise, -1.24f), new Vector3(3.78f, h, 4.15f));
                Rail(new Vector3(5.62f, rise, -1.24f), new Vector3(5.62f, h, 4.15f));
                // Protección del hueco desde la planta alta; acceso libre por el rellano posterior.
                Rail(new Vector3(3.65f, h, -1.4f), new Vector3(3.65f, h, 4.3f));
                Rail(new Vector3(3.65f, h, -1.4f), new Vector3(5.7f, h, -1.4f));
            }

            private void BuildRooms()
            {
                float h = _theme.StoreyHeight;
                Seat(new Vector3(-3.1f, 0f, 0.4f), 2.6f, 0f);
                B(Fabric, new Vector3(-3.1f, 0.008f, -1.6f), new Vector3(3.6f, 0.016f, 2.5f), true);
                Table(new Vector3(-3.1f, 0f, -1.35f), 1.4f, 0.7f, 0.45f);
                Lamp(new Vector3(-4.8f, 0f, 0.2f));
                Plant(new Vector3(-5.3f, 0f, -3.6f), 1.15f);
                Seat(new Vector3(1.7f, 0f, -1f), 1f, 180f);
                Table(new Vector3(0.55f, 0f, -1.0f), 0.55f, 0.55f, 0.53f);
                Lamp(new Vector3(0.5f, 0f, -0.1f));
                Bookshelf(new Vector3(1.7f, 0f, 4.55f), 3.2f, 0f);
                ShelfRows(new Vector3(1.7f, 0f, 4.55f), 3.2f, 0f);
                Bookshelf(new Vector3(5.55f, 0f, -3.25f), 2.6f, 90f);
                B(Cream, new Vector3(2.1f, 0.006f, 1.1f), new Vector3(2.7f, 0.012f, 3f), true);
                // Cama orientada al balcón, con cabecera, colchón, manta y dos almohadas.
                Vector3 bed = new Vector3(-2.8f, h, 1.25f);
                B(Wood, bed + new Vector3(0f, 0.3f, 0f), new Vector3(2.1f, 0.5f, 2.5f));
                B(Cream, bed + new Vector3(0f, 0.63f, 0f), new Vector3(1.96f, 0.2f, 2.35f));
                B(Wood, bed + new Vector3(0f, 0.72f, 1.28f), new Vector3(2.2f, 1.3f, 0.12f));
                B(Fabric, bed + new Vector3(0f, 0.75f, -0.42f), new Vector3(1.98f, 0.05f, 1.45f));
                for (int i = 0; i < 2; i++)
                    B(Cream, bed + new Vector3(-0.48f + i * 0.96f, 0.79f, 0.81f), new Vector3(0.8f, 0.12f, 0.45f));
                Table(new Vector3(-4.35f, h, 1.8f), 0.65f, 0.6f, 0.57f);
                Lamp(new Vector3(-4.6f, h, 2.7f));
                B(Fabric, new Vector3(-2.8f, h + 0.008f, -1.5f), new Vector3(3.4f, 0.016f, 1.5f), true);
                Plant(new Vector3(-5.3f, h, -3.8f), 1.2f);
                Seat(new Vector3(-2.8f, h, -6f), 0.9f, 0f);
                Table(new Vector3(-1.5f, h, -6.3f), 0.5f, 0.5f, 0.5f);
                Plant(new Vector3(-5.4f, h, -6.5f), 0.8f);
                // Tumbona: asiento alargado y respaldo inclinado hacia el jardín.
                Matrix4x4 lounge = Group(new Vector3(-3.4f, 0.12f, -7f), 0f);
                GB(lounge, Wood, new Vector3(0f, 0.30f, -0.45f), new Vector3(0.92f, 0.12f, 1.9f));
                GB(lounge, Fabric, new Vector3(0f, 0.40f, -0.45f), new Vector3(0.83f, 0.12f, 1.82f));
                Add(_box, Fabric, lounge * Matrix4x4.TRS(new Vector3(0f, 0.73f, 0.45f), Quaternion.Euler(22f, 0f, 0f), new Vector3(0.83f, 0.7f, 0.13f)));
                for (int i = 0; i < 2; i++)
                    GB(lounge, Wood, new Vector3(0f, 0.16f, -1.1f + i * 1.5f), new Vector3(0.8f, 0.32f, 0.12f));
                Table(new Vector3(-2.15f, 0.12f, -7.2f), 0.55f, 0.55f, 0.48f);
                Plant(new Vector3(-5.35f, 0.12f, -8.1f), 0.9f);
                int extras = Mathf.RoundToInt(_theme.FurnitureDensity * 4f);
                for (int i = 0; i < extras; i++)
                    Plant(new Vector3(-5.15f + i * 0.9f, 0f, 4.1f), 0.65f + 0.15f * Next());
            }

            private void Bookshelf(Vector3 origin, float width, float yaw)
            {
                Matrix4x4 group = Group(origin, yaw);
                const float shelfSpacing = 0.38f;
                GB(group, Wood, new Vector3(0f, 1.18f, 0.22f), new Vector3(width, 2.36f, 0.07f));
                for (int side = 0; side < 2; side++)
                    GB(group, Wood, new Vector3((side - 0.5f) * (width - 0.08f), 1.18f, 0f), new Vector3(0.08f, 2.36f, 0.5f));
                for (int row = 0; row <= 6; row++)
                    GB(group, Wood, new Vector3(0f, 0.08f + row * shelfSpacing, 0f), new Vector3(width, 0.055f, 0.5f));
                // Siempre llena; la densidad varía el grosor, no elimina las estanterías esenciales.
                for (int row = 0; row < 6; row++)
                {
                    float x = -width * 0.5f + 0.10f;
                    while (x < width * 0.5f - 0.13f)
                    {
                        float bw = (0.065f + Next() * 0.035f) / _theme.FurnitureDensity;
                        if (x + bw > width * 0.5f - 0.08f) break;
                        float height = 0.23f + Next() * 0.095f;
                        float depth = 0.25f + Next() * 0.08f;
                        Add(_bookBoxes[_random.Next(_bookBoxes.Length)], Books,
                            group * Matrix4x4.TRS(new Vector3(x + bw * 0.5f, 0.108f + row * shelfSpacing + height * 0.5f, -0.03f),
                                                 Quaternion.identity, new Vector3(bw, height, depth)));
                        x += bw + 0.006f;
                    }
                }
            }

            /// <summary>
            /// Filas de la biblioteca (a la altura de los ojos primero) para los libros del usuario. Van 2 cm por
            /// delante de los libros pintados, que en la versión horneada terminan en z local −0,20.
            /// </summary>
            private void ShelfRows(Vector3 origin, float width, float yaw)
            {
                Matrix4x4 m = _planScale * Group(origin, yaw);
                var rows = new List<ShelfRow>();
                foreach (int row in new[] { 3, 4, 2 })
                {
                    float y = 0.108f + row * 0.38f;
                    rows.Add(new ShelfRow(m.MultiplyPoint3x4(new Vector3(-width * 0.5f + 0.10f, y, -0.205f)),
                                          m.MultiplyPoint3x4(new Vector3(width * 0.5f - 0.10f, y, -0.205f)),
                                          m.MultiplyVector(Vector3.back).normalized));
                }
                _result.ShelfRows = rows.AsReadOnly();
            }

            private void Seat(Vector3 position, float width, float yaw)
            {
                Matrix4x4 group = Group(position, yaw);
                GB(group, Wood, new Vector3(0f, 0.25f, 0f), new Vector3(width, 0.24f, 0.9f));
                GB(group, Fabric, new Vector3(0f, 0.45f, -0.03f), new Vector3(width - 0.13f, 0.18f, 0.79f));
                GB(group, Fabric, new Vector3(0f, 0.72f, 0.39f), new Vector3(width - 0.1f, 0.6f, 0.17f));
                for (int side = 0; side < 2; side++)
                {
                    GB(group, Fabric, new Vector3((side - 0.5f) * width, 0.55f, 0.02f), new Vector3(0.14f, 0.36f, 0.9f));
                    for (int end = 0; end < 2; end++)
                        GB(group, Wood, new Vector3((side - 0.5f) * (width - 0.2f), 0.1f, (end - 0.5f) * 0.65f), new Vector3(0.1f, 0.2f, 0.1f));
                }
            }

            private void Table(Vector3 position, float width, float depth, float height)
            {
                B(Wood, position + Vector3.up * height, new Vector3(width, 0.07f, depth));
                for (int x = 0; x < 2; x++)
                    for (int z = 0; z < 2; z++)
                        B(Metal, position + new Vector3((x - 0.5f) * (width - 0.14f), height * 0.5f, (z - 0.5f) * (depth - 0.14f)),
                          new Vector3(0.045f, height, 0.045f));
            }

            private void Lamp(Vector3 position)
            {
                C(Metal, position + Vector3.up * 0.035f, new Vector3(0.42f, 0.07f, 0.42f));
                C(Metal, position + Vector3.up * 0.85f, new Vector3(0.035f, 1.65f, 0.035f));
                C(Cream, position + Vector3.up * 1.68f, new Vector3(0.5f, 0.35f, 0.5f));
                // Pantalla decorativa opaca: no crea PointLight, emisión ni sombras.
            }

            private void Plant(Vector3 position, float height)
            {
                C(Cream, position + Vector3.up * height * 0.17f, new Vector3(height * 0.42f, height * 0.34f, height * 0.42f));
                C(Wood, position + Vector3.up * height * 0.50f, new Vector3(0.035f, height * 0.65f, 0.035f));
                for (int i = 0; i < 5; i++)
                {
                    float angle = i * Mathf.PI * 0.4f;
                    Vector3 offset = new Vector3(Mathf.Cos(angle) * 0.16f, 0.55f + (i % 3) * 0.12f, Mathf.Sin(angle) * 0.16f) * height;
                    Add(_octahedron, Green, Matrix4x4.TRS(position + offset, Quaternion.Euler(0f, i * 72f, 22f),
                                                        new Vector3(height * 0.3f, height * 0.29f, height * 0.12f)));
                }
            }

            private void Rail(Vector3 start, Vector3 end)
            {
                Vector3 lift = Vector3.up * 1.02f;
                Beam(Metal, start + lift, end + lift, 0.045f);
                int segments = Mathf.CeilToInt(Vector3.Distance(start, end) / 0.8f);
                for (int i = 0; i <= segments; i++)
                {
                    Vector3 foot = Vector3.Lerp(start, end, i / (float)segments);
                    B(Metal, foot + lift * 0.5f, new Vector3(0.035f, lift.y, 0.035f));
                }
            }

            private void Beam(int material, Vector3 start, Vector3 end, float width)
            {
                Vector3 delta = end - start;
                Add(_box, material, Matrix4x4.TRS((start + end) * 0.5f, Quaternion.FromToRotation(Vector3.up, delta.normalized),
                                                 new Vector3(width, delta.magnitude, width)));
            }

            private void SetReadingPoints()
            {
                float h = _theme.StoreyHeight;
                _result.ReadingPoints = Array.AsReadOnly(new[]
                {
                    Point("Sofá de la sala", new Vector3(-3.1f, 1.2f, 0.4f), Vector3.back, new Vector3(-1.35f, 0f, -0.3f)),
                    Point("Sillón de la biblioteca", new Vector3(1.7f, 1.2f, -1f), Vector3.forward, new Vector3(1.7f, 0f, 0.15f)),
                    Point("Cama del dormitorio", new Vector3(-2.8f, h + 1.2f, 2.1f), Vector3.back, new Vector3(-1.25f, h, 0.5f)),
                    Point("Silla del balcón", new Vector3(-2.8f, h + 1.2f, -6f), Vector3.back, new Vector3(-3.8f, h, -6f)),
                    Point("Tumbona de la terraza", new Vector3(-3.4f, 1.32f, -7f), Vector3.back, new Vector3(-4.45f, 0.12f, -7f))
                });
            }

            private ReadingPoint Point(string name, Vector3 position, Vector3 direction, Vector3 floor)
            {
                // Llegada libre junto al asiento: una cápsula no cabe dentro del mueble.
                return new ReadingPoint(name, _planScale.MultiplyPoint3x4(position),
                    _planScale.MultiplyVector(direction), _planScale.MultiplyPoint3x4(floor));
            }

            private void Combine()
            {
                for (int i = 0; i < _batches.Length; i++)
                {
                    if (_batches[i].Count == 0) continue;
                    int vertices = 0;
                    foreach (CombineInstance instance in _batches[i]) vertices += instance.mesh.vertexCount;
                    Mesh combined = _result.Own(new Mesh
                    {
                        name = "FolioXR lote " + _materials[i].name,
                        indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
                    });
                    combined.CombineMeshes(_batches[i].ToArray(), true, true, false);
                    combined.RecalculateBounds();
                    _result.TriangleCount += (int)(combined.GetIndexCount(0) / 3);
                    combined.UploadMeshData(true);
                    var part = new GameObject(_materials[i].name);
                    part.transform.SetParent(_result.Root.transform, false);
                    part.AddComponent<MeshFilter>().sharedMesh = combined;
                    var renderer = part.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = _materials[i];
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    _result.MeshCount++;
                }
                _result.MaterialCount = _materials.Length;
                // Reservar un material/malla para el rótulo del controlador. El coste de pases se mide en Quest.
                if (_result.TriangleCount > 149000 || _result.MeshCount > 8 || _result.MaterialCount > 8)
                    throw new InvalidOperationException("El entorno excede su presupuesto de geometría/materiales.");
            }

            private void CreateMaterials()
            {
                FolioBook book = UnityEngine.Object.FindFirstObjectByType<FolioBook>();
                Renderer coverRenderer = book != null && book.cover != null ? book.cover.GetComponent<Renderer>() : null;
                Renderer pageRenderer = book != null ? (book.leftPage != null ? book.leftPage : book.rightPage) : null;
                Material cover = coverRenderer != null ? coverRenderer.sharedMaterial : null;
                Material page = pageRenderer != null ? pageRenderer.sharedMaterial : null;
                if (cover == null || page == null || cover.shader == null || page.shader == null ||
                    cover.shader.name != "Universal Render Pipeline/Lit" || page.shader.name != "Universal Render Pipeline/Unlit")
                    throw new InvalidOperationException("El entorno necesita la tapa URP Lit y las páginas URP Unlit del FolioBook de la escena.");

                _unlit = page;
                if (_baked) return; // Lo que se ve viene horneado; no hacen falta materiales ni texturas.
                _materials[Wood] = Clone(cover, "Madera clara", _theme.Wood);
                _materials[Cream] = Clone(cover, "Crema", _theme.Wall);
                _materials[Fabric] = Clone(cover, "Tapizado", _theme.Fabric);
                _materials[Metal] = Clone(cover, "Metal mate", _theme.Metal);
                _materials[Green] = Clone(cover, "Vegetación", _theme.Foliage);
                _materials[Books] = Clone(cover, "Lomos", Color.white);
                _materials[Sky] = Clone(page, "Cielo", Color.white);
                _materials[Terrain] = Clone(cover, "Terreno y follaje", Color.white);
                _materials[Terrain].mainTexture = TerrainTexture();
                _materials[Wood].mainTexture = WoodTexture();
                _materials[Books].mainTexture = BookTexture();
                _materials[Sky].mainTexture = SkyTexture();
            }

            private Material Clone(Material source, string name, Color color)
            {
                Material material = _result.Own(new Material(source) { name = "FolioXR " + name });
                material.mainTexture = null; // La página puede contener ya el PNG del PDF.
                material.mainTextureScale = Vector2.one;
                material.mainTextureOffset = Vector2.zero;
                color.a = 1f;
                material.color = color;
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                return material;
            }

            private Texture2D WoodTexture()
            {
                var pixels = new Color[128 * 128];
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        float wave = Mathf.Sin((x + Mathf.Sin(y * 0.09f) * 2f) * 0.65f);
                        float shade = 0.86f + wave * 0.07f + Next() * 0.05f;
                        if (x % 32 == 0) shade *= 0.82f;
                        pixels[y * 128 + x] = new Color(shade, shade, shade, 1f);
                    }
                return Texture("Vetas 128", 128, 128, pixels, true);
            }

            private Texture2D BookTexture()
            {
                var pixels = new Color[128 * 128];
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        Color color = _theme.BookPalette[(x / 16) % _theme.BookPalette.Length];
                        int bx = x % 16;
                        bool label = (y > 82 && y < 88 || y > 74 && y < 78) && bx > 3 && bx < 12;
                        float shade = bx < 2 || bx > 13 ? 0.65f : 1f;
                        pixels[y * 128 + x] = label ? Color.Lerp(color, _theme.Wall, 0.8f) : color * shade;
                        pixels[y * 128 + x].a = 1f;
                    }
                return Texture("Lomos 128", 128, 128, pixels, false);
            }

            private Texture2D Texture(string name, int width, int height, Color[] pixels, bool repeat, bool mips = true)
            {
                Texture2D texture = _result.Own(new Texture2D(width, height, TextureFormat.RGBA32, mips)
                {
                    name = "FolioXR " + name,
                    wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 2
                });
                texture.SetPixels(pixels);
                texture.Apply(mips, true);
                return texture;
            }

            private float Next() { return (float)_random.NextDouble(); }
            private static Matrix4x4 Group(Vector3 position, float yaw)
            {
                return Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            }
            private void B(int material, Vector3 position, Vector3 size, bool floor = false)
            {
                Add(_box, material, Matrix4x4.TRS(position, Quaternion.identity, size), floor);
            }
            private void C(int material, Vector3 position, Vector3 size)
            {
                Add(_cylinder, material, Matrix4x4.TRS(position, Quaternion.identity, size));
            }
            private void GB(Matrix4x4 group, int material, Vector3 position, Vector3 size)
            {
                Add(_box, material, group * Matrix4x4.TRS(position, Quaternion.identity, size));
            }
            private void Add(Mesh mesh, int material, Matrix4x4 transform, bool floor = false)
            {
                AddWorld(mesh, material, _planScale * transform, floor);
            }
            private void AddWorld(Mesh mesh, int material, Matrix4x4 transform, bool floor = false)
            {
                if (!_baked) _batches[material].Add(new CombineInstance { mesh = mesh, subMeshIndex = 0, transform = transform });
                // Solo sólidos estructurales/muebles; el cielo, follaje y los lomos son decorativos.
                // No se toca ninguna malla ni se añaden renderers a la física.
                if (mesh == _box || mesh == _cylinder) AddCollider(transform, floor);
            }

            private void AddCollider(Matrix4x4 matrix, bool floor)
            {
                var part = new GameObject("Sólido " + (++_colliderCount));
                part.transform.SetParent(_colliders, false);
                Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
                Quaternion rotation = Quaternion.LookRotation(z, y);
                part.transform.localPosition = matrix.GetColumn(3);
                part.transform.localRotation = rotation;
                // Proyectar los tres ejes encierra también vigas inclinadas si el tema
                // escala ancho/profundidad de forma distinta (matriz con cizallamiento).
                Quaternion inverse = Quaternion.Inverse(rotation);
                x = inverse * x; y = inverse * y; z = inverse * z;
                var collider = part.AddComponent<BoxCollider>();
                if (floor) _result.TeleportFloors.Add(collider);
                collider.size = new Vector3(
                    Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                    Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            }
            private Mesh Template(Mesh mesh)
            {
                _templates.Add(mesh);
                return mesh;
            }
            internal void DisposeTemplates()
            {
                foreach (Mesh mesh in _templates)
                    if (mesh != null) UnityEngine.Object.Destroy(mesh);
                _templates.Clear();
            }
        }
    }
}
