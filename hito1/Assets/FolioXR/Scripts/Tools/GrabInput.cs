using FolioXR.Locomotion;
using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Tools
{
    internal sealed class GrabInput
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
        {
            Node = node;
            IsHand = isHand;
            Label = label;
        }

        internal void ClearMotion()
        {
            _count = _slot = 0;
            _hasPrevious = false;
        }

        internal void RecordMotion()
        {
            float dt = Time.unscaledDeltaTime;
            if (!Reliable || PoseChanged || dt <= 0f || dt > 0.1f)
                ClearMotion();
            if (!Reliable)
                return;
            if (_hasPrevious)
            {
                _velocities[_slot] = (Position - _previousPosition) / dt;
                Quaternion delta = Rotation * Quaternion.Inverse(_previousRotation);
                if (delta.w < 0f)
                    delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                _angular[_slot] = angle > 0.001f ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
                _slot = (_slot + 1) % 5;
                _count = Mathf.Min(_count + 1, 5);
            }
            _previousPosition = Position;
            _previousRotation = Rotation;
            _hasPrevious = true;
        }

        internal void TransformFrame(Vector3 oldOrigin, Vector3 newOrigin, Quaternion rotation)
        {
            Position = newOrigin + rotation * (Position - oldOrigin);
            PinchPosition = newOrigin + rotation * (PinchPosition - oldOrigin);
            PalmPosition = newOrigin + rotation * (PalmPosition - oldOrigin);
            Rotation = rotation * Rotation;
            _previousPosition = newOrigin + rotation * (_previousPosition - oldOrigin);
            _previousRotation = rotation * _previousRotation;
            for (int i = 0; i < _count; i++)
            {
                _velocities[i] = rotation * _velocities[i];
                _angular[i] = rotation * _angular[i];
            }
        }

        internal Vector3 Velocity(bool angular = false)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < _count; i++)
                sum += angular ? _angular[i] : _velocities[i];
            return _count > 0 ? sum / _count : Vector3.zero;
        }

        private const float OnsetWindowSeconds = 0.3f;
        internal Transform Wrist, IndexTip;
        internal void Sample(OVRCameraRig rig, bool held, System.Func<Vector3, float> distance, float pinchDistance)
        {
            Sample(this, rig, held, distance, pinchDistance);
        }

        private static void Sample(GrabInput input, OVRCameraRig rig, bool held, System.Func<Vector3, float> distance,
                                   float pinchDistance)
        {
            input.Started = false;
            input.Pressed = false;
            input.PoseChanged = false;
            if (input.IsHand)
                SampleHand(input, held, distance, pinchDistance);
            else
            {
                input.Device = InputDevices.GetDeviceAtXRNode(input.Node);
                Transform anchor = rig == null                     ? null
                                   : input.Node == XRNode.LeftHand ? rig.leftControllerAnchor
                                                                   : rig.rightControllerAnchor;
                input.Reliable = anchor != null && input.Device.isValid &&
                                 (input.Device.characteristics & InputDeviceCharacteristics.Controller) != 0 &&
                                 input.Device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked &&
                                 input.Device.TryGetFeatureValue(CommonUsages.gripButton, out input.Pressed);
                if (input.Reliable)
                {
                    input.Position = anchor.position;
                    input.Rotation = anchor.rotation;
                }
                input.Started = input.Reliable && input.Pressed && !input.WasGrip;
                input.WasGrip = !input.Reliable || input.Pressed;
            }
            input.RecordMotion();
        }

        private static void SampleHand(GrabInput input, bool held, System.Func<Vector3, float> distance,
                                       float pinchDistance)
        {
            OVRHand hand = input.Hand;
            input.Reliable = hand != null && hand.isActiveAndEnabled && hand.IsTracked && hand.IsDataHighConfidence &&
                             !hand.IsSystemGestureInProgress;
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
            Transform wrist = null, thumb = null, index = null, middle = null, ring = null, little = null, palm = null,
                      knuckle = null;
            // Buscar también en datos no fiables para que el diagnóstico distinga huesos ausentes de datos malos.
            if (skeleton != null && skeleton.Bones != null)
                foreach (OVRBone bone in skeleton.Bones)
                {
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_Wrist : OVRSkeleton.BoneId.Hand_WristRoot))
                        wrist = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_ThumbTip : OVRSkeleton.BoneId.Hand_ThumbTip))
                        thumb = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip))
                        index = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_MiddleTip : OVRSkeleton.BoneId.Hand_MiddleTip))
                        middle = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_RingTip : OVRSkeleton.BoneId.Hand_RingTip))
                        ring = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_LittleTip : OVRSkeleton.BoneId.Hand_PinkyTip))
                        little = bone.Transform;
                    if (bone.Id == (xr ? OVRSkeleton.BoneId.XRHand_MiddleProximal : OVRSkeleton.BoneId.Hand_Middle1))
                        knuckle = bone.Transform;
                    if (xr && bone.Id == OVRSkeleton.BoneId.XRHand_Palm)
                        palm = bone.Transform;
                }
            input.Wrist = valid ? wrist : null;
            input.IndexTip = valid ? index : null;
            input.PinchBones = valid && thumb != null && index != null && wrist != null;
            input.PalmBones = valid && wrist != null && (palm != null || knuckle != null) && index != null &&
                              middle != null && ring != null && little != null;
            if (input.PinchBones)
                input.PinchPosition = (thumb.position + index.position) * 0.5f;
            if (input.PalmBones)
            {
                input.PalmPosition = palm != null ? palm.position : (wrist.position + knuckle.position) * 0.5f;
                float mean = (Vector3.Distance(index.position, input.PalmPosition) +
                              Vector3.Distance(middle.position, input.PalmPosition) +
                              Vector3.Distance(ring.position, input.PalmPosition) +
                              Vector3.Distance(little.position, input.PalmPosition)) *
                             0.25f;
                input.Fist = mean < 0.06f;
            }
            if ((input.Pinch && !input.LoggedPinch) || (input.Fist && !input.LoggedFist))
                input.BonesFound = "muñeca=" + (wrist != null) + ",pulgar=" + (thumb != null) +
                                   ",índice=" + (index != null) + ",medio=" + (middle != null) +
                                   ",anular=" + (ring != null) + ",meñique=" + (little != null) +
                                   ",palma=" + (palm != null) + ",nudillo=" + (knuckle != null);
            // Sin huesos se conserva el pellizco de OVRHand y su pose; no se inventa un puño geométrico.
            float now = Time.unscaledTime;
            if (input.Pinch)
            {
                if (input.PinchSince < 0f)
                    input.PinchSince = now;
            }
            else
                input.PinchSince = -1f;
            if (input.Fist)
            {
                if (input.FistSince < 0f)
                    input.FistSince = now;
            }
            else
                input.FistSince = -1f;
            if (!input.Pinch && !input.Fist)
                input.StartConsumed = false;
            // Inicio reciente del gesto, aunque haya empezado con confianza baja.
            bool pinchStart = input.Pinch && now - input.PinchSince <= OnsetWindowSeconds;
            bool fistStart = input.Fist && now - input.FistSince <= OnsetWindowSeconds;
            // El medio reservado para teletransporte no inicia un agarre por puño.
            if (!held && FolioLocomotion.ReservesMiddlePinch(hand))
            {
                input.Fist = fistStart = false;
            }
            if (!held)
                input.FistMode = fistStart && (!pinchStart || distance(input.PinchPosition) >= pinchDistance);
            bool bones = input.FistMode ? input.PalmBones : input.PinchBones;
            if (bones)
                input.Rotation = wrist.rotation;
            input.Position = input.FistMode ? input.PalmPosition : input.PinchPosition;
            int source = (input.FistMode ? 2 : 0) + (bones ? 1 : 0);
            input.PoseChanged = source != input.PoseSource;
            input.PoseSource = source;
            input.Pressed = input.Pinch || input.Fist;
            input.Started = input.Reliable && !input.StartConsumed && (pinchStart || fistStart);
            if (input.Started)
                input.StartConsumed = true; // Un solo intento por gesto.
        }

        internal static void BindHands(GrabInput[] inputs, OVRCameraRig rig)
        {
            var skeletons = rig.GetComponentsInChildren<OVRSkeleton>(true);
            OVRHand[] hands = rig.GetComponentsInChildren<OVRHand>(true);
            // Respaldo: manos que no cuelgan del rig.
            if (hands.Length == 0)
                hands = Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (skeletons.Length == 0)
                skeletons =
                    Object.FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (OVRHand hand in hands)
            {
                GrabInput input = hand.GetHand() == OVRPlugin.Hand.HandLeft ? inputs[0] : inputs[1];
                if (input.Hand != null && input.Hand != hand && input.Hand.isActiveAndEnabled)
                    continue;
                if (input.Hand != hand)
                    input.StartConsumed = true;
                input.Hand = hand;
                input.Skeleton = hand.GetComponent<OVRSkeleton>();
                if (input.Skeleton == null)
                    input.Skeleton = hand.GetComponentInChildren<OVRSkeleton>(true);
                // Algunos rigs guardan el esqueleto como hermano, no como hijo del OVRHand.
                if (input.Skeleton == null)
                    foreach (OVRSkeleton skeleton in skeletons)
                    {
                        var type = skeleton.GetSkeletonType();
                        bool left =
                            type == OVRSkeleton.SkeletonType.HandLeft || type == OVRSkeleton.SkeletonType.XRHandLeft;
                        bool right =
                            type == OVRSkeleton.SkeletonType.HandRight || type == OVRSkeleton.SkeletonType.XRHandRight;
                        if ((input.Node == XRNode.LeftHand && left) || (input.Node == XRNode.RightHand && right))
                        {
                            input.Skeleton = skeleton;
                            break;
                        }
                    }
            }
        }
    }
}
