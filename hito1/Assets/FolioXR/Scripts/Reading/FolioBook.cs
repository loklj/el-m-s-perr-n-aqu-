// Folio XR - Hito 1
// Libro 3D simple: dos paginas de un PDF sobre una tapa, frente al usuario.
//
// Controles (mandos Touch):
//   A o X              -> siguiente doble pagina
//   B o Y              -> doble pagina anterior
//   Clic del stick der. -> volver a poner el libro frente a la vista
//   Stick izq. arriba/abajo -> nitidez en passthrough; caminar en la casa
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
using FolioXR.Accessibility;
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
        /// <summary>Identificador estable del documento (nombre + tamaño) para guardar trazos por página.</summary>
        public string DocumentId { get; private set; }
        /// <summary>Página izquierda visible (desde 0) o -1 mientras no hay doble página lista.</summary>
        public int LeftPageIndex => _spreadReady ? _leftIndex : -1;
        public bool RightPageVisible => _spreadReady && rightPage != null && rightPage.enabled;
        public int PageCount => _pageCount;

        // ── Encargos 8 y 9 (Claude, 11 nov 2026): manga, animación, modo acostado ──
        public const string LyingPrefsKey = "folioxr.lying";
        private const string RtlPrefsPrefix = "folioxr.rtl.";
        /// <summary>Lectura de derecha a izquierda (manga), guardada por libro.</summary>
        public bool RightToLeft { get; private set; }
        /// <summary>Página (desde 0) que muestra cada lado, o -1 si ese lado está vacío.</summary>
        public int LeftSlotPage => !_spreadReady ? -1 : !RightToLeft ? _leftIndex : _leftIndex + 1 < _pageCount ? _leftIndex + 1 : -1;
        public int RightSlotPage => !_spreadReady ? -1 : RightToLeft ? _leftIndex : _leftIndex + 1 < _pageCount ? _leftIndex + 1 : -1;
        /// <summary>Modo acostado: el libro se coloca hacia donde mira la cabeza, aunque sea el techo.</summary>
        public static bool LyingMode => PlayerPrefs.GetInt(LyingPrefsKey, 0) != 0;
        private PageTurnEffect _turn;
        /// <summary>Tapas de cuero con título o tapas propias (11 nov 2026).</summary>
        public BookCover Cover { get; private set; }
        private bool _coverPagesPending;

        internal void RequestCoverPages(BookCover cover)
        {
            if (!_coverPagesPending) StartCoroutine(LoadCoverPages(cover));
        }

        /// <summary>Primera y última página a 1024 px para las tapas propias, cuando el libro está libre.</summary>
        private IEnumerator LoadCoverPages(BookCover cover)
        {
            _coverPagesPending = true;
            while (_busy || _renderer == null || _pageCount <= 0) yield return null;
            string document = DocumentId;
            _busy = true;
            Texture2D front = null, back = null;
            yield return _renderer.RenderPage(0, 1024, (t, r) => front = t);
            if (_pageCount > 1) yield return _renderer.RenderPage(_pageCount - 1, 1024, (t, r) => back = t);
            _busy = false;
            _coverPagesPending = false;
            if (document != DocumentId)
            {
                if (front != null) Destroy(front);
                if (back != null) Destroy(back);
                yield break;
            }
            cover.SetCoverPages(front, back);
        }

        public static void SetLyingMode(bool value)
        {
            PlayerPrefs.SetInt(LyingPrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FolioXR] Modo acostado: " + (value ? "sí" : "no"));
        }

        public static bool GetRightToLeft(string documentId) =>
            !string.IsNullOrEmpty(documentId) && PlayerPrefs.GetInt(RtlPrefsPrefix + documentId, 0) != 0;

        /// <summary>Lectura manga de un libro que puede no estar abierto (configuración al agregarlo).</summary>
        public void SetRightToLeftFor(string documentId, bool value)
        {
            if (documentId == DocumentId) { SetRightToLeft(value); return; }
            if (string.IsNullOrEmpty(documentId)) return;
            PlayerPrefs.SetInt(RtlPrefsPrefix + documentId, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetRightToLeft(bool value)
        {
            if (RightToLeft == value) return;
            RightToLeft = value;
            if (!string.IsNullOrEmpty(DocumentId))
            {
                PlayerPrefs.SetInt(RtlPrefsPrefix + DocumentId, value ? 1 : 0);
                PlayerPrefs.Save();
            }
            Debug.Log("[FolioXR] Lectura: " + (value ? "manga (derecha a izquierda)" : "normal"));
            if (Cover != null) Cover.SetRightToLeft(value);
            // Vuelve a mostrar la doble página con los lados cambiados (mismo índice: sin animación).
            if (!_busy && _spreadReady && _renderer != null) StartCoroutine(ShowSpread(_leftIndex));
        }
        /// <summary>Doble página mostrada: página izquierda (desde 0) y total. La biblioteca guarda el progreso.</summary>
        public event System.Action<int, int> SpreadShown;
        /// <summary>Un documento pedido no se pudo abrir: identificador (o null) y mensaje para el usuario.</summary>
        public event System.Action<string, string> DocumentFailed;
        private string _pendingPath, _pendingId, _defaultPath;
        private int _pendingPage;
        private bool _started;

        /// <summary>
        /// Abre otro documento (biblioteca). Si el libro está ocupado o arrancando, se abre apenas pueda;
        /// pedido durante el arranque, reemplaza al PDF de prueba sin mostrarlo.
        /// </summary>
        public void RequestDocument(string path, string documentId, int leftPage)
        {
            _pendingPath = path;
            _pendingId = documentId;
            _pendingPage = Mathf.Max(0, leftPage);
        }

        /// <summary>Vuelve al PDF con el que arranca la app (por ejemplo, al quitar el libro abierto).</summary>
        public void OpenDefaultDocument()
        {
            if (_defaultPath != null) RequestDocument(_defaultPath, null, 0);
        }

        /// <summary>Salta a una página (marcadores). Devuelve false si el libro está ocupado.</summary>
        /// <summary>Índice del renderizador abierto; no crea otra instancia que cierre el libro.</summary>
        public IEnumerator ReadContents(System.Action<ContentsList, string> done)
        {
            string id = DocumentId;
            while (_busy && DocumentId == id) yield return null;
            if (_renderer == null || DocumentId != id) { done(null, "El libro cambió."); yield break; }
            yield return _renderer.GetContents(done);
        }

        public void CancelContents() { _renderer?.CancelContents(); }

        public bool GoToPage(int leftIndex)
        {
            if (_busy || _renderer == null || _pageCount <= 0) return false;
            int target = Mathf.Clamp(leftIndex, 0, _pageCount - 1);
            target -= target % 2;
            StartCoroutine(ShowSpread(target));
            return true;
        }
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

        private readonly bool[] _nextWasDown = new bool[2], _prevWasDown = new bool[2];
        private readonly bool[] _toolInputSuppressed = new bool[2];
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
        /// <summary>Reserva un lado físico para herramientas; consume también el pellizco hasta abrir.</summary>
        public void SetToolInputSuppressed(XRNode side, bool suppressed)
        {
            _toolInputSuppressed[side == Hands.Support ? 0 : 1] = suppressed;
            if (suppressed)
            {
                if (side == Hands.Support) _leftGrabPinchConsumed = true;
                else _rightGrabPinchConsumed = true;
            }
        }

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
            PageTheme.Changed += OnPageThemeChanged;
            FolioAccessibility.Changed += OnAccessibilityChanged;
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

            _defaultPath = pdfPath;
            if (Cover == null) Cover = BookCover.Attach(this);
            if (_pendingPath == null) Cover.Apply(false, 0, "Libro de muestra", false);
            // Un libro de la biblioteca pedido durante el arranque reemplaza al de prueba.
            string startPath = pdfPath, startId = null;
            int startPage = 0;
            if (_pendingPath != null)
            {
                startPath = _pendingPath;
                startId = _pendingId;
                startPage = _pendingPage;
                _pendingPath = null;
            }
            yield return LoadDocument(startPath, startId, startPage);
            _started = true;
        }

        /// <summary>Abre un documento y muestra su página. Si falla, sigue el anterior o vuelve al de arranque.</summary>
        private IEnumerator LoadDocument(string path, string documentId, int leftPage)
        {
            _busy = true;
            string previousPath = _renderer != null ? _renderer.PdfPath : null;
            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }
            var renderer = new PdfPageRenderer(path);
            int pages = 0;
            PdfResult countResult = null;
            yield return renderer.GetPageCount((count, r) =>
            {
                pages = count;
                countResult = r;
            });
            if (!countResult.Ok || pages <= 0)
            {
                string message = countResult.Ok ? "El PDF no tiene páginas." : ErrorMessage(countResult);
                Debug.LogWarning("[FolioXR] No se pudo abrir " + path + ": " +
                    (countResult.Ok ? "sin páginas" : countResult.ErrorKind + ": " + countResult.ErrorDetail));
                renderer.Dispose();
                _busy = false;
                DocumentFailed?.Invoke(documentId, message);
                if (previousPath != null)
                    _renderer = new PdfPageRenderer(previousPath); // Sigue abierto el libro anterior.
                else if (_defaultPath != null && path != _defaultPath)
                    yield return LoadDocument(_defaultPath, null, 0);
                else
                    SetStatus(message + (countResult.Ok ? string.Empty : "\n" + countResult.ErrorDetail), true);
                yield break;
            }

            _renderer = renderer;
            _pageCount = pages;
            DocumentId = documentId ?? BuildDocumentId(path);
            RightToLeft = PlayerPrefs.GetInt(RtlPrefsPrefix + DocumentId, 0) != 0;
            _spreadReady = false; // Otro documento: sin animación de hoja.
            Debug.Log("[FolioXR] PDF: " + path + " (" + pages + " páginas, id=" + DocumentId + ")");
            int start = Mathf.Clamp(leftPage, 0, pages - 1);
            yield return ShowSpread(start - start % 2);
        }

        private void Update()
        {
            bool next = false, prev = false;
            for (int i = 0; i < 2; i++)
            {
                XRNode side = i == 0 ? Hands.Support : Hands.Main;
                bool primary = Pressed(side, CommonUsages.primaryButton);
                bool secondary = Pressed(side, CommonUsages.secondaryButton);
                if (!_toolInputSuppressed[i])
                {
                    next |= primary && !_nextWasDown[i];
                    prev |= secondary && !_prevWasDown[i];
                }
                // Guardar el botón real evita un flanco falso al devolver con A/B/X/Y sostenido.
                _nextWasDown[i] = primary;
                _prevWasDown[i] = secondary;
            }
            bool recenter = Pressed(Hands.Main, CommonUsages.primary2DAxisClick);
            if (!InputPaused && next) NextSpread();
            if (!InputPaused && prev) PreviousSpread();
            if (!InputPaused && recenter && !_recenterWasDown) PlaceInFrontOfView();
            _recenterWasDown = recenter;

            if (pinchToTurnPages) UpdatePinchPageTurn();
            if (_pageThemeDirty && GoToPage(_leftIndex)) _pageThemeDirty = false;
            if (!InputPaused) UpdateRenderScaleInput();
            else _renderScaleStickLatched = true;
            UpdateRenderScaleStatus();
            UpdateBookRecovery();
            if (!_recenterHooked && OVRManager.display != null)
            {
                OVRManager.display.RecenteredPose += OnRecentered;
                _recenterHooked = true;
            }
            // Dos cuadros después del recentrado, cuando la cabeza ya tiene su pose nueva.
            if (_recenterPending > 0 && --_recenterPending == 0) PlaceInFrontOfView();
            if (_pendingPath != null && _started && !_busy)
            {
                string path = _pendingPath, id = _pendingId;
                _pendingPath = null;
                _spreadReady = false;
                StartCoroutine(LoadDocument(path, id, _pendingPage));
            }
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
            scale = Mathf.Clamp(scale, 0.5f, 5f); // Máximo 5x, pedido por el usuario (9 nov 2026).
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
            // Revisión de Claude: techo de 3072 px. Con 4096 px cada página pesaba unos 127 MB
            // con mipmaps y Android tardaba segundos en codificar el PNG. A 3x el libro ocupa
            // ~60° de la vista y el Quest 3 no muestra más de ~1800 px ahí.
            const int MaxPageWidth = 3072;
            int baseline = Mathf.Clamp(renderWidthPixels, MinRenderWidth, MaxPageWidth);
            // Tramos de 256 px limitan trabajo y oscilaciones.
            return BookScale <= 1.5f ? baseline : Mathf.Min(MaxPageWidth,
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
            if (_environment == null) _environment = FindFirstObjectByType<FolioEnvironmentController>();
            if (_environment != null && _environment.IsActive)
            {
                _renderScaleStickLatched = true;
                return;
            }
            float y = FolioXR.Tools.ToolRegistry.ReservesStick(Hands.Support)
                ? 0f : ReadAxis(Hands.Support, CommonUsages.primary2DAxis).y;
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
                    (_environment != null && _environment.IsActive ? " (Ajustes)." : " (stick izquierdo arriba/abajo).") +
                    " Más nitidez usa más batería.";
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
                if (!_toolInputSuppressed[0] && HandReliable(_leftHand) && !left) _leftGrabPinchConsumed = false;
                if (!_toolInputSuppressed[1] && HandReliable(_rightHand) && !right) _rightGrabPinchConsumed = false;
            }

            if (!InputPaused && !IsGrabbed && Time.time >= _nextPinchTime)
            {
                if (!_toolInputSuppressed[1] && right && !_rightPinchWas && !_rightGrabPinchConsumed && !NearBook(_rightHand))
                {
                    NextSpread();
                    _nextPinchTime = Time.time + PinchCooldownSeconds;
                }
                else if (!_toolInputSuppressed[0] && !LeftPinchSuppressed && left && !_leftPinchWas && !_leftGrabPinchConsumed && !NearBook(_leftHand))
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

        // Modo zurdo: las manos cambian de papel (pasar página, apoyo) y se vuelven a buscar.
        private void OnAccessibilityChanged()
        {
            _leftHand = _rightHand = null;
            FindHands();
        }

        private void FindHands()
        {
            OVRHand[] hands = FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (OVRHand hand in hands)
            {
                OVRPlugin.Hand side = hand.GetHand();
                if (side == Hands.SupportHand && _leftHand == null) _leftHand = hand;
                else if (side == Hands.MainHand && _rightHand == null) _rightHand = hand;
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
            bool lying = LyingMode;
            if (lying) direction = head.forward;

            Vector3 position = head.position + direction * viewDistance + (lying ? -head.up : Vector3.down) * belowEyes;
            Vector3 awayFromEyes = position - head.position;
            if (awayFromEyes.sqrMagnitude < 0.0001f) return;

            // Las paginas miran hacia -Z local: apuntar +Z lejos de los ojos
            // deja las paginas de frente, inclinadas como en un atril.
            Quaternion rotation = Quaternion.LookRotation(awayFromEyes.normalized, lying ? head.up : Vector3.up);

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

            bool turning = _spreadReady && leftIndex != _leftIndex && _leftTexture != null;
            bool forward = leftIndex > _leftIndex;
            _leftIndex = leftIndex;
            _spreadReady = true;
            _renderedWidth = width;
            SpreadShown?.Invoke(leftIndex, _pageCount);
            // En manga la primera página de la pareja va a la derecha.
            Texture2D newLeft = RightToLeft ? right : left, newRight = RightToLeft ? left : right;
            if (left.width > 0)
            {
                _aspectHeightOverWidth = left.height / (float)left.width;
                ApplyLayout();
            }
            if (_turn == null) _turn = PageTurnEffect.Create(leftPage != null ? leftPage : rightPage);
            if (turning && _turn != null && leftPage != null && rightPage != null && !FolioAccessibility.ReduceMotion)
            {
                // La hoja sale del lado que se deja y cae sobre el otro; debajo ya está la página nueva.
                bool towardLeft = forward != RightToLeft;
                Texture2D lifted = towardLeft ? _rightTexture : _leftTexture;
                if (lifted != null)
                {
                    if (towardLeft) ShowPage(rightPage, newRight);
                    else ShowPage(leftPage, newLeft);
                    float pageWidth = rightPage.transform.localScale.x;
                    yield return _turn.Play(towardLeft, lifted, towardLeft ? newLeft : newRight, pageWidth,
                        rightPage.transform.localScale.y, gutter * 0.5f);
                }
            }
            if (_leftTexture != null && _leftTexture != newLeft) Destroy(_leftTexture);
            if (_rightTexture != null && _rightTexture != newRight) Destroy(_rightTexture);
            _leftTexture = newLeft;
            _rightTexture = newRight;
            ShowPage(leftPage, newLeft);
            ShowPage(rightPage, newRight);

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

        private static string BuildDocumentId(string path)
        {
            if (string.IsNullOrEmpty(path)) return "vista-previa";
            try
            {
                var info = new System.IO.FileInfo(path);
                return System.IO.Path.GetFileNameWithoutExtension(path) + "-" + (info.Exists ? info.Length : 0L);
            }
            catch (System.Exception)
            {
                return System.IO.Path.GetFileNameWithoutExtension(path);
            }
        }

        private void ShowPage(Renderer page, Texture2D texture)
        {
            if (page == null) return;
            page.enabled = texture != null;
            // .material crea una copia propia del material para este libro.
            if (texture != null)
            {
                page.material.mainTexture = texture;
                page.material.color = PageTheme.Tint(_renderer != null ? _renderer.PdfPath : null);
            }
        }

        private void ReplaceTexture(ref Texture2D slot, Texture2D incoming, Renderer target)
        {
            if (slot != null && slot != incoming) Destroy(slot);
            slot = incoming;
            if (target != null && incoming != null)
            {
                // .material crea una copia propia del material para este libro.
                target.material.mainTexture = incoming;
                target.material.color = PageTheme.Tint(_renderer != null ? _renderer.PdfPath : null);
            }
        }

        // Modo de página (día/sepia/noche): se vuelve a dibujar la doble página actual.
        private void OnPageThemeChanged()
        {
            if (!GoToPage(_leftIndex)) _pageThemeDirty = true;
        }

        private bool _pageThemeDirty;

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
            if (Cover != null) Cover.Layout();
        }

        private void ShowError(PdfResult result)
        {
            Debug.LogWarning("[FolioXR] " + result.ErrorKind + ": " + result.ErrorDetail);
            SetStatus(ErrorMessage(result) + "\n" + result.ErrorDetail, true);
        }

        private static string ErrorMessage(PdfResult result)
        {
            string message;
            switch (result.ErrorKind)
            {
                case "PASSWORD":
                    message = "Este PDF necesita una contraseña para abrirse.\n(El Hito 1 todavía no la pide.)";
                    break;
                case "DRM":
                    message = "Este libro tiene DRM y no se puede abrir.";
                    break;
                case "IO":
                    // PdfRenderer avisa así de un daño estructural («file not in PDF format or corrupted»).
                    string detail = result.ErrorDetail ?? string.Empty;
                    message = detail.Contains("corrupt") || detail.Contains("not in PDF")
                        ? "El archivo parece estar dañado. Prueba a volver a descargarlo."
                        : "No se pudo leer el archivo. Prueba a copiarlo de nuevo.";
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
            return message;
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
            // Al suspender o reanudar el libro queda quieto: el primer cuadro tras la pausa trae un salto de
            // tiempo y de seguimiento que podría lanzarlo o hacerlo atravesar el suelo.
            if (_body != null && !_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.Sleep();
            }
        }

        private void OnRecentered()
        {
            // Recentrar del sistema (botón Meta mantenido): el libro vuelve frente a la vista, salvo en la mano.
            if (IsGrabbed) return;
            Debug.Log("[FolioXR] Recentrado del sistema: libro frente a la vista");
            _recenterPending = 2;
        }

        private int _recenterPending;
        private bool _recenterHooked;

        private void OnDestroy()
        {
            PageTheme.Changed -= OnPageThemeChanged;
            FolioAccessibility.Changed -= OnAccessibilityChanged;
            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }
            if (_recenterHooked && OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
            if (_physicsMaterial != null) Destroy(_physicsMaterial);
            if (_leftTexture != null) Destroy(_leftTexture);
            if (_rightTexture != null) Destroy(_rightTexture);
        }
    }
}
