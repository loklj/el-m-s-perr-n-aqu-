using System;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FolioXR.Locomotion
{
    /// <summary>Recursos propios creados por el bootstrap AfterSceneLoad de FolioLocomotion.</summary>
    internal sealed class LocomotionVisuals : IDisposable
    {
        private readonly GameObject _root;
        private readonly Material _material;
        private readonly LineRenderer _arc, _marker;
        private readonly Volume _volume;
        private readonly VolumeProfile _profile;
        private readonly Vignette _vignette;
        private readonly UniversalAdditionalCameraData _cameraData;
        private readonly Camera _camera;
        private VolumeFrameworkUpdateMode _savedVolumeMode;
        private bool _comfortActive, _savedPostProcess;
        private LayerMask _savedVolumeMask;
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
            // Efecto de pantalla de URP 17: estéreo por ojo, sin Shader.Find ni material propio.
            // El arco/marca sí usa exclusivamente un clon del material de las páginas.
            _camera = camera;
            _cameraData = camera.GetUniversalAdditionalCameraData();
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "FolioXR comodidad temporal";
            _vignette = _profile.Add<Vignette>(true);
            _vignette.color.Override(Color.black);
            _vignette.center.Override(new Vector2(0.5f, 0.5f));
            _vignette.intensity.Override(0f);
            _vignette.smoothness.Override(0.65f);
            _vignette.rounded.Override(true);
            _volume = _root.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.sharedProfile = _profile;
            _volume.enabled = false;
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
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
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
            if (_cameraData == null) return;
            _intensity = Mathf.MoveTowards(_intensity, Mathf.Clamp01(movement) * 0.32f, dt * 1.5f);
            if (_intensity <= 0f) { StopComfort(); return; }
            if (!_comfortActive)
            {
                _savedPostProcess = _cameraData.renderPostProcessing;
                _savedVolumeMask = _cameraData.volumeLayerMask;
                _savedVolumeMode = _camera.GetVolumeFrameworkUpdateMode();
                _camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
                // Usar una capa ya incluida; si la máscara está vacía, habilitar Default temporalmente.
                int mask = _savedVolumeMask.value;
                int layer = 0;
                if (mask != 0) while ((mask & (1 << layer)) == 0 && layer < 31) layer++;
                _root.layer = layer;
                _cameraData.volumeLayerMask = mask | (1 << layer);
                _cameraData.renderPostProcessing = true;
                _volume.enabled = true;
                _comfortActive = true;
            }
            _vignette.intensity.value = _intensity;
        }

        internal void StopComfort()
        {
            _intensity = 0f;
            if (_vignette != null) _vignette.intensity.value = 0f;
            if (_volume != null) _volume.enabled = false;
            if (_comfortActive && _cameraData != null)
            {
                _cameraData.renderPostProcessing = _savedPostProcess;
                _cameraData.volumeLayerMask = _savedVolumeMask;
                _camera.SetVolumeFrameworkUpdateMode(_savedVolumeMode);
            }
            _comfortActive = false;
        }

        public void Dispose()
        {
            StopComfort();
            UnityEngine.Object.Destroy(_root);
            UnityEngine.Object.Destroy(_material);
            UnityEngine.Object.Destroy(_vignette);
            UnityEngine.Object.Destroy(_profile);
        }
    }
}
