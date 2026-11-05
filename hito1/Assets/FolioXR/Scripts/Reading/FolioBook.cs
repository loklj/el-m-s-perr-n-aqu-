// Folio XR - Hito 1
// Libro 3D simple: dos paginas de un PDF sobre una tapa, frente al usuario.
//
// Controles (mandos Touch):
//   A o X              -> siguiente doble pagina
//   B o Y              -> doble pagina anterior
//   Clic del stick der. -> volver a poner el libro frente a la vista
// Con las manos se puede agarrar y mover el libro (lo configura la Grab Quick
// Action de Meta, ver docs/HITO1_GUIA.md). Pasar pagina con las manos llega
// en un hito posterior.
//
// PDF que se abre:
//   1) <persistentDataPath>/libro.pdf si existe (se puede copiar con adb), o
//   2) StreamingAssets/folio-prueba.pdf, incluido en la app.

using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;

namespace FolioXR.Reading
{
    [DisallowMultipleComponent]
    public sealed class FolioBook : MonoBehaviour
    {
        public const string BundledPdfName = "folio-prueba.pdf";
        public const string UserPdfName = "libro.pdf";

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
        [Tooltip("Ancho en píxeles con que se dibuja cada página.")]
        public int renderWidthPixels = 1400;

        [Header("Colocación")]
        public bool placeInFrontOfViewOnStart = true;
        public float viewDistance = 0.45f;
        public float belowEyes = 0.12f;

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

        private IEnumerator Start()
        {
            ApplyLayout();
            SetStatus("Cargando el PDF…");

            if (placeInFrontOfViewOnStart)
            {
                // Esperar a que el visor reporte una pose estable.
                yield return new WaitForSeconds(1.0f);
                PlaceInFrontOfView();
            }

            string pdfPath = null;
            string copyError = null;
            yield return ResolvePdfPath(p => pdfPath = p, e => copyError = e);
            if (pdfPath == null && PdfPageRenderer.IsRealRendererAvailable)
            {
                SetStatus("No se pudo preparar el PDF.\n" + copyError);
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
                SetStatus("El PDF no tiene páginas.");
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

            if (next && !_nextWasDown) NextSpread();
            if (prev && !_prevWasDown) PreviousSpread();
            if (recenter && !_recenterWasDown) PlaceInFrontOfView();

            _nextWasDown = next;
            _prevWasDown = prev;
            _recenterWasDown = recenter;
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

            yield return _renderer.RenderPage(leftIndex, renderWidthPixels, (t, r) =>
            {
                left = t;
                leftResult = r;
            });

            if (leftResult.Ok && leftIndex + 1 < _pageCount)
            {
                yield return _renderer.RenderPage(leftIndex + 1, renderWidthPixels, (t, r) =>
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
                SetStatus(preview + "Páginas " + (leftIndex + 1) +
                          (right != null ? "-" + shownRight : string.Empty) +
                          " de " + _pageCount + "   (A/X siguiente, B/Y anterior)");
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
            SetStatus(message + "\n" + result.ErrorDetail);
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
            if (!string.IsNullOrEmpty(message)) Debug.Log("[FolioXR] " + message.Replace('\n', ' '));
        }

        private static bool Pressed(XRNode node, InputFeatureUsage<bool> usage)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(usage, out bool value) && value;
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

        private void OnDestroy()
        {
            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }
            if (_leftTexture != null) Destroy(_leftTexture);
            if (_rightTexture != null) Destroy(_rightTexture);
        }
    }
}
