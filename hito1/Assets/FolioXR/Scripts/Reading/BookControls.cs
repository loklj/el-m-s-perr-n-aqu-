using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Reading
{
    /// <summary>
    /// Controles sobre el propio libro (pedido del usuario, 16 nov 2026):
    ///  · una barra de avance sobre el borde de arriba, de un extremo al otro del libro abierto;
    ///  · una X en la esquina de arriba, que guarda el libro en la estantería;
    ///  · un círculo a cada lado: se agarra (pellizco, gatillo o agarre) y, con el movimiento de cerrar un
    ///    libro, la tapa sigue a la mano. Con el libro cerrado, el mismo círculo lo abre.
    /// Todo cuelga de las mitades del libro (BookBody), así que acompaña al pliegue y al tamaño.
    /// </summary>
    internal sealed class BookControls : MonoBehaviour
    {
        private const float Reach = 0.04f;       // Distancia a un control para tomarlo, en metros.
        private const float Outside = 0.024f;    // Del borde de la hoja al centro de los círculos.
        private const float Above = 0.013f;      // Del borde de arriba al centro de la barra.
        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.35f), Dark = new Color(0.12f, 0.11f, 0.1f),
                                      Red = new Color(0.78f, 0.2f, 0.16f);
        private static BookControls _instance;

        private FolioBook _book;
        private BookHands _hands;
        private Mesh _quad, _disc;
        private Material _goldMaterial, _darkMaterial, _redMaterial, _lightMaterial;
        private readonly Transform[] _track = new Transform[2], _fill = new Transform[2], _circle = new Transform[2];
        private Transform _close;
        private bool _built;
        private readonly bool[] _was = new bool[2];
        // Tapa en la mano.
        private int _dragHand = -1;
        private bool _mirrored;
        private float _along, _shown, _velocity, _lost;

        private void Awake() { _book = GetComponent<FolioBook>(); _instance = this; }
        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            foreach (Object o in new Object[] { _quad, _disc, _goldMaterial, _darkMaterial, _redMaterial, _lightMaterial })
                if (o != null) Destroy(o);
        }

        /// <summary>¿Ese punto está sobre un control del libro? BookGrab no agarra el libro desde ahí.</summary>
        internal static bool Claims(Vector3 point)
        {
            BookControls c = _instance;
            if (c == null || !c._built || !c.isActiveAndEnabled) return false;
            if (c._dragHand >= 0) return true;
            // Con algo de margen: el punto del pellizco de BookGrab no es exactamente la punta del índice.
            return c.Near(c._close, point, 0.015f) || c.Near(c._circle[0], point, 0.015f) || c.Near(c._circle[1], point, 0.015f);
        }

        private bool Near(Transform control, Vector3 point, float extra = 0f) =>
            control != null && control.gameObject.activeInHierarchy && Vector3.Distance(control.position, point) < Reach + extra;

        private void Build()
        {
            BookBody body = _book.Body;
            Renderer page = _book.leftPage;
            if (body == null || page == null || page.sharedMaterial == null || body.Half(-1) == null) return;
            _hands = GetComponent<BookHands>();
            _quad = new Mesh { name = "FolioXR control (rectángulo)" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateBounds();
            const int n = 28;
            var vertices = new Vector3[n + 1];
            var triangles = new int[n * 3];
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * Mathf.PI / n;
                vertices[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f);
                triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = (i + 1) % n + 1;
            }
            _disc = new Mesh { name = "FolioXR control (círculo)" };
            _disc.vertices = vertices;
            _disc.uv = new Vector2[n + 1];
            _disc.triangles = triangles;
            _disc.RecalculateBounds();
            _goldMaterial = Flat(page.sharedMaterial, Gold, "dorado");
            _darkMaterial = Flat(page.sharedMaterial, Dark, "oscuro");
            _redMaterial = Flat(page.sharedMaterial, Red, "rojo");
            _lightMaterial = Flat(page.sharedMaterial, new Color(0.96f, 0.94f, 0.9f), "claro");
            for (int i = 0; i < 2; i++)
            {
                Transform half = body.Half(i == 0 ? -1 : 1);
                _track[i] = Part("Barra de avance (fondo)", _quad, _darkMaterial, half);
                _fill[i] = Part("Barra de avance", _quad, _goldMaterial, half);
                // Círculo: aro dorado con centro oscuro y un punto claro.
                _circle[i] = Part("Círculo para cerrar", _disc, _goldMaterial, half);
                // Las capas se separan en unidades del círculo (mide 2,6 cm): 0,03 es menos de un milímetro.
                Transform center = Part("Centro", _disc, _darkMaterial, _circle[i]);
                center.localScale = new Vector3(0.74f, 0.74f, 1f);
                center.localPosition = new Vector3(0f, 0f, -0.03f);
                Transform dot = Part("Punto", _disc, _lightMaterial, _circle[i]);
                dot.localScale = new Vector3(0.3f, 0.3f, 1f);
                dot.localPosition = new Vector3(0f, 0f, -0.06f);
            }
            // X: disco rojo con dos trazos claros cruzados.
            _close = Part("X guardar libro", _disc, _redMaterial, body.Half(1));
            for (int k = 0; k < 2; k++)
            {
                Transform bar = Part("Trazo", _quad, _lightMaterial, _close);
                bar.localScale = new Vector3(0.62f, 0.13f, 1f);
                bar.localRotation = Quaternion.Euler(0f, 0f, k == 0 ? 45f : -45f);
                bar.localPosition = new Vector3(0f, 0f, -0.03f - k * 0.02f);
            }
            _built = true;
        }

        private static Material Flat(Material source, Color color, string name)
        {
            var material = new Material(source) { name = "FolioXR control " + name, mainTexture = Texture2D.whiteTexture, color = color };
            material.SetFloat("_Cull", (float)CullMode.Off);
            return material;
        }

        private static Transform Part(string name, Mesh mesh, Material material, Transform parent)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return item.transform;
        }

        private void LateUpdate()
        {
            if (_book == null || _book.Empty) { _dragHand = -1; return; }
            if (!_built) { Build(); if (!_built) return; }
            Place();
            Interact();
        }

        /// <summary>Coloca la barra, la X y los círculos según el tamaño del libro, el avance y si está cerrado.</summary>
        private void Place()
        {
            BookBody body = _book.Body;
            float width = _book.rightPage.transform.localScale.x, height = _book.rightPage.transform.localScale.y;
            float inner = _book.gutter * 0.5f;
            bool rtl = _book.RightToLeft;
            float fold = body.FoldAmount;
            bool open = fold < 0.02f, closed = fold > 0.98f;
            int pages = Mathf.Max(1, _book.PageCount);
            float progress = _book.PageCount > 0 ? Mathf.Clamp01((_book.LeftPageIndex + 2f) / pages) : 0f;
            float top = height * 0.5f + Above;
            const float thick = 0.0055f, front = -0.0015f;
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                // Barra: de la orilla izquierda a la derecha pasando por el lomo; en manga se llena al revés.
                bool first = (side < 0) != rtl;                     // Mitad por donde empieza la lectura.
                float part = Mathf.Clamp01(first ? progress * 2f : progress * 2f - 1f);
                _track[i].gameObject.SetActive(open);
                _fill[i].gameObject.SetActive(open && part > 0.001f);
                _track[i].localPosition = new Vector3(side * (inner + width * 0.5f), top, front);
                _track[i].localScale = new Vector3(width, thick, 1f);
                // La mitad donde empieza se llena desde afuera hacia el lomo; la otra, desde el lomo hacia afuera.
                float from = first ? inner + width : inner, to = first ? inner + width * (1f - part) : inner + width * part;
                _fill[i].localPosition = new Vector3(side * (from + to) * 0.5f, top, front - 0.0005f);
                _fill[i].localScale = new Vector3(Mathf.Max(0.0001f, Mathf.Abs(to - from)), thick, 1f);
                // Círculos: a media altura, justo por fuera del borde. Con el libro cerrado queda uno solo.
                bool rest = side == body.RestSide;
                _circle[i].gameObject.SetActive(!closed || rest);
                float lift = closed && rest ? -body.CoverOuterDepth - 0.002f : front;
                _circle[i].localPosition = new Vector3(side * (inner + width + Outside), 0f, lift);
                _circle[i].localScale = Vector3.one * 0.026f;
            }
            // La X va en la mitad que no se mueve al cerrar, arriba y afuera.
            Transform home = body.Half(body.RestSide);
            if (_close.parent != home) _close.SetParent(home, false);
            _close.gameObject.SetActive(!_book.Empty && (open || closed));
            _close.localPosition = new Vector3(body.RestSide * (inner + width + Outside), top + 0.012f,
                                               closed ? -body.CoverOuterDepth - 0.002f : front);
            _close.localRotation = Quaternion.identity;
            _close.localScale = Vector3.one * 0.028f;
        }

        private void Interact()
        {
            BookBody body = _book.Body;
            if (_hands == null) _hands = GetComponent<BookHands>();
            if (_hands == null || body == null) return;
            if (_dragHand >= 0) { Follow(body); return; }
            bool idle = _book.InputPaused || _book.HandTurning;
            for (int i = 0; i < 2; i++)
            {
                bool have = _hands.Pointer(i, out Vector3 point, out bool pressed);
                bool began = have && pressed && !_was[i];
                _was[i] = have && pressed;
                // Al acercarse, el control crece un poco: se ve cuál se va a tomar.
                if (have && !idle) Hover(point);
                if (!began || idle) continue;
                if (Near(_close, point))
                {
                    Debug.Log("[FolioXR] Libro: X, se guarda en la estantería.");
                    var library = FolioXR.Books.BookLibrary.Instance;
                    if (library != null && library.Current != null) library.CloseCurrent();
                    else _book.PutAway();                            // Libro de muestra: solo se guarda.
                    return;
                }
                for (int c = 0; c < 2; c++)
                {
                    if (!Near(_circle[c], point) || body.Folding) continue;
                    int side = c == 0 ? -1 : 1;
                    _dragHand = i;
                    _along = _book.gutter * 0.5f + _book.rightPage.transform.localScale.x + Outside;
                    // Abierto y tomado del lado que no se mueve: la otra mitad viene hacia la mano, en espejo.
                    _mirrored = !body.Closed && side == body.RestSide;
                    _shown = body.Closed ? 1f : 0f;
                    _velocity = 0f;
                    _lost = -1f;
                    body.SetManualFold(_shown);
                    Debug.Log("[FolioXR] Libro: círculo " + (side < 0 ? "izquierdo" : "derecho") + " tomado (" + (body.Closed ? "abrir" : "cerrar") + ").");
                    return;
                }
            }
        }

        private void Hover(Vector3 point)
        {
            foreach (Transform control in new[] { _close, _circle[0], _circle[1] })
                if (control != null && Near(control, point)) control.localScale *= 1.25f;
        }

        /// <summary>La tapa sigue a la mano mientras siga apretando; al soltar queda cerrada o abierta.</summary>
        private void Follow(BookBody body)
        {
            if (!body.Manual) { _dragHand = -1; return; }           // Otra parte terminó el gesto (se abrió otro libro…).
            bool have = _hands.Pointer(_dragHand, out Vector3 world, out bool pressed) && !_book.InputPaused;
            if (!have)
            {
                // Seguimiento perdido un instante: la tapa espera.
                if (_lost < 0f) _lost = Time.unscaledTime;
                if (Time.unscaledTime - _lost < 0.3f) return;
                pressed = false;
            }
            else _lost = -1f;
            if (!pressed)
            {
                _book.EndHandFold(_shown + _velocity * 0.2f > 0.5f);
                _was[_dragHand] = false;
                _dragHand = -1;
                return;
            }
            Transform root = _book.rightPage.transform.parent;
            float x = root.InverseTransformPoint(world).x;
            int front = -body.RestSide;
            float s = PageTurnShape.FoldFromX(_mirrored ? -x : x, front, _along, body.TiltRadians);
            float dt = Mathf.Max(1e-4f, Time.deltaTime), before = _shown;
            _shown = Mathf.Lerp(_shown, s, 1f - Mathf.Exp(-dt / 0.05f));
            _velocity = Mathf.Lerp(_velocity, (_shown - before) / dt, 0.35f);
            body.SetManualFold(_shown);
        }
    }
}
