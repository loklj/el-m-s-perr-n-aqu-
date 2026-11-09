using FolioXR.Environments;
using FolioXR.Reading;
using FolioXR.Tools;
using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Menu
{
    // Meta actualiza las manos primero; FolioBook (200) consume la pausa después.
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class MenuController : MonoBehaviour
    {
        public const string StylePrefsKey = "folioxr.menuStyle";
        public bool IsOpen { get; private set; }
        public MenuModel Model { get; private set; }
        private FolioBook _book;
        private FolioEnvironmentController _environment;
        private OVRCameraRig _rig;
        private PanelMenuView _panel;
        private IMenuView _view; // Punto de extensión para la futura SphereMenuView.
        private readonly MenuHandInput _left = new MenuHandInput();
        private readonly MenuHandInput _right = new MenuHandInput();
        private readonly bool[] _triggerWas = { true, true };
        private readonly bool[] _touchArmed = new bool[2];
        private readonly Vector3[] _previousTip = new Vector3[2];
        private readonly bool[] _hasTip = new bool[2];
        private bool _menuWas = true, _waitingRelease, _focused = true;
        private readonly bool[] _backWas = { true, true };
        private float _nextFind;
        private Vector3 _lastRigPosition;
        private int _clearFrames;
        private AudioSource _audio;
        private AudioClip _click, _notice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateController()
        {
            if (FindFirstObjectByType<MenuController>(FindObjectsInactive.Include) == null)
                new GameObject("Menú (Folio XR)").AddComponent<MenuController>();
        }

        private void Awake()
        {
            UseSimpleStyle();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _click = Tone("Clic del menú", 800f, 0.035f, 0.08f);
            _notice = Tone("Fin de lectura", 440f, 0.65f, 0.07f);
        }

        public void UseSimpleStyle()
        {
            // Hasta implementar la otra vista, incluso una preferencia antigua usa el panel.
            PlayerPrefs.SetString(StylePrefsKey, "simple");
            PlayerPrefs.Save();
        }

        private void ResolveScene()
        {
            if (_book == null) _book = FindFirstObjectByType<FolioBook>();
            if (_environment == null) _environment = FindFirstObjectByType<FolioEnvironmentController>();
            if (_rig == null) _rig = FindFirstObjectByType<OVRCameraRig>();
            foreach (OVRHand hand in FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (hand.GetHand() == OVRPlugin.Hand.HandLeft) _left.Bind(hand);
                if (hand.GetHand() == OVRPlugin.Hand.HandRight) _right.Bind(hand);
            }
            if (_panel == null && _book != null && _environment != null)
            {
                var root = new GameObject("Panel simple (Folio XR)");
                root.transform.SetParent(transform, false);
                _panel = root.AddComponent<PanelMenuView>();
                if (!_panel.Initialize(_book))
                {
                    Destroy(root);
                    _panel = null;
                    return;
                }
                Model = new MenuModel(_book, _environment, UseSimpleStyle);
                _view = _panel;
                _view.Bind(Model, Close);
                Model.TimerElapsed += OnTimerElapsed;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 1f;
                ResolveScene();
            }
            if (!_focused) return;
            // El puño usado para sostener el libro no debe abrir/cerrar el menú al soltar.
            bool gesture = false;
            if ((_book != null && _book.IsGrabbed) || ToolRegistry.HoldsHand(XRNode.LeftHand)) _left.Reset();
            else gesture = _left.UpdateGesture();
            if (_book != null) _book.LeftPinchSuppressed = _left.PalmUp;
            InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool menu = Down(leftDevice, CommonUsages.menuButton);
            bool close = false;
            for (int i = 0; i < 2; i++)
            {
                XRNode side = i == 0 ? XRNode.LeftHand : XRNode.RightHand;
                bool back = Down(InputDevices.GetDeviceAtXRNode(side), CommonUsages.secondaryButton);
                close |= back && !_backWas[i] && !ToolRegistry.HoldsController(side);
                _backWas[i] = back; // Flanco real incluso si la herramienta reserva B/Y.
            }
            bool toggle = (menu && !_menuWas) || gesture;
            _menuWas = !leftDevice.isValid || menu;
            if (toggle) { if (IsOpen) Close(); else Open(); }
            else if (IsOpen && close) Close();
            if (IsOpen)
            {
                PauseControls(true);
                FollowRigJump();
                _panel.BeginPointers();
                UpdatePointer(0, XRNode.LeftHand, _rig != null ? _rig.leftControllerAnchor : null, _left);
                if (IsOpen) UpdatePointer(1, XRNode.RightHand, _rig != null ? _rig.rightControllerAnchor : null, _right);
                _panel.EndPointers();
            }
            else
            {
                // También consume flancos mientras está cerrado: abrir con un gatillo
                // sostenido nunca selecciona el botón que aparezca debajo del rayo.
                for (int i = 0; i < 2; i++)
                {
                    InputDevice device = InputDevices.GetDeviceAtXRNode(i == 0 ? XRNode.LeftHand : XRNode.RightHand);
                    _triggerWas[i] = !device.isValid || Down(device, CommonUsages.triggerButton);
                    _touchArmed[i] = _hasTip[i] = false;
                }
            }
            if (_waitingRelease && !IsOpen)
            {
                // Dos cuadros neutros evitan que la nueva muestra de Meta del mismo
                // cuadro reactive un pellizco. No se bloquea el agarre del libro.
                _clearFrames = AllReleased() ? _clearFrames + 1 : 0;
                if (_clearFrames >= 2) { _waitingRelease = false; PauseControls(false); }
            }
            Model?.Tick();
        }

        public void Open()
        {
            Camera camera = Camera.main;
            if (IsOpen || _view == null || camera == null) return;
            IsOpen = true;
            _waitingRelease = false;
            PauseControls(true);
            for (int i = 0; i < 2; i++) { _touchArmed[i] = _hasTip[i] = false; _triggerWas[i] = true; }
            _view.SetOpen(true, camera.transform);
            if (_rig != null) _lastRigPosition = _rig.transform.position;
            Debug.Log("[FolioXR] Menú abierto: Simple.");
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _waitingRelease = true;
            _clearFrames = 0;
            PauseControls(true);
            _view.SetOpen(false, Camera.main != null ? Camera.main.transform : null);
            Debug.Log("[FolioXR] Menú cerrado: esperando soltar los controles.");
        }

        // Revisión de Claude: al elegir la casa o un lugar desde el menú, el entorno mueve el rig
        // en su LateUpdate. Sin esto el panel quedaba atrás, abierto y fuera de la vista.
        private void FollowRigJump()
        {
            if (_rig == null) return;
            Vector3 position = _rig.transform.position;
            if ((position - _lastRigPosition).sqrMagnitude > 0.0025f && Camera.main != null)
                _panel.PlaceInFront(Camera.main.transform);
            _lastRigPosition = position;
        }

        private void PauseControls(bool paused)
        {
            if (_book != null) _book.InputPaused = paused;
            if (_environment != null) _environment.InputPaused = paused;
        }

        private bool AllReleased()
        {
            foreach (XRNode node in new[] { XRNode.LeftHand, XRNode.RightHand })
            {
                InputDevice device = InputDevices.GetDeviceAtXRNode(node);
                if (Down(device, CommonUsages.primaryButton) || Down(device, CommonUsages.secondaryButton) ||
                    Down(device, CommonUsages.primary2DAxisClick) || Down(device, CommonUsages.menuButton) ||
                    Down(device, CommonUsages.triggerButton)) return false;
                if ((node == XRNode.LeftHand || (_environment != null && _environment.IsActive)) &&
                    device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis) &&
                    (Mathf.Abs(axis.x) >= 0.3f || Mathf.Abs(axis.y) >= 0.3f)) return false;
            }
            return !Pinching(_left.Hand, false) &&
                !Pinching(_right.Hand, _environment != null && _environment.IsActive);
        }

        private static bool Pinching(OVRHand hand, bool includeMiddle) => hand != null && hand.isActiveAndEnabled &&
            hand.IsTracked && (!hand.IsDataHighConfidence || hand.IsSystemGestureInProgress ||
                hand.GetFingerIsPinching(OVRHand.HandFinger.Index) ||
                (includeMiddle && hand.GetFingerIsPinching(OVRHand.HandFinger.Middle)));

        private void UpdatePointer(int slot, XRNode node, Transform anchor, MenuHandInput hand)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            bool trigger = Down(device, CommonUsages.triggerButton);
            bool edge = trigger && !_triggerWas[slot];
            _triggerWas[slot] = !device.isValid || trigger;
            if (ToolRegistry.HoldsController(node) || ToolRegistry.HoldsHand(node))
            { _touchArmed[slot] = _hasTip[slot] = false; return; }
            if (hand.TryBone(OVRSkeleton.BoneId.Hand_IndexTip, out Vector3 tip))
            {
                Vector3 local = _panel.transform.InverseTransformPoint(tip);
                // El frente es -Z. Hay que retirar el dedo 18 mm para rearmar.
                if (local.z < -0.018f) _touchArmed[slot] = true;
                if (local.z >= -0.025f && local.z <= 0.015f) _panel.Hover(local);
                if (_hasTip[slot] && _touchArmed[slot] && _previousTip[slot].z < -0.003f && local.z >= -0.003f)
                {
                    float t = (-0.003f - _previousTip[slot].z) / (local.z - _previousTip[slot].z);
                    Vector3 hit = Vector3.Lerp(_previousTip[slot], local, t);
                    _touchArmed[slot] = false; // Se consume incluso al tocar un espacio vacío.
                    if (_panel.Activate(hit)) Feedback(default, false);
                }
                _previousTip[slot] = local;
                _hasTip[slot] = true;
                return;
            }
            _hasTip[slot] = _touchArmed[slot] = false;
            // No usar anchors obsoletos al perder seguimiento ni rayos emulados por manos.
            if (anchor == null || !device.isValid ||
                (device.characteristics & InputDeviceCharacteristics.Controller) == 0 ||
                !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked) return;
            if (_panel.Intersect(new Ray(anchor.position, anchor.forward), out Vector3 hitWorld))
            {
                Vector3 local = _panel.transform.InverseTransformPoint(hitWorld);
                _panel.SetRay(slot, anchor.position, hitWorld);
                _panel.Hover(local);
                if (edge && _panel.Activate(local)) Feedback(device, true);
            }
        }

        private void Feedback(InputDevice device, bool haptic)
        {
            _audio.PlayOneShot(_click);
            if (haptic && device.TryGetHapticCapabilities(out HapticCapabilities capabilities) &&
                capabilities.supportsImpulse) device.SendHapticImpulse(0, 0.25f, 0.035f);
        }

        private void OnTimerElapsed()
        {
            _audio.PlayOneShot(_notice);
            _view.ShowNotice("Tiempo de lectura cumplido.\nPuedes seguir leyendo.", Camera.main != null ? Camera.main.transform : null);
        }

        private static AudioClip Tone(string name, float frequency, float seconds, float volume)
        {
            const int rate = 24000;
            var samples = new float[Mathf.CeilToInt(rate * seconds)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * volume *
                    Mathf.Sin(Mathf.PI * i / (samples.Length - 1));
            }
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static bool Down(InputDevice device, InputFeatureUsage<bool> usage) =>
            device.isValid && device.TryGetFeatureValue(usage, out bool value) && value;

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            Close();
            _menuWas = _backWas[0] = _backWas[1] = true;
            _left.Reset();
            _waitingRelease = true;
            _clearFrames = 0;
            PauseControls(true);
        }

        private void OnApplicationPause(bool paused) { OnApplicationFocus(!paused); }

        private void OnDisable()
        {
            Close();
            PauseControls(false);
            if (_book != null) _book.LeftPinchSuppressed = false;
        }

        private void OnDestroy()
        {
            if (Model != null) Model.TimerElapsed -= OnTimerElapsed;
            if (_click != null) Destroy(_click);
            if (_notice != null) Destroy(_notice);
        }
    }
}
