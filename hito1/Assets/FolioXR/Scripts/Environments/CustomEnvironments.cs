using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FolioXR.Environments
{
    /// <summary>
    /// Entornos propios (pedido del usuario, 17 nov 2026: «la opción de nuevo entorno para que el usuario cree
    /// su propio entorno»). La persona elige una foto 360° del visor (panorámica equirrectangular, 2:1) y la
    /// app la pone alrededor como un cielo: el libro, la estantería libre, las herramientas y el piso funcionan
    /// igual que en passthrough, pero en vez de la habitación se ve la foto. Las fotos se copian a la carpeta
    /// privada de la app (entornos/) y se recuerdan entre sesiones.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10010)]
    public sealed class CustomEnvironments : MonoBehaviour
    {
        public const string ActiveKey = "folioxr.env.custom";
        private const int MaxWidth = 4096;
        private const float Radius = 60f;

        public static CustomEnvironments Instance { get; private set; }
        /// <summary>Cambió la lista o el entorno que se ve (el menú se rehace).</summary>
        public static event Action Changed;

        private FolioEnvironmentController _controller;
        private OVRPassthroughLayer _passthrough;
        private OVRCameraRig _rig;
        private Camera _view;
        private GameObject _sphere;
        private Mesh _sphereMesh;
        private Material _material;
        private Texture2D _texture;
        private string _shown, _pending;
        private bool _savedPassthrough;
        private CameraClearFlags _savedFlags;
        private Color _savedBackground;
        private float _savedFar;
        private float _restoreAt = 2f;
        private string _lastStatus = "idle";

        /// <summary>Aviso para el menú (eligiendo, copiando, error…) o vacío.</summary>
        public string Notice { get; private set; } = string.Empty;
        public bool Picking { get; private set; }
        public bool Showing => _shown != null;
        public string Shown => _shown;

        public static string Folder => Path.Combine(Application.persistentDataPath, "entornos");

        /// <summary>Fotos guardadas, de la más antigua a la más nueva.</summary>
        public static List<string> Files()
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(Folder)) return list;
                foreach (string file in Directory.GetFiles(Folder))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".jpeg" || ext == ".png") list.Add(file);
                }
                list.Sort(StringComparer.Ordinal);                     // entorno-<milisegundos>: orden de creación.
            }
            catch (Exception e) { Debug.LogWarning("[FolioXR] Entornos propios: " + e.Message); }
            return list;
        }

        /// <summary>Nombre para el menú: «Mi entorno N» según el orden de creación.</summary>
        public static string NameOf(string file)
        {
            int at = Files().IndexOf(file);
            return "Mi entorno " + (at >= 0 ? at + 1 : 1);
        }

        private void Awake()
        {
            Instance = this;
            _controller = GetComponent<FolioEnvironmentController>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Application.onBeforeRender -= Follow;
            Hide(false);                                              // Al cerrar la app se recuerda cuál era.
            if (_material != null) Destroy(_material);
            if (_sphereMesh != null) Destroy(_sphereMesh);
        }

        /// <summary>Abre el selector de imágenes del visor; la foto elegida se muestra apenas se copia.</summary>
        public void Pick()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var picker = new AndroidJavaClass("com.loklj.folioxr.FolioImagePicker"))
                    picker.CallStatic("pick", activity, Folder);
                Picking = true;
                Notice = "Elige una foto 360°\n(panorámica 2:1)";
            }
            catch (Exception e)
            {
                Notice = "No se pudo abrir\nel selector";
                Debug.LogWarning("[FolioXR] Entorno propio: " + e.Message);
            }
#else
            Notice = "Solo en el visor";
#endif
            Changed?.Invoke();
        }

        /// <summary>Muestra esa foto alrededor (sale de la casa si hacía falta).</summary>
        public void Show(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
            _pending = file;
            if (_controller != null && _controller.IsActive) _controller.Leave();
        }

        /// <summary>Vuelve al passthrough: la foto se quita y la cámara queda como estaba.</summary>
        public void Hide() => Hide(true);

        private void Hide(bool forget)
        {
            _pending = null;
            if (_shown == null) return;
            _shown = null;
            if (forget)
            {
                PlayerPrefs.DeleteKey(ActiveKey);
                PlayerPrefs.Save();
            }
            if (_sphere != null) _sphere.SetActive(false);
            if (_texture != null) { Destroy(_texture); _texture = null; }
            if (_passthrough != null) _passthrough.enabled = _savedPassthrough;
            if (_view != null)
            {
                _view.clearFlags = _savedFlags;
                _view.backgroundColor = _savedBackground;
                _view.farClipPlane = _savedFar;
            }
            Debug.Log("[FolioXR] Entorno propio: fuera.");
            Changed?.Invoke();
        }

        /// <summary>Borra esa foto (si se estaba viendo, vuelve al passthrough).</summary>
        public void Remove(string file)
        {
            if (file == _shown) Hide();
            try { File.Delete(file); } catch (Exception e) { Debug.LogWarning("[FolioXR] Entorno propio: " + e.Message); }
            Changed?.Invoke();
        }

        private void Update()
        {
            PollPicker();
            // Al iniciar: la última foto que se estaba viendo, si la persona no entró a una casa.
            if (_restoreAt > 0f && Time.unscaledTime >= _restoreAt)
            {
                _restoreAt = -1f;
                string last = PlayerPrefs.GetString(ActiveKey, string.Empty);
                if (last.Length > 0 && File.Exists(last) && (_controller == null || !_controller.IsActive)) Show(last);
            }
            if (_pending != null && (_controller == null || !_controller.IsActive))
            {
                string file = _pending;
                _pending = null;
                Apply(file);
            }
            // Si la persona entra a una casa (menú o clic del joystick), la foto se quita antes (ver Enter).
        }

        private void PollPicker()
        {
            if (!Picking) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            string status;
            try
            {
                using (var picker = new AndroidJavaClass("com.loklj.folioxr.FolioImagePicker"))
                    status = picker.CallStatic<string>("status");
            }
            catch (Exception) { status = "error|"; }
            if (status == _lastStatus) return;
            _lastStatus = status;
            if (status == "copying") { Notice = "Copiando la foto…"; Changed?.Invoke(); return; }
            if (status == "picking") return;
            Picking = false;
            if (status.StartsWith("done|", StringComparison.Ordinal))
            {
                Notice = string.Empty;
                Show(status.Substring(5));
            }
            else if (status.StartsWith("error|", StringComparison.Ordinal))
                Notice = "No se pudo usar\nesa foto";
            else
                Notice = string.Empty;
            Changed?.Invoke();
#endif
        }

        private bool Resolve()
        {
            if (_view == null) _view = Camera.main;
            if (_rig == null) _rig = FindFirstObjectByType<OVRCameraRig>();
            if (_passthrough == null) _passthrough = FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
            return _view != null && _rig != null && _rig.centerEyeAnchor != null;
        }

        private void Apply(string file)
        {
            if (!Resolve()) return;
            Texture2D texture = Load(file);
            if (texture == null)
            {
                Notice = "No se pudo abrir\nesa foto";
                Changed?.Invoke();
                return;
            }
            if (_shown == null)
            {
                // Primera foto desde el passthrough: se guarda cómo estaba la cámara para devolverla igual.
                _savedPassthrough = _passthrough != null && _passthrough.enabled;
                _savedFlags = _view.clearFlags;
                _savedBackground = _view.backgroundColor;
                _savedFar = _view.farClipPlane;
            }
            if (_texture != null) Destroy(_texture);
            _texture = texture;
            EnsureSphere();
            _material.mainTexture = _texture;
            _sphere.SetActive(true);
            if (_passthrough != null) _passthrough.enabled = false;
            _view.clearFlags = CameraClearFlags.SolidColor;
            _view.backgroundColor = Color.black;
            _view.farClipPlane = Mathf.Max(_savedFar, Radius * 2.2f);
            _shown = file;
            PlayerPrefs.SetString(ActiveKey, file);
            PlayerPrefs.Save();
            Follow();
            Debug.Log("[FolioXR] Entorno propio: " + Path.GetFileName(file) + " (" + texture.width + "x" + texture.height + ").");
            Changed?.Invoke();
        }

        /// <summary>Lee la foto; si es más ancha que 4096 px se reduce (memoria del visor).</summary>
        private static Texture2D Load(string file)
        {
            Texture2D original = null;
            try
            {
                original = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "FolioXR foto 360" };
                if (!original.LoadImage(File.ReadAllBytes(file), false)) { Destroy(original); return null; }
                int width = Mathf.Min(original.width, MaxWidth);
                int height = Mathf.Max(1, Mathf.RoundToInt(original.height * (width / (float)original.width)));
                var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                RenderTexture before = RenderTexture.active;
                Graphics.Blit(original, target);
                RenderTexture.active = target;
                var result = new Texture2D(width, height, TextureFormat.RGB24, true)
                    { name = "FolioXR foto 360", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, anisoLevel = 2 };
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0, true);
                result.Apply(true, true);
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(target);
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Entorno propio: no se pudo leer la foto: " + e.Message);
                return null;
            }
            finally
            {
                if (original != null) Destroy(original);
            }
        }

        private void EnsureSphere()
        {
            if (_sphere != null) return;
            Shader shader = Resources.Load<Shader>("FolioXRBaked");
            _material = shader != null && shader.isSupported
                ? new Material(shader) { name = "FolioXR foto 360" }
                : new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "FolioXR foto 360" };
            if (_material.HasProperty("_Cull")) _material.SetFloat("_Cull", 0f);
            _sphere = new GameObject("Entorno propio (foto 360°)");
            _sphere.transform.SetParent(transform, false);
            _sphereMesh = BakedEnvironment.Sphere(Radius);
            _sphere.AddComponent<MeshFilter>().sharedMesh = _sphereMesh;
            var renderer = _sphere.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Application.onBeforeRender += Follow;
        }

        // La foto se ve siempre desde su centro: la esfera acompaña la cabeza (sin girar con ella).
        private void Follow()
        {
            if (_shown == null || _sphere == null || _rig == null || _rig.centerEyeAnchor == null) return;
            _sphere.transform.SetPositionAndRotation(_rig.centerEyeAnchor.position, Quaternion.identity);
        }
    }
}
