// Folio XR: entorno de prueba. No requiere cambios en la escena del Hito 1.
using System;
using System.Collections.Generic;
using System.Globalization;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace FolioXR.Environments
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class FolioEnvironmentController : MonoBehaviour
    {
        private const float AxisPress = 0.7f;
        private const float AxisRelease = 0.3f;
        private const double FpsInterval = 5.0;
        private const float LabelSeconds = 3f;

        private HouseBuildResult _house;
        private EnvironmentTheme _theme;
        private FolioBook _book;
        private OVRCameraRig _rig;
        private OVRPassthroughLayer _passthrough;
        private Camera _view;
        private TextMesh _pointLabel;
        private bool _active;
        private bool _hasSnapshot;
        private bool _clickWasDown = true;
        private bool _axisLatched = true;
        private bool _toggleRequested;
        private int _requestedStep;
        private int _pointIndex;
        private float _labelUntil;
        private double _fpsWindowStart;
        private int _fpsFrames;

        private Vector3 _savedRigPosition;
        private Quaternion _savedRigRotation;
        private CameraClearFlags _savedClearFlags;
        private Color _savedBackground;
        private float _savedFarClip;
        private bool _savedPassthroughEnabled;
        private AmbientMode _savedAmbientMode;
        private Color _savedAmbientSky;
        private Color _savedAmbientEquator;
        private Color _savedAmbientGround;
        private float _savedAmbientIntensity;
        private float _savedReflectionIntensity;
        private bool _hasBoundarySnapshot;
        private bool _savedBoundarySuppressed;
        private readonly List<LightSnapshot> _lights = new List<LightSnapshot>();

        private struct LightSnapshot
        {
            public Light Light;
            public LightShadows Shadows;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateController()
        {
            if (FindFirstObjectByType<FolioEnvironmentController>(FindObjectsInactive.Include) != null)
                return;
            new GameObject("Entornos (Folio XR)").AddComponent<FolioEnvironmentController>();
        }

        private void Update()
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!device.isValid ||
                !device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool click) ||
                !device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
            {
                // Al reconectar hay que soltar el clic y centrar el eje antes de actuar.
                _clickWasDown = true;
                _axisLatched = true;
                return;
            }

            bool toggle = click && !_clickWasDown;
            _clickWasDown = click;
            if (toggle) _toggleRequested = true;

            float x = axis.x;
            if (Mathf.Abs(x) < AxisRelease) _axisLatched = false;
            if (!_axisLatched && Mathf.Abs(x) > AxisPress)
            {
                // También se enclava en passthrough: entrar con el stick inclinado
                // debe mostrar primero el sofá. Y sigue siendo exclusivo de FolioBook.
                _axisLatched = true;
                if (_active && !toggle) _requestedStep = x > 0f ? 1 : -1;
            }
        }

        private void LateUpdate()
        {
            // OVRCameraRig actualiza los anchors en Update/FixedUpdate. Movemos su
            // raíz después, conservando intacta la pose local que entrega OpenXR.
            if (_toggleRequested)
            {
                _toggleRequested = false;
                _requestedStep = 0;
                if (_active) LeaveEnvironment();
                else EnterEnvironment();
                return;
            }

            if (!_active) return;
            if (_rig == null || _rig.centerEyeAnchor == null || _view == null)
            {
                LeaveEnvironment();
                return;
            }

            if (_requestedStep != 0)
            {
                GoToReadingPoint(_pointIndex + _requestedStep);
                _requestedStep = 0;
            }
            UpdatePointLabel();
            LogAverageFps();
        }

        private bool ResolveScene()
        {
            _book = FindFirstObjectByType<FolioBook>();
            _rig = FindFirstObjectByType<OVRCameraRig>();
            _passthrough = FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
            _view = Camera.main;
            if (_book == null || _rig == null || _rig.centerEyeAnchor == null ||
                _passthrough == null || _view == null)
            {
                Debug.LogWarning("[FolioXR] Entorno: faltan libro, OVRCameraRig, centerEyeAnchor, " +
                    "OVRPassthroughLayer o cámara MainCamera; se mantiene el modo actual.");
                return false;
            }
            if (!_view.transform.IsChildOf(_rig.transform))
            {
                Debug.LogWarning("[FolioXR] Entorno: MainCamera debe pertenecer al OVRCameraRig.");
                return false;
            }
            return true;
        }

        private void EnterEnvironment()
        {
            if (!ResolveScene()) return;
            try
            {
                if (_house == null)
                {
                    _theme = EnvironmentThemes.All[0];
                    // Build devuelve la raíz desactivada. Si falla, el passthrough
                    // y la cámara siguen como estaban y se puede volver a intentar.
                    _house = HouseGenerator.Build(_theme, transform);
                }
                EnsurePointLabel();
                CaptureScene();
                _passthrough.enabled = false;
                // Meta pide no suprimir el límite del Guardian mientras el passthrough está apagado.
                if (OVRManager.instance != null) OVRManager.instance.shouldBoundaryVisibilityBeSuppressed = false;
                _view.clearFlags = CameraClearFlags.SolidColor;
                Color background = _theme.SkyHorizon;
                background.a = 1f;
                _view.backgroundColor = background;
                _view.farClipPlane = Mathf.Max(_savedFarClip, HouseGenerator.SkyRadius * 2f);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = _theme.AmbientSky;
                RenderSettings.ambientEquatorColor = _theme.AmbientEquator;
                RenderSettings.ambientGroundColor = _theme.AmbientGround;
                RenderSettings.ambientIntensity = 1f;
                // Las reflexiones del skybox de la escena no corresponden a esta casa.
                RenderSettings.reflectionIntensity = 0f;
                // Recalcula la sonda de luz ambiente con los colores nuevos (revisión de Claude).
                DynamicGI.UpdateEnvironment();
                foreach (LightSnapshot item in _lights)
                    if (item.Light != null) item.Light.shadows = LightShadows.None;

                _house.Root.SetActive(true);
                _active = true;
                GoToReadingPoint(0);
                ResetFpsWindow();
            }
            catch (Exception exception)
            {
                LeaveEnvironment();
                Debug.LogError("[FolioXR] No se pudo activar el entorno: " + exception.Message);
            }
        }

        private void CaptureScene()
        {
            _savedRigPosition = _rig.transform.position;
            _savedRigRotation = _rig.transform.rotation;
            _savedClearFlags = _view.clearFlags;
            _savedBackground = _view.backgroundColor;
            _savedFarClip = _view.farClipPlane;
            _savedPassthroughEnabled = _passthrough.enabled;
            _savedAmbientMode = RenderSettings.ambientMode;
            _savedAmbientSky = RenderSettings.ambientSkyColor;
            _savedAmbientEquator = RenderSettings.ambientEquatorColor;
            _savedAmbientGround = RenderSettings.ambientGroundColor;
            _savedAmbientIntensity = RenderSettings.ambientIntensity;
            _savedReflectionIntensity = RenderSettings.reflectionIntensity;
            OVRManager manager = OVRManager.instance;
            _hasBoundarySnapshot = manager != null;
            if (_hasBoundarySnapshot) _savedBoundarySuppressed = manager.shouldBoundaryVisibilityBeSuppressed;
            _lights.Clear();
            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                _lights.Add(new LightSnapshot { Light = light, Shadows = light.shadows });
            _hasSnapshot = true;
        }

        private void LeaveEnvironment()
        {
            _active = false;
            _requestedStep = 0;
            if (_house != null && _house.Root != null) _house.Root.SetActive(false);
            if (_pointLabel != null) _pointLabel.gameObject.SetActive(false);
            if (!_hasSnapshot) return;

            if (_rig != null) _rig.transform.SetPositionAndRotation(_savedRigPosition, _savedRigRotation);
            if (_view != null)
            {
                _view.clearFlags = _savedClearFlags;
                _view.backgroundColor = _savedBackground;
                _view.farClipPlane = _savedFarClip;
            }
            if (_passthrough != null) _passthrough.enabled = _savedPassthroughEnabled;
            RenderSettings.ambientMode = _savedAmbientMode;
            RenderSettings.ambientSkyColor = _savedAmbientSky;
            RenderSettings.ambientEquatorColor = _savedAmbientEquator;
            RenderSettings.ambientGroundColor = _savedAmbientGround;
            RenderSettings.ambientIntensity = _savedAmbientIntensity;
            RenderSettings.reflectionIntensity = _savedReflectionIntensity;
            DynamicGI.UpdateEnvironment();
            if (_hasBoundarySnapshot && OVRManager.instance != null)
                OVRManager.instance.shouldBoundaryVisibilityBeSuppressed = _savedBoundarySuppressed;
            _hasBoundarySnapshot = false;
            foreach (LightSnapshot item in _lights)
                if (item.Light != null) item.Light.shadows = item.Shadows;
            _lights.Clear();
            _hasSnapshot = false;
            if (_book != null) _book.PlaceInFrontOfView();
        }

        private void GoToReadingPoint(int index)
        {
            int count = _house.ReadingPoints.Count;
            _pointIndex = (index % count + count) % count;
            ReadingPoint point = _house.ReadingPoints[_pointIndex];
            Transform root = _house.Root.transform;
            Transform eyes = _rig.centerEyeAnchor;
            Vector3 targetPosition = root.TransformPoint(point.EyePosition);
            Vector3 targetForward = Vector3.ProjectOnPlane(root.TransformDirection(point.ViewDirection), Vector3.up);
            Vector3 currentForward = Vector3.ProjectOnPlane(eyes.forward, Vector3.up);
            if (currentForward.sqrMagnitude < 0.0001f)
                currentForward = Vector3.ProjectOnPlane(Vector3.Cross(eyes.right, Vector3.up), Vector3.up);
            if (currentForward.sqrMagnitude < 0.0001f)
                currentForward = Vector3.ProjectOnPlane(_rig.transform.forward, Vector3.up);

            float yaw = Vector3.SignedAngle(currentForward, targetForward, Vector3.up);
            _rig.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * _rig.transform.rotation;
            // Compensar DESPUÉS del giro incluye la altura física y el desplazamiento
            // del usuario respecto al origen del área de juego, también estando sentado.
            _rig.transform.position += targetPosition - eyes.position;
            if (_book != null) _book.PlaceInFrontOfView();

            _pointLabel.text = (_pointIndex + 1) + "/" + count + " · " + point.Name;
            _labelUntil = Time.unscaledTime + LabelSeconds;
            _pointLabel.gameObject.SetActive(true);
            UpdatePointLabel();
        }

        private void EnsurePointLabel()
        {
            if (_pointLabel != null) return;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) throw new InvalidOperationException("No se encontró LegacyRuntime.ttf.");
            var labelObject = new GameObject("Punto de lectura (Folio XR)");
            labelObject.transform.SetParent(transform, false);
            _pointLabel = labelObject.AddComponent<TextMesh>();
            _pointLabel.font = font;
            _pointLabel.fontSize = 64;
            _pointLabel.characterSize = 0.004f;
            _pointLabel.anchor = TextAnchor.MiddleCenter;
            _pointLabel.alignment = TextAlignment.Center;
            _pointLabel.color = new Color(0.18f, 0.12f, 0.08f, 1f);
            MeshRenderer renderer = labelObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            labelObject.SetActive(false);
        }

        private void UpdatePointLabel()
        {
            if (_pointLabel == null || !_pointLabel.gameObject.activeSelf) return;
            if (Time.unscaledTime >= _labelUntil)
            {
                _pointLabel.gameObject.SetActive(false);
                return;
            }
            Transform head = _view.transform;
            _pointLabel.transform.SetPositionAndRotation(
                head.position + head.forward * 0.95f + head.up * 0.24f, head.rotation);
        }

        private void ResetFpsWindow()
        {
            _fpsWindowStart = Time.realtimeSinceStartupAsDouble;
            _fpsFrames = 0;
        }

        private void LogAverageFps()
        {
            ++_fpsFrames;
            double elapsed = Time.realtimeSinceStartupAsDouble - _fpsWindowStart;
            if (elapsed < FpsInterval) return;
            Debug.Log("[FolioXR] Entorno: FPS promedio=" +
                (_fpsFrames / elapsed).ToString("F1", CultureInfo.InvariantCulture) + " (" +
                elapsed.ToString("F1", CultureInfo.InvariantCulture) + " s).");
            ResetFpsWindow();
        }

        private void OnApplicationPause(bool paused)
        {
            // No contar el tiempo que el visor pasó suspendido como cuadros perdidos.
            ResetInputAndFps();
        }

        private void OnApplicationFocus(bool focused)
        {
            ResetInputAndFps();
        }

        private void ResetInputAndFps()
        {
            ResetFpsWindow();
            _clickWasDown = true;
            _axisLatched = true;
            _toggleRequested = false;
            _requestedStep = 0;
        }

        private void OnDisable()
        {
            _toggleRequested = false;
            LeaveEnvironment();
        }

        private void OnDestroy()
        {
            LeaveEnvironment();
            if (_house != null) _house.Dispose();
            _house = null;
            if (_pointLabel != null) Destroy(_pointLabel.gameObject);
        }
    }
}
