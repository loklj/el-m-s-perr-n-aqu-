using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Reading
{
    /// <summary>
    /// Controles sobre el propio libro (pedido del usuario, 16 nov 2026):
    ///  · una barra de avance sobre el borde de arriba, de un extremo al otro del libro abierto, con un 0 en
    ///    un extremo, la última página en el otro y un círculo dorado en la página actual con su número
    ///    encima; el círculo se agarra y se lleva a la página que uno quiera;
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
        // Barra: círculo de la página actual, su número y los números de los extremos.
        private Transform _knob;
        private TextMesh _knobLabel, _startLabel, _endLabel;
        private Font _font;
        private Material _textMaterial;
        private int _barHand = -1, _barPage, _goTo = -1;
        private float _barLost;
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
            Font.textureRebuilt -= OnFontRebuilt;
            foreach (Object o in new Object[] { _quad, _disc, _goldMaterial, _darkMaterial, _redMaterial, _lightMaterial, _textMaterial })
                if (o != null) Destroy(o);
        }

        /// <summary>¿Ese punto está sobre un control del libro? BookGrab no agarra el libro desde ahí.</summary>
        internal static bool Claims(Vector3 point)
        {
            BookControls c = _instance;
            if (c == null || !c._built || !c.isActiveAndEnabled) return false;
            if (c._dragHand >= 0 || c._barHand >= 0) return true;
            if (c.OnBar(point, 0.008f, out _)) return true;
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
            // Círculo de la página actual (dorado con borde oscuro) y números.
            _knob = Part("Página actual", _disc, _darkMaterial, body.Half(1));
            Transform gold = Part("Centro", _disc, _goldMaterial, _knob);
            gold.localScale = new Vector3(0.78f, 0.78f, 1f);
            gold.localPosition = new Vector3(0f, 0f, -0.04f);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Shader text = Resources.Load<Shader>("FolioXRText3D");
            if (_font != null)
            {
                _textMaterial = text != null && text.isSupported
                    ? new Material(text) { name = "FolioXR números de la barra", mainTexture = _font.material.mainTexture }
                    : new Material(_font.material);
                Font.textureRebuilt += OnFontRebuilt;
                _knobLabel = Label("Número de página", TextAnchor.LowerCenter, body.Half(1));
                _startLabel = Label("Inicio", TextAnchor.LowerLeft, body.Half(-1));
                _endLabel = Label("Final", TextAnchor.LowerRight, body.Half(1));
            }
            _built = true;
        }

        // La textura de la fuente se rehace al pedir letras nuevas: el material propio tiene que seguirla.
        private void OnFontRebuilt(Font font)
        {
            if (font == _font && _textMaterial != null) _textMaterial.mainTexture = _font.material.mainTexture;
        }

        private TextMesh Label(string name, TextAnchor anchor, Transform parent)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            var label = item.AddComponent<TextMesh>();
            label.font = _font;
            label.fontSize = 48;
            label.characterSize = 0.0021f;                         // Cifras de cerca de 1 cm de alto.
            label.anchor = anchor;
            label.alignment = TextAlignment.Center;
            label.color = new Color(0.97f, 0.9f, 0.7f);
            var renderer = item.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _textMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return label;
        }

        /// <summary>
        /// ¿El punto está sobre la barra (libro abierto)? along: lugar a lo largo de la barra, de 0 (orilla
        /// izquierda) a 1 (orilla derecha).
        /// </summary>
        private bool OnBar(Vector3 point, float extra, out float along)
        {
            along = 0f;
            BookBody body = _book.Body;
            if (body == null || !_built || _track[0] == null || !_track[0].gameObject.activeInHierarchy) return false;
            float width = _book.rightPage.transform.localScale.x, height = _book.rightPage.transform.localScale.y;
            float inner = _book.gutter * 0.5f, top = height * 0.5f + Above;
            Transform root = _book.rightPage.transform.parent;
            int side = root.InverseTransformPoint(point).x < 0f ? -1 : 1;
            Vector3 local = body.Half(side).InverseTransformPoint(point);
            float flat = Mathf.Clamp((local.x - side * inner) * side, 0f, width);   // Del lomo hacia afuera.
            along = 0.5f + side * flat / (2f * width);
            return Mathf.Abs(local.y - top) < 0.022f + extra && Mathf.Abs(local.z) < 0.04f + extra &&
                   (local.x - side * inner) * side > -0.02f && (local.x - side * inner) * side < width + 0.02f;
        }

        /// <summary>Lugar de la barra (0 a 1, de izquierda a derecha) que le toca a una página (1…N).</summary>
        private float AlongOf(int page, int pages) => _book.RightToLeft ? 1f - page / (float)pages : page / (float)pages;

        private int PageAt(float along, int pages) =>
            Mathf.Clamp(Mathf.RoundToInt((_book.RightToLeft ? 1f - along : along) * pages), 1, pages);

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
            if (_book == null || _book.Empty) { _dragHand = _barHand = _goTo = -1; return; }
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
            // Página que marca el círculo: la que se está leyendo o, mientras se arrastra, la elegida.
            int page = _barHand >= 0 ? _barPage : Mathf.Clamp(_book.LeftPageIndex + 1, 1, pages);
            float progress = _book.PageCount > 0 ? Mathf.Clamp01(page / (float)pages) : 0f;
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
            // Círculo en la página actual, con su número encima; en los extremos, 0 y la última página.
            float spot = AlongOf(page, pages);                       // 0 = orilla izquierda, 1 = orilla derecha.
            int knobSide = spot < 0.5f ? -1 : 1;
            float knobX = knobSide * (inner + Mathf.Abs(spot - 0.5f) * 2f * width);
            Transform knobHome = body.Half(knobSide);
            if (_knob.parent != knobHome) _knob.SetParent(knobHome, false);
            _knob.gameObject.SetActive(open && _book.PageCount > 0);
            _knob.localPosition = new Vector3(knobX, top, front - 0.001f);
            _knob.localRotation = Quaternion.identity;
            _knob.localScale = Vector3.one * (_barHand >= 0 ? 0.02f : 0.015f);
            if (_knobLabel != null)
            {
                if (_knobLabel.transform.parent != knobHome) _knobLabel.transform.SetParent(knobHome, false);
                _knobLabel.gameObject.SetActive(open && _book.PageCount > 0);
                _knobLabel.transform.localPosition = new Vector3(knobX, top + 0.012f, front);
                _knobLabel.transform.localRotation = Quaternion.identity;
                string number = page.ToString();
                if (_knobLabel.text != number) _knobLabel.text = number;
                // Los números de los extremos se esconden cuando el círculo les queda encima.
                bool nearStart = spot < 0.12f, nearEnd = spot > 0.88f;
                _startLabel.gameObject.SetActive(open && !nearStart);
                _endLabel.gameObject.SetActive(open && !nearEnd);
                _startLabel.transform.localPosition = new Vector3(-(inner + width), top + 0.006f, front);
                _endLabel.transform.localPosition = new Vector3(inner + width, top + 0.006f, front);
                string first = rtl ? pages.ToString() : "0", last = rtl ? "0" : pages.ToString();
                if (_startLabel.text != first) _startLabel.text = first;
                if (_endLabel.text != last) _endLabel.text = last;
            }
            // La X va en la mitad que no se mueve al cerrar, arriba y afuera.
            Transform home = body.Half(body.RestSide);
            if (_close.parent != home) _close.SetParent(home, false);
            _close.gameObject.SetActive(!_book.Empty && (open || closed));
            _close.localPosition = new Vector3(body.RestSide * (inner + width + Outside + 0.006f), top + 0.016f,
                                               closed ? -body.CoverOuterDepth - 0.002f : front);
            _close.localRotation = Quaternion.identity;
            _close.localScale = Vector3.one * 0.028f;
        }

        private void Interact()
        {
            BookBody body = _book.Body;
            if (_hands == null) _hands = GetComponent<BookHands>();
            if (_hands == null || body == null) return;
            if (_goTo >= 0 && _book.GoToPage(_goTo)) _goTo = -1;      // Página elegida en la barra (espera si el libro está ocupado).
            if (_barHand >= 0) { FollowBar(); return; }
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
                if (!Near(_circle[0], point) && !Near(_circle[1], point) && _book.PageCount > 1 && OnBar(point, 0f, out float along))
                {
                    _barHand = i;
                    _barPage = PageAt(along, _book.PageCount);
                    _barLost = -1f;
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

        /// <summary>El círculo de la barra sigue a la mano; al soltar, el libro va a esa página.</summary>
        private void FollowBar()
        {
            bool have = _hands.Pointer(_barHand, out Vector3 world, out bool pressed) && !_book.InputPaused;
            if (!have)
            {
                if (_barLost < 0f) _barLost = Time.unscaledTime;
                if (Time.unscaledTime - _barLost < 0.3f) return;
                _was[_barHand] = false;
                _barHand = -1;                                       // Mano perdida: no se cambia de página.
                return;
            }
            _barLost = -1f;
            int pages = Mathf.Max(1, _book.PageCount);
            // Alejar mucho la mano de la barra antes de soltar cancela.
            bool on = OnBar(world, 0.08f, out float along);
            if (pressed) { if (on) _barPage = PageAt(along, pages); return; }
            if (on && _barPage != Mathf.Clamp(_book.LeftPageIndex + 1, 1, pages))
            {
                Debug.Log("[FolioXR] Libro: barra, ir a la página " + _barPage + ".");
                _goTo = _barPage - 1;
            }
            _was[_barHand] = false;
            _barHand = -1;
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
