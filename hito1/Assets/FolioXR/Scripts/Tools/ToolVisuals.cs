using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Tools
{
    /// <summary>Recursos generados una vez; solo se actualizan poses/visibilidad durante el uso.</summary>
    internal sealed class ToolVisuals : IDisposable
    {
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        internal readonly Material Dark, White, Cyan, Red, Violet, Cone;
        private readonly Mesh _box;

        internal ToolVisuals(Material source)
        {
            Dark = Clone(source, "cuerpo", new Color(0.055f, 0.07f, 0.09f));
            White = Clone(source, "íconos", Color.white);
            Cyan = Clone(source, "encendido", new Color(0.1f, 0.9f, 0.8f));
            Red = Clone(source, "láser", Color.red);
            Violet = Clone(source, "selector", new Color(0.65f, 0.25f, 1f));
            Cone = Clone(source, "cono suave", Color.white);
            Cone.SetFloat("_Surface", 1f);
            Cone.SetFloat("_Blend", 0f);
            Cone.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            Cone.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            Cone.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            Cone.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            Cone.SetFloat("_ZWrite", 0f);
            Cone.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            Cone.DisableKeyword("_ALPHATEST_ON");
            Cone.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            Cone.DisableKeyword("_ALPHAMODULATE_ON");
            Cone.SetOverrideTag("RenderType", "Transparent");
            Cone.renderQueue = (int)RenderQueue.Transparent;
            Cone.SetShaderPassEnabled("DepthOnly", false);
            Cone.SetShaderPassEnabled("DepthNormalsOnly", false);
            var texture = Own(new Texture2D(32, 32, TextureFormat.RGBA32, false));
            texture.name = "FolioXR degradado del cono";
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float v = y / 31f;
                    pixels[y * 32 + x] = new Color(1f, 1f, 1f, Mathf.Sin(v * Mathf.PI) * 0.055f);
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            Cone.mainTexture = texture;
            // Cubo reutilizable, sin primitivas ni colliders temporales.
            _box = Own(new Mesh { name = "FolioXR caja de herramienta" });
            _box.vertices = new[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                                    new Vector3(0.5f, 0.5f, -0.5f),   new Vector3(-0.5f, 0.5f, -0.5f),
                                    new Vector3(-0.5f, -0.5f, 0.5f),  new Vector3(0.5f, -0.5f, 0.5f),
                                    new Vector3(0.5f, 0.5f, 0.5f),    new Vector3(-0.5f, 0.5f, 0.5f) };
            _box.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
                                     3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
            _box.RecalculateBounds();
        }

        private T Own<T>(T item)
            where T : UnityEngine.Object
        {
            _owned.Add(item);
            return item;
        }
        private Material Clone(Material source, string name, Color color)
        {
            var m = Own(new Material(source) { name = "FolioXR " + name });
            m.mainTexture = null;
            m.mainTextureScale = Vector2.one;
            m.mainTextureOffset = Vector2.zero;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_AlphaClip", 0f);
            m.SetShaderPassEnabled("ShadowCaster", false);
            return m;
        }

        internal static void Configure(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        internal Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            Transform t = MeshObject(parent, name, _box, material).transform;
            t.localPosition = position;
            t.localScale = size;
            return t;
        }

        private static MeshRenderer MeshObject(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            Configure(renderer);
            return renderer;
        }

        internal LineRenderer Line(Transform parent, string name, Material material, float width, int count)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.positionCount = count;
            Configure(line);
            return line;
        }

        internal Transform Icon(Transform parent, ToolKind kind)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            // Trazos en metros: un único mesh/renderer por ícono.
            void Stroke(float ax, float ay, float bx, float by)
            {
                Vector3 a = new Vector3(ax, ay, 0f) * 0.009f, b = new Vector3(bx, by, 0f) * 0.009f;
                Vector3 n = Vector3.Cross(b - a, Vector3.forward).normalized * 0.0007f;
                int k = vertices.Count;
                vertices.Add(a - n);
                vertices.Add(a + n);
                vertices.Add(b + n);
                vertices.Add(b - n);
                triangles.Add(k);
                triangles.Add(k + 1);
                triangles.Add(k + 2);
                triangles.Add(k);
                triangles.Add(k + 2);
                triangles.Add(k + 3);
            }
            void Rect(float x, float y, float w, float h)
            {
                Stroke(x, y, x + w, y);
                Stroke(x + w, y, x + w, y + h);
                Stroke(x + w, y + h, x, y + h);
                Stroke(x, y + h, x, y);
            }
            void Circle(float x, float y, float radius)
            {
                for (int i = 0; i < 20; i++)
                {
                    float a = i * Mathf.PI / 10f, b = (i + 1) * Mathf.PI / 10f;
                    Stroke(x + Mathf.Cos(a) * radius, y + Mathf.Sin(a) * radius, x + Mathf.Cos(b) * radius,
                           y + Mathf.Sin(b) * radius);
                }
            }
            switch (kind)
            {
            case ToolKind.Magnifier:
                Circle(-0.2f, 0.2f, 0.6f);
                Stroke(0.25f, -0.25f, 0.9f, -0.9f);
                break;
            case ToolKind.Notebook:
                Rect(-0.7f, -0.9f, 1.4f, 1.8f);
                Stroke(-0.4f, -0.9f, -0.4f, 0.9f);
                Stroke(-0.2f, 0.3f, 0.5f, 0.3f);
                Stroke(-0.2f, -0.1f, 0.5f, -0.1f);
                break;
            case ToolKind.Pencils:
                for (int i = 0; i < 3; i++)
                {
                    float x = -0.8f + i * 0.6f;
                    Rect(x, -0.6f, 0.3f, 1.3f);
                    Stroke(x, -0.6f, x + 0.15f, -0.95f);
                    Stroke(x + 0.15f, -0.95f, x + 0.3f, -0.6f);
                }
                break;
            case ToolKind.InvisibleInk:
                Rect(-0.65f, -0.9f, 1.3f, 1f);
                Rect(-0.35f, 0.1f, 0.7f, 0.3f);
                Stroke(0, 0.55f, 0.25f, 0.85f);
                Stroke(0.25f, 0.85f, 0, 1.1f);
                Stroke(0, 1.1f, -0.25f, 0.85f);
                Stroke(-0.25f, 0.85f, 0, 0.55f);
                break;
            case ToolKind.Flashlight:
                Rect(-0.3f, -0.9f, 0.6f, 1f);
                Stroke(-0.3f, 0.1f, -0.65f, 0.55f);
                Stroke(-0.65f, 0.55f, 0.65f, 0.55f);
                Stroke(0.65f, 0.55f, 0.3f, 0.1f);
                for (int i = -1; i <= 1; i++)
                    Stroke(i * 0.4f, 0.75f, i * 0.55f, 1.1f);
                break;
            case ToolKind.Ruler:
                Rect(-1f, -0.4f, 2f, 0.8f);
                for (int i = 0; i < 7; i++)
                    Stroke(-0.75f + i * 0.25f, 0.4f, -0.75f + i * 0.25f, i % 2 == 0 ? 0f : 0.15f);
                break;
            }
            Mesh mesh = Own(new Mesh { name = "FolioXR ícono " + kind });
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            MeshObject(go.transform, "Símbolo", mesh, White);
            return go.transform;
        }

        internal Transform SoftCone(Transform parent)
        {
            const int segments = 32;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            float radius = Mathf.Tan(17.5f * Mathf.Deg2Rad);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * 2f * Mathf.PI / segments;
                vertices[i * 2] = Vector3.zero;
                vertices[i * 2 + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 1f);
                uv[i * 2] = new Vector2(i / (float)segments, 0f);
                uv[i * 2 + 1] = new Vector2(i / (float)segments, 1f);
                if (i == segments)
                    continue;
                int k = i * 6, j = i * 2;
                triangles[k] = j;
                triangles[k + 1] = j + 1;
                triangles[k + 2] = j + 3;
                triangles[k + 3] = j;
                triangles[k + 4] = j + 3;
                triangles[k + 5] = j + 2;
            }
            Mesh mesh = Own(new Mesh { name = "FolioXR cono suave" });
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return MeshObject(parent, "Cono visible", mesh, Cone).transform;
        }

        public void Dispose()
        {
            foreach (var item in _owned)
                if (item != null)
                    UnityEngine.Object.Destroy(item);
        }
    }
}
