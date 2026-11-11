using System;
using System.Collections.Generic;
using FolioXR.Menu;
using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Tools
{
    /// <summary>
    /// Revisión del 11 de noviembre de 2026 (pedido del usuario tras probar en el visor):
    /// - El reloj muestra la hora y tiene un botón «Menú» que se toca con el índice derecho.
    /// - Con las manos, tocar un ícono del reloj con el índice derecho pone la herramienta en esa mano
    ///   sin pellizcar. Queda pegada: se suelta pellizcando (o cerrando la mano) y abriendo, o se guarda
    ///   acercándola al reloj.
    /// - Guantes de gravedad (como en Half-Life: Alyx): apuntar con la mano abierta o el mando a una
    ///   herramienta o al libro, pellizcar (o apretar el grip) y tirar la mano hacia atrás: el objeto
    ///   vuela en arco hasta la mano.
    /// </summary>
    public sealed partial class ToolRegistry
    {
        private const float SummonTouch = 0.016f, SummonRearm = 0.03f, StickyAway = 0.2f;
        private const float GravityMin = 0.6f, GravityMax = 10f, GravityCone = 7f, PullDistance = 0.07f, PullSpeed = 0.9f;

        private readonly bool[] _sticky = new bool[4], _stickyPinched = new bool[4], _stickyAway = new bool[4],
                                _stickyWait = new bool[4];
        private readonly bool[] _summonArmed = new bool[6];
        private TextMesh _clock;
        private Transform _menuButton;
        private bool _menuArmed;
        private long _clockMinute = -1;

        private sealed class GravitySide
        {
            internal object Target;        // FolioTool o el libro.
            internal bool Locked, WasPressed;
            internal float HoverSince;
            internal Vector3 LockLocal, PreviousLocal;
            internal bool HasPrevious;
            internal LineRenderer Marker, Beam;
        }

        private sealed class Flight
        {
            internal FolioTool Tool;       // null = el libro.
            internal int Input;
            internal Vector3 Start;
            internal Quaternion StartRotation;
            internal float Began, Duration, Height;
        }

        private readonly GravitySide[] _gravity = { new GravitySide(), new GravitySide() };
        private readonly List<Flight> _flights = new List<Flight>(2);

        // ───────────────────────── Esfera del reloj ─────────────────────────

        private void BuildWatchFace()
        {
            var clock = new GameObject("Hora del reloj");
            clock.transform.SetParent(_watch, false);
            clock.transform.localPosition = new Vector3(0f, 0.012f, -0.001f);
            _clock = clock.AddComponent<TextMesh>();
            _clock.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _clock.fontSize = 64;
            _clock.characterSize = 0.0011f;
            _clock.anchor = TextAnchor.MiddleCenter;
            _clock.alignment = TextAlignment.Center;
            _clock.color = Color.white;
            if (_clock.font != null)
                clock.GetComponent<MeshRenderer>().sharedMaterial = _clock.font.material;
            ToolVisuals.Configure(clock.GetComponent<Renderer>());

            _menuButton = _visuals.Box(_watch, "Botón Menú (tocar con el índice derecho)", new Vector3(0f, -0.018f, 0f),
                                       new Vector3(0.036f, 0.014f, 0.004f), _visuals.Violet);
            var label = new GameObject("Texto Menú");
            label.transform.SetParent(_watch, false);
            label.transform.localPosition = new Vector3(0f, -0.018f, -0.0035f);
            TextMesh text = label.AddComponent<TextMesh>();
            text.font = _clock.font;
            text.fontSize = 64;
            text.characterSize = 0.0006f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.text = "Menú";
            if (text.font != null)
                label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            ToolVisuals.Configure(label.GetComponent<Renderer>());

            for (int i = 0; i < 2; i++)
            {
                _gravity[i].Marker = _visuals.Line(transform, "Guante de gravedad: objetivo " + i, _visuals.Cyan, 0.003f, 32);
                _gravity[i].Marker.loop = true;
                _gravity[i].Marker.useWorldSpace = true;
                _gravity[i].Marker.enabled = false;
                _gravity[i].Beam = _visuals.Line(transform, "Guante de gravedad: haz " + i, _visuals.Cyan, 0.0015f, 2);
                _gravity[i].Beam.useWorldSpace = true;
                _gravity[i].Beam.enabled = false;
            }
        }

        private void UpdateClock()
        {
            if (_clock == null)
                return;
            long minute = DateTime.Now.Ticks / TimeSpan.TicksPerMinute;
            if (minute == _clockMinute)
                return;
            _clockMinute = minute;
            _clock.text = DateTime.Now.ToString("HH:mm");
        }

        /// <summary>Toques del índice derecho en el reloj: «Menú» y sacar herramientas sin pellizcar.</summary>
        private void UpdateWatchTouches()
        {
            UpdateClock();
            bool rightFree = !SideHeld(XRNode.RightHand) && (_grab == null || !_grab.HoldsSide(XRNode.RightHand));
            Vector3 tip = Vector3.zero;
            bool pointer = _wristValid && rightFree && TryPointer(XRNode.RightHand, out tip);

            // «Menú»: también funciona con el menú abierto, para cerrarlo con la mano.
            if (!pointer || _menuButton == null)
                _menuArmed = false;
            else
            {
                float d = Vector3.Distance(tip, _menuButton.position);
                if (d > 0.03f)
                    _menuArmed = true;
                else if (_menuArmed && d < 0.015f)
                {
                    _menuArmed = false;
                    _reserved[1] = true;
                    Debug.Log("[FolioXR] Reloj: botón Menú");
                    MenuController.ToggleFromWatch();
                }
            }

            // Sacar herramienta tocando su ícono: solo con la mano derecha real (sin mando).
            GrabInput hand = _inputs[1];
            bool summon = pointer && !_book.InputPaused && hand.Reliable && hand.IndexTip != null && !_inputs[3].Reliable;
            for (int slot = 0; slot < _tools.Count; slot++)
            {
                FolioTool tool = _tools[slot];
                if (!summon || tool.State != ToolState.Stored || !tool.Available)
                {
                    _summonArmed[slot] = false;
                    continue;
                }
                float d = Vector3.Distance(hand.IndexTip.position, _icons[slot].position);
                if (d > SummonRearm)
                    _summonArmed[slot] = true;
                else if (_summonArmed[slot] && d < SummonTouch)
                {
                    _summonArmed[slot] = false;
                    GiveTo(1, tool, true);
                    Debug.Log("[FolioXR] Reloj: " + tool.Kind + " a la mano derecha (tocar ícono)");
                    break;
                }
            }
        }

        /// <summary>Pone la herramienta en la mano o mando i. Con mano queda pegada hasta soltarla a propósito.</summary>
        private void GiveTo(int i, FolioTool tool, bool sticky)
        {
            GrabInput input = _inputs[i];
            _held[i] = tool;
            _sticky[i] = sticky && input.IsHand;
            _stickyPinched[i] = false;
            _stickyWait[i] = input.Pressed; // El pellizco con que se atrajo/tomó no la suelta: hay que abrir primero.
            _stickyAway[i] = false;
            _reserved[i % 2] = true;
            input.LostSince = -1f;
            tool.Take(input.Node, input.IsHand, _rig.trackingSpace != null ? _rig.trackingSpace : _rig.transform,
                      input.Position, input.Rotation);
            int slot = _tools.IndexOf(tool);
            if (slot >= 0)
                _icons[slot].gameObject.SetActive(false);
        }

        /// <summary>
        /// Herramienta pegada a la mano. Devuelve true si la decisión ya está tomada este cuadro
        /// (sigue en la mano o se guardó); false = soltarla como siempre.
        /// </summary>
        private bool UpdateSticky(int i, FolioTool tool, GrabInput input)
        {
            if (!_sticky[i])
                return false;
            float wrist = _wristValid ? Vector3.Distance(tool.transform.position, _wrist.position) : float.PositiveInfinity;
            if (wrist > StickyAway)
                _stickyAway[i] = true;
            if (_stickyAway[i] && wrist < ReturnDistance)
            {
                Return(tool);
                return true;
            }
            if (_stickyWait[i])
            {
                if (!input.Pressed)
                    _stickyWait[i] = false;
                return true;
            }
            if (input.Pressed)
            {
                _stickyPinched[i] = true;
                return true;
            }
            if (!_stickyPinched[i])
                return true;
            _sticky[i] = false; // Pellizcó (o cerró la mano) y abrió: se suelta normalmente.
            return false;
        }

        // ───────────────────────── Guantes de gravedad ─────────────────────────

        private int GravityInput(int side) => _inputs[side + 2].Reliable ? side + 2 : side;

        private bool AimRay(int i, out Vector3 origin, out Vector3 direction)
        {
            GrabInput input = _inputs[i];
            origin = direction = Vector3.zero;
            if (!input.Reliable)
                return false;
            if (!input.IsHand)
            {
                Transform anchor = input.Node == XRNode.LeftHand ? _rig.leftControllerAnchor : _rig.rightControllerAnchor;
                if (anchor == null)
                    return false;
                origin = anchor.position;
                direction = anchor.forward;
                return true;
            }
            OVRHand hand = input.Hand;
            if (hand == null || !hand.IsPointerPoseValid || hand.PointerPose == null)
                return false;
            origin = hand.PointerPose.position;
            direction = hand.PointerPose.forward;
            return true;
        }

        private object FindTarget(Vector3 origin, Vector3 direction, out Vector3 center, out float size)
        {
            object best = null;
            float bestAngle = float.PositiveInfinity;
            center = Vector3.zero;
            size = 0f;
            foreach (FolioTool tool in _tools)
            {
                if (tool.State != ToolState.World || Flying(tool))
                    continue;
                Consider(tool, tool.GripCollider.bounds, origin, direction, ref best, ref bestAngle, ref center, ref size);
            }
            // El libro solo se atrae si está lejos (más de 1,5 m de la cabeza): uno que se está leyendo
            // no debe capturar el pellizco que pasa página.
            Camera camera = Camera.main;
            bool bookFar = camera != null && _book != null &&
                           Vector3.Distance(camera.transform.position, _book.transform.position) > 1.5f;
            if (bookFar && _book.bookCollider != null && !_book.IsGrabbed && !Flying(null))
                Consider(_book, _book.bookCollider.bounds, origin, direction, ref best, ref bestAngle, ref center, ref size);
            return best;
        }

        private static void Consider(object item, Bounds bounds, Vector3 origin, Vector3 direction, ref object best,
                                     ref float bestAngle, ref Vector3 center, ref float size)
        {
            Vector3 to = bounds.center - origin;
            float distance = to.magnitude;
            if (distance < GravityMin || distance > GravityMax)
                return;
            float radius = Mathf.Max(0.04f, bounds.extents.magnitude * 0.6f);
            float allowance = GravityCone + Mathf.Atan2(radius, distance) * Mathf.Rad2Deg;
            float angle = Vector3.Angle(direction, to);
            if (angle > allowance || angle >= bestAngle)
                return;
            best = item;
            bestAngle = angle;
            center = bounds.center;
            size = radius;
        }

        private bool Flying(FolioTool tool)
        {
            foreach (Flight flight in _flights)
                if (flight.Tool == tool)
                    return true;
            return false;
        }

        private void UpdateGravity()
        {
            Transform space = _rig.trackingSpace != null ? _rig.trackingSpace : _rig.transform;
            for (int side = 0; side < 2; side++)
            {
                GravitySide g = _gravity[side];
                int i = GravityInput(side);
                GrabInput input = _inputs[i];
                XRNode node = side == 0 ? XRNode.LeftHand : XRNode.RightHand;
                Vector3 origin = Vector3.zero, direction = Vector3.forward;
                bool busy = _book.InputPaused || SideHeld(node) || (_grab != null && _grab.HoldsSide(node)) ||
                            !AimRay(i, out origin, out direction);
                bool pressed = input.Reliable && input.Pressed;
                bool onset = pressed && !g.WasPressed;
                g.WasPressed = pressed;
                if (busy)
                {
                    ClearGravity(g);
                    continue;
                }
                Vector3 local = space.InverseTransformPoint(input.Position);
                if (g.Locked)
                {
                    if (!pressed || !StillValid(g.Target))
                    {
                        ClearGravity(g);
                        continue;
                    }
                    // Tirar hacia atrás: alejarse del objeto 7 cm o moverse rápido en esa dirección.
                    Vector3 targetLocal = space.InverseTransformPoint(TargetCenter(g.Target));
                    Vector3 away = (g.LockLocal - targetLocal).normalized;
                    float pulled = Vector3.Dot(local - g.LockLocal, away);
                    float speed = g.HasPrevious && Time.unscaledDeltaTime > 0f
                        ? Vector3.Dot(local - g.PreviousLocal, away) / Time.unscaledDeltaTime : 0f;
                    g.PreviousLocal = local;
                    g.HasPrevious = true;
                    DrawGravity(g, TargetCenter(g.Target), Mathf.Max(0.05f, TargetSize(g.Target)), true, input.Position);
                    if (pulled > PullDistance || speed > PullSpeed)
                    {
                        Launch(g.Target, i);
                        ClearGravity(g);
                    }
                    continue;
                }
                object target = FindTarget(origin, direction, out Vector3 center, out float size);
                if (target != g.Target)
                {
                    g.Target = target;
                    g.HoverSince = Time.unscaledTime;
                }
                if (target == null)
                {
                    ClearGravity(g);
                    continue;
                }
                bool hovering = Time.unscaledTime - g.HoverSince > 0.12f;
                DrawGravity(g, center, size, false, input.Position);
                g.Marker.enabled = hovering;
                if (hovering && onset)
                {
                    g.Locked = true;
                    g.LockLocal = g.PreviousLocal = local;
                    g.HasPrevious = true;
                    _reserved[side] = true; // Este pellizco no pasa página.
                    Debug.Log("[FolioXR] Guante de gravedad: apuntando a " + Describe(target) + " con " + input.Label);
                }
            }
            UpdateFlights();
        }

        private bool StillValid(object target) => target is FolioTool tool
            ? tool.State == ToolState.World && !Flying(tool)
            : target != null && _book != null && !_book.IsGrabbed && !Flying(null);

        private Vector3 TargetCenter(object target) => target is FolioTool tool ? tool.GripCollider.bounds.center
            : _book.bookCollider != null ? _book.bookCollider.bounds.center : _book.transform.position;

        private float TargetSize(object target) => target is FolioTool tool
            ? Mathf.Max(0.04f, tool.GripCollider.bounds.extents.magnitude * 0.6f)
            : _book.bookCollider != null ? Mathf.Max(0.04f, _book.bookCollider.bounds.extents.magnitude * 0.6f) : 0.15f;

        private static string Describe(object target) => target is FolioTool tool ? tool.Kind.ToString() : "el libro";

        private void DrawGravity(GravitySide g, Vector3 center, float size, bool locked, Vector3 hand)
        {
            Camera camera = Camera.main;
            if (camera == null)
                return;
            Vector3 right = camera.transform.right, up = camera.transform.up;
            for (int k = 0; k < 32; k++)
            {
                float a = k * Mathf.PI / 16f;
                g.Marker.SetPosition(k, center + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * size);
            }
            g.Marker.widthMultiplier = locked ? 1.8f : 1f;
            g.Marker.enabled = true;
            g.Beam.enabled = locked;
            if (locked)
            {
                g.Beam.SetPosition(0, hand);
                g.Beam.SetPosition(1, center);
            }
        }

        private static void ClearGravity(GravitySide g)
        {
            g.Target = null;
            g.Locked = false;
            g.HasPrevious = false;
            if (g.Marker != null)
                g.Marker.enabled = false;
            if (g.Beam != null)
                g.Beam.enabled = false;
        }

        private void Launch(object target, int input)
        {
            var flight = new Flight { Input = input, Began = Time.unscaledTime };
            if (target is FolioTool tool)
            {
                flight.Tool = tool;
                tool.Body.isKinematic = true;
                tool.Body.useGravity = false;
                tool.GripCollider.enabled = false;
                flight.Start = tool.transform.position;
                flight.StartRotation = tool.transform.rotation;
            }
            else
            {
                _book.FreezePhysics();
                flight.Start = _book.transform.position;
                flight.StartRotation = _book.transform.rotation;
            }
            float distance = Vector3.Distance(flight.Start, _inputs[input].Position);
            flight.Duration = Mathf.Clamp(0.25f + distance * 0.09f, 0.35f, 0.9f);
            flight.Height = Mathf.Clamp(distance * 0.18f, 0.08f, 0.7f);
            _flights.Add(flight);
            Debug.Log("[FolioXR] Guante de gravedad: atraer " + Describe(target) + " (" + distance.ToString("F1") + " m)");
        }

        private void UpdateFlights()
        {
            for (int f = _flights.Count - 1; f >= 0; f--)
            {
                Flight flight = _flights[f];
                GrabInput input = _inputs[flight.Input];
                float t = Mathf.Clamp01((Time.unscaledTime - flight.Began) / flight.Duration);
                Vector3 end = TargetPose(flight, input, out Quaternion endRotation);
                // Arco: interpolación hacia la mano (que se sigue mientras vuela) más una parábola hacia arriba.
                Vector3 position = Vector3.Lerp(flight.Start, end, t) + Vector3.up * (4f * flight.Height * t * (1f - t));
                Quaternion rotation = Quaternion.Slerp(flight.StartRotation, endRotation, t * t);
                Transform item = flight.Tool != null ? flight.Tool.transform : _book.transform;
                item.SetPositionAndRotation(position, rotation);
                if (t < 1f)
                    continue;
                _flights.RemoveAt(f);
                Arrive(flight, input);
            }
        }

        private Vector3 TargetPose(Flight flight, GrabInput input, out Quaternion rotation)
        {
            if (flight.Tool != null)
            {
                rotation = input.Reliable ? input.Rotation : flight.StartRotation;
                return input.Reliable ? input.Position : flight.Start;
            }
            // El libro llega frente a la vista, a la altura de lectura.
            Camera camera = Camera.main;
            Transform head = camera != null ? camera.transform : _rig.centerEyeAnchor;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.04f ? forward.normalized : head.forward;
            Vector3 position = head.position + forward * 0.5f + Vector3.down * 0.15f;
            rotation = Quaternion.LookRotation((position - head.position).normalized, Vector3.up);
            return position;
        }

        private void Arrive(Flight flight, GrabInput input)
        {
            if (flight.Tool == null)
            {
                _book.PlaceInFrontOfView(); // Deja el libro fijo y listo para tomarlo.
                Debug.Log("[FolioXR] Guante de gravedad: el libro llegó frente a la vista");
                return;
            }
            int i = flight.Input;
            if (input.Reliable && _held[i] == null && !SideHeld(input.Node) &&
                (_grab == null || !_grab.HoldsSide(input.Node)))
            {
                GiveTo(i, flight.Tool, true);
                Debug.Log("[FolioXR] Guante de gravedad: " + flight.Tool.Kind + " atrapada con " + input.Label);
            }
            else
                flight.Tool.Release(_book.RealisticPhysics); // Nadie la atrapó: cae o flota donde llegó.
        }

        private void CancelFlights()
        {
            foreach (Flight flight in _flights)
                if (flight.Tool != null && flight.Tool.State == ToolState.World)
                    flight.Tool.Release(_book != null && _book.RealisticPhysics);
            _flights.Clear();
            foreach (GravitySide g in _gravity)
                ClearGravity(g);
        }
    }
}
