// Folio XR: entorno de prueba. No requiere cambios en la escena del Hito 1.
using System;
using System.Collections.Generic;
using System.Globalization;
using FolioXR.Reading;
using FolioXR.Locomotion;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace FolioXR.Environments
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class FolioEnvironmentController : MonoBehaviour
    {
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
        private bool _toggleRequested;
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

        private readonly List<string> _pointNames = new List<string>();
        private IReadOnlyList<string> _readOnlyPointNames;
        private GameObject _passthroughFloor;
        private bool _floorOriginSet;
        private float _replaceBookAt = -1f;
        public bool IsActive => _active;
        public float FloorHeight => _active && _house != null && _house.Root != null
            ? _house.Root.transform.position.y : _rig != null && _rig.trackingSpace != null
                ? _rig.trackingSpace.position.y : 0f;
        public bool InputPaused { get; set; }
        /// <summary>Raíz de la casa activa (trazos guardados en sus coordenadas) o null en passthrough.</summary>
        public Transform HouseRoot => _active && _house != null && _house.Root != null ? _house.Root.transform : null;
        public string EnvironmentName => _theme != null ? _theme.Name : null;
        public bool IsTeleportFloor(Collider collider) =>
            _active && _house != null && _house.TeleportFloors.Contains(collider);
        public IReadOnlyList<string> PointNames =>
            _readOnlyPointNames ?? (_readOnlyPointNames = _pointNames.AsReadOnly());

        // Las acciones del menú se ejecutan al final del cuadro, igual que los mandos.
        private int _requestedPoint = -1;
        private int _requestedMode;
        public void Enter()
        {
            _requestedMode = 1;
        }
        public void Leave()
        {
            _requestedMode = -1;
        }
        public void GoToPoint(int index)
        {
            if (_active && index >= 0 && index < _pointNames.Count) _requestedPoint = index;
        }

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

        private void UpdatePassthroughFloor()
        {
            if (_rig == null) _rig = FindFirstObjectByType<OVRCameraRig>();
            if (_rig == null || _rig.trackingSpace == null) return;
            if (!_floorOriginSet && OVRManager.instance != null)
            {
                // Revisión de Claude: solo se cambia el origen a nivel de los ojos. Con FloorLevel o
                // Stage el piso ya está bien. Al cambiar, la altura de la cabeza salta, así que
                // el libro se vuelve a poner frente a la vista medio segundo después.
                if (OVRManager.instance.trackingOriginType == OVRManager.TrackingOrigin.EyeLevel)
                {
                    OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
                    _replaceBookAt = Time.unscaledTime + 0.5f;
                    Debug.Log("[FolioXR] Origen del seguimiento: EyeLevel → FloorLevel (piso real para la física).");
                }
                _floorOriginSet = true;
            }
            if (_replaceBookAt >= 0f && Time.unscaledTime >= _replaceBookAt)
            {
                _replaceBookAt = -1f;
                if (_book == null) _book = FindFirstObjectByType<FolioXR.Reading.FolioBook>();
                if (_book != null && !_active) _book.PlaceInFrontOfView();
            }
            if (_passthroughFloor == null)
            {
                _passthroughFloor = new GameObject("Piso físico passthrough (sin renderer)");
                _passthroughFloor.transform.SetParent(transform, false);
                var floor = _passthroughFloor.AddComponent<BoxCollider>();
                floor.size = new Vector3(100f, 0.1f, 100f);
            }
            _passthroughFloor.SetActive(!_active);
            Vector3 origin = _rig.trackingSpace.position;
            _passthroughFloor.transform.SetPositionAndRotation(origin - Vector3.up * 0.05f, Quaternion.identity);
        }

        private void Update()
        {
            UpdatePassthroughFloor();
            if (InputPaused)
            {
                _clickWasDown = true;
                return;
            }
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!device.isValid ||
                !device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool click))
            {
                // Al reconectar hay que soltar el clic antes de actuar.
                _clickWasDown = true;
                return;
            }

            bool toggle = click && !_clickWasDown;
            _clickWasDown = click;
            if (toggle) _toggleRequested = true;
        }

        private void LateUpdate()
        {
            // OVRCameraRig actualiza los anchors en Update/FixedUpdate. Movemos su
            // raíz después, conservando intacta la pose local que entrega OpenXR.
            if (_requestedMode != 0)
            {
                bool enter = _requestedMode > 0;
                _requestedMode = 0;
                _toggleRequested = false;
                if (enter && !_active) EnterEnvironment();
                else if (!enter && _active) LeaveEnvironment();
                return;
            }
            if (_toggleRequested)
            {
                _toggleRequested = false;
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

            if (_requestedPoint >= 0)
            {
                GoToReadingPoint(_requestedPoint);
                _requestedPoint = -1;
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
            if (_book == null || _rig == null || _rig.centerEyeAnchor == null || _rig.trackingSpace == null ||
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
                    _pointNames.Clear();
                    foreach (ReadingPoint point in _house.ReadingPoints) _pointNames.Add(point.Name);
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
                UpdatePassthroughFloor();
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
            FolioLocomotion.SuspendForRelocation();
            _active = false;
            _requestedPoint = -1;
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
            UpdatePassthroughFloor();
            if (_book != null) _book.PlaceInFrontOfView();
        }

        private void GoToReadingPoint(int index)
        {
            FolioLocomotion.SuspendForRelocation();
            int count = _house.ReadingPoints.Count;
            _pointIndex = (index % count + count) % count;
            ReadingPoint point = _house.ReadingPoints[_pointIndex];
            Transform root = _house.Root.transform;
            Transform eyes = _rig.centerEyeAnchor;
            float eyeHeight = Mathf.Max(0.5f, eyes.position.y - _rig.trackingSpace.position.y);
            Vector3 targetPosition = root.TransformPoint(point.FloorPosition) + Vector3.up * (eyeHeight + 0.03f);
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
            _toggleRequested = false;
            _requestedMode = 0;
            _requestedPoint = -1;
        }

        private void OnDisable()
        {
            _requestedMode = 0;
            _toggleRequested = false;
            LeaveEnvironment();
        }

        private void OnDestroy()
        {
            LeaveEnvironment();
            if (_house != null) _house.Dispose();
            _house = null;
            if (_pointLabel != null) Destroy(_pointLabel.gameObject);
            if (_passthroughFloor != null) Destroy(_passthroughFloor);
        }
    }
}
