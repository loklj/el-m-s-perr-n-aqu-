using FolioXR.Tools;
using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Reading
{
    /// <summary>Agarre por pose, física al soltar y escala bimanual. Solo Meta Core y Unity XR.</summary>
    [DefaultExecutionOrder(150)] // Después del menú; antes de los pellizcos para páginas (200).
    [DisallowMultipleComponent]
    public sealed class BookGrab : MonoBehaviour
    {
        private const float PinchDistance = 0.06f, PalmDistance = 0.08f, ControllerDistance = 0.08f;
        private const float TrackingGraceSeconds = 0.3f;

        private readonly GrabInput[] _inputs =
        {
            new GrabInput(XRNode.LeftHand, true, "mano izquierda"),
            new GrabInput(XRNode.RightHand, true, "mano derecha"),
            new GrabInput(XRNode.LeftHand, false, "mando izquierdo"),
            new GrabInput(XRNode.RightHand, false, "mando derecho")
        };
        private FolioBook _book;
        private OVRCameraRig _rig;
        private Rigidbody _body;
        private GrabInput _owner, _second;
        private Vector3 _positionOffset, _midpointOffset;
        private Quaternion _rotationOffset;
        private float _initialDistance, _initialScale, _nextFind;
        private bool _focused = true;
        private readonly bool[] _toolSuppressed = new bool[2];
        private System.Func<Vector3, float> _distanceToBook;

        private void Awake() { _distanceToBook = DistanceToBook; }

        public bool HoldsSide(XRNode node) => (_owner != null && _owner.Node == node) ||
            (_second != null && _second.Node == node);

        public void SetToolSuppressed(XRNode node, bool value)
        { _toolSuppressed[node == XRNode.LeftHand ? 0 : 1] = value; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateGrab()
        {
            if (FindFirstObjectByType<BookGrab>(FindObjectsInactive.Include) == null)
                new GameObject("Agarre propio (Folio XR)").AddComponent<BookGrab>();
        }

        private void ResolveScene()
        {
            if (_book == null)
            {
                _owner = _second = null;
                _book = FindFirstObjectByType<FolioBook>();
                if (_book != null)
                {
                    _body = _book.GetComponent<Rigidbody>();
                    _book.Recalled += CancelGrab;
                    _book.ScaleReset += RebaseHeldPose;
                }
            }
            if (_rig == null) _rig = FindFirstObjectByType<OVRCameraRig>();
            if (_rig == null) return;
            GrabInput.BindHands(_inputs, _rig);
        }

        private void Update()
        {
            if (!_focused) return;
            if (Time.unscaledTime >= _nextFind)
            { _nextFind = Time.unscaledTime + 1f; ResolveScene(); }
            foreach (GrabInput input in _inputs)
                input.Sample(_rig, input == _owner || input == _second, _distanceToBook, PinchDistance);
            if (_book == null || !_book.isActiveAndEnabled || _rig == null)
            { Release(false); return; }

            UpdateHeldPose();
            bool attempt = false;
            foreach (GrabInput input in _inputs) attempt |= input.Started ||
                (input.IsHand && ((input.Pinch && !input.LoggedPinch) || (input.Fist && !input.LoggedFist)));
            if (attempt) Physics.SyncTransforms();
            foreach (GrabInput input in _inputs)
            {
                if (input.IsHand)
                {
                    if (input.Pinch && !input.LoggedPinch) LogAttempt(input, "pellizco", input.PinchPosition);
                    if (input.Fist && !input.LoggedFist) LogAttempt(input, "puño", input.PalmPosition);
                    input.LoggedPinch = input.Pinch;
                    input.LoggedFist = input.Fist;
                }
                if (!input.Started || input == _owner || input == _second ||
                    _toolSuppressed[input.Node == XRNode.LeftHand ? 0 : 1]) continue;
                // Un lado físico nunca cuenta dos veces (mano + mando emulado).
                if (_owner != null && (_owner.Node == input.Node || _owner.IsHand != input.IsHand)) continue;
                float distance = input.IsHand ? (input.FistMode ? PalmDistance : PinchDistance) : ControllerDistance;
                if (DistanceToBook(input.Position) < distance) Take(input);
            }
        }

        private float DistanceToBook(Vector3 point)
        {
            BoxCollider collider = _book != null ? _book.bookCollider : null;
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy
                ? Vector3.Distance(collider.ClosestPoint(point), point) : float.PositiveInfinity;
        }

        private void LogAttempt(GrabInput input, string gesture, Vector3 point)
        {
            var s = input.Skeleton;
            Debug.Log("[FolioXR] Agarre intento: " + input.Label + " " + gesture + ", fiable=" + input.Reliable +
                ", skeleton=" + (s != null ? s.GetSkeletonType().ToString() : "ausente") +
                ", huesos=" + (s != null && s.Bones != null ? s.Bones.Count : 0) + ", " + input.BonesFound +
                ", pinchBones=" + input.PinchBones + ", palmBones=" + input.PalmBones +
                ", respaldoOVRHand=" + !(gesture == "puño" ? input.PalmBones : input.PinchBones) +
                ", distancia=" + DistanceToBook(point).ToString("F3") + " m");
        }

        private void Take(GrabInput input)
        {
            if (_owner == null)
            {
                _owner = input;
                _book.FreezePhysics();
                _book.IsGrabbed = true;
                RebaseHeldPose();
            }
            else
            {
                if (_second != null || !_owner.Reliable || !_owner.Pressed ||
                    Vector3.Distance(_owner.Position, input.Position) < 0.02f) return;
                _second = input;
                RebaseHeldPose();
                Debug.Log("[FolioXR] Agarre: modo dos " + (input.IsHand ? "manos" : "mandos"));
            }
            input.LostSince = -1f;
            input.ClearMotion();
            Debug.Log("[FolioXR] Agarre: tomado con " + input.Label);
            if (!input.IsHand && input.Device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                input.Device.SendHapticImpulse(0, 0.25f, 0.035f);
        }

        private bool StillHolding(GrabInput input)
        {
            if (input.LostSince >= 0f && Time.unscaledTime - input.LostSince > TrackingGraceSeconds) return false;
            if (!input.Reliable)
            {
                if (input.LostSince < 0f) input.LostSince = Time.unscaledTime;
                return input.IsHand;
            }
            input.LostSince = -1f;
            return input.Pressed;
        }

        private void UpdateHeldPose()
        {
            if (_owner == null) return;
            bool firstHolds = StillHolding(_owner);
            bool secondHolds = _second != null && StillHolding(_second);
            if (!firstHolds && !secondHolds)
            {
                bool trackedRelease = _owner.Reliable && _owner.LostSince < 0f && (_second == null || _second.Reliable);
                Release(trackedRelease);
                return;
            }
            if (_second != null && (!firstHolds || !secondHolds))
            {
                if (!firstHolds) _owner = _second;
                _second = null;
                RebaseHeldPose();
                _owner.ClearMotion();
                _book.SaveBookScale();
                Debug.Log("[FolioXR] Agarre: vuelve a una mano/mando sin salto.");
                return;
            }
            if (!_owner.Reliable || (_second != null && !_second.Reliable)) return;
            if (_owner.PoseChanged || (_second != null && _second.PoseChanged)) { RebaseHeldPose(); return; }
            Vector3 position;
            if (_second != null)
            {
                float distance = Vector3.Distance(_owner.Position, _second.Position);
                _book.SetBookScale(_initialScale * distance / _initialDistance);
                position = (_owner.Position + _second.Position) * 0.5f + _midpointOffset;
            }
            else position = _owner.Position + _owner.Rotation * _positionOffset;
            Quaternion rotation = _owner.Rotation * _rotationOffset;
            _book.FreezePhysics();
            if (_body != null) { _body.position = position; _body.rotation = rotation; }
            _book.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Compensa el LateUpdate del rig sin soltar ni inventar velocidad de lanzamiento.</summary>
        public void CarryWithRig(Vector3 oldPosition, Quaternion oldRotation, Vector3 position, Quaternion rotation)
        {
            Quaternion delta = rotation * Quaternion.Inverse(oldRotation);
            if (_owner != null && _book != null)
            {
                Vector3 target = position + delta * (_book.transform.position - oldPosition);
                Quaternion facing = delta * _book.transform.rotation;
                if (_body != null) { _body.position = target; _body.rotation = facing; }
                _book.transform.SetPositionAndRotation(target, facing);
                _midpointOffset = delta * _midpointOffset;
            }
            foreach (GrabInput input in _inputs) input.TransformFrame(oldPosition, position, delta);
        }

        private void RebaseHeldPose()
        {
            if (_owner == null || _book == null) return;
            Quaternion inverse = Quaternion.Inverse(_owner.Rotation);
            _positionOffset = inverse * (_book.transform.position - _owner.Position);
            _rotationOffset = inverse * _book.transform.rotation;
            if (_second == null) return;
            _initialDistance = Mathf.Max(0.02f, Vector3.Distance(_owner.Position, _second.Position));
            _initialScale = _book.BookScale;
            _midpointOffset = _book.transform.position - (_owner.Position + _second.Position) * 0.5f;
        }

        private void Release(bool throwBook)
        {
            if (_owner == null) return;
            Vector3 velocity = throwBook ? _owner.Velocity() : Vector3.zero;
            Vector3 angular = throwBook ? _owner.Velocity(true) : Vector3.zero;
            if (throwBook && _second != null) velocity = (velocity + _second.Velocity()) * 0.5f;
            if (_book != null)
            {
                _book.IsGrabbed = false;
                _book.SaveBookScale();
                _book.ReleasePhysics(velocity, angular);
            }
            _owner = _second = null;
            Debug.Log("[FolioXR] Agarre: soltado, velocidad=" + velocity.ToString("F2"));
        }

        private void CancelGrab()
        {
            _owner = _second = null;
            if (_book != null) { _book.IsGrabbed = false; _book.SaveBookScale(); _book.FreezePhysics(); }
            foreach (GrabInput input in _inputs)
            {
                input.StartConsumed = input.WasGrip = true;
                input.LostSince = -1f;
                input.ClearMotion();
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            // Suspender no debe lanzar el libro con una muestra obsoleta.
            if (!focused) CancelGrab();
        }
        private void OnApplicationPause(bool paused) { OnApplicationFocus(!paused); }
        private void OnDisable() { CancelGrab(); }
        private void OnDestroy()
        {
            if (_book == null) return;
            _book.Recalled -= CancelGrab;
            _book.ScaleReset -= RebaseHeldPose;
        }
    }
}
