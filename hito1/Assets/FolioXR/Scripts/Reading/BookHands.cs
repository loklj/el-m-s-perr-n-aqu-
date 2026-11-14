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
        private readonly float[] _startX = new float[2];   // f donde empezó el gesto.
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

        /// <summary>
        /// Sigue el dedo sobre una hoja. Las mitades del libro abren en V y las hojas se curvan hacia el lomo
        /// (BookBody), así que todo se mide en el espacio de cada página: f = distancia al lomo (0 en el lomo,
        /// 1 en el borde libre) y la distancia a la hoja ya curvada.
        /// </summary>
        private void Track(int i, Vector3 world)
        {
            bool closed = _book.Closed;
            int side = 0;
            float f = 0f;
            if (closed)
            {
                // Libro cerrado: la portada queda delante de la hoja de la mitad que no se movió.
                int rest = _book.Body != null ? _book.Body.RestSide : 1;
                if (OnPage(rest, world, _book.Body != null ? -_book.Body.CoverOuterDepth : 0f, out f)) side = rest;
            }
            else
            {
                for (int s = -1; s <= 1 && side == 0; s += 2)
                    if (OnPage(s, world, float.NaN, out float fs)) { side = s; f = fs; }
            }
            if (side == 0)
            {
                _side[i] = 0;
                return;
            }
            if (_side[i] == 0)
            {
                // Empieza solo en la mitad exterior de una hoja (el borde libre), no junto al lomo.
                if (f < 0.5f) return;
                _side[i] = side;
                _startX[i] = f;
                return;
            }
            // Hacia el lomo = f baja; si el dedo ya cruzó a la otra hoja, se suma lo recorrido en ella.
            float moved = side == _side[i] ? _startX[i] - f : _startX[i] + f;
            if (side == _side[i] && moved < -0.06f) { _startX[i] = f; return; }   // Se alejó: el gesto vuelve a empezar ahí.
            if (moved < Travel) return;
            int from = _side[i];
            _side[i] = 0;
            _nextTurn = Time.time + Cooldown;
            if (closed) { _book.OpenBook(); return; }
            // La hoja derecha cae a la izquierda: en lectura normal es avanzar; en manga, retroceder.
            bool forward = (from > 0) != _book.RightToLeft;
            Debug.Log("[FolioXR] Libro: hoja " + (from > 0 ? "derecha" : "izquierda") + " pasada con el dedo.");
            if (forward) _book.NextSpread(); else _book.PreviousSpread();
        }

        /// <summary>
        /// ¿El dedo toca la hoja de ese lado? surface: profundidad fija (libro cerrado) o NaN para la hoja curva.
        /// Devuelve f = distancia al lomo en fracciones del ancho de página.
        /// </summary>
        private bool OnPage(int side, Vector3 world, float surface, out float f)
        {
            f = 0f;
            Renderer page = side < 0 ? _book.leftPage : _book.rightPage;
            if (page == null) return false;
            Transform t = page.transform;
            Vector3 p = t.InverseTransformPoint(world);       // x, y en fracciones de la página; z en metros a escala 1.
            Vector3 scale = t.lossyScale;
            float meters = Mathf.Max(1e-4f, Mathf.Abs(scale.z));
            float marginX = 0.03f / Mathf.Max(1e-4f, Mathf.Abs(scale.x)), marginY = 0.02f / Mathf.Max(1e-4f, Mathf.Abs(scale.y));
            if (Mathf.Abs(p.y) > 0.5f + marginY || p.x < -0.5f - marginX || p.x > 0.5f + marginX) return false;
            f = side < 0 ? 0.5f - p.x : p.x + 0.5f;
            float depth = float.IsNaN(surface) ? (_book.Body != null ? _book.Body.PageSurface(side, Mathf.Clamp01(f)) : 0f) : surface;
            return Mathf.Abs((p.z - depth) * meters) < Touch;
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
