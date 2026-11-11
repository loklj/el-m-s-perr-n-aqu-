using FolioXR.Tools.Ink;
using UnityEngine;
using UnityEngine.XR;

namespace FolioXR.Tools
{
    /// <summary>
    /// Tanda D (escrita por Claude). Libreta A5 de 20 páginas rayadas; la hoja mira hacia +Y local.
    /// Esquinas inferiores: tocar con el índice (o la punta del mando) pasa de página. A/X y B/Y si se sostiene con mando.
    /// </summary>
    public sealed class NotebookTool : FolioTool
    {
        public const int PageCount = 20;
        public const string PagePrefsKey = "folioxr.notebookPage";
        public int Page { get; private set; }

        internal static readonly Vector3 ColliderSize = new Vector3(0.156f, 0.012f, 0.218f);
        private const float PageWidth = 0.148f, PageHeight = 0.210f, PageY = 0.0042f;
        private const float CornerTouch = 0.02f, CornerRearm = 0.035f;
        private readonly Transform[] _anchors = new Transform[PageCount];
        private readonly InkCanvas[] _canvases = new InkCanvas[PageCount];
        private readonly bool[] _armed = new bool[2];
        private Transform _nextCorner, _previousCorner;
        private TextMesh _label;

        internal void Build(ToolVisuals visuals)
        {
            Body.mass = 0.25f;
            GripCollider.size = ColliderSize;
            visuals.Box(transform, "Tapa", new Vector3(0f, -0.001f, 0f), new Vector3(0.156f, 0.01f, 0.218f),
                        visuals.Solid("tapa de la libreta", new Color(0.12f, 0.2f, 0.42f)));
            Texture2D lines = visuals.Own(LinedTexture());
            Mesh plane = visuals.Own(PlaneMesh(PageWidth, PageHeight));
            Transform sheet = ToolVisuals.MeshObject(transform, "Hoja rayada", plane, visuals.Textured("hoja rayada", lines))
                                  .transform;
            sheet.localPosition = new Vector3(0f, PageY, 0f);
            _nextCorner = Corner(visuals, "Esquina: página siguiente", 1f);
            _previousCorner = Corner(visuals, "Esquina: página anterior", -1f);

            // Con la libreta en la mano su colisionador se apaga; esta hoja sigue recibiendo el lápiz.
            var surface = new GameObject("Superficie de escritura");
            surface.transform.SetParent(transform, false);
            var trigger = surface.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, PageY, 0f);
            trigger.size = new Vector3(PageWidth, 0.003f, PageHeight);
            surface.AddComponent<InkSurface>().Resolve = ResolvePage;
            gameObject.AddComponent<InkSurface>().Resolve = ResolvePage;

            for (int i = 0; i < PageCount; i++)
            {
                _anchors[i] = new GameObject("Trazos libreta página " + (i + 1)).transform;
                _anchors[i].SetParent(transform, false);
                _anchors[i].localPosition = new Vector3(0f, PageY, 0f);
                _anchors[i].gameObject.SetActive(false);
            }

            var label = new GameObject("Número de página");
            label.transform.SetParent(transform, false);
            label.transform.localPosition = new Vector3(0f, PageY + 0.0006f, -PageHeight * 0.5f + 0.008f);
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _label = label.AddComponent<TextMesh>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = 48;
            _label.characterSize = 0.0009f;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = new Color(0.25f, 0.3f, 0.45f);
            if (_label.font != null)
                label.GetComponent<MeshRenderer>().sharedMaterial = _label.font.material;
            ToolVisuals.Configure(label.GetComponent<Renderer>());

            ShowPage(Mathf.Clamp(PlayerPrefs.GetInt(PagePrefsKey, 0), 0, PageCount - 1), false);
        }

        private Transform Corner(ToolVisuals visuals, string name, float side)
        {
            Transform corner = visuals.Box(transform, name,
                                           new Vector3(side * (PageWidth * 0.5f - 0.006f), PageY + 0.0004f,
                                                       -PageHeight * 0.5f + 0.006f),
                                           new Vector3(0.009f, 0.0006f, 0.009f), visuals.Cyan);
            corner.localRotation = Quaternion.Euler(0f, 45f, 0f);
            return corner;
        }

        private bool ResolvePage(Vector3 tip, Vector3 hitPoint, Vector3 hitNormal, out InkCanvas canvas,
                                 out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            canvas = null;
            surfaceNormal = transform.up;
            surfacePoint = Vector3.zero;
            if (Vector3.Dot(hitNormal, surfaceNormal) < 0.5f)
                return false; // Solo la hoja; la contratapa no se raya.
            Vector3 local = transform.InverseTransformPoint(tip);
            if (Mathf.Abs(local.x) > PageWidth * 0.5f || Mathf.Abs(local.z) > PageHeight * 0.5f)
                return false;
            canvas = CurrentCanvas();
            Vector3 plane = transform.TransformPoint(new Vector3(0f, PageY, 0f));
            surfacePoint = tip - surfaceNormal * Vector3.Dot(tip - plane, surfaceNormal);
            return canvas != null;
        }

        private InkCanvas CurrentCanvas()
        {
            InkManager ink = InkManager.Instance;
            if (ink == null)
                return null;
            int page = Page;
            if (_canvases[page] == null || _canvases[page].Anchor == null)
                _canvases[page] = ink.GetPersistent("libreta/p" + (page + 1), () => _anchors[page], GripCollider, false);
            return _canvases[page];
        }

        private void ShowPage(int page, bool log)
        {
            page = Mathf.Clamp(page, 0, PageCount - 1);
            if (_anchors[Page] != null)
                _anchors[Page].gameObject.SetActive(false);
            Page = page;
            _anchors[Page].gameObject.SetActive(true);
            CurrentCanvas(); // Cargar sus trazos ya, no al primer toque del lápiz.
            if (_label != null)
                _label.text = (Page + 1) + " / " + PageCount;
            if (!log)
                return;
            PlayerPrefs.SetInt(PagePrefsKey, Page);
            PlayerPrefs.Save();
            Debug.Log("[FolioXR] Libreta: página " + (Page + 1) + " de " + PageCount);
        }

        private void Turn(int delta)
        {
            int target = Mathf.Clamp(Page + delta, 0, PageCount - 1);
            if (target != Page)
                ShowPage(target, true);
        }

        public override void ControllerInput(ToolButtons buttons)
        {
            if (buttons.PrimaryDown)
                Turn(1);
            else if (buttons.SecondaryDown)
                Turn(-1);
        }

        private void Update()
        {
            if (State == ToolState.Stored || Registry == null)
                return;
            for (int i = 0; i < 2; i++)
            {
                XRNode side = i == 0 ? XRNode.LeftHand : XRNode.RightHand;
                // Ni la mano que sostiene la libreta ni la que tiene el lápiz pasan página sin querer.
                bool busy = (State == ToolState.Held && HolderSide == side) || ToolRegistry.HoldsHand(side) ||
                            ToolRegistry.HoldsController(side);
                if (busy || !Registry.TryPointer(side, out Vector3 tip))
                {
                    _armed[i] = false;
                    continue;
                }
                float next = Vector3.Distance(tip, _nextCorner.position);
                float previous = Vector3.Distance(tip, _previousCorner.position);
                float nearest = Mathf.Min(next, previous);
                if (nearest > CornerRearm)
                    _armed[i] = true;
                else if (_armed[i] && nearest < CornerTouch)
                {
                    _armed[i] = false;
                    Turn(next < previous ? 1 : -1);
                }
            }
        }

        protected override void OnTaken()
        {
            _armed[0] = _armed[1] = false;
        }
        protected override void OnReturned()
        {
            _armed[0] = _armed[1] = false;
        }

        private static Mesh PlaneMesh(float width, float height)
        {
            float x = width * 0.5f, z = height * 0.5f;
            var mesh = new Mesh { name = "FolioXR hoja A5" };
            mesh.vertices = new[] { new Vector3(-x, 0f, -z), new Vector3(x, 0f, -z), new Vector3(-x, 0f, z), new Vector3(x, 0f, z) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Papel crema, renglones azules cada 8 mm y margen rojo a 20 mm, hecho por código.</summary>
        private static Texture2D LinedTexture()
        {
            const int width = 512, height = 726;
            float pxPerMm = height / 210f;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = "FolioXR hoja rayada",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            var paper = new Color(0.98f, 0.97f, 0.92f);
            var line = new Color(0.55f, 0.68f, 0.9f);
            var margin = new Color(0.9f, 0.45f, 0.45f);
            var pixels = new Color[width * height];
            float marginX = 20f * pxPerMm;
            for (int y = 0; y < height; y++)
            {
                float mm = (height - 1 - y) / pxPerMm; // Desde el borde superior.
                float phase = Mathf.Repeat(mm - 24f, 8f) * pxPerMm;
                float lineWeight = mm > 22f && mm < 200f ? Mathf.Clamp01(1.4f - Mathf.Min(phase, 8f * pxPerMm - phase)) : 0f;
                for (int x = 0; x < width; x++)
                {
                    Color c = Color.Lerp(paper, line, lineWeight * 0.8f);
                    float marginWeight = Mathf.Clamp01(1.4f - Mathf.Abs(x - marginX));
                    pixels[y * width + x] = Color.Lerp(c, margin, marginWeight * 0.8f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
