// Folio XR - Hito 1
// Libro 3D simple: dos paginas de un PDF sobre una tapa, frente al usuario.
//
// Controles (mandos Touch):
//   A o X              -> siguiente doble pagina
//   B o Y              -> doble pagina anterior
//   Clic del stick der. -> volver a poner el libro frente a la vista
//   Stick izq. arriba/abajo -> subir/bajar nitidez (1,4 a 1,8, Hito 1.2)
// Con las manos (Hito 1.1):
//   Pellizco de la mano derecha (indice + pulgar) lejos del libro -> siguiente
//   Pellizco de la mano izquierda lejos del libro                 -> anterior
//   Cerca del libro el pellizco se deja para BookGrab (agarre propio).
//   Pasar la hoja tomandola llega mas adelante.
//
// Nitidez (Hito 1.1): al iniciar en el visor sube la resolucion de render
// (renderScale de URP) y activa MSAA; las texturas de pagina usan filtrado
// anisotropico y un sesgo de mipmap negativo.
//
// PDF que se abre:
//   1) <persistentDataPath>/libro.pdf si existe (se puede copiar con adb), o
//   2) StreamingAssets/folio-prueba.pdf, incluido en la app.

using System.Collections;
using System.Globalization;
using System.IO;
using FolioXR.Environments;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace FolioXR.Reading
{
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class FolioBook : MonoBehaviour
    {
        public const string BundledPdfName = "folio-prueba.pdf";
        public const string UserPdfName = "libro.pdf";
        public const float MinRenderScale = 1.4f;
        public const float MaxRenderScale = 1.8f;
        public const float RenderScaleStep = 0.1f;

        [Header("Partes del libro (las asigna el menu Folio XR)")]
        public Renderer leftPage;
        public Renderer rightPage;
        public Transform cover;
        public BoxCollider bookCollider;
        public TextMesh statusText;

        [Header("Tamaño y calidad")]
        [Tooltip("Alto de cada página en metros.")]
        public float pageHeight = 0.26f;
        [Tooltip("Separación entre páginas (lomo) en metros.")]
        public float gutter = 0.008f;
        [Tooltip("Ancho en píxeles con que se dibuja cada página (mínimo 1800).")]
        public int renderWidthPixels = 1400;

        [Header("Nitidez (stick izquierdo arriba/abajo, solo se aplica en el visor)")]
        [Tooltip("Resolución inicial si no hay un ajuste guardado. De 1,4 a 1,8 en pasos de 0,1. " +
                 "Más nitidez usa más batería.")]
        [Range(MinRenderScale, MaxRenderScale)] public float renderScale = MinRenderScale;
        [Tooltip("Muestras de antialiasing MSAA: 1, 2, 4 u 8.")]
        public int msaaSamples = 4;

        [Header("Pasar página con las manos")]
        public bool pinchToTurnPages = true;
        [Tooltip("Si la muñeca está más cerca que esto (metros) del libro, el pellizco se " +
                 "toma como agarre y no pasa página.")]
        public float pinchIgnoreNearBook = 0.15f;

        [Header("Colocación")]
        public bool placeInFrontOfViewOnStart = true;
        public float viewDistance = 0.45f;
        public float belowEyes = 0.12f;

        public const string PhysicsPrefsKey = "folioxr.bookPhysics";
        public const string ScalePrefsKey = "folioxr.bookScale";
        public bool RealisticPhysics { get; private set; }
        public float BookScale { get; private set; } = 1f;
        public event System.Action Recalled;
        public event System.Action ScaleReset;
        private Rigidbody _body;
        private PhysicsMaterial _physicsMaterial;
        private Vector3 _baseScale;
        private FolioEnvironmentController _environment;
        private int _renderedWidth;
        private float _resolutionChangedAt;
        private bool _resolutionDirty;
        private bool _spreadReady;

        private PdfPageRenderer _renderer;
        private int _pageCount;
        private int _leftIndex; // pagina izquierda de la doble pagina actual (indice desde 0)
        private bool _busy;
        private Texture2D _leftTexture;
        private Texture2D _rightTexture;
        private float _aspectHeightOverWidth = 1.414f; // A4/A5 hasta conocer el PDF real

        private bool _nextWasDown;
        private bool _prevWasDown;
        private bool _recenterWasDown;

        private const string RenderScalePrefsKey = "folioxr.renderScale";
        private const float RenderScaleStatusSeconds = 2.5f;
        private float _currentRenderScale;
        private bool _renderScaleStickLatched;
        private float _renderScaleStatusUntil;
        private string _lastPageStatus = string.Empty;
        private bool _statusIsError;

        // Las escenas del Hito 1 ya guardaron 1400; se sube sin rehacer el libro.
        private const int MinRenderWidth = 1800;
        private const float PinchCooldownSeconds = 0.4f;

        private OVRHand _leftHand;
        private OVRHand _rightHand;
        private bool _leftPinchWas;
        private bool _rightPinchWas;
        private bool _leftGrabPinchConsumed;
        private bool _rightGrabPinchConsumed;
        private bool _isGrabbed;
        private float _nextPinchTime;

        public float RenderScale => _currentRenderScale;
        public bool InputPaused { get; set; }
        public bool LeftPinchSuppressed { get; set; }
        public bool IsGrabbed
        {
            get => _isGrabbed;
            set
            {
                _isGrabbed = value;
                // También sobrevive a una suelta por pérdida prolongada del seguimiento.
                if (value) _leftGrabPinchConsumed = _rightGrabPinchConsumed = true;
            }
        }

        private void Awake()
        {
            _baseScale = transform.localScale;
            RealisticPhysics = PlayerPrefs.GetInt(PhysicsPrefsKey, 1) != 0;
            _body = GetComponent<Rigidbody>();
            if (_body == null) _body = gameObject.AddComponent<Rigidbody>();
            _body.mass = 0.6f;
            _body.detectCollisions = true;
            _body.linearDamping = 0.05f;
            _body.angularDamping = 0.15f;
            _physicsMaterial = new PhysicsMaterial("FolioXR libro: fricción alta")
            {
                staticFriction = 0.85f, dynamicFriction = 0.75f, bounciness = 0.02f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            if (bookCollider != null)
            {
                bookCollider.isTrigger = false;
                bookCollider.sharedMaterial = _physicsMaterial;
            }
            FreezePhysics();
            SetBookScale(PlayerPrefs.GetFloat(ScalePrefsKey, 1f));
            renderScale = ClampRenderScale(renderScale);
            _currentRenderScale = ClampRenderScale(PlayerPrefs.GetFloat(RenderScalePrefsKey, renderScale));
            ApplyRenderQuality();
        }

        private void OnValidate()
        {
            renderScale = ClampRenderScale(renderScale);
        }

        private IEnumerator Start()
        {
            // OVRManager ya pasó por su Awake aquí; en el Awake de este script podría no existir.
            DisableOvrDynamicResolution();
            ApplyLayout();
            SetStatus("Cargando el PDF…");

            if (placeInFrontOfViewOnStart)
            {
                // Esperar a que el visor reporte una pose estable.
                yield return new WaitForSeconds(1.0f);
                PlaceInFrontOfView();
            }

            FindHands();
            LogGrabSetup();

            string pdfPath = null;
            string copyError = null;
            yield return ResolvePdfPath(p => pdfPath = p, e => copyError = e);
            if (pdfPath == null && PdfPageRenderer.IsRealRendererAvailable)
            {
                SetStatus("No se pudo preparar el PDF.\n" + copyError, true);
                yield break;
            }

            _renderer = new PdfPageRenderer(pdfPath);
            Debug.Log("[FolioXR] PDF: " + _renderer.PdfPath);

            PdfResult countResult = null;
            yield return _renderer.GetPageCount((count, r) =>
            {
                _pageCount = count;
                countResult = r;
            });
            if (!countResult.Ok)
            {
                ShowError(countResult);
                yield break;
            }
            if (_pageCount <= 0)
            {
                SetStatus("El PDF no tiene páginas.", true);
                yield break;
            }

            yield return ShowSpread(0);
        }

        private void Update()
        {
            bool next = Pressed(XRNode.RightHand, CommonUsages.primaryButton) ||
                        Pressed(XRNode.LeftHand, CommonUsages.primaryButton);
            bool prev = Pressed(XRNode.RightHand, CommonUsages.secondaryButton) ||
                        Pressed(XRNode.LeftHand, CommonUsages.secondaryButton);
            bool recenter = Pressed(XRNode.RightHand, CommonUsages.primary2DAxisClick);

            if (!InputPaused && next && !_nextWasDown) NextSpread();
            if (!InputPaused && prev && !_prevWasDown) PreviousSpread();
            if (!InputPaused && recenter && !_recenterWasDown) PlaceInFrontOfView();

            _nextWasDown = next;
            _prevWasDown = prev;
            _recenterWasDown = recenter;

            if (pinchToTurnPages) UpdatePinchPageTurn();
            if (!InputPaused) UpdateRenderScaleInput();
            else _renderScaleStickLatched = true;
            UpdateRenderScaleStatus();
            UpdateBookRecovery();
            // Agrupa cambios continuos del gesto; no iniciar renders cada cuadro.
            if (_resolutionDirty && !_busy && _spreadReady && _renderer != null &&
                Time.unscaledTime - _resolutionChangedAt >= 0.35f)
            {
                _resolutionDirty = false;
                if (DesiredPageWidth() != _renderedWidth) StartCoroutine(ShowSpread(_leftIndex));
            }
        }

        public void FreezePhysics()
        {
            if (_body == null) return;
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.interpolation = RigidbodyInterpolation.None;
        }

        public void ReleasePhysics(Vector3 velocity, Vector3 angularVelocity)
        {
            if (_body == null) return;
            if (!RealisticPhysics) { FreezePhysics(); return; }
            _body.isKinematic = false;
            _body.useGravity = true;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.linearVelocity = velocity;
            _body.angularVelocity = angularVelocity;
            _body.WakeUp();
        }

        public void SetRealisticPhysics(bool realistic)
        {
            RealisticPhysics = realistic;
            PlayerPrefs.SetInt(PhysicsPrefsKey, realistic ? 1 : 0);
            PlayerPrefs.Save();
            // Activar Realista no hace caer un libro que acaba de traerse.
            if (!realistic) FreezePhysics();
        }

        public void SetBookScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale)) scale = 1f;
            scale = Mathf.Clamp(scale, 0.5f, 3f);
            int previousWidth = DesiredPageWidth();
            BookScale = scale;
            transform.localScale = _baseScale * scale;
            if (previousWidth != DesiredPageWidth())
            {
                _resolutionDirty = true;
                _resolutionChangedAt = Time.unscaledTime;
            }
        }

        public void SaveBookScale()
        {
            PlayerPrefs.SetFloat(ScalePrefsKey, BookScale);
            PlayerPrefs.Save();
        }

        public void ResetBookScale()
        {
            SetBookScale(1f);
            SaveBookScale();
            ScaleReset?.Invoke();
        }

        private int DesiredPageWidth()
        {
            int baseline = Mathf.Clamp(renderWidthPixels, MinRenderWidth, 4096);
            // Tramos de 256 px limitan trabajo y oscilaciones, con techo absoluto 4096.
            return BookScale <= 1.5f ? baseline : Mathf.Min(4096,
                Mathf.CeilToInt(baseline * BookScale / 256f) * 256);
        }

        private void UpdateBookRecovery()
        {
            if (_environment == null) _environment = FindFirstObjectByType<FolioEnvironmentController>();
            Camera view = Camera.main;
            if (IsGrabbed || view == null || _environment == null) return;
            if (transform.position.y < _environment.FloorHeight - 2f ||
                Vector3.Distance(transform.position, view.transform.position) > 20f)
            {
                Debug.Log("[FolioXR] Libro: recuperación automática por caída/distancia.");
                PlaceInFrontOfView();
            }
        }

        private void UpdateRenderScaleInput()
        {
            float y = ReadAxis(XRNode.LeftHand, CommonUsages.primary2DAxis).y;
            if (Mathf.Abs(y) < 0.3f) _renderScaleStickLatched = false;
            if (_renderScaleStickLatched || Mathf.Abs(y) <= 0.7f) return;

            // Un paso por movimiento; solo volver al centro permite otro paso.
            _renderScaleStickLatched = true;
            SetRenderScale(_currentRenderScale + (y > 0f ? RenderScaleStep : -RenderScaleStep));
        }

        /// <summary>Mismo camino para el stick y las vistas del menú.</summary>
        public void SetRenderScale(float value)
        {
            float next = ClampRenderScale(value);
            bool changed = !Mathf.Approximately(next, _currentRenderScale);
            string displayValue = next.ToString("F1", CultureInfo.InvariantCulture);

            if (changed)
            {
                _currentRenderScale = next;
                ApplyRenderQuality();
                PlayerPrefs.SetFloat(RenderScalePrefsKey, _currentRenderScale);
                PlayerPrefs.Save();
                Debug.Log("[FolioXR] Nitidez: renderScale=" + displayValue + " (guardado).");
            }
            if (_statusIsError) return;

            // En el límite también se avisa, para que se note que el stick respondió.
            string limit = !changed ? (next >= MaxRenderScale ? " (máximo)" : next <= MinRenderScale ? " (mínimo)" : string.Empty) : string.Empty;
            _renderScaleStatusUntil = Time.unscaledTime + RenderScaleStatusSeconds;
            if (statusText != null)
                statusText.text = "Nitidez: " + displayValue.Replace('.', ',') + "x" + limit +
                    " (stick izquierdo arriba/abajo). Más nitidez usa más batería.";
        }

        private void UpdateRenderScaleStatus()
        {
            if (_renderScaleStatusUntil <= 0f || Time.unscaledTime < _renderScaleStatusUntil) return;
            _renderScaleStatusUntil = 0f;
            if (!_statusIsError) SetStatus(_lastPageStatus);
        }

        private static float ClampRenderScale(float value)
        {
            if (float.IsNaN(value)) return MinRenderScale;
            value = Mathf.Clamp(value, MinRenderScale, MaxRenderScale);
            // Ajustar a pasos evita acumular errores al subir y bajar repetidamente.
            float steps = Mathf.Round((value - MinRenderScale) / RenderScaleStep);
            return Mathf.Clamp(MinRenderScale + steps * RenderScaleStep, MinRenderScale, MaxRenderScale);
        }

        private void UpdatePinchPageTurn()
        {
            bool left = IsPinching(_leftHand);
            bool right = IsPinching(_rightHand);

            if (IsGrabbed) _leftGrabPinchConsumed = _rightGrabPinchConsumed = true;
            else
            {
                // Solo una apertura fiable rearma la mano: baja confianza no significa soltar.
                if (HandReliable(_leftHand) && !left) _leftGrabPinchConsumed = false;
                if (HandReliable(_rightHand) && !right) _rightGrabPinchConsumed = false;
            }

            if (!InputPaused && !IsGrabbed && Time.time >= _nextPinchTime)
            {
                if (right && !_rightPinchWas && !_rightGrabPinchConsumed && !NearBook(_rightHand))
                {
                    NextSpread();
                    _nextPinchTime = Time.time + PinchCooldownSeconds;
                }
                else if (!LeftPinchSuppressed && left && !_leftPinchWas && !_leftGrabPinchConsumed && !NearBook(_leftHand))
                {
                    PreviousSpread();
                    _nextPinchTime = Time.time + PinchCooldownSeconds;
                }
            }

            _leftPinchWas = left;
            _rightPinchWas = right;
        }

        private static bool IsPinching(OVRHand hand)
        {
            return HandReliable(hand) && hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        }

        private static bool HandReliable(OVRHand hand)
        {
            return hand != null && hand.isActiveAndEnabled && hand.IsTracked && hand.IsDataHighConfidence &&
                   !hand.IsSystemGestureInProgress;
        }

        private bool NearBook(OVRHand hand)
        {
            if (bookCollider == null || hand == null) return false;
            Vector3 wrist = hand.transform.position;
            return Vector3.Distance(bookCollider.ClosestPoint(wrist), wrist) < pinchIgnoreNearBook;
        }

        private void FindHands()
        {
            OVRHand[] hands = FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (OVRHand hand in hands)
            {
                OVRPlugin.Hand side = hand.GetHand();
                if (side == OVRPlugin.Hand.HandLeft && _leftHand == null) _leftHand = hand;
                else if (side == OVRPlugin.Hand.HandRight && _rightHand == null) _rightHand = hand;
            }
            Debug.Log("[FolioXR] Manos para pasar página: " + hands.Length + " OVRHand, izquierda=" +
                      (_leftHand != null) + ", derecha=" + (_rightHand != null));
        }

        /// <summary>Deja en el registro qué piezas de agarre hay en la escena (diagnóstico).</summary>
        private static void LogGrabSetup()
        {
            int handInteractors = 0, controllerInteractors = 0, handInteractables = 0, grabInteractables = 0;
            MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (MonoBehaviour behaviour in all)
            {
                if (behaviour == null) continue;
                switch (behaviour.GetType().Name)
                {
                    case "HandGrabInteractor": handInteractors++; break;
                    case "GrabInteractor": controllerInteractors++; break;
                    case "HandGrabInteractable": handInteractables++; break;
                    case "GrabInteractable": grabInteractables++; break;
                }
            }
            Debug.Log("[FolioXR] Agarre: HandGrabInteractor=" + handInteractors + ", GrabInteractor=" +
                      controllerInteractors + ", HandGrabInteractable=" + handInteractables +
                      ", GrabInteractable=" + grabInteractables);
        }

        private void ApplyRenderQuality()
        {
            // En el Editor no se toca: cambiaría el asset de URP guardado en el proyecto.
            if (Application.isEditor) return;

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                Debug.LogWarning("[FolioXR] No se encontró el asset de URP; no se ajusta la nitidez.");
                return;
            }
            DisableOvrDynamicResolution();
            urp.renderScale = ClampRenderScale(_currentRenderScale);
            urp.msaaSampleCount = Mathf.ClosestPowerOfTwo(Mathf.Clamp(msaaSamples, 1, 8));
            Debug.Log("[FolioXR] Nitidez: renderScale=" + urp.renderScale + ", MSAA=" + urp.msaaSampleCount);
        }

        // El Project Setup Tool de Meta puede activar Dynamic Resolution en el OVRManager.
        // Esa función sube y baja la resolución sola y pasa por encima de la nitidez que elige
        // la persona, así que se desactiva y la vista queda a escala completa.
        private static void DisableOvrDynamicResolution()
        {
            if (Application.isEditor) return;
            OVRManager manager = OVRManager.instance;
            if (manager == null || !manager.enableDynamicResolution) return;
            manager.enableDynamicResolution = false;
            XRSettings.renderViewportScale = 1f;
            Debug.Log("[FolioXR] Dynamic Resolution desactivada: la nitidez la controla la app (1,4 a 1,8).");
        }

        [ContextMenu("Siguiente doble página")]
        public void NextSpread()
        {
            if (_busy || _renderer == null) return;
            int target = _leftIndex + 2;
            if (target >= _pageCount) return;
            StartCoroutine(ShowSpread(target));
        }

        [ContextMenu("Doble página anterior")]
        public void PreviousSpread()
        {
            if (_busy || _renderer == null) return;
            int target = Mathf.Max(0, _leftIndex - 2);
            if (target == _leftIndex) return;
            StartCoroutine(ShowSpread(target));
        }

        [ContextMenu("Poner el libro frente a la vista")]
        public void PlaceInFrontOfView()
        {
            Camera view = Camera.main;
            if (view == null) return;

            Recalled?.Invoke();
            IsGrabbed = false;
            FreezePhysics();
            Transform head = view.transform;
            Vector3 flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            // Si se mira casi en vertical (por ejemplo, acostado), usar la vista tal cual.
            Vector3 direction = flatForward.sqrMagnitude > 0.04f ? flatForward.normalized : head.forward;

            Vector3 position = head.position + direction * viewDistance + Vector3.down * belowEyes;
            Vector3 awayFromEyes = position - head.position;
            if (awayFromEyes.sqrMagnitude < 0.0001f) return;

            // Las paginas miran hacia -Z local: apuntar +Z lejos de los ojos
            // deja las paginas de frente, inclinadas como en un atril.
            Quaternion rotation = Quaternion.LookRotation(awayFromEyes.normalized, Vector3.up);

            var body = GetComponent<Rigidbody>();
            if (body != null && body.isKinematic)
            {
                body.position = position;
                body.rotation = rotation;
            }
            transform.SetPositionAndRotation(position, rotation);
        }

        private IEnumerator ShowSpread(int leftIndex)
        {
            _busy = true;
            SetStatus(string.Empty);

            Texture2D left = null;
            Texture2D right = null;
            PdfResult leftResult = null;
            PdfResult rightResult = null;

            int width = DesiredPageWidth();
            _resolutionDirty = false;
            Debug.Log("[FolioXR] Páginas: render a " + width + " px, escala=" + BookScale.ToString("F2"));
            yield return _renderer.RenderPage(leftIndex, width, (t, r) =>
            {
                left = t;
                leftResult = r;
            });

            if (leftResult.Ok && leftIndex + 1 < _pageCount)
            {
                yield return _renderer.RenderPage(leftIndex + 1, width, (t, r) =>
                {
                    right = t;
                    rightResult = r;
                });
            }

            if (!leftResult.Ok)
            {
                ShowError(leftResult);
                _busy = false;
                yield break;
            }

            _leftIndex = leftIndex;
            _spreadReady = true;
            _renderedWidth = width;
            ReplaceTexture(ref _leftTexture, left, leftPage);
            ReplaceTexture(ref _rightTexture, right, rightPage);
            if (rightPage != null) rightPage.enabled = right != null;

            if (left.width > 0)
            {
                _aspectHeightOverWidth = left.height / (float)left.width;
                ApplyLayout();
            }

            if (rightResult != null && !rightResult.Ok)
            {
                ShowError(rightResult);
            }
            else
            {
                int shownRight = right != null ? leftIndex + 2 : leftIndex + 1;
                string preview = PdfPageRenderer.IsRealRendererAvailable
                    ? string.Empty
                    : "Vista previa del Editor: el PDF real solo se dibuja en el Quest.\n";
                _lastPageStatus = preview + "Páginas " + (leftIndex + 1) +
                          (right != null ? "-" + shownRight : string.Empty) + " de " + _pageCount +
                          "\nSiguiente: A/X o pellizco derecho · Anterior: B/Y o pellizco izquierdo";
                SetStatus(_lastPageStatus);
            }

            _busy = false;
        }

        private static void ReplaceTexture(ref Texture2D slot, Texture2D incoming, Renderer target)
        {
            if (slot != null && slot != incoming) Destroy(slot);
            slot = incoming;
            if (target != null && incoming != null)
            {
                // .material crea una copia propia del material para este libro.
                target.material.mainTexture = incoming;
            }
        }

        /// <summary>Ajusta tamaños y posiciones de páginas, tapa y colisionador.</summary>
        public void ApplyLayout()
        {
            float height = Mathf.Max(0.05f, pageHeight);
            float width = height / Mathf.Max(0.1f, _aspectHeightOverWidth);
            float half = width * 0.5f + gutter * 0.5f;

            if (leftPage != null)
            {
                leftPage.transform.localPosition = new Vector3(-half, 0f, 0f);
                leftPage.transform.localRotation = Quaternion.identity;
                leftPage.transform.localScale = new Vector3(width, height, 1f);
            }
            if (rightPage != null)
            {
                rightPage.transform.localPosition = new Vector3(half, 0f, 0f);
                rightPage.transform.localRotation = Quaternion.identity;
                rightPage.transform.localScale = new Vector3(width, height, 1f);
            }

            float coverWidth = width * 2f + gutter + 0.024f;
            float coverHeight = height + 0.02f;
            const float coverDepth = 0.012f;
            if (cover != null)
            {
                // Detras de las paginas (las paginas miran hacia -Z).
                cover.localPosition = new Vector3(0f, 0f, coverDepth * 0.5f + 0.001f);
                cover.localRotation = Quaternion.identity;
                cover.localScale = new Vector3(coverWidth, coverHeight, coverDepth);
            }
            if (bookCollider != null)
            {
                bookCollider.center = new Vector3(0f, 0f, coverDepth * 0.5f);
                bookCollider.size = new Vector3(coverWidth, coverHeight, coverDepth + 0.01f);
            }
            if (statusText != null)
            {
                statusText.transform.localPosition = new Vector3(0f, -(coverHeight * 0.5f + 0.012f), -0.002f);
                statusText.transform.localRotation = Quaternion.identity;
            }
        }

        private void ShowError(PdfResult result)
        {
            string message;
            switch (result.ErrorKind)
            {
                case "PASSWORD":
                    message = "Este PDF necesita una contraseña para abrirse.\n(El Hito 1 todavía no la pide.)";
                    break;
                case "IO":
                    message = "No se pudo leer el archivo. Prueba a copiarlo de nuevo.";
                    break;
                case "MEMORY":
                    message = "La página es demasiado grande para la memoria disponible.";
                    break;
                case "TIMEOUT":
                    message = "El PDF tardó demasiado en responder.";
                    break;
                default:
                    message = "No se pudo abrir el archivo y no conocemos la causa.";
                    break;
            }
            Debug.LogWarning("[FolioXR] " + result.ErrorKind + ": " + result.ErrorDetail);
            SetStatus(message + "\n" + result.ErrorDetail, true);
        }

        private void SetStatus(string message, bool isError = false)
        {
            _statusIsError = isError;
            // El último texto normal (cargando, páginas) se recupera al terminar el aviso de nitidez.
            if (!isError) _lastPageStatus = message ?? string.Empty;
            // Los errores cancelan el aviso; las páginas se actualizan sin ocultarlo.
            if (isError) _renderScaleStatusUntil = 0f;
            if (statusText != null && Time.unscaledTime >= _renderScaleStatusUntil)
                statusText.text = message;
            if (!string.IsNullOrEmpty(message)) Debug.Log("[FolioXR] " + message.Replace('\n', ' '));
        }

        private static bool Pressed(XRNode node, InputFeatureUsage<bool> usage)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(usage, out bool value) && value;
        }

        private static Vector2 ReadAxis(XRNode node, InputFeatureUsage<Vector2> usage)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(usage, out Vector2 value) ? value : Vector2.zero;
        }

        /// <summary>
        /// Decide que PDF abrir. En Android copia el PDF incluido a una ruta normal,
        /// porque PdfRenderer necesita un archivo real (no puede leer dentro del APK).
        /// </summary>
        private static IEnumerator ResolvePdfPath(System.Action<string> found, System.Action<string> failed)
        {
            string userPdf = Path.Combine(Application.persistentDataPath, UserPdfName);
            if (File.Exists(userPdf))
            {
                found(userPdf);
                yield break;
            }

            if (!PdfPageRenderer.IsRealRendererAvailable)
            {
                // En el Editor no se dibuja el PDF; no hace falta copiarlo.
                found(Path.Combine(Application.streamingAssetsPath, BundledPdfName));
                yield break;
            }

            string target = Path.Combine(Application.persistentDataPath, BundledPdfName);
            string source = Application.streamingAssetsPath + "/" + BundledPdfName;
            if (!source.Contains("://")) source = "file://" + source;

            using (UnityWebRequest request = UnityWebRequest.Get(source))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failed(request.error);
                    yield break;
                }

                byte[] data = request.downloadHandler.data;
                try
                {
                    bool upToDate = File.Exists(target) && new FileInfo(target).Length == data.Length;
                    if (!upToDate)
                    {
                        string temp = target + ".tmp";
                        File.WriteAllBytes(temp, data);
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(temp, target);
                    }
                    found(target);
                }
                catch (System.Exception e)
                {
                    failed(e.Message);
                }
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveBookScale();
        }

        private void OnDestroy()
        {
            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }
            if (_physicsMaterial != null) Destroy(_physicsMaterial);
            if (_leftTexture != null) Destroy(_leftTexture);
            if (_rightTexture != null) Destroy(_rightTexture);
        }
    }
}
