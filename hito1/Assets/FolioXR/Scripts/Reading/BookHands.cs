using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Reading
{
    /// <summary>
    /// Pasar página, abrir y cerrar el libro con el dedo, como uno de papel (pedido del usuario, 13 nov 2026).
    /// Se apoya el índice (sin pellizcar: el pellizco sobre el libro lo agarra) en la mitad exterior de una
    /// hoja y se desliza hacia el lomo. Desde la hoja derecha pasa la hoja a la izquierda y al revés; pasar
    /// hacia atrás desde la primera página cierra el libro; deslizar por la tapa cerrada lo abre.
    /// </summary>
    internal sealed class BookHands : MonoBehaviour
    {
        private const float Touch = 0.035f;       // Distancia máxima del dedo a la hoja, en metros.
        private const float Travel = 0.3f;        // Fracción del ancho de página que hay que recorrer.
        private const float Cooldown = 0.6f;

        private FolioBook _book;
        private readonly OVRHand[] _hands = new OVRHand[2];
        private readonly OVRSkeleton[] _skeletons = new OVRSkeleton[2];
        private readonly int[] _side = new int[2];        // Hoja donde empezó el gesto (-1, +1) o 0.
        private readonly float[] _startX = new float[2];
        private float _nextFind, _nextTurn;

        private void Awake() { _book = GetComponent<FolioBook>(); }

        private void Update()
        {
            if (_book == null || _book.leftPage == null) return;
            if (Time.unscaledTime >= _nextFind) { _nextFind = Time.unscaledTime + 1.5f; FindHands(); }
            bool idle = _book.InputPaused || _book.IsGrabbed || Time.time < _nextTurn ||
                        (_book.Body != null && _book.Body.Folding);
            for (int i = 0; i < 2; i++)
            {
                if (idle || !Tip(i, out Vector3 world)) { _side[i] = 0; continue; }
                Track(i, world);
            }
        }

        private void Track(int i, Vector3 world)
        {
            Transform root = _book.leftPage.transform.parent != null ? _book.leftPage.transform.parent : _book.transform;
            Vector3 p = root.InverseTransformPoint(world);
            float scale = Mathf.Max(0.0001f, root.lossyScale.x);
            Vector3 page = _book.leftPage.transform.localScale;
            float w = page.x, h = page.y, g = _book.gutter * 0.5f;
            bool closed = _book.Closed;
            // Libro cerrado: la tapa queda más cerca de quien lee, sobre la mitad que no se mueve.
            float surface = closed ? -BookBody.BackFace : 0f;
            bool touching = Mathf.Abs(p.y) < h * 0.5f + 0.02f / scale && Mathf.Abs(p.z - surface) < Touch / scale &&
                            Mathf.Abs(p.x) < g + w + 0.03f / scale;
            if (!touching) { _side[i] = 0; return; }
            int side = p.x >= 0f ? 1 : -1;
            if (_side[i] == 0)
            {
                // Empieza solo en la mitad exterior de una hoja (el borde libre), no junto al lomo.
                if (Mathf.Abs(p.x) < g + w * 0.5f) return;
                if (closed && side != _book.Body.RestSide) return;
                _side[i] = side;
                _startX[i] = p.x;
                return;
            }
            float moved = (_startX[i] - p.x) * _side[i];   // Positivo = hacia el lomo.
            if (moved < -0.02f / scale) { _startX[i] = p.x; return; }   // Se alejó: el gesto vuelve a empezar ahí.
            if (moved < w * Travel) return;
            int from = _side[i];
            _side[i] = 0;
            _nextTurn = Time.time + Cooldown;
            if (closed) { _book.OpenBook(); return; }
            // La hoja derecha cae a la izquierda: en lectura normal es avanzar; en manga, retroceder.
            bool forward = (from > 0) != _book.RightToLeft;
            Debug.Log("[FolioXR] Libro: hoja " + (from > 0 ? "derecha" : "izquierda") + " pasada con el dedo.");
            if (forward) _book.NextSpread(); else _book.PreviousSpread();
        }

        private bool Tip(int i, out Vector3 position)
        {
            position = Vector3.zero;
            OVRHand hand = _hands[i];
            if (hand == null || !hand.isActiveAndEnabled || !hand.IsTracked || !hand.IsDataHighConfidence ||
                hand.IsSystemGestureInProgress) return false;
            if (hand.GetFingerIsPinching(OVRHand.HandFinger.Index)) return false;
            XRNode node = hand.GetHand() == OVRPlugin.Hand.HandLeft ? XRNode.LeftHand : XRNode.RightHand;
            if (FolioXR.Tools.ToolRegistry.HoldsHand(node) || FolioXR.Tools.ToolRegistry.HoldsController(node)) return false;
            OVRSkeleton skeleton = _skeletons[i];
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null) return false;
            OVRSkeleton.SkeletonType type = skeleton.GetSkeletonType();
            bool xr = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight;
            OVRSkeleton.BoneId tip = xr ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip;
            foreach (OVRBone bone in skeleton.Bones)
                if (bone != null && bone.Transform != null && bone.Id == tip)
                {
                    position = bone.Transform.position;
                    return true;
                }
            return false;
        }

        private void FindHands()
        {
            if (_hands[0] != null && _hands[1] != null && _hands[0].isActiveAndEnabled && _hands[1].isActiveAndEnabled) return;
            int n = 0;
            _hands[0] = _hands[1] = null;
            foreach (OVRHand hand in FindObjectsByType<OVRHand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (n >= 2) break;
                _hands[n] = hand;
                _skeletons[n] = hand.GetComponent<OVRSkeleton>();
                n++;
            }
        }
    }
}
