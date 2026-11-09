using System;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Locomotion
{
    /// <summary>Recursos propios creados por el bootstrap AfterSceneLoad de FolioLocomotion.</summary>
    /// <remarks>
    /// Revisión de Claude: la viñeta usaba el posprocesado de URP (Volume + Vignette) y lo
    /// encendía en la cámara mientras se caminaba. En Quest eso agrega una pasada de pantalla
    /// completa justo al moverse, puede reasignar texturas (tirones) y sus variantes pueden
    /// faltar en el APK. Ahora es una malla pegada a la vista con un degradado radial
    /// generado por código, sobre un clon transparente del material de las páginas.
    /// </remarks>
    internal sealed class LocomotionVisuals : IDisposable
    {
        private const int TunnelResolution = 128;
        private const float TunnelMaxAlpha = 0.85f;
        private readonly GameObject _root, _tunnel;
        private readonly Material _material, _tunnelMaterial;
        private readonly Texture2D _tunnelTexture;
        private readonly Mesh _tunnelMesh;
        private readonly MeshRenderer _tunnelRenderer;
        private readonly LineRenderer _arc, _marker;
        private float _intensity;

        internal static LocomotionVisuals TryCreate(Transform parent, FolioBook book, Camera camera)
        {
            Renderer page = book.leftPage != null ? book.leftPage : book.rightPage;
            Material source = page != null ? page.sharedMaterial : null;
            if (source == null || source.shader == null || source.shader.name != "Universal Render Pipeline/Unlit")
                return null;
            return new LocomotionVisuals(parent, source, camera);
        }

        private LocomotionVisuals(Transform parent, Material source, Camera camera)
        {
            _root = new GameObject("Arco y comodidad (Folio XR)");
            _root.transform.SetParent(parent, false);
            _material = new Material(source) { name = "FolioXR arco (página clonada)" };
            _material.mainTexture = null;
            _material.mainTextureScale = Vector2.one;
            _material.mainTextureOffset = Vector2.zero;
            _material.SetFloat("_Cull", (float)CullMode.Off);
            _arc = Line("Arco", 0.012f);
            _marker = Line("Destino válido", 0.018f);
            _marker.loop = true;
            _marker.useWorldSpace = false;
            _marker.positionCount = 48;
            for (int i = 0; i < 48; i++)
            {
                float angle = i * 2f * Mathf.PI / 48;
                _marker.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.22f);
            }
            HideArc();

            // Viñeta: cuadrado pegado a la cámara, transparente en el centro y oscuro en los bordes.
            _tunnelTexture = new Texture2D(TunnelResolution, TunnelResolution, TextureFormat.RGBA32, false)
            {
                name = "FolioXR viñeta (degradado)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[TunnelResolution * TunnelResolution];
            for (int y = 0; y < TunnelResolution; y++)
                for (int x = 0; x < TunnelResolution; x++)
                {
                    float dx = (x + 0.5f) / TunnelResolution * 2f - 1f;
                    float dy = (y + 0.5f) / TunnelResolution * 2f - 1f;
                    float a = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.42f) / 0.38f);
                    pixels[y * TunnelResolution + x] = new Color(0f, 0f, 0f, a * a * (3f - 2f * a));
                }
            _tunnelTexture.SetPixels(pixels);
            _tunnelTexture.Apply(false, true);
            _tunnelMaterial = TransparentClone(source, _tunnelTexture);

            float distance = Mathf.Max(0.15f, camera.nearClipPlane * 1.5f);
            float half = distance * 1.8f; // Cubre más que el campo de visión del Quest 3.
            _tunnelMesh = new Mesh { name = "FolioXR viñeta" };
            _tunnelMesh.vertices = new[]
            {
                new Vector3(-half, -half, 0f), new Vector3(half, -half, 0f),
                new Vector3(half, half, 0f), new Vector3(-half, half, 0f)
            };
            _tunnelMesh.uv = new[] { Vector2.zero, new Vector2(1f, 0f), Vector2.one, new Vector2(0f, 1f) };
            _tunnelMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _tunnelMesh.RecalculateBounds();
            _tunnel = new GameObject("Viñeta de comodidad");
            _tunnel.transform.SetParent(camera.transform, false);
            _tunnel.transform.localPosition = new Vector3(0f, 0f, distance);
            _tunnel.transform.localRotation = Quaternion.identity;
            _tunnel.AddComponent<MeshFilter>().sharedMesh = _tunnelMesh;
            _tunnelRenderer = _tunnel.AddComponent<MeshRenderer>();
            _tunnelRenderer.sharedMaterial = _tunnelMaterial;
            Configure(_tunnelRenderer);
            _tunnelRenderer.enabled = false;
        }

        private static Material TransparentClone(Material source, Texture texture)
        {
            var material = new Material(source) { name = "FolioXR viñeta (página clonada)" };
            material.mainTexture = texture;
            material.mainTextureScale = Vector2.one;
            material.mainTextureOffset = Vector2.zero;
            material.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
            // Misma configuración transparente que el menú (URP 17 usa _Surface en OutputAlpha).
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Overlay; // Después del libro, la casa y el menú.
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("DepthNormalsOnly", false);
            material.SetShaderPassEnabled("MotionVectors", false);
            material.SetShaderPassEnabled("XRMotionVectors", false);
            return material;
        }

        private static void Configure(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private LineRenderer Line(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            Configure(line);
            return line;
        }

        internal void ShowArc(Vector3[] points, int count, bool valid, Vector3 destination)
        {
            _material.color = valid ? new Color(0.12f, 0.9f, 0.65f, 1f) : new Color(1f, 0.32f, 0.12f, 1f);
            _arc.enabled = true;
            _arc.positionCount = count;
            for (int i = 0; i < count; i++) _arc.SetPosition(i, points[i]);
            _marker.enabled = valid;
            if (valid) _marker.transform.position = destination;
        }

        internal void HideArc()
        {
            if (_arc != null) _arc.enabled = false;
            if (_marker != null) _marker.enabled = false;
        }

        internal void SetComfort(float movement, float dt)
        {
            if (_tunnelMaterial == null) return;
            _intensity = Mathf.MoveTowards(_intensity, Mathf.Clamp01(movement) * TunnelMaxAlpha, dt * 3f);
            if (_intensity <= 0.001f) { StopComfort(); return; }
            _tunnelRenderer.enabled = true;
            _tunnelMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, _intensity));
        }

        internal void StopComfort()
        {
            _intensity = 0f;
            if (_tunnelRenderer != null) _tunnelRenderer.enabled = false;
            if (_tunnelMaterial != null) _tunnelMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
        }

        public void Dispose()
        {
            StopComfort();
            UnityEngine.Object.Destroy(_root);
            UnityEngine.Object.Destroy(_tunnel);
            UnityEngine.Object.Destroy(_material);
            UnityEngine.Object.Destroy(_tunnelMaterial);
            UnityEngine.Object.Destroy(_tunnelTexture);
            UnityEngine.Object.Destroy(_tunnelMesh);
        }
    }
}
