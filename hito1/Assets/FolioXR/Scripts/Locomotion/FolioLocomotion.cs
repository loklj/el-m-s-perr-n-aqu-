using FolioXR.Environments;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Locomotion
{
    /// <summary>Locomoción solo en el entorno. Se ejecuta después de sus recolocaciones.</summary>
    [DefaultExecutionOrder(10010)]
    [DisallowMultipleComponent]
    public sealed class FolioLocomotion : MonoBehaviour
    {
        public const string VignettePrefsKey = "folioxr.locomotionVignette";
        public const string SmoothTurnPrefsKey = "folioxr.smoothTurn";
        private const float Speed = 1.4f, Acceleration = 3.5f, Radius = 0.20f;
        private const float Press = 0.7f, Release = 0.3f, MaxTeleport = 10f;
        private static FolioLocomotion _instance;
        public static bool VignetteEnabled => PlayerPrefs.GetInt(VignettePrefsKey, 1) != 0;
        public static bool SmoothTurn => PlayerPrefs.GetInt(SmoothTurnPrefsKey, 0) != 0;
        private FolioEnvironmentController _environment;
        private OVRCameraRig _rig;
        private FolioBook _book;
        private BookGrab _grab;
        private OVRHand _hand;
        private CharacterController _capsule;
        private LocomotionVisuals _visuals;
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private readonly Collider[] _overlaps = new Collider[64];
        private readonly Vector3[] _arc = new Vector3[81];
        private Vector3 _velocity, _localHead, _destination;
        private float _fallSpeed, _nextFind;
        private int _relocatedFrame = -1;
        private bool _focused = true, _paused, _ready, _walkArmed, _turnArmed;
        private bool _controllerArmed, _handArmed, _validTarget, _ignoredBook;
        private enum AimSource { None, Controller, Hand }
        private AimSource _aim;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (FindFirstObjectByType<FolioLocomotion>(FindObjectsInactive.Include) == null)
                new GameObject("Movimiento libre (Folio XR)").AddComponent<FolioLocomotion>();
        }

        private void Awake() { _instance = this; }

        public static void SetVignette(bool value)
        {
            PlayerPrefs.SetInt(VignettePrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void SetSmoothTurn(bool value)
        {
            PlayerPrefs.SetInt(SmoothTurnPrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
            if (_instance != null) _instance._turnArmed = false;
        }

        // El entorno llama ANTES de mover el rig, también al salir o desactivarse.
        // Se habilita de nuevo en otro cuadro, con centro y altura ya actualizados.
        public static void SuspendForRelocation()
        {
            if (_instance == null) return;
            _instance.Stop();
            _instance._relocatedFrame = Time.frameCount;
        }

        public static bool ReservesMiddlePinch(OVRHand hand) => _instance != null &&
            _instance._environment != null && _instance._environment.IsActive && hand != null &&
            hand.GetHand() == OVRPlugin.Hand.HandRight &&
            hand.GetFingerIsPinching(OVRHand.HandFinger.Middle) &&
            !hand.GetFingerIsPinching(OVRHand.HandFinger.Index);

        private void Resolve()
        {
            if (_environment == null) _environment = FindFirstObjectByType<FolioEnvironmentController>();
            if (_book == null) _book = FindFirstObjectByType<FolioBook>();
            if (_grab == null) _grab = FindFirstObjectByType<BookGrab>();
            if (_rig == null) _rig = FindFirstObjectByType<OVRCameraRig>();
            if (_rig != null && _capsule == null)
            {
                _capsule = _rig.GetComponent<CharacterController>();
                if (_capsule == null) _capsule = _rig.gameObject.AddComponent<CharacterController>();
                _capsule.enabled = false;
                _capsule.radius = Radius;
                _capsule.height = 1.7f;
                _capsule.skinWidth = 0.02f;
                _capsule.stepOffset = 0.3f;
                _capsule.slopeLimit = 45f;
                _capsule.minMoveDistance = 0f;
                _capsule.detectCollisions = true;
                _capsule.enableOverlapRecovery = true;
            }
            if (_hand == null || !_hand.isActiveAndEnabled)
                foreach (OVRHand hand in FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (hand.GetHand() == OVRPlugin.Hand.HandRight && hand.isActiveAndEnabled)
                    { _hand = hand; break; }
            if (_visuals == null && _book != null && Camera.main != null)
                _visuals = LocomotionVisuals.TryCreate(transform, _book, Camera.main);
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= _nextFind)
            { _nextFind = Time.unscaledTime + 1f; Resolve(); }
            if (!_focused || _paused || _environment == null || !_environment.IsActive ||
                _rig == null || _rig.centerEyeAnchor == null || _rig.trackingSpace == null || _capsule == null ||
                _environment.InputPaused || _relocatedFrame == Time.frameCount)
            { Stop(); return; }
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid || !head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked)
            { Stop(); return; }
            float dt = Time.deltaTime;
            if (dt <= 0f || dt > 0.1f) { Stop(); return; }

            Vector3 oldPosition = _rig.transform.position;
            Quaternion oldRotation = _rig.transform.rotation;
            if (!_ready)
            {
                _localHead = _rig.transform.InverseTransformPoint(_rig.centerEyeAnchor.position);
                UpdateCapsule();
                Physics.SyncTransforms();
                _capsule.enabled = true;
                _ready = true;
            }
            bool ignoreBook = _book != null && _book.IsGrabbed;
            if (_book != null && _book.bookCollider != null && ignoreBook != _ignoredBook)
            {
                Physics.IgnoreCollision(_capsule, _book.bookCollider, ignoreBook);
                _ignoredBook = ignoreBook;
            }

            bool leftValid = ControllerAxis(XRNode.LeftHand, out Vector2 left);
            left = Vector2.ClampMagnitude(left, 1f);
            bool rightValid = ControllerAxis(XRNode.RightHand, out Vector2 right);
            if (!leftValid) _walkArmed = false;
            else if (left.magnitude < Release) _walkArmed = true;
            Vector3 forward = Vector3.ProjectOnPlane(_rig.centerEyeAnchor.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(Vector3.Cross(_rig.centerEyeAnchor.right, Vector3.up), Vector3.up);
            forward.Normalize();
            Vector3 wanted = _walkArmed && leftValid ?
                (forward * left.y + Vector3.Cross(Vector3.up, forward) * left.x) * Speed : Vector3.zero;
            if (left.magnitude < 0.15f) wanted = Vector3.zero;
            _velocity = leftValid ? Vector3.MoveTowards(_velocity, wanted, Acceleration * dt) : Vector3.zero;

            // Barrer también el desplazamiento físico horizontal. Cambiar solo center
            // teletransportaría la cápsula a través de una pared al caminar en room-scale.
            Vector3 nextLocal = _rig.transform.InverseTransformPoint(_rig.centerEyeAnchor.position);
            Vector3 physical = _rig.transform.TransformVector(new Vector3(
                nextLocal.x - _localHead.x, 0f, nextLocal.z - _localHead.z));
            _capsule.enabled = false;
            _rig.transform.position -= physical;
            UpdateCapsule();
            Physics.SyncTransforms();
            _capsule.enabled = true;
            if (_ignoredBook && _book != null && _book.bookCollider != null)
                Physics.IgnoreCollision(_capsule, _book.bookCollider, true);
            if (_capsule.isGrounded && _fallSpeed < 0f) _fallSpeed = -2f;
            _fallSpeed = Mathf.Max(-20f, _fallSpeed - 9.81f * dt);
            Vector3 beforeMove = _rig.transform.position;
            CollisionFlags flags = _capsule.Move(physical + (_velocity + Vector3.up * _fallSpeed) * dt);
            if ((flags & CollisionFlags.Below) != 0) _fallSpeed = -2f;
            if ((flags & CollisionFlags.Above) != 0 && _fallSpeed > 0f) _fallSpeed = 0f;
            float walked = Vector3.ProjectOnPlane(_rig.transform.position - beforeMove - physical, Vector3.up).magnitude / dt;

            UpdateTeleport(rightValid, right);
            UpdateTurn(rightValid, right, dt);
            _localHead = _rig.transform.InverseTransformPoint(_rig.centerEyeAnchor.position);
            // BookGrab tomó las poses en Update: trasladar su estado en este mismo cuadro.
            if (_grab != null) _grab.CarryWithRig(oldPosition, oldRotation, _rig.transform.position, _rig.transform.rotation);
            _visuals?.SetComfort(VignetteEnabled && _velocity.sqrMagnitude > 0.001f ? walked / Speed : 0f, dt);
        }

        private void UpdateCapsule()
        {
            Vector3 head = _rig.transform.InverseTransformPoint(_rig.centerEyeAnchor.position);
            Vector3 floor = _rig.transform.InverseTransformPoint(_rig.trackingSpace.position);
            _capsule.height = Mathf.Max(0.6f, head.y - floor.y + 0.10f);
            _capsule.center = new Vector3(head.x, floor.y + _capsule.height * 0.5f, head.z);
        }

        private static bool ControllerAxis(XRNode node, out Vector2 axis)
        {
            axis = Vector2.zero;
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid || (device.characteristics & InputDeviceCharacteristics.Controller) == 0 ||
                !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis)) return false;
            return true;
        }

        private void UpdateTurn(bool valid, Vector2 axis, float dt)
        {
            if (!valid) { _turnArmed = false; return; }
            if (Mathf.Abs(axis.x) < Release) _turnArmed = true;
            if (_aim != AimSource.None || axis.y > Press) { _turnArmed = false; return; }
            float yaw = 0f;
            if (SmoothTurn)
            {
                if (_turnArmed && Mathf.Abs(axis.x) > Release) yaw = axis.x * 60f * dt;
            }
            else if (_turnArmed && Mathf.Abs(axis.x) > Press)
            { yaw = Mathf.Sign(axis.x) * 30f; _turnArmed = false; }
            if (yaw == 0f) return;
            _capsule.enabled = false;
            _rig.transform.RotateAround(_rig.centerEyeAnchor.position, Vector3.up, yaw);
            UpdateCapsule();
            Physics.SyncTransforms();
            _capsule.enabled = true;
        }

        private bool ReliableHand() => _hand != null && _hand.isActiveAndEnabled && _hand.IsTracked &&
            _hand.IsDataHighConfidence && !_hand.IsSystemGestureInProgress && _hand.IsPointerPoseValid &&
            _hand.GetFingerConfidence(OVRHand.HandFinger.Middle) == OVRHand.TrackingConfidence.High &&
            _hand.GetFingerConfidence(OVRHand.HandFinger.Thumb) == OVRHand.TrackingConfidence.High;

        private void UpdateTeleport(bool controller, Vector2 axis)
        {
            // Nunca saltar si no se ha podido construir la previsualización.
            if (_visuals == null) { CancelAim(); return; }
            bool reliable = ReliableHand();
            bool middle = reliable && _hand.GetFingerIsPinching(OVRHand.HandFinger.Middle);
            bool index = reliable && _hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            if (!controller) _controllerArmed = false;
            else if (axis.y < Release && Mathf.Abs(axis.x) < Release) _controllerArmed = true;
            if (!reliable || controller || index) _handArmed = false;
            else if (!middle) _handArmed = true;

            if (_aim == AimSource.Controller && (!controller || Mathf.Abs(axis.x) > Press))
            { CancelAim(); _controllerArmed = false; return; }
            if (_aim == AimSource.Hand && (!reliable || controller || index))
            { CancelAim(); _handArmed = false; return; }
            if (_aim == AimSource.None)
            {
                if (_controllerArmed && controller && axis.y > Press && Mathf.Abs(axis.x) < Release)
                { _aim = AimSource.Controller; _controllerArmed = false; }
                else if (_handArmed && middle && !index && !controller)
                { _aim = AimSource.Hand; _handArmed = false; }
            }
            if (_aim == AimSource.None) return;
            bool released = _aim == AimSource.Controller ? axis.y < Release : !middle;
            if (released)
            {
                Physics.SyncTransforms();
                // Conserva el último punto mostrado, pero vuelve a comprobar soporte y espacio.
                if (_validTarget && ValidateDestination(_destination, out Vector3 landing)) Teleport(landing);
                CancelAim();
                _turnArmed = false;
                return;
            }
            Transform source = _aim == AimSource.Controller ? _rig.rightControllerAnchor : _hand.PointerPose;
            if (source == null) { CancelAim(); return; }
            DrawArc(source.position, source.forward);
        }

        private bool Ignored(Collider collider) => collider == _capsule ||
            (collider != null && collider.transform.IsChildOf(_rig.transform)) ||
            (_book != null && _book.IsGrabbed && collider == _book.bookCollider);

        private bool Cast(Vector3 start, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.RaycastNonAlloc(start, direction, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
            // Un buffer lleno no permite saber si falta un obstáculo más cercano: rechazar.
            if (count == _hits.Length) { nearest.point = start; return true; }
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (!Ignored(_hits[i].collider) && _hits[i].distance < best)
                { best = _hits[i].distance; nearest = _hits[i]; }
            return best < float.PositiveInfinity;
        }

        private void DrawArc(Vector3 start, Vector3 forward)
        {
            Physics.SyncTransforms();
            _validTarget = false;
            _arc[0] = start;
            // Un mando/mano que atraviese físicamente un muro no permite apuntar a través de él.
            Vector3 fromHead = start - _rig.centerEyeAnchor.position;
            if (fromHead.sqrMagnitude > 0.0001f && Cast(_rig.centerEyeAnchor.position,
                fromHead.normalized, fromHead.magnitude, out RaycastHit obstruction))
            {
                _arc[0] = _rig.centerEyeAnchor.position;
                _arc[1] = obstruction.point;
                _visuals.ShowArc(_arc, 2, false, Vector3.zero);
                return;
            }
            Vector3 velocity = forward.normalized * 6f + Vector3.up * 2f;
            int count = 1;
            for (int i = 1; i < _arc.Length; i++)
            {
                float t = i * 0.04f;
                Vector3 next = start + velocity * t + Vector3.down * (4.905f * t * t);
                Vector3 delta = next - _arc[count - 1];
                if (Cast(_arc[count - 1], delta.normalized, delta.magnitude, out RaycastHit hit))
                {
                    _arc[count++] = hit.point;
                    if (_environment.IsTeleportFloor(hit.collider) && hit.normal.y >= 0.95f &&
                        ValidateDestination(hit.point, out _destination)) _validTarget = true;
                    break;
                }
                _arc[count++] = next;
                if (Vector3.Distance(Feet(), next) > MaxTeleport || next.y < Feet().y - MaxTeleport) break;
            }
            _visuals?.ShowArc(_arc, count, _validTarget, _destination);
        }

        private Vector3 Feet() => new Vector3(_rig.centerEyeAnchor.position.x,
            _rig.trackingSpace.position.y, _rig.centerEyeAnchor.position.z);

        private bool ValidateDestination(Vector3 point, out Vector3 landing)
        {
            landing = point;
            if (Vector3.Distance(Feet(), point) > MaxTeleport) return false;
            if (!Cast(point + Vector3.up * 0.10f, Vector3.down, 0.20f, out RaycastHit floor) ||
                !_environment.IsTeleportFloor(floor.collider) || floor.normal.y < 0.95f) return false;
            landing = floor.point + Vector3.up * 0.03f;
            // Cápsula completa, incluida una holgura superior, no solo un rayo a la cabeza.
            float radius = _capsule.radius + 0.01f;
            Vector3 bottom = landing + Vector3.up * radius;
            Vector3 top = landing + Vector3.up * Mathf.Max(radius, _capsule.height - radius + 0.03f);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, _overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == _overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (!Ignored(_overlaps[i])) return false;
            return Vector3.Distance(Feet(), landing) <= MaxTeleport;
        }

        private void Teleport(Vector3 landing)
        {
            _capsule.enabled = false;
            _rig.transform.position += landing - Feet();
            _velocity = Vector3.zero;
            _fallSpeed = 0f;
            UpdateCapsule();
            Physics.SyncTransforms();
            _capsule.enabled = true;
        }

        private void CancelAim()
        {
            _aim = AimSource.None;
            _validTarget = false;
            _visuals?.HideArc();
        }

        private void Stop()
        {
            CancelAim();
            _visuals?.StopComfort();
            _velocity = Vector3.zero;
            _fallSpeed = 0f;
            _ready = _walkArmed = _turnArmed = _controllerArmed = _handArmed = false;
            if (_capsule != null) _capsule.enabled = false;
        }

        private void OnApplicationFocus(bool focused) { _focused = focused; Stop(); }
        private void OnApplicationPause(bool paused) { _paused = paused; Stop(); }
        private void OnDisable() { Stop(); }
        private void OnDestroy()
        {
            Stop();
            _visuals?.Dispose();
            if (_capsule != null) Destroy(_capsule);
            if (_instance == this) _instance = null;
        }
    }
}
