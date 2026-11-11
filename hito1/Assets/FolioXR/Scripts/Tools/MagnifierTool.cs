using System.Globalization;
using FolioXR.Reading;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FolioXR.Tools
{
    /// <summary>
    /// Tanda E (escrita por Claude). Lupa de 8 cm: una cámara secundaria en el ojo mira a través del centro
    /// del lente con un campo visual dividido por el aumento, y dibuja en una RenderTexture de 512 px.
    /// Mango hacia -Y desde el lente; el lente mira hacia +Z local. Imagen monoscópica (la misma en ambos ojos).
    /// </summary>
    [DefaultExecutionOrder(20040)] // Después del rig: el ojo y el lente ya están en su pose del cuadro.
    public sealed class MagnifierTool : FolioTool
    {
        public const float MinZoom = 1.5f, MaxZoom = 4f, DefaultZoom = 2.5f;
        public const string ZoomPrefsKey = "folioxr.magnifierZoom";
        public float Zoom { get; private set; } = DefaultZoom;

        internal static readonly Vector3 ColliderSize = new Vector3(0.09f, 0.19f, 0.02f);
        private static readonly Vector3 LensCenter = new Vector3(0f, 0.095f, 0f);
        private const float LensRadius = 0.04f, RenderNearBook = 1f;
        private const int UiLayer = 5; // Menú y reloj: la cámara de la lupa no los ve.

        private Camera _camera;
        private RenderTexture _texture;
        private Transform _lens, _plus, _minus;
        private Renderer _image, _glass;
        private TextMesh _label;
        private FolioBook _book;
        private bool _plusArmed, _minusArmed, _rendering, _skipFrame;
        private float _nextBookFind;

        internal void Build(ToolVisuals visuals)
        {
            Body.mass = 0.12f;
            GripCollider.size = ColliderSize;
            GripCollider.center = new Vector3(0f, 0.04f, 0f);
            Zoom = Mathf.Clamp(PlayerPrefs.GetFloat(ZoomPrefsKey, DefaultZoom), MinZoom, MaxZoom);
            visuals.Box(transform, "Mango", new Vector3(0f, 0f, 0f), new Vector3(0.016f, 0.1f, 0.016f), visuals.Dark);
            _lens = new GameObject("Lente").transform;
            _lens.SetParent(transform, false);
            _lens.localPosition = LensCenter;
            LineRenderer rim = visuals.Line(_lens, "Aro", visuals.Dark, 0.006f, 48);
            rim.loop = true;
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI / 24f;
                rim.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (LensRadius + 0.002f));
            }

            _texture = visuals.Own(new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32)
            {
                name = "FolioXR lupa",
                antiAliasing = 1,
                useMipMap = false
            });
            Mesh disc = visuals.Own(Disc());
            _image = ToolVisuals.MeshObject(_lens, "Imagen ampliada", disc, visuals.Textured("imagen de la lupa", _texture));
            _glass = ToolVisuals.MeshObject(_lens, "Vidrio", disc,
                                            visuals.Transparent("vidrio de la lupa", new Color(0.8f, 0.92f, 1f, 0.12f)));
            _image.enabled = false;

            _plus = visuals.Box(_lens, "Botón +", new Vector3(LensRadius + 0.012f, 0.012f, 0f),
                                new Vector3(0.012f, 0.012f, 0.006f), visuals.Cyan);
            _minus = visuals.Box(_lens, "Botón −", new Vector3(LensRadius + 0.012f, -0.012f, 0f),
                                 new Vector3(0.012f, 0.012f, 0.006f), visuals.Violet);
            var label = new GameObject("Aumento");
            label.transform.SetParent(_lens, false);
            label.transform.localPosition = new Vector3(0f, -LensRadius - 0.012f, -0.004f);
            _label = label.AddComponent<TextMesh>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = 48;
            _label.characterSize = 0.0012f;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            if (_label.font != null)
                label.GetComponent<MeshRenderer>().sharedMaterial = _label.font.material;
            ToolVisuals.Configure(label.GetComponent<Renderer>());

            var cameraObject = new GameObject("Cámara de la lupa");
            cameraObject.transform.SetParent(transform, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.stereoTargetEye = StereoTargetEyeMask.None;
            _camera.targetTexture = _texture;
            _camera.aspect = 1f;
            _camera.allowMSAA = false;
            _camera.allowHDR = false;
            _camera.useOcclusionCulling = false;
            _camera.cullingMask = ~(1 << UiLayer);
            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderShadows = false;
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.requiresDepthTexture = false;
                data.requiresColorTexture = false;
            }
            UpdateLabel();
        }

        public override void ControllerInput(ToolButtons buttons)
        {
            if (Mathf.Abs(buttons.Stick.y) > 0.25f)
                SetZoom(Zoom * Mathf.Pow(1.8f, buttons.Stick.y * Time.unscaledDeltaTime));
            if (buttons.PrimaryDown)
                SetZoom(Zoom + 0.5f);
            else if (buttons.SecondaryDown)
                SetZoom(Zoom - 0.5f);
        }

        public override void OtherIndexTouch(Vector3 position, bool reliable)
        {
            if (!reliable)
            {
                _plusArmed = _minusArmed = false;
                return;
            }
            Touch(position, _plus, ref _plusArmed, 0.5f);
            Touch(position, _minus, ref _minusArmed, -0.5f);
        }

        private void Touch(Vector3 tip, Transform button, ref bool armed, float delta)
        {
            float distance = Vector3.Distance(tip, button.position);
            if (distance > 0.025f)
                armed = true;
            else if (armed && distance < 0.012f)
            {
                armed = false;
                SetZoom(Zoom + delta);
            }
        }

        private void SetZoom(float zoom)
        {
            float previous = Zoom;
            Zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            if (Mathf.Approximately(previous, Zoom))
                return;
            UpdateLabel();
            PlayerPrefs.SetFloat(ZoomPrefsKey, Zoom);
        }

        private void UpdateLabel()
        {
            if (_label != null)
                _label.text = Zoom.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + "×";
        }

        private void LateUpdate()
        {
            if (_camera == null)
                return;
            Camera eye = Camera.main;
            bool wanted = eye != null && State != ToolState.Stored && (State == ToolState.Held || NearBook());
            Vector3 toLens = wanted ? _lens.position - eye.transform.position : Vector3.forward;
            float distance = toLens.magnitude;
            // Solo si se mira a través del lente desde delante y a una distancia razonable.
            wanted &= distance > 0.08f && distance < 1.5f && Vector3.Dot(_lens.forward, toLens / distance) > 0.2f;
            if (wanted != _rendering)
            {
                _rendering = wanted;
                _image.enabled = wanted;
                _glass.enabled = !wanted;
                if (!wanted)
                    _camera.enabled = false;
                Debug.Log("[FolioXR] Lupa: " + (wanted ? "renderizando" : "en pausa"));
            }
            if (!wanted)
                return;
            // A 72 FPS se renderiza un cuadro sí y uno no (36 Hz): la imagen apenas cambia entre ambos.
            _skipFrame = !_skipFrame;
            _camera.enabled = !_skipFrame;
            if (_skipFrame)
                return;
            float angle = 2f * Mathf.Atan(LensRadius / distance) * Mathf.Rad2Deg;
            _camera.fieldOfView = Mathf.Clamp(angle / Zoom, 1f, 90f);
            _camera.nearClipPlane = distance + 0.01f; // Ni el lente, ni la mano, ni el mango.
            _camera.farClipPlane = Mathf.Max(eye.farClipPlane, distance + 1f);
            _camera.clearFlags = eye.clearFlags;
            _camera.backgroundColor = eye.backgroundColor;
            _camera.depth = eye.depth - 1f; // Antes que la vista principal, que ya usa la imagen nueva.
            _camera.transform.SetPositionAndRotation(eye.transform.position, Quaternion.LookRotation(toLens, _lens.up));
        }

        private bool NearBook()
        {
            if (_book == null && Time.unscaledTime >= _nextBookFind)
            {
                _nextBookFind = Time.unscaledTime + 1f;
                _book = FindFirstObjectByType<FolioBook>();
            }
            return _book != null && Vector3.Distance(_book.transform.position, _lens.position) < RenderNearBook * Mathf.Max(1f, _book.BookScale);
        }

        protected override void OnTaken()
        {
            _plusArmed = _minusArmed = false;
        }
        protected override void OnReturned()
        {
            _plusArmed = _minusArmed = false;
            _rendering = false;
            if (_camera != null)
                _camera.enabled = false;
            if (_image != null)
                _image.enabled = false;
            if (_glass != null)
                _glass.enabled = true;
            PlayerPrefs.Save();
        }

        private static Mesh Disc()
        {
            const int segments = 48;
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * 2f * Mathf.PI / segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * LensRadius;
                uv[i + 1] = new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a));
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }
            var mesh = new Mesh { name = "FolioXR lente" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
