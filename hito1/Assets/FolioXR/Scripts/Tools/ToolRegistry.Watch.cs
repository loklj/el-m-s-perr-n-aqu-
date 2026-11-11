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
        private Transform _menuButton, _watchBody;
        private bool _menuArmed, _faceArmed, _trayOpen;
        private float _trayCloseAt;
        private Material _outlineMaterial;
        private readonly Dictionary<object, List<Renderer>> _outlines = new Dictionary<object, List<Renderer>>();
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
            BuildWristWatch();

            _menuButton = _visuals.Box(_watch, "Botón Menú (tocar con el índice derecho)", new Vector3(0f, 0f, 0f),
                                       new Vector3(0.036f, 0.016f, 0.004f), _visuals.Violet);
            var label = new GameObject("Texto Menú");
            label.transform.SetParent(_watch, false);
            label.transform.localPosition = new Vector3(0f, 0f, -0.0035f);
            TextMesh text = label.AddComponent<TextMesh>();
            text.font = _clock.font;
            text.fontSize = 64;
            text.characterSize = 0.00065f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.text = "Menú";
            if (text.font != null)
                label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            ToolVisuals.Configure(label.GetComponent<Renderer>());

            // Contorno blanco brillante del guante de gravedad: casco invertido (caras traseras) un poco más grande.
            _outlineMaterial = _visuals.Transparent("contorno del guante de gravedad", new Color(1f, 1f, 1f, 0.9f));
            _outlineMaterial.SetFloat("_Cull", 1f); // CullMode.Front
            _outlineMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;
            for (int i = 0; i < 2; i++)
            {
                _gravity[i].Marker = _visuals.Line(transform, "Guante de gravedad: objetivo " + i, _visuals.Cyan, 0.003f, 32);
                _gravity[i].Marker.loop = true;
                _gravity[i].Marker.useWorldSpace = true;
                _gravity[i].Marker.enabled = false;
                _gravity[i].Beam = _visuals.Line(transform, "Guante de gravedad: haz " + i, _visuals.White, 0.0015f, 2);
                _gravity[i].Beam.useWorldSpace = true;
                _gravity[i].Beam.enabled = false;
            }
        }

        /// <summary>Reloj de verdad en el dorso de la muñeca izquierda: caja, esfera con la hora y correa.</summary>
        private void BuildWristWatch()
        {
            _watchBody = new GameObject("Reloj de pulsera").transform;
            _watchBody.SetParent(_rig.trackingSpace != null ? _rig.trackingSpace : _rig.transform, false);
            Material steel = _visuals.Solid("caja del reloj", new Color(0.55f, 0.58f, 0.62f));
            Material face = _visuals.Solid("esfera del reloj", new Color(0.05f, 0.07f, 0.13f));
            Material strap = _visuals.Solid("correa del reloj", new Color(0.12f, 0.12f, 0.14f));
            // Local: -Z hacia fuera del dorso (la esfera se lee desde -Z), Y = de las 6 a las 12, X = hacia los dedos.
            _visuals.Box(_watchBody, "Caja", new Vector3(0f, 0f, 0.002f), new Vector3(0.04f, 0.04f, 0.008f), steel);
            _visuals.Box(_watchBody, "Esfera", new Vector3(0f, 0f, -0.0021f), new Vector3(0.034f, 0.034f, 0.0004f), face);
            _visuals.Box(_watchBody, "Corona", new Vector3(0.0215f, 0f, 0.001f), new Vector3(0.004f, 0.005f, 0.004f), steel);
            for (int side = -1; side <= 1; side += 2)
            {
                Transform band = _visuals.Box(_watchBody, side > 0 ? "Correa (12)" : "Correa (6)",
                                              new Vector3(0f, side * 0.032f, 0.008f), new Vector3(0.022f, 0.026f, 0.003f), strap);
                band.localRotation = Quaternion.Euler(side * -35f, 0f, 0f);
            }
            var clock = new GameObject("Hora del reloj");
            clock.transform.SetParent(_watchBody, false);
            clock.transform.localPosition = new Vector3(0f, 0.002f, -0.0026f);
            _clock = clock.AddComponent<TextMesh>();
            _clock.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _clock.fontSize = 64;
            _clock.characterSize = 0.00085f;
            _clock.anchor = TextAnchor.MiddleCenter;
            _clock.alignment = TextAlignment.Center;
            _clock.color = new Color(0.75f, 0.95f, 1f);
            if (_clock.font != null)
                clock.GetComponent<MeshRenderer>().sharedMaterial = _clock.font.material;
            ToolVisuals.Configure(clock.GetComponent<Renderer>());
            var hint = new GameObject("Indicación");
            hint.transform.SetParent(_watchBody, false);
            hint.transform.localPosition = new Vector3(0f, -0.0105f, -0.0026f);
            TextMesh tap = hint.AddComponent<TextMesh>();
            tap.font = _clock.font;
            tap.fontSize = 64;
            tap.characterSize = 0.00035f;
            tap.anchor = TextAnchor.MiddleCenter;
            tap.alignment = TextAlignment.Center;
            tap.color = new Color(0.55f, 0.75f, 0.85f);
            tap.text = "tocar";
            if (tap.font != null)
                hint.GetComponent<MeshRenderer>().sharedMaterial = tap.font.material;
            ToolVisuals.Configure(hint.GetComponent<Renderer>());
            _watchBody.gameObject.SetActive(false);
        }

        private static bool TryBone(OVRSkeleton skeleton, OVRSkeleton.BoneId classic, OVRSkeleton.BoneId openXr,
                                    out Vector3 position)
        {
            position = Vector3.zero;
            if (skeleton == null || !skeleton.isActiveAndEnabled || !skeleton.IsDataValid || skeleton.Bones == null)
                return false;
            OVRSkeleton.SkeletonType type = skeleton.GetSkeletonType();
            OVRSkeleton.BoneId id = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight
                                        ? openXr : classic;
            foreach (OVRBone bone in skeleton.Bones)
                if (bone.Id == id && bone.Transform != null)
                {
                    position = bone.Transform.position;
                    return true;
                }
            return false;
        }

        /// <summary>Coloca el reloj en el dorso de la muñeca izquierda a partir de los huesos de la mano.</summary>
        private bool UpdateWristWatch()
        {
            if (_watchBody == null)
                return false;
            OVRSkeleton skeleton = _inputs[0].Skeleton;
            if (!TryBone(skeleton, OVRSkeleton.BoneId.Hand_WristRoot, OVRSkeleton.BoneId.XRHand_Wrist, out Vector3 wrist) ||
                !TryBone(skeleton, OVRSkeleton.BoneId.Hand_Index1, OVRSkeleton.BoneId.XRHand_IndexProximal, out Vector3 index) ||
                !TryBone(skeleton, OVRSkeleton.BoneId.Hand_Pinky1, OVRSkeleton.BoneId.XRHand_LittleProximal, out Vector3 pinky) ||
                !TryBone(skeleton, OVRSkeleton.BoneId.Hand_Middle1, OVRSkeleton.BoneId.XRHand_MiddleProximal, out Vector3 middle))
                return false;
            Vector3 fingers = middle - wrist;
            Vector3 palm = Vector3.Cross(index - wrist, pinky - wrist); // Mano izquierda: sale de la palma.
            if (fingers.sqrMagnitude < 1e-6f || palm.sqrMagnitude < 1e-10f)
                return false;
            fingers.Normalize();
            Vector3 back = -palm.normalized; // Dorso.
            Vector3 twelve = Vector3.Cross(fingers, back);
            Vector3 position = wrist - fingers * 0.035f + back * 0.022f;
            Quaternion rotation = Quaternion.LookRotation(-back, twelve);
            _watchBody.SetPositionAndRotation(position, rotation);
            if (!_watchBody.gameObject.activeSelf)
                _watchBody.gameObject.SetActive(true);
            return true;
        }

        private void SetTray(bool open)
        {
            if (_trayOpen == open)
                return;
            _trayOpen = open;
            _trayCloseAt = Time.unscaledTime + 8f;
            for (int i = 0; i < _summonArmed.Length; i++)
                _summonArmed[i] = false;
            _menuArmed = false;
            Debug.Log("[FolioXR] Reloj: herramientas " + (open ? "abiertas" : "cerradas"));
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

            // Tocar la esfera del reloj abre o cierra la bandeja de herramientas (solo con manos).
            bool face = pointer && _wrist == _watchBody && _watchBody != null && _watchBody.gameObject.activeSelf;
            if (!face)
                _faceArmed = false;
            else
            {
                float d = Vector3.Distance(tip, _watchBody.position);
                if (d > 0.045f)
                    _faceArmed = true;
                else if (_faceArmed && d < 0.022f)
                {
                    _faceArmed = false;
                    _reserved[1] = true;
                    SetTray(!_trayOpen);
                }
            }
            bool trayVisible = _watch != null && _watch.gameObject.activeInHierarchy;
            pointer &= trayVisible;

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
                    SetTray(false);
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
                    SetTray(false);
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
                    ShowOutline(g.Target, false, false);
                    g.Target = target;
                    g.HoverSince = Time.unscaledTime;
                }
                if (target == null)
                {
                    ClearGravity(g);
                    continue;
                }
                bool hovering = Time.unscaledTime - g.HoverSince > 0.12f;
                if (hovering)
                    DrawGravity(g, center, size, false, input.Position);
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
            g.Marker.enabled = false; // El contorno blanco reemplaza al círculo.
            Highlight(g, locked);
            g.Beam.enabled = locked;
            if (locked)
            {
                g.Beam.SetPosition(0, hand);
                g.Beam.SetPosition(1, center);
            }
        }

        private void ClearGravity(GravitySide g)
        {
            ShowOutline(g.Target, false, false);
            g.Target = null;
            g.Locked = false;
            g.HasPrevious = false;
            if (g.Marker != null)
                g.Marker.enabled = false;
            if (g.Beam != null)
                g.Beam.enabled = false;
        }

        private void Highlight(GravitySide g, bool locked) => ShowOutline(g.Target, true, locked);

        /// <summary>Contorno blanco que late suavemente; al fijar el objetivo brilla al máximo.</summary>
        private void ShowOutline(object target, bool show, bool locked)
        {
            if (target == null || _outlineMaterial == null)
                return;
            if (!_outlines.TryGetValue(target, out List<Renderer> parts))
            {
                if (!show)
                    return;
                parts = BuildOutline(target is FolioTool tool ? tool.transform : _book.transform);
                _outlines[target] = parts;
            }
            float alpha = locked ? 1f : 0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 7f);
            _outlineMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            foreach (Renderer part in parts)
            {
                if (part == null)
                    continue;
                if (show)
                    FitOutline(part.transform, locked ? 0.006f : 0.004f);
                if (part.enabled != show)
                    part.enabled = show;
            }
        }

        private List<Renderer> BuildOutline(Transform root)
        {
            var parts = new List<Renderer>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || filter.name.StartsWith("Contorno", StringComparison.Ordinal))
                    continue;
                Vector3 size = mesh.bounds.size;
                // Solo volúmenes: páginas, cintas de tinta y textos son planos y no se recortan bien.
                if (size.x < 0.001f || size.y < 0.001f || size.z < 0.001f)
                    continue;
                var outline = new GameObject("Contorno");
                outline.transform.SetParent(filter.transform, false);
                outline.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = outline.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _outlineMaterial;
                ToolVisuals.Configure(renderer);
                renderer.enabled = false;
                parts.Add(renderer);
            }
            return parts;
        }

        /// <summary>Agranda la copia lo justo para que sobresalga «thickness» metros por lado, con cualquier escala.</summary>
        private static void FitOutline(Transform outline, float thickness)
        {
            Mesh mesh = outline.GetComponent<MeshFilter>().sharedMesh;
            Vector3 size = mesh.bounds.size;
            Vector3 world = outline.parent.lossyScale;
            outline.localPosition = mesh.bounds.center - Vector3.Scale(mesh.bounds.center, Grow(size, world, thickness));
            outline.localScale = Grow(size, world, thickness);
        }

        private static Vector3 Grow(Vector3 size, Vector3 world, float thickness) => new Vector3(
            1f + 2f * thickness / Mathf.Max(1e-4f, Mathf.Abs(size.x * world.x)),
            1f + 2f * thickness / Mathf.Max(1e-4f, Mathf.Abs(size.y * world.y)),
            1f + 2f * thickness / Mathf.Max(1e-4f, Mathf.Abs(size.z * world.z)));

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
