using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Reading
{
    /// <summary>Agarre por pose con Meta Core y XR; no necesita interactores ni simulación física.</summary>
    // Después de Meta y del menú (100), antes de los pellizcos de FolioBook (200).
    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    public sealed class BookGrab : MonoBehaviour
    {
        private const float HandDistance = 0.04f;
        private const float ControllerDistance = 0.08f;
        private const float TrackingGraceSeconds = 0.3f;

        private sealed class GrabInput
        {
            internal readonly XRNode Node;
            internal readonly bool IsHand;
            internal readonly string Label;
            internal OVRHand Hand;
            internal OVRSkeleton Skeleton;
            internal InputDevice Device;
            internal bool WasPressed = true; // Al aparecer, exigir primero una muestra suelta.
            internal bool Pressed, Started, Reliable;
            internal Vector3 Position;
            internal Quaternion Rotation;

            internal GrabInput(XRNode node, bool isHand, string label)
            {
                Node = node;
                IsHand = isHand;
                Label = label;
            }

            internal void Bind(OVRHand hand)
            {
                if (Hand == hand && Skeleton != null) return;
                if (Hand != hand) WasPressed = true;
                Hand = hand;
                Skeleton = hand.GetComponent<OVRSkeleton>();
                if (Skeleton == null) Skeleton = hand.GetComponentInChildren<OVRSkeleton>(true);
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
        private GrabInput _owner;
        private Vector3 _positionOffset;
        private Quaternion _rotationOffset;
        private float _lostSince = -1f;
        private float _nextFind;
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
                Release();
                _book = FindFirstObjectByType<FolioBook>();
            }
            if (_rig == null) _rig = FindFirstObjectByType<OVRCameraRig>();
            if (_rig == null) return;
            foreach (OVRHand hand in FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (hand.GetHand() == OVRPlugin.Hand.HandLeft) _inputs[0].Bind(hand);
                else if (hand.GetHand() == OVRPlugin.Hand.HandRight) _inputs[1].Bind(hand);
            }
        }

        private void Update()
        {
            if (!_focused) return;
            if (Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 1f;
                ResolveScene();
            }
            foreach (GrabInput input in _inputs) Sample(input);
            if (_book == null || !_book.isActiveAndEnabled || _rig == null)
            {
                Release();
                return;
            }

            UpdateHeldPose();
            // ClosestPoint debe ver también los cambios de Transform de este cuadro,
            // incluso en libros sin Rigidbody. Solo sincroniza geometría; no simula física.
            bool hasNewPress = false;
            foreach (GrabInput input in _inputs) hasNewPress |= input.Started;
            if (hasNewPress) Physics.SyncTransforms();
            // Un flanco nuevo puede transferir el libro aunque el dueño anterior siga apretando.
            // Si llegan varios en el mismo cuadro, el último de este orden toma la propiedad.
            foreach (GrabInput input in _inputs)
            {
                if (!input.Started || !NearBook(input.Position,
                    input.IsHand ? HandDistance : ControllerDistance)) continue;
                Take(input);
            }
        }

        private void Sample(GrabInput input)
        {
            input.Pressed = false;
            if (input.IsHand)
            {
                input.Reliable = TryHandPose(input, out input.Position, out input.Rotation);
                if (input.Reliable)
                    input.Pressed = input.Hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            }
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
                {
                    input.Position = anchor.position;
                    input.Rotation = anchor.rotation;
                }
            }
            input.Started = input.Reliable && input.Pressed && !input.WasPressed;
            // Una recuperación del seguimiento con el gesto sostenido no inventa otro flanco.
            input.WasPressed = !input.Reliable || input.Pressed;
        }

        private static bool TryHandPose(GrabInput input, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            OVRHand hand = input.Hand;
            OVRSkeleton skeleton = input.Skeleton;
            if (hand == null || !hand.isActiveAndEnabled || !hand.IsTracked || !hand.IsDataHighConfidence ||
                hand.IsSystemGestureInProgress || skeleton == null || !skeleton.isActiveAndEnabled ||
                !skeleton.IsDataValid || !skeleton.IsDataHighConfidence || skeleton.Bones == null) return false;

            var type = skeleton.GetSkeletonType();
            bool xr = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight;
            OVRSkeleton.BoneId thumbId = xr ? OVRSkeleton.BoneId.XRHand_ThumbTip : OVRSkeleton.BoneId.Hand_ThumbTip;
            OVRSkeleton.BoneId indexId = xr ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip;
            OVRSkeleton.BoneId wristId = xr ? OVRSkeleton.BoneId.XRHand_Wrist : OVRSkeleton.BoneId.Hand_WristRoot;
            Transform thumb = null, index = null, wrist = null;
            foreach (OVRBone bone in skeleton.Bones)
            {
                if (bone.Id == thumbId) thumb = bone.Transform;
                else if (bone.Id == indexId) index = bone.Transform;
                else if (bone.Id == wristId) wrist = bone.Transform;
            }
            if (thumb == null || index == null || wrist == null) return false;
            position = (thumb.position + index.position) * 0.5f;
            rotation = wrist.rotation;
            return true;
        }

        private bool NearBook(Vector3 point, float distance)
        {
            BoxCollider collider = _book.bookCollider;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return false;
            return (collider.ClosestPoint(point) - point).sqrMagnitude < distance * distance;
        }

        private void Take(GrabInput input)
        {
            if (_owner != null) Release();
            _owner = input;
            _lostSince = -1f;
            Quaternion inverse = Quaternion.Inverse(input.Rotation);
            _positionOffset = inverse * (_book.transform.position - input.Position);
            _rotationOffset = inverse * _book.transform.rotation;
            _body = _book.GetComponent<Rigidbody>();
            KeepKinematic();
            _book.IsGrabbed = true;
            Debug.Log("[FolioXR] Agarre: tomado con " + input.Label);
            if (!input.IsHand && input.Device.TryGetHapticCapabilities(out HapticCapabilities capabilities) &&
                capabilities.supportsImpulse) input.Device.SendHapticImpulse(0, 0.25f, 0.035f);
        }

        private void UpdateHeldPose()
        {
            if (_owner == null) return;
            // Comprobar también el primer cuadro recuperado si ya venció la tolerancia.
            if (_lostSince >= 0f && Time.unscaledTime - _lostSince > TrackingGraceSeconds)
            {
                Release();
                return;
            }
            if (!_owner.Reliable)
            {
                if (_lostSince < 0f) _lostSince = Time.unscaledTime;
                if (!_owner.IsHand) Release();
                return; // Con poca confianza, conservar la última pose mundial del libro.
            }
            _lostSince = -1f;
            if (!_owner.Pressed) { Release(); return; }

            Vector3 position = _owner.Position + _owner.Rotation * _positionOffset;
            Quaternion rotation = _owner.Rotation * _rotationOffset;
            KeepKinematic();
            if (_body != null)
            {
                _body.position = position;
                _body.rotation = rotation;
            }
            _book.transform.SetPositionAndRotation(position, rotation);
        }

        private void KeepKinematic()
        {
            if (_body == null) return;
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.isKinematic = true;
            }
            _body.useGravity = false;
            // El Transform se actualiza por cuadro; la interpolación física no debe sobrescribirlo.
            _body.interpolation = RigidbodyInterpolation.None;
        }

        private void Release()
        {
            if (_owner == null) return;
            KeepKinematic(); // Al soltar no restaurar gravedad, velocidades ni simulación.
            if (_book != null) _book.IsGrabbed = false;
            _owner = null;
            _body = null;
            _lostSince = -1f;
            Debug.Log("[FolioXR] Agarre: soltado");
        }

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            if (!focused) Release();
            foreach (GrabInput input in _inputs) input.WasPressed = true;
        }

        private void OnApplicationPause(bool paused) { OnApplicationFocus(!paused); }

        private void OnDisable()
        {
            Release();
            foreach (GrabInput input in _inputs) input.WasPressed = true;
        }
    }
}
