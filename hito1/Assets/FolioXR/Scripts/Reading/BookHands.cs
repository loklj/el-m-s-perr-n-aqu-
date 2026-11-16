using FolioXR.Accessibility;
using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Reading
{
    /// <summary>
    /// Pasar página, abrir y cerrar el libro con la mano, como uno de papel (pedidos del usuario, 13 y 15 nov 2026).
    /// Se apoya el índice (sin pellizcar: el pellizco sobre el libro lo agarra) o la punta del mando en la mitad
    /// exterior de una hoja y se lleva hacia el lomo: la hoja se levanta y sigue a la mano hasta el otro lado.
    /// Si se suelta antes, termina sola de pasar o vuelve, según dónde quedó y con qué impulso. Desde la primera
    /// página, llevar la hoja hacia atrás cierra la tapa; con el libro cerrado, la tapa se levanta igual.
    /// La otra mano puede estar sosteniendo el libro.
    /// Con «Paso de página: rápido» (o «Reducir movimiento») vale el gesto corto de antes: deslizar un tercio
    /// de la hoja dispara el paso entero.
    /// </summary>
    internal sealed class BookHands : MonoBehaviour
    {
        private const float Touch = 0.035f;         // Distancia máxima a la hoja para empezar, en metros.
        private const float QuickTravel = 0.3f;     // Paso rápido: fracción del ancho de página que hay que recorrer.
        private const float StartTravel = 0.018f;   // Hoja en la mano: metros hacia el lomo para que se levante.
        private const float Cooldown = 0.6f;
        private const float Grace = 0.25f;          // Seguimiento perdido: cuánto espera la hoja antes de soltarse.

        private enum Drag { None, Page, Cover }

        private FolioBook _book;
        private BookGrab _grab;
        private OVRCameraRig _rig;
        private readonly OVRHand[] _hands = new OVRHand[2];            // 0 = izquierda, 1 = derecha.
        private readonly OVRSkeleton[] _skeletons = new OVRSkeleton[2];
        private readonly int[] _side = new int[2];        // Hoja donde empezó el gesto (-1, +1) o 0.
        private readonly float[] _startX = new float[2];   // f donde empezó el gesto.
        private readonly bool[] _mustLift = new bool[2];   // Acaba de soltar una hoja: debe separarse antes de tomar otra.
        private float _nextFind, _nextTurn;

        // Hoja o tapa en la mano.
        private Drag _drag;
        private int _dragHand, _dragSide;
        private float _along, _shown, _velocity, _lostSince = -1f;

        private void Awake() { _book = GetComponent<FolioBook>(); }

        private void Update()
        {
            if (_book == null || _book.leftPage == null || _book.rightPage == null) return;
            if (Time.unscaledTime >= _nextFind) { _nextFind = Time.unscaledTime + 1.5f; FindHands(); }
            if (_drag != Drag.None) { Follow(); return; }
            // Con el libro agarrado solo pasa la hoja la mano que no lo sostiene (Tip la distingue con BookGrab).
            bool idle = _book.InputPaused || Time.time < _nextTurn || _book.HandTurning ||
                        (_book.Body != null && _book.Body.Folding) || (_grab == null && _book.IsGrabbed);
            for (int i = 0; i < 2; i++)
            {
                if (idle || !Tip(i, out Vector3 world)) { _side[i] = 0; _mustLift[i] = false; continue; }
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
                _mustLift[i] = false;
                return;
            }
            if (_mustLift[i]) return;
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
            bool follow = !FolioBook.QuickTurn && !FolioAccessibility.ReduceMotion;
            float needed = follow ? StartTravel / Mathf.Max(0.02f, PageWidthMeters()) : QuickTravel;
            if (moved < needed) return;
            int from = _side[i];
            _side[i] = 0;
            if (follow) { Take(i, from, closed); return; }
            _nextTurn = Time.time + Cooldown;
            if (closed) { _book.OpenBook(); return; }
            // La hoja derecha cae a la izquierda: en lectura normal es avanzar; en manga, retroceder.
            bool forward = (from > 0) != _book.RightToLeft;
            Debug.Log("[FolioXR] Libro: hoja " + (from > 0 ? "derecha" : "izquierda") + " pasada con el dedo.");
            if (forward) _book.NextSpread(); else _book.PreviousSpread();
        }

        // ───────────── Hoja (o tapa) en la mano ─────────────

        private float PageWidthMeters() => Mathf.Abs(_book.rightPage.transform.lossyScale.x);

        /// <summary>La mano toma la hoja de ese lado (o la tapa) a la distancia del lomo donde apoyó el dedo.</summary>
        private void Take(int i, int from, bool closed)
        {
            float width = _book.rightPage.transform.localScale.x;
            _along = Mathf.Clamp(_startX[i], 0.5f, 1f) * width;
            _velocity = 0f;
            _lostSince = -1f;
            _dragHand = i;
            BookBody body = _book.Body;
            if (closed)
            {
                if (body == null) { _book.OpenBook(); _nextTurn = Time.time + Cooldown; return; }
                _drag = Drag.Cover;
                _dragSide = -body.RestSide;   // La mitad de la portada vuelve a su lado.
                _shown = 1f;
                body.SetManualFold(_shown);
                Debug.Log("[FolioXR] Libro: tapa en la mano (abrir).");
                return;
            }
            int target = _book.HandTurnTarget(from, out bool closes);
            if (target >= 0)
            {
                if (!_book.BeginHandTurn(from)) return;
                _drag = Drag.Page;
                _dragSide = from;
                _shown = 0f;
                Debug.Log("[FolioXR] Libro: hoja " + (from > 0 ? "derecha" : "izquierda") + " en la mano.");
            }
            else if (closes && body != null)
            {
                _drag = Drag.Cover;
                _dragSide = from;
                _shown = 0f;
                body.SetManualFold(_shown);
                Debug.Log("[FolioXR] Libro: tapa en la mano (cerrar).");
            }
            else _nextTurn = Time.time + 0.3f;   // No hay adónde pasar (última página).
        }

        private void Follow()
        {
            BookBody body = _book.Body;
            // Otra parte del programa terminó el gesto (falló el dibujo de la página, se abrió otro libro…).
            if ((_drag == Drag.Page && !_book.HandTurnHeld) || (_drag == Drag.Cover && (body == null || !body.Manual)))
            {
                Finish();
                return;
            }
            Transform root = _book.rightPage.transform.parent;
            float width = _book.rightPage.transform.localScale.x, height = _book.rightPage.transform.localScale.y;
            float perMeter = 1f / Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.x));   // Metros a unidades del libro.
            bool have = Tip(_dragHand, out Vector3 world) && !_book.InputPaused;
            Vector3 p = have ? root.InverseTransformPoint(world) : Vector3.zero;
            bool inside = false;
            if (have)
            {
                // Zona de contacto: sobre el libro y no más alto que el arco que recorre el punto tomado.
                float margin = 0.07f * perMeter, lift = -p.z;
                inside = Mathf.Abs(p.y) <= height * 0.5f + margin && Mathf.Abs(p.x) <= width + margin &&
                         lift >= -0.05f * perMeter && lift <= PageTurnShape.ArcHeight(_along, p.x) + 0.09f * perMeter;
            }
            if (!inside)
            {
                if (_lostSince < 0f) _lostSince = Time.unscaledTime;
                // Sin seguimiento un instante, la hoja espera; con la mano ya lejos del libro, se suelta enseguida.
                if (Time.unscaledTime - _lostSince < (have ? 0.08f : Grace)) return;
                Release();
                return;
            }
            _lostSince = -1f;
            float tilt = body != null ? body.TiltRadians : 0f;
            float dt = Mathf.Max(1e-4f, Time.deltaTime), before = _shown;
            if (_drag == Drag.Page)
            {
                float t = PageTurnShape.Solve(p.x, _dragSide > 0, width, _book.gutter * 0.5f, tilt,
                                              body != null ? body.CurlDepth(_dragSide) : 0f,
                                              body != null ? body.CurlDepth(-_dragSide) : 0f,
                                              body != null ? body.CurlReach : 0f, _along);
                _shown = t;
                _book.DragHandTurn(t);
            }
            else
            {
                float s = PageTurnShape.FoldFromX(p.x, _dragSide, _along, tilt);
                _shown = Mathf.Lerp(_shown, s, 1f - Mathf.Exp(-dt / 0.045f));
                body.SetManualFold(_shown);
            }
            _velocity = Mathf.Lerp(_velocity, (_shown - before) / dt, 0.35f);
        }

        private void Release()
        {
            if (_drag == Drag.Page) _book.EndHandTurn(_velocity);
            else if (_book.Body != null && _book.Body.Manual) _book.EndHandFold(_shown + _velocity * 0.2f > 0.5f);
            Finish();
        }

        private void Finish()
        {
            _mustLift[_dragHand] = true;
            _side[0] = _side[1] = 0;
            _drag = Drag.None;
            _lostSince = -1f;
            _nextTurn = Time.time + 0.25f;
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

        /// <summary>Punta que toca la hoja: unos centímetros delante del mando, o el índice de la mano, sin apretar nada.</summary>
        private bool Tip(int i, out Vector3 position) => Pointer(i, out position, out bool pressed) && !pressed;

        /// <summary>
        /// Punta de esa mano (0 izquierda, 1 derecha) o de su mando, y si está apretando: pellizco del índice,
        /// gatillo o agarre. Sin apretar pasa hojas; apretando toma los círculos y la X del libro (BookControls).
        /// La mano que sostiene el libro o una herramienta no cuenta.
        /// </summary>
        internal bool Pointer(int i, out Vector3 position, out bool pressed)
        {
            position = Vector3.zero;
            pressed = false;
            XRNode node = i == 0 ? XRNode.LeftHand : XRNode.RightHand;
            if (FolioXR.Tools.ToolRegistry.HoldsHand(node) || FolioXR.Tools.ToolRegistry.HoldsController(node)) return false;
            if (_grab != null && _grab.HoldsSide(node)) return false;   // Esa mano sostiene el libro: actúa la otra.
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid && (device.characteristics & InputDeviceCharacteristics.Controller) != 0 &&
                device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
            {
                Transform anchor = _rig == null ? null : node == XRNode.LeftHand ? _rig.leftControllerAnchor : _rig.rightControllerAnchor;
                if (anchor == null) return false;
                pressed = (device.TryGetFeatureValue(CommonUsages.gripButton, out bool grip) && grip) ||
                          (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger) && trigger);
                position = anchor.position + anchor.forward * 0.05f;
                return true;
            }
            OVRHand hand = _hands[i];
            if (hand == null || !hand.isActiveAndEnabled || !hand.IsTracked || !hand.IsDataHighConfidence ||
                hand.IsSystemGestureInProgress) return false;
            pressed = hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
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
            if (_grab == null) _grab = FindFirstObjectByType<BookGrab>();
            if (_rig == null) _rig = FindFirstObjectByType<OVRCameraRig>();
            if (_hands[0] != null && _hands[1] != null && _hands[0].isActiveAndEnabled && _hands[1].isActiveAndEnabled) return;
            _hands[0] = _hands[1] = null;
            foreach (OVRHand hand in FindObjectsByType<OVRHand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                int i = hand.GetHand() == OVRPlugin.Hand.HandLeft ? 0 : 1;
                if (_hands[i] != null) continue;
                _hands[i] = hand;
                _skeletons[i] = hand.GetComponent<OVRSkeleton>();
            }
        }
    }
}
