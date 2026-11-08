using UnityEngine;

namespace FolioXR.Menu
{
    /// <summary>Huesos de Meta Core; sin Interaction SDK, física ni gesto reservado del sistema.</summary>
    internal sealed class MenuHandInput
    {
        internal OVRHand Hand { get; private set; }
        private OVRSkeleton _skeleton;
        private int _stage; // 0: esperar abierta, 1: armada, 2: puño, 3: puño confirmado
        private float _fistSince, _cooldownUntil, _nextLog;
        internal bool PalmUp { get; private set; }

        internal void Bind(OVRHand hand)
        {
            if (Hand == hand && _skeleton != null) return;
            Hand = hand;
            _skeleton = hand != null ? hand.GetComponent<OVRSkeleton>() : null;
            if (_skeleton == null && hand != null) _skeleton = hand.GetComponentInChildren<OVRSkeleton>(true);
            Reset();
        }

        internal bool Reliable => Hand != null && Hand.isActiveAndEnabled && Hand.IsTracked &&
            Hand.IsDataHighConfidence && !Hand.IsSystemGestureInProgress &&
            _skeleton != null && _skeleton.isActiveAndEnabled && _skeleton.IsDataValid && _skeleton.IsDataHighConfidence;

        internal bool TryBone(OVRSkeleton.BoneId id, out Vector3 position)
        {
            position = default;
            if (!Reliable || _skeleton.Bones == null) return false;
            // Core 207 permite ambas versiones del esqueleto con OpenXR como backend.
            var type = _skeleton.GetSkeletonType();
            if (type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight)
                id = OpenXrBone(id);
            foreach (OVRBone bone in _skeleton.Bones)
                if (bone.Id == id && bone.Transform != null)
                {
                    position = bone.Transform.position;
                    return true;
                }
            return false;
        }

        private static OVRSkeleton.BoneId OpenXrBone(OVRSkeleton.BoneId id)
        {
            switch (id)
            {
                case OVRSkeleton.BoneId.Hand_WristRoot: return OVRSkeleton.BoneId.XRHand_Wrist;
                case OVRSkeleton.BoneId.Hand_Index1: return OVRSkeleton.BoneId.XRHand_IndexProximal;
                case OVRSkeleton.BoneId.Hand_Middle1: return OVRSkeleton.BoneId.XRHand_MiddleProximal;
                case OVRSkeleton.BoneId.Hand_Pinky1: return OVRSkeleton.BoneId.XRHand_LittleProximal;
                case OVRSkeleton.BoneId.Hand_IndexTip: return OVRSkeleton.BoneId.XRHand_IndexTip;
                case OVRSkeleton.BoneId.Hand_MiddleTip: return OVRSkeleton.BoneId.XRHand_MiddleTip;
                case OVRSkeleton.BoneId.Hand_RingTip: return OVRSkeleton.BoneId.XRHand_RingTip;
                case OVRSkeleton.BoneId.Hand_PinkyTip: return OVRSkeleton.BoneId.XRHand_LittleTip;
                default: return id;
            }
        }

        internal bool UpdateGesture()
        {
            float now = Time.unscaledTime;
            if (!TryBone(OVRSkeleton.BoneId.Hand_WristRoot, out Vector3 wrist) ||
                !TryBone(OVRSkeleton.BoneId.Hand_Index1, out Vector3 index) ||
                !TryBone(OVRSkeleton.BoneId.Hand_Middle1, out Vector3 middle) ||
                !TryBone(OVRSkeleton.BoneId.Hand_Pinky1, out Vector3 pinky))
            {
                Reset();
                return false;
            }
            // Mano IZQUIERDA: índice × meñique apunta hacia la palma, no hacia el dorso.
            Vector3 normal = Vector3.Cross(index - wrist, pinky - wrist);
            if (normal.sqrMagnitude < 0.00000001f) { Reset(); return false; }
            float angle = Vector3.Angle(normal, Vector3.up);
            if (now >= _nextLog)
            {
                Debug.Log("[FolioXR] Menú: ángulo palma izquierda=" + angle.ToString("F1") + "°");
                // Cada segundo solo cerca de la palma arriba; si no, cada 10 s para no llenar el registro.
                _nextLog = now + (angle < 60f ? 1f : 10f);
            }
            PalmUp = angle < 45f;
            if (!PalmUp || now < _cooldownUntil) { _stage = 0; return false; }
            if (!TryBone(OVRSkeleton.BoneId.Hand_IndexTip, out Vector3 a) ||
                !TryBone(OVRSkeleton.BoneId.Hand_MiddleTip, out Vector3 b) ||
                !TryBone(OVRSkeleton.BoneId.Hand_RingTip, out Vector3 c) ||
                !TryBone(OVRSkeleton.BoneId.Hand_PinkyTip, out Vector3 d))
            {
                _stage = 0;
                return false;
            }
            Vector3 center = (wrist + middle) * 0.5f;
            float distance = (Vector3.Distance(a, center) + Vector3.Distance(b, center) +
                Vector3.Distance(c, center) + Vector3.Distance(d, center)) * 0.25f;
            if (_stage == 0)
            {
                if (distance > 0.08f) _stage = 1;
            }
            else if (_stage == 1)
            {
                if (distance < 0.055f) { _stage = 2; _fistSince = now; }
            }
            else
            {
                if (now - _fistSince > 1.2f) { _stage = 0; return false; }
                if (_stage == 2)
                {
                    // Debe permanecer bajo 5,5 cm durante 0,15 s continuos.
                    if (distance >= 0.055f) { _stage = 1; return false; }
                    if (now - _fistSince >= 0.15f) _stage = 3;
                }
                else if (distance > 0.08f)
                {
                    _cooldownUntil = now + 0.8f;
                    _stage = 0;
                    return true;
                }
            }
            return false;
        }

        internal void Reset()
        {
            _stage = 0;
            PalmUp = false;
        }
    }
}
