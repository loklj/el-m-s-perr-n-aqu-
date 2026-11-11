using System;
using System.Collections.Generic;
using FolioXR.Reading;
using FolioXR.Tools.Ink;
using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Tools
{
    /// <summary>Un dueño por lado físico, reloj izquierdo y registro extensible para D/E.</summary>
    [DefaultExecutionOrder(125)] // Menú 100 → herramientas 125 → libro 150/200.
    public sealed partial class ToolRegistry : MonoBehaviour
    {
        public static ToolRegistry Instance { get; private set; }
        public IReadOnlyList<FolioTool> Tools => _tools;
        private const float TakeDistance = 0.03f, ReturnDistance = 0.10f;
        private readonly List<FolioTool> _tools = new List<FolioTool>(6);
        private readonly List<Transform> _icons = new List<Transform>(6);
        private readonly GrabInput[] _inputs = { new GrabInput(XRNode.LeftHand, true, "mano izquierda"),
                                                 new GrabInput(XRNode.RightHand, true, "mano derecha"),
                                                 new GrabInput(XRNode.LeftHand, false, "mando izquierdo"),
                                                 new GrabInput(XRNode.RightHand, false, "mando derecho") };
        private readonly FolioTool[] _held = new FolioTool[4];
        private readonly bool[] _reserved = new bool[2], _touchArmed = new bool[6];
        private readonly bool[] _triggerWas = { true, true, true, true }, _primaryWas = { true, true, true, true },
                                _secondaryWas = { true, true, true, true };
        private FolioBook _book;
        private BookGrab _grab;
        private OVRCameraRig _rig;
        private ToolVisuals _visuals;
        private InkManager _ink;
        private Transform _watch, _wrist;
        private TextMesh _notice;
        private float _nextFind, _noticeUntil, _watchScale = 0.65f;
        private bool _focused = true, _wristValid;
        private Func<Vector3, float> _distance;
        private int _sampling;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (FindFirstObjectByType<ToolRegistry>(FindObjectsInactive.Include) == null)
                new GameObject("Herramientas (Folio XR)").AddComponent<ToolRegistry>();
        }

        private void Awake()
        {
            Instance = this;
            _distance = NearestDistance;
        }

        /// <summary>Consulta para reservar los botones del mando propietario, también frente al menú.</summary>
        public static bool HoldsController(XRNode side) => Instance != null
                                                           && Instance._held[side == XRNode.LeftHand ? 2 : 3] != null;
        /// <summary>Tanda E: la lupa en un mando usa su stick para el aumento; el movimiento lo ignora.</summary>
        public static bool ReservesStick(XRNode side) => Instance != null
                                                         && Instance._held[side == XRNode.LeftHand ? 2 : 3] is MagnifierTool;
        public static bool HoldsHand(XRNode side) => Instance != null
                                                     && Instance._held[side == XRNode.LeftHand ? 0 : 1] != null;
        /// <summary>
        /// Punta para tocar (libreta): índice de la mano o un punto 3 cm delante del mando. El mando real
        /// tiene prioridad sobre la mano emulada del mismo lado, como en el resto del registro.
        /// </summary>
        internal bool TryPointer(XRNode side, out Vector3 tip)
        {
            int hand = side == XRNode.LeftHand ? 0 : 1;
            GrabInput controller = _inputs[hand + 2];
            if (controller.Reliable)
            {
                tip = controller.Position + controller.Rotation * Vector3.forward * 0.03f;
                return true;
            }
            GrabInput input = _inputs[hand];
            tip = input.Reliable && input.IndexTip != null ? input.IndexTip.position : Vector3.zero;
            return input.Reliable && input.IndexTip != null;
        }

        public static void StoreAll()
        {
            if (Instance != null)
                Instance.ReturnAll();
        }

        /// <summary>Registrar antes de usar. Una entrada por tipo; el registro es dueño de su ciclo de vida.</summary>
        public void Register(FolioTool tool, ToolKind kind, Vector3 colliderSize)
        {
            if (_visuals == null || tool == null)
                throw new InvalidOperationException("El reloj aún no está listo.");
            foreach (FolioTool existing in _tools)
                if (existing.Kind == kind)
                    throw new InvalidOperationException("Tipo ya registrado: " + kind);
            tool.transform.SetParent(transform, false);
            tool.Initialize(this, kind, colliderSize);
            _tools.Add(tool);
            Transform icon = _visuals.Icon(_watch, kind);
            float angle = (int)kind * Mathf.PI / 3f;
            icon.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.052f;
            _icons.Add(icon);
        }

        private void Resolve()
        {
            if (_book == null)
                _book = FindFirstObjectByType<FolioBook>();
            if (_grab == null)
                _grab = FindFirstObjectByType<BookGrab>();
            if (_rig == null)
                _rig = FindFirstObjectByType<OVRCameraRig>();
            if (_rig != null)
                GrabInput.BindHands(_inputs, _rig);
            if (_visuals != null || _book == null || _rig == null)
                return;
            Renderer page = _book.leftPage != null ? _book.leftPage : _book.rightPage;
            Material source = page != null ? page.sharedMaterial : null;
            if (source == null || source.shader == null || source.shader.name != "Universal Render Pipeline/Unlit")
                return;
            _visuals = new ToolVisuals(source);
            _watch = new GameObject("Reloj de seis herramientas").transform;
            _watch.SetParent(_rig.trackingSpace != null ? _rig.trackingSpace : _rig.transform, false);
            LineRenderer ring = _visuals.Line(_watch, "Anillo", _visuals.Cyan, 0.0012f, 48);
            ring.loop = true;
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI / 24f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.052f);
            }
            BuildWatchFace(); // Hora, botón «Menú» y marcadores del guante de gravedad.
            // Tanda D: lienzos y guardado de trazos, antes que los lápices que los usan.
            _ink = gameObject.AddComponent<InkManager>();
            _ink.Initialize(_visuals, _book, _rig.transform);
            for (int i = 0; i < 6; i++)
            {
                var kind = (ToolKind)i;
                var go = new GameObject("Herramienta " + kind);
                switch (kind)
                {
                case ToolKind.Flashlight:
                    var flashlight = go.AddComponent<TripleFlashlight>();
                    Register(flashlight, kind, new Vector3(0.045f, 0.045f, 0.16f));
                    flashlight.Build(_visuals);
                    break;
                case ToolKind.Pencils:
                case ToolKind.InvisibleInk:
                    var pencil = go.AddComponent<PencilTool>();
                    Register(pencil, kind, new Vector3(0.014f, 0.014f, 0.18f));
                    pencil.Build(_visuals, kind == ToolKind.InvisibleInk);
                    break;
                case ToolKind.Notebook:
                    var notebook = go.AddComponent<NotebookTool>();
                    Register(notebook, kind, NotebookTool.ColliderSize);
                    notebook.Build(_visuals);
                    break;
                case ToolKind.Magnifier:
                    var magnifier = go.AddComponent<MagnifierTool>();
                    Register(magnifier, kind, MagnifierTool.ColliderSize);
                    magnifier.Build(_visuals);
                    break;
                case ToolKind.Ruler:
                    var ruler = go.AddComponent<RulerTool>();
                    Register(ruler, kind, RulerTool.ColliderSize);
                    ruler.Build(_visuals);
                    break;
                default:
                    Register(go.AddComponent<UnavailableTool>(), kind, new Vector3(0.045f, 0.045f, 0.16f));
                    break;
                }
            }
            // Tanda E: la cámara de la lupa excluye la capa UI; el reloj vive en ella.
            SetLayer(_watch, 5);
            var label = new GameObject("Aviso de herramientas");
            label.transform.SetParent(transform, false);
            _notice = label.AddComponent<TextMesh>();
            _notice.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _notice.fontSize = 64;
            _notice.characterSize = 0.0018f;
            _notice.anchor = TextAnchor.MiddleCenter;
            _notice.alignment = TextAlignment.Center;
            if (_notice.font != null)
                label.GetComponent<MeshRenderer>().sharedMaterial = _notice.font.material;
            ToolVisuals.Configure(label.GetComponent<Renderer>());
            label.SetActive(false);
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayer(root.GetChild(i), layer);
        }

        private void UpdateWatch()
        {
            if (_watch == null)
                return;
            // Un mando real tiene prioridad sobre cualquier mano emulada del mismo lado.
            _wrist = _inputs[2].Reliable   ? _rig.leftControllerAnchor
                     : _inputs[0].Reliable ? (_inputs[0].Wrist != null ? _inputs[0].Wrist : _inputs[0].Hand.transform)
                                           : null;
            _wristValid = _wrist != null && _focused;
            // Visible también con el menú abierto: su botón «Menú» lo cierra con la mano.
            _watch.gameObject.SetActive(_wristValid);
            if (!_wristValid)
                return;
            Camera camera = Camera.main;
            Vector3 position = _wrist.position + _wrist.up * 0.025f;
            // Anclado al hueso/anchor; la cara de los símbolos mira al lector.
            Quaternion rotation = camera != null ? camera.transform.rotation : _wrist.rotation;
            _watch.SetPositionAndRotation(position, rotation);
            Vector3 toWrist = camera != null ? position - camera.transform.position : Vector3.zero;
            bool looking = camera != null && toWrist.magnitude < 0.7f &&
                           Vector3.Dot(camera.transform.forward, toWrist.normalized) > 0.92f;
            _watchScale = Mathf.MoveTowards(_watchScale, looking ? 1.2f : 0.65f, Time.unscaledDeltaTime * 3f);
            _watch.localScale = Vector3.one * _watchScale;
        }

        private float NearestDistance(Vector3 point)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < _tools.Count; i++)
                best = Mathf.Min(best, Distance(i, point, _inputs[_sampling].Node));
            return best;
        }

        private float Distance(int slot, Vector3 point, XRNode node)
        {
            FolioTool tool = _tools[slot];
            if (tool.State == ToolState.Stored)
                return _wristValid && node == XRNode.RightHand ? Vector3.Distance(point, _icons[slot].position)
                                                               : float.PositiveInfinity;
            if (tool.State != ToolState.World || !tool.GripCollider.enabled)
                return float.PositiveInfinity;
            return Vector3.Distance(point, tool.GripCollider.ClosestPoint(point));
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 1f;
                Resolve();
            }
            if (!_focused || _visuals == null || _rig == null || _book == null)
                return;
            // Primero mandos y mano izquierda, para decidir el anclaje del reloj con datos actuales.
            for (int pass = 0; pass < 4; pass++)
            {
                int i = pass < 2 ? pass + 2 : pass - 2;
                _sampling = i;
                _inputs[i].Sample(_rig, _held[i] != null, _distance, TakeDistance);
                // Solo BookGrab emite diagnósticos; no generar cadenas cada cuadro al mantener el gesto.
                _inputs[i].LoggedPinch = _inputs[i].Pinch;
                _inputs[i].LoggedFist = _inputs[i].Fist;
                if (i == 0)
                    UpdateWatch();
            }
            for (int side = 0; side < 2; side++)
                if (_inputs[side].Reliable && !_inputs[side].Pressed &&
                    (!_inputs[side + 2].Reliable || !_inputs[side + 2].Pressed))
                    _reserved[side] = false;
                else if (_inputs[side + 2].Reliable && !_inputs[side + 2].Pressed && !_inputs[side].Reliable)
                    _reserved[side] = false;
            bool attempts = false;
            for (int i = 0; i < 4; i++)
                attempts |= _inputs[i].Started;
            if (attempts)
                Physics.SyncTransforms();
            for (int i = 0; i < 4; i++)
            {
                GrabInput input = _inputs[i];
                bool justTaken = false;
                if (_held[i] != null)
                    UpdateHeld(i);
                else if (input.Started && !_book.InputPaused && (_grab == null || !_grab.HoldsSide(input.Node)) &&
                         !SideHeld(input.Node) && (!input.IsHand || !_inputs[i + 2].Reliable))
                {
                    int nearest = -1;
                    float best = TakeDistance;
                    for (int slot = 0; slot < _tools.Count; slot++)
                    {
                        float d = Distance(slot, input.Position, input.Node);
                        if (d < best)
                        {
                            best = d;
                            nearest = slot;
                        }
                    }
                    if (nearest >= 0)
                    {
                        _reserved[i % 2] = true;
                        FolioTool tool = _tools[nearest];
                        if (!tool.Available)
                            ShowSoon();
                        else
                        {
                            _held[i] = tool;
                            _sticky[i] = false; // Tomada con pellizco: se suelta al abrir, como siempre.
                            input.LostSince = -1f;
                            tool.Take(input.Node, input.IsHand,
                                      _rig.trackingSpace != null ? _rig.trackingSpace : _rig.transform, input.Position,
                                      input.Rotation);
                            _icons[nearest].gameObject.SetActive(false);
                            justTaken = true;
                        }
                    }
                }
                Dispatch(i, justTaken);
            }
            TouchPlaceholders();
            UpdateWatchTouches();
            UpdateGravity();
            if (!_book.RealisticPhysics)
                foreach (FolioTool tool in _tools)
                    tool.FreezeWorld();
            ApplySuppression();
            if (_notice != null && _notice.gameObject.activeSelf)
            {
                Camera camera = Camera.main;
                if (camera != null)
                    _notice.transform.SetPositionAndRotation(
                        camera.transform.position + camera.transform.forward * 0.45f, camera.transform.rotation);
                if (Time.unscaledTime >= _noticeUntil)
                    _notice.gameObject.SetActive(false);
            }
        }

        private bool SideHeld(XRNode side) => _held[side == XRNode.LeftHand ? 0 : 1] != null
                                              || _held[side == XRNode.LeftHand ? 2 : 3] != null;

        private void UpdateHeld(int i)
        {
            GrabInput input = _inputs[i];
            FolioTool tool = _held[i];
            if (!input.Reliable)
            {
                if (input.LostSince < 0f)
                    input.LostSince = Time.unscaledTime;
                tool.OtherIndexTouch(Vector3.zero, false);
                if (Time.unscaledTime - input.LostSince > 0.3f)
                    Return(tool); // Nunca dejar caer por oclusión/suspensión.
                return;
            }
            input.LostSince = -1f;
            tool.Hold(input.Position, input.Rotation);
            if (UpdateSticky(i, tool, input))
                return;
            if (input.Pressed)
                return;
            // Se evalúa la pose de suelta actual, no la del cuadro anterior.
            if (_wristValid && Vector3.Distance(tool.transform.position, _wrist.position) < ReturnDistance)
                Return(tool);
            else
            {
                tool.Release(_book.RealisticPhysics);
                _held[i] = null;
                _sticky[i] = false;
            }
        }

        private void Dispatch(int i, bool justTaken)
        {
            GrabInput input = _inputs[i];
            bool trigger = Down(input.Device, CommonUsages.triggerButton),
                 primary = Down(input.Device, CommonUsages.primaryButton),
                 secondary = Down(input.Device, CommonUsages.secondaryButton);
            var buttons = new ToolButtons { Trigger = trigger,
                                            Primary = primary,
                                            Secondary = secondary,
                                            TriggerDown = trigger && !_triggerWas[i],
                                            PrimaryDown = primary && !_primaryWas[i],
                                            SecondaryDown = secondary && !_secondaryWas[i] };
            input.Device.TryGetFeatureValue(CommonUsages.primary2DAxis, out buttons.Stick);
            _triggerWas[i] = !input.Reliable || trigger;
            _primaryWas[i] = !input.Reliable || primary;
            _secondaryWas[i] = !input.Reliable || secondary;
            FolioTool tool = _held[i];
            if (tool == null)
                return;
            bool active = input.Reliable && !_book.InputPaused && !justTaken;
            if (!input.IsHand && active)
                tool.ControllerInput(buttons);
            GrabInput other = _inputs[input.Node == XRNode.LeftHand ? 1 : 0];
            bool touch = active && input.IsHand && other.Reliable && other.IndexTip != null &&
                         !_inputs[input.Node == XRNode.LeftHand ? 3 : 2].Reliable;
            tool.OtherIndexTouch(touch ? other.IndexTip.position : Vector3.zero, touch);
        }

        private static bool Down(InputDevice device, InputFeatureUsage<bool> usage) =>
            device.isValid && device.TryGetFeatureValue(usage, out bool down) && down;

        private void TouchPlaceholders()
        {
            GrabInput right = _inputs[1];
            bool valid = _wristValid && !_book.InputPaused && right.Reliable && right.IndexTip != null &&
                         !_inputs[3].Reliable && !SideHeld(XRNode.RightHand);
            for (int i = 0; i < _tools.Count; i++)
            {
                if (!valid || _tools[i].Available)
                {
                    _touchArmed[i] = false;
                    continue;
                }
                float d = Vector3.Distance(right.IndexTip.position, _icons[i].position);
                if (d > 0.025f)
                    _touchArmed[i] = true;
                if (d < 0.012f && _touchArmed[i])
                {
                    _touchArmed[i] = false;
                    _reserved[1] = true;
                    ShowSoon();
                }
            }
        }

        private void ShowSoon()
        {
            if (_notice == null)
                return;
            _notice.text = "Próximamente";
            _noticeUntil = Time.unscaledTime + 2.5f;
            _notice.gameObject.SetActive(true);
        }

        private void ApplySuppression()
        {
            for (int i = 0; i < 2; i++)
            {
                XRNode side = i == 0 ? XRNode.LeftHand : XRNode.RightHand;
                bool blocked = _reserved[i] || SideHeld(side);
                if (_grab != null)
                    _grab.SetToolSuppressed(side, blocked);
                if (_book != null)
                    _book.SetToolInputSuppressed(side, blocked);
            }
        }

        private void Return(FolioTool tool)
        {
            for (int i = 0; i < 4; i++)
                if (_held[i] == tool)
                {
                    _held[i] = null;
                    _sticky[i] = false;
                    _reserved[i % 2] = true;
                }
            tool.ReturnToWatch();
            int slot = _tools.IndexOf(tool);
            if (slot >= 0 && _icons[slot] != null)
                _icons[slot].gameObject.SetActive(true);
        }

        public void ReturnAll()
        {
            CancelFlights();
            foreach (FolioTool tool in _tools)
                if (tool != null && tool.State != ToolState.Stored)
                    Return(tool);
            foreach (GrabInput input in _inputs)
            {
                input.StartConsumed = input.WasGrip = true;
                input.ClearMotion();
            }
            ApplySuppression();
        }

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            if (!focused)
            {
                ReturnAll();
                if (_watch != null)
                    _watch.gameObject.SetActive(false);
            }
        }
        private void OnApplicationPause(bool paused)
        {
            OnApplicationFocus(!paused);
        }
        private void OnDisable()
        {
            ReturnAll();
            for (int i = 0; i < 2; i++)
                _reserved[i] = false;
            ApplySuppression();
            if (_watch != null)
                _watch.gameObject.SetActive(false);
        }
        private void OnDestroy()
        {
            // Las herramientas sostenidas pueden ser hijas del rig: recuperar antes de destruir.
            ReturnAll();
            _reserved[0] = _reserved[1] = false;
            ApplySuppression();
            foreach (FolioTool tool in _tools)
                if (tool != null)
                    Destroy(tool.gameObject);
            if (_watch != null)
                Destroy(_watch.gameObject);
            _visuals?.Dispose();
            if (Instance == this)
                Instance = null;
        }
    }
}
