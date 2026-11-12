using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Environments
{
    /// <summary>
    /// Entorno horneado en Blender (herramientas/entornos/generar.py): casa, terreno cercano y cielo
    /// llegan como mallas con la luz ya pintada en una textura. Aquí solo se cargan y se muestran sin
    /// iluminación. Los colisionadores y los puntos de lectura siguen saliendo de HouseGenerator.
    /// Si faltan los archivos de un estilo, HouseGenerator dibuja la versión procedural de siempre.
    /// </summary>
    internal static class BakedEnvironment
    {
        private const string Folder = "Entornos/";

        [Serializable]
        private sealed class Data
        {
            public float exposicion = 0.1f;
            public float[] ojo;
            public int atlas = 4096;
            public float texels_casa = 70f;
            public float texels_cerca = 60f;
        }

        internal static bool Exists(EnvironmentTheme theme)
        {
            if (theme == null || string.IsNullOrEmpty(theme.Baked)) return false;
            string path = Folder + theme.Baked + "/";
            return Resources.Load<TextAsset>(path + "casa_malla") != null &&
                   Resources.Load<Texture2D>(path + "casa_luz") != null &&
                   Resources.Load<TextAsset>(path + "cerca_malla") != null &&
                   Resources.Load<Texture2D>(path + "cerca_luz") != null &&
                   Resources.Load<Texture2D>(path + "cielo") != null;
        }

        /// <summary>Crea los tres renderers (casa, terreno cercano y cielo) bajo la raíz del resultado.</summary>
        internal static void Attach(EnvironmentTheme theme, HouseBuildResult result, Material unlitFallback)
        {
            string path = Folder + theme.Baked + "/";
            var data = new Data();
            TextAsset json = Resources.Load<TextAsset>(path + "datos");
            if (json != null)
            {
                try { JsonUtility.FromJsonOverwrite(json.text, data); }
                catch (Exception e) { Debug.LogWarning("[FolioXR] datos.json del entorno ilegible: " + e.Message); }
            }
            Shader shader = Resources.Load<Shader>("FolioXRBaked");
            if (shader != null && !shader.isSupported) shader = null;
            if (shader == null) Debug.LogWarning("[FolioXR] Falta FolioXR/Baked: el entorno usa el sombreador de las páginas.");
            Texture2D detail = shader != null ? result.Own(DetailTexture()) : null;

            Texture2D houseLight = Resources.Load<Texture2D>(path + "casa_luz");
            Texture2D nearLight = Resources.Load<Texture2D>(path + "cerca_luz");
            Texture2D sky = Resources.Load<Texture2D>(path + "cielo");
            Configure(houseLight, false);
            Configure(nearLight, false);
            Configure(sky, true);
            // El atlas se reduce si Unity lo importó a menos tamaño: se avisa porque se vería borroso.
            if (houseLight.width < data.atlas)
                Debug.LogWarning("[FolioXR] casa_luz se importó a " + houseLight.width + " px (se esperaban " + data.atlas +
                                 "): revisa que FolioXREnvironmentImport esté en Assets/FolioXR/Editor.");

            // El detalle se repite cada ~35 cm: un UV completo cubre atlas/texels metros.
            float houseTiling = data.atlas / Mathf.Max(1f, data.texels_casa) / 0.35f;
            float nearTiling = data.atlas / Mathf.Max(1f, data.texels_cerca) / 0.45f;
            Part(result, "Casa horneada", ReadMesh(Resources.Load<TextAsset>(path + "casa_malla"), "FolioXR casa"),
                 Material(result, shader, unlitFallback, houseLight, detail, houseTiling, 0.22f, "Casa horneada"), Vector3.zero);
            Part(result, "Terreno cercano", ReadMesh(Resources.Load<TextAsset>(path + "cerca_malla"), "FolioXR cerca"),
                 Material(result, shader, unlitFallback, nearLight, detail, nearTiling, 0.34f, "Terreno cercano"), Vector3.zero);
            Vector3 eye = data.ojo != null && data.ojo.Length == 3 ? new Vector3(data.ojo[0], data.ojo[1], data.ojo[2]) : Vector3.up * 1.6f;
            Part(result, "Cielo panorámico", SkySphere(HouseGenerator.SkyRadius),
                 Material(result, shader, unlitFallback, sky, null, 0f, 0f, "Cielo panorámico"), eye);
            result.MaterialCount = 3;
            Debug.Log("[FolioXR] Entorno horneado «" + theme.Baked + "»: atlas " + houseLight.width + " px, cielo " +
                      sky.width + "x" + sky.height + ", sombreador " + (shader != null ? "FolioXR/Baked" : "URP Unlit") + ".");
        }

        private static void Configure(Texture2D texture, bool sky)
        {
            texture.wrapModeU = sky ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            texture.wrapModeV = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = sky ? 2 : 8;
        }

        private static Material Material(HouseBuildResult result, Shader shader, Material fallback, Texture2D light,
                                         Texture2D detail, float tiling, float strength, string name)
        {
            Material material;
            if (shader != null)
            {
                material = new Material(shader);
                material.SetTexture("_MainTex", light);
                if (detail != null)
                {
                    material.SetTexture("_DetailTex", detail);
                    material.SetFloat("_DetailTiling", tiling);
                    material.SetFloat("_DetailStrength", strength);
                }
            }
            else
            {
                // Reserva: el URP Unlit de las páginas, sin descartar caras traseras.
                material = new Material(fallback);
                material.mainTexture = light;
                material.color = Color.white;
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            }
            material.name = "FolioXR " + name;
            return result.Own(material);
        }

        private static void Part(HouseBuildResult result, string name, Mesh mesh, Material material, Vector3 position)
        {
            result.Own(mesh);
            var part = new GameObject(name);
            part.transform.SetParent(result.Root.transform, false);
            part.transform.localPosition = position;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            result.TriangleCount += (int)(mesh.GetIndexCount(0) / 3);
            result.MeshCount++;
            mesh.UploadMeshData(true);
        }

        /// <summary>FXM1: "FXM1", vértices (uint32), índices (uint32), posiciones, UV e índices (uint32).</summary>
        private static Mesh ReadMesh(TextAsset asset, string name)
        {
            byte[] bytes = asset.bytes;
            if (bytes.Length < 12 || bytes[0] != 'F' || bytes[1] != 'X' || bytes[2] != 'M' || bytes[3] != '1')
                throw new InvalidOperationException("Malla de entorno con formato desconocido: " + name);
            int vertexCount = BitConverter.ToInt32(bytes, 4), indexCount = BitConverter.ToInt32(bytes, 8);
            long expected = 12L + vertexCount * 20L + indexCount * 4L;
            if (vertexCount <= 0 || indexCount <= 0 || indexCount % 3 != 0 || bytes.Length < expected)
                throw new InvalidOperationException("Malla de entorno incompleta: " + name);
            var raw = new float[vertexCount * 5];
            Buffer.BlockCopy(bytes, 12, raw, 0, vertexCount * 20);
            var positions = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];
            int uvStart = vertexCount * 3;
            for (int i = 0; i < vertexCount; i++)
            {
                positions[i] = new Vector3(raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);
                uv[i] = new Vector2(raw[uvStart + i * 2], raw[uvStart + i * 2 + 1]);
            }
            var indices = new int[indexCount];
            Buffer.BlockCopy(bytes, 12 + vertexCount * 20, indices, 0, indexCount * 4);
            var mesh = new Mesh { name = name, indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Esfera con UV equirectangular: u = 0,5 + atan2(x, z)/2π, v = 0,5 + asin(y)/π (igual que el panorama).</summary>
        private static Mesh SkySphere(float radius)
        {
            const int segments = 96, rings = 48;
            var positions = new Vector3[(segments + 1) * (rings + 1)];
            var uv = new Vector2[positions.Length];
            var indices = new int[segments * rings * 6];
            for (int r = 0; r <= rings; r++)
            {
                float lat = -Mathf.PI * 0.5f + Mathf.PI * r / rings;
                for (int s = 0; s <= segments; s++)
                {
                    float lon = -Mathf.PI + 2f * Mathf.PI * s / segments;
                    int i = r * (segments + 1) + s;
                    positions[i] = new Vector3(Mathf.Sin(lon) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Cos(lon) * Mathf.Cos(lat)) * radius;
                    uv[i] = new Vector2(s / (float)segments, r / (float)rings);
                }
            }
            int k = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * (segments + 1) + s, b = a + 1, c = a + segments + 1, d = c + 1;
                    indices[k++] = a; indices[k++] = b; indices[k++] = c;
                    indices[k++] = b; indices[k++] = d; indices[k++] = c;
                }
            var mesh = new Mesh { name = "FolioXR cielo" };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Ruido gris repetible (media 0,5) para el detalle de cerca. Lineal, con mipmaps.</summary>
        private static Texture2D DetailTexture()
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float grain = Hash(x, y, 1);
                    float blot = Periodic(x / 16f, y / 16f, size / 16, 2);
                    float soft = Periodic(x / 4f, y / 4f, size / 4, 3);
                    float v = 0.5f + (grain - 0.5f) * 0.45f + (blot - 0.5f) * 0.5f + (soft - 0.5f) * 0.35f;
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                    pixels[y * size + x] = new Color32(b, b, b, 255);
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "FolioXR detalle", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 144665;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Ruido de valor que se repite cada «period» celdas.</summary>
        private static float Periodic(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int x0 = xi % period, x1 = (xi + 1) % period, y0 = yi % period, y1 = (yi + 1) % period;
            float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
            float b = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }
    }
}
