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
        // Revisión de Claude: al cerrar los dedos la confianza del seguimiento suele bajar un instante.
        // Un gesto que empieza en ese instante sigue valiendo si vuelve la confianza dentro de esta ventana.
        private const float OnsetWindowSeconds = 0.3f;

        private sealed class GrabInput
        {
            internal readonly XRNode Node;
            internal readonly bool IsHand;
            internal readonly string Label;
            internal OVRHand Hand;
            internal OVRSkeleton Skeleton;
            internal InputDevice Device;
            internal bool WasGrip = true;
            internal bool StartConsumed = true; // Exige soltar antes del primer agarre.
            internal float PinchSince = -1f, FistSince = -1f;
            internal bool LoggedPinch, LoggedFist;
            internal bool Pressed, Started, Reliable, Pinch, Fist, PinchBones, PalmBones;
            internal bool FistMode, PoseChanged;
            internal Vector3 Position, PinchPosition, PalmPosition;
            internal Quaternion Rotation;
            internal float LostSince = -1f;
            internal int PoseSource = -1;
            internal string BonesFound;
            private readonly Vector3[] _velocities = new Vector3[5], _angular = new Vector3[5];
            private int _count, _slot;
            private bool _hasPrevious;
            private Vector3 _previousPosition;
            private Quaternion _previousRotation;

            internal GrabInput(XRNode node, bool isHand, string label)
            { Node = node; IsHand = isHand; Label = label; }

            internal void ClearMotion() { _count = _slot = 0; _hasPrevious = false; }

            internal void RecordMotion()
            {
                float dt = Time.unscaledDeltaTime;
                if (!Reliable || PoseChanged || dt <= 0f || dt > 0.1f) ClearMotion();
                if (!Reliable) return;
                if (_hasPrevious)
                {
                    _velocities[_slot] = (Position - _previousPosition) / dt;
                    Quaternion delta = Rotation * Quaternion.Inverse(_previousRotation);
                    if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
                    delta.ToAngleAxis(out float angle, out Vector3 axis);
                    _angular[_slot] = angle > 0.001f ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
                    _slot = (_slot + 1) % 5;
                    _count = Mathf.Min(_count + 1, 5);
                }
                _previousPosition = Position;
                _previousRotation = Rotation;
                _hasPrevious = true;
            }

            internal Vector3 Velocity(bool angular = false)
            {
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < _count; i++) sum += angular ? _angular[i] : _velocities[i];
                return _count > 0 ? sum / _count : Vector3.zero;
            }
        }

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
            var skeletons = _rig.GetComponentsInChildren<OVRSkeleton>(true);
            OVRHand[] hands = _rig.GetComponentsInChildren<OVRHand>(true);
            // Respaldo: manos que no cuelgan del rig.
            if (hands.Length == 0) hands = FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (skeletons.Length == 0) skeletons = FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (OVRHand hand in hands)
            {
                GrabInput input = hand.GetHand() == OVRPlugin.Hand.HandLeft ? _inputs[0] : _inputs[1];
                if (input.Hand != null && input.Hand != hand && input.Hand.isActiveAndEnabled) continue;
                if (input.Hand != hand) input.StartConsumed = true;
                input.Hand = hand;
                input.Skeleton = hand.GetComponent<OVRSkeleton>();
                if (input.Skeleton == null) input.Skeleton = hand.GetComponentInChildren<OVRSkeleton>(true);
                // Algunos rigs guardan el esqueleto como hermano, no como hijo del OVRHand.
                if (input.Skeleton == null)
                    foreach (OVRSkeleton skeleton in skeletons)
                    {
                        var type = skeleton.GetSkeletonType();
                        bool left = type == OVRSkeleton.SkeletonType.HandLeft || type == OVRSkeleton.SkeletonType.XRHandLeft;
                        bool right = type == OVRSkeleton.SkeletonType.HandRight || type == OVRSkeleton.SkeletonType.XRHandRight;
                        if ((input.Node == XRNode.LeftHand && left) || (input.Node == XRNode.RightHand && right))
                        { input.Skeleton = skeleton; break; }
                    }
            }
        }

        private void Update()
        {
            if (!_focused) return;
            if (Time.unscaledTime >= _nextFind)
            { _nextFind = Time.unscaledTime + 1f; ResolveScene(); }
            foreach (GrabInput input in _inputs) Sample(input);
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
                if (!input.Started || input == _owner || input == _second) continue;
                // Un lado físico nunca cuenta dos veces (mano + mando emulado).
                if (_owner != null && (_owner.Node == input.Node || _owner.IsHand != input.IsHand)) continue;
                float distance = input.IsHand ? (input.FistMode ? PalmDistance : PinchDistance) : ControllerDistance;
                if (DistanceToBook(input.Position) < distance) Take(input);
            }
        }

        private void Sample(GrabInput input)
        {
            input.Started = false;
            input.Pressed = false;
            input.PoseChanged = false;
            if (input.IsHand) SampleHand(input);
            else
            {
                input.Device = InputDevices.GetDeviceAtXRNode(input.Node);
                Transform anchor = _rig == null ? null : input.Node == XRNode.LeftHand
                    ? _rig.leftControllerAnchor : _rig.rightControllerAnchor;
                input.Reliable = anchor != null && input.Device.isValid &&
                    (input.Device.characteristics & InputDeviceCharacteristics.Controller) != 0 &&
                    input.Device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked &&
                    input.Device.TryGetFeatureValue(CommonUsages.gripButton, out input.Pressed);
                if (input.Reliable)
                { input.Position = anchor.position; input.Rotation = anchor.rotation; }
                input.Started = input.Reliable && input.Pressed && !input.WasGrip;
                input.WasGrip = !input.Reliable || input.Pressed;
            }
            input.RecordMotion();
        }

        private void SampleHand(GrabInput input)
        {
            OVRHand hand = input.Hand;
            input.Reliable = hand != null && hand.isActiveAndEnabled && hand.IsTracked &&
                hand.IsDataHighConfidence && !hand.IsSystemGestureInProgress;
            input.Pinch = hand != null && hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            input.Fist = false;
            Transform fallback = hand != null ? hand.transform : null;
            input.PinchPosition = input.PalmPosition = fallback != null ? fallback.position : Vector3.zero;
            input.Rotation = fallback != null ? fallback.rotation : Quaternion.identity;

            OVRSkeleton skeleton = input.Skeleton;
            bool valid = skeleton != null && skeleton.isActiveAndEnabled && skeleton.IsDataValid &&
                skeleton.IsDataHighConfidence && skeleton.Bones != null;
            var type = skeleton != null ? skeleton.GetSkeletonType() : OVRSkeleton.SkeletonType.None;
            bool xr = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight;
            Transform wrist = null, thumb = null, index = null, middle = null, ring = null, little = null, palm = null, knuckle = null;
            // Buscar también en datos no fiables para que el diagnóstico distinga huesos ausentes de datos malos.
            if (skeleton != null && skeleton.Bones != null)
                foreach (OVRBone bone in skeleton.Bones)
                {
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_Wrist : OVRSkeleton.BoneId.Hand_WristRoot)) wrist = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_ThumbTip : OVRSkeleton.BoneId.Hand_ThumbTip)) thumb = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip)) index = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_MiddleTip : OVRSkeleton.BoneId.Hand_MiddleTip)) middle = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_RingTip : OVRSkeleton.BoneId.Hand_RingTip)) ring = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_LittleTip : OVRSkeleton.BoneId.Hand_PinkyTip)) little = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_MiddleProximal : OVRSkeleton.BoneId.Hand_Middle1)) knuckle = bone.Transform;
                    if (xr && bone.Id == OVRSkeleton.BoneId.XRHand_Palm) palm = bone.Transform;
                }
            input.PinchBones = valid && thumb != null && index != null && wrist != null;
            input.PalmBones = valid && wrist != null && (palm != null || knuckle != null) &&
                index != null && middle != null && ring != null && little != null;
            if (input.PinchBones) input.PinchPosition = (thumb.position + index.position) * 0.5f;
            if (input.PalmBones)
            {
                input.PalmPosition = palm != null ? palm.position : (wrist.position + knuckle.position) * 0.5f;
                float mean = (Vector3.Distance(index.position, input.PalmPosition) + Vector3.Distance(middle.position, input.PalmPosition) +
                    Vector3.Distance(ring.position, input.PalmPosition) + Vector3.Distance(little.position, input.PalmPosition)) * 0.25f;
                input.Fist = mean < 0.06f;
            }
            if ((input.Pinch && !input.LoggedPinch) || (input.Fist && !input.LoggedFist))
                input.BonesFound = "muñeca=" + (wrist != null) + ",pulgar=" + (thumb != null) + ",índice=" + (index != null) +
                ",medio=" + (middle != null) + ",anular=" + (ring != null) + ",meñique=" + (little != null) +
                ",palma=" + (palm != null) + ",nudillo=" + (knuckle != null);
            // Sin huesos se conserva el pellizco de OVRHand y su pose; no se inventa un puño geométrico.
            float now = Time.unscaledTime;
            if (input.Pinch) { if (input.PinchSince < 0f) input.PinchSince = now; } else input.PinchSince = -1f;
            if (input.Fist) { if (input.FistSince < 0f) input.FistSince = now; } else input.FistSince = -1f;
            if (!input.Pinch && !input.Fist) input.StartConsumed = false;
            // Inicio reciente del gesto, aunque haya empezado con confianza baja.
            bool pinchStart = input.Pinch && now - input.PinchSince <= OnsetWindowSeconds;
            bool fistStart = input.Fist && now - input.FistSince <= OnsetWindowSeconds;
            bool held = input == _owner || input == _second;
            if (!held) input.FistMode = fistStart && (!pinchStart || DistanceToBook(input.PinchPosition) >= PinchDistance);
            bool bones = input.FistMode ? input.PalmBones : input.PinchBones;
            if (bones) input.Rotation = wrist.rotation;
            input.Position = input.FistMode ? input.PalmPosition : input.PinchPosition;
            int source = (input.FistMode ? 2 : 0) + (bones ? 1 : 0);
            input.PoseChanged = source != input.PoseSource;
            input.PoseSource = source;
            input.Pressed = input.Pinch || input.Fist;
            input.Started = input.Reliable && !input.StartConsumed && (pinchStart || fistStart);
            if (input.Started) input.StartConsumed = true; // Un solo intento por gesto.
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
