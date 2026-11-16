using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Reading
{
    /// <summary>
    /// Hoja que pasa de un lado al otro del libro, curvada por código (encargo 8, Claude; 15 nov 2026: la hoja
    /// puede seguir a la mano). Vive en el mismo padre que las páginas: lomo en x = 0, páginas hacia -Z.
    /// Tiene dos caras, como una hoja de papel: una muestra la página que se va y la otra la que llega.
    /// Dos maneras de moverla:
    ///  · Rápida (Play): la animación de siempre, en un tercio de segundo.
    ///  · Natural (Begin, Pose, End o PlayNatural): la forma de PageTurnShape, con el avance que diga la mano.
    /// </summary>
    internal sealed class PageTurnEffect : MonoBehaviour
    {
        private const int Columns = PageTurnShape.Columns;
        private const float Seconds = 0.35f, Lag = 0.75f;
        internal const float NaturalSeconds = 0.7f;
        private Mesh _mesh;
        private Material _faceRight, _faceLeft;   // Cara que se ve con la hoja a la derecha / a la izquierda.
        private MeshRenderer _renderer;
        private readonly Vector3[] _vertices = new Vector3[(Columns + 1) * 2];
        private readonly float[] _x = new float[Columns + 1], _lift = new float[Columns + 1];
        private AudioSource _audio;
        private AudioClip _clip;
        // Giro en curso (modo natural).
        private bool _towardLeft, _sounded;
        private float _width, _height, _inner, _tilt, _curlFrom, _curlTo, _reach;
        private Color _colorRight = Color.white, _colorLeft = Color.white;   // Color de cada cara sin sombrear.

        internal bool Showing => _renderer != null && _renderer.enabled;

        internal static PageTurnEffect Create(Renderer page)
        {
            if (page == null || page.sharedMaterial == null || page.transform.parent == null) return null;
            var item = new GameObject("Hoja que pasa");
            item.transform.SetParent(page.transform.parent, false);
            var effect = item.AddComponent<PageTurnEffect>();
            effect.Build(page.sharedMaterial);
            return effect;
        }

        private void Build(Material source)
        {
            // Con la hoja acostada a la derecha, los triángulos miran a quien lee: esa cara se dibuja sin las
            // caras traseras. La otra cara es la misma malla vista por detrás, con la imagen espejada.
            _faceRight = new Material(source) { name = "FolioXR hoja que pasa (cara derecha)" };
            _faceRight.SetFloat("_Cull", (float)CullMode.Back);
            _faceLeft = new Material(source) { name = "FolioXR hoja que pasa (cara izquierda)" };
            _faceLeft.SetFloat("_Cull", (float)CullMode.Front);
            _faceLeft.mainTextureScale = new Vector2(-1f, 1f);
            _faceLeft.mainTextureOffset = new Vector2(1f, 0f);
            _mesh = new Mesh { name = "FolioXR hoja que pasa" };
            _mesh.MarkDynamic();
            var uv = new Vector2[_vertices.Length];
            var triangles = new int[Columns * 6];
            for (int i = 0; i <= Columns; i++)
            {
                uv[i * 2] = new Vector2(i / (float)Columns, 0f);
                uv[i * 2 + 1] = new Vector2(i / (float)Columns, 1f);
            }
            for (int i = 0; i < Columns; i++)
            {
                int a = i * 2, t = i * 6;
                triangles[t] = a; triangles[t + 1] = a + 1; triangles[t + 2] = a + 2;
                triangles[t + 3] = a + 2; triangles[t + 4] = a + 1; triangles[t + 5] = a + 3;
            }
            _mesh.vertices = _vertices;
            _mesh.uv = uv;
            // Dos partes con los mismos triángulos: una por cara.
            _mesh.subMeshCount = 2;
            _mesh.SetTriangles(triangles, 0);
            _mesh.SetTriangles(triangles, 1);
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterials = new[] { _faceRight, _faceLeft };
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.volume = 0.35f;
            _clip = Swoosh();
        }

        /// <summary>Roce de papel: ruido filtrado con envolvente, generado por código.</summary>
        private static AudioClip Swoosh()
        {
            const int rate = 24000;
            int count = (int)(rate * 0.28f);
            var samples = new float[count];
            var random = new System.Random(7);
            float low = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                float noise = (float)random.NextDouble() * 2f - 1f;
                low += (noise - low) * 0.35f;
                samples[i] = (noise - low) * Mathf.Sin(Mathf.PI * t) * Mathf.Sin(Mathf.PI * t) * 0.5f;
            }
            AudioClip clip = AudioClip.Create("Paso de página", count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>Pone en cada cara la página que le toca. leaving: la que se va; arriving: la que llega (o null: papel en blanco).</summary>
        private void SetFaces(bool towardLeft, Texture leaving, Texture arriving, Color tint, Color blank)
        {
            Set(towardLeft, leaving, tint, blank);
            Set(!towardLeft, arriving, tint, blank);
        }

        /// <summary>right: la cara que se ve con la hoja acostada a la derecha (si no, la otra).</summary>
        private void Set(bool right, Texture texture, Color tint, Color blank)
        {
            Material face = right ? _faceRight : _faceLeft;
            Color color = texture != null ? tint : blank;
            face.mainTexture = texture != null ? texture : Texture2D.whiteTexture;
            face.color = color;
            if (right) _colorRight = color; else _colorLeft = color;
        }

        /// <summary>La hoja levantada recibe menos luz: un poco más oscura cuanto más parada está.</summary>
        private void Shade(float upright)
        {
            float k = 1f - 0.13f * Mathf.Clamp01(upright);
            _faceRight.color = new Color(_colorRight.r * k, _colorRight.g * k, _colorRight.b * k, _colorRight.a);
            _faceLeft.color = new Color(_colorLeft.r * k, _colorLeft.g * k, _colorLeft.b * k, _colorLeft.a);
        }

        // ───────────── Rápida ─────────────

        /// <param name="towardLeft">La hoja sale de la derecha y cae a la izquierda.</param>
        /// <param name="inner">Distancia del lomo al borde interior de la página (medio margen central).</param>
        /// <param name="tilt">Inclinación de cada mitad del libro abierto (radianes): la hoja sale y llega en esa V.</param>
        internal IEnumerator Play(bool towardLeft, Texture first, Texture second, float width, float height, float inner,
                                  float tilt, Color tint, Color blank)
        {
            SetFaces(towardLeft, first, second, tint, blank);
            _renderer.enabled = true;
            if (_audio != null && _clip != null) _audio.PlayOneShot(_clip);
            float elapsed = 0f;
            while (elapsed < Seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Seconds));
                // tilt = sobre la mitad derecha, π - tilt = sobre la izquierda (el libro abre en V).
                float angle = Mathf.Lerp(tilt, Mathf.PI - tilt, towardLeft ? t : 1f - t);
                QuickShape(angle, towardLeft, width, height, inner);
                yield return null;
            }
            _renderer.enabled = false;
        }

        private void QuickShape(float angle, bool towardLeft, float width, float height, float inner)
        {
            // El borde exterior va atrasado respecto del lomo: eso curva la hoja.
            float lag = Lag * Mathf.Sin(angle) * (towardLeft ? -1f : 1f);
            float segment = width / Columns, half = height * 0.5f;
            float x = Mathf.Cos(angle) * inner, lift = Mathf.Sin(angle) * inner;
            for (int i = 0; i <= Columns; i++)
            {
                _vertices[i * 2] = new Vector3(x, -half, -0.0015f - lift);
                _vertices[i * 2 + 1] = new Vector3(x, half, -0.0015f - lift);
                // La hoja se curva más en el medio del giro y queda apoyada al llegar a cada lado.
                float a = Mathf.Clamp(angle + lag * (i + 0.5f) / Columns, 0f, Mathf.PI);
                x += Mathf.Cos(a) * segment;
                lift += Mathf.Sin(a) * segment;
            }
            _mesh.vertices = _vertices;
            _mesh.RecalculateBounds();
        }

        // ───────────── Natural (sigue a la mano) ─────────────

        /// <summary>
        /// Levanta la hoja para llevarla con Pose. curlFrom/curlTo/reach: hundimiento de las páginas junto al lomo
        /// del lado del que sale y del lado al que llega (BookBody), para que la hoja nazca del pliegue.
        /// </summary>
        internal void Begin(bool towardLeft, Texture leaving, Texture arriving, float width, float height, float inner,
                            float tilt, float curlFrom, float curlTo, float reach, Color tint, Color blank)
        {
            _towardLeft = towardLeft;
            _width = width; _height = height; _inner = inner; _tilt = tilt;
            _curlFrom = curlFrom; _curlTo = curlTo; _reach = reach;
            _sounded = false;
            SetFaces(towardLeft, leaving, arriving, tint, blank);
            Pose(0f);
            _renderer.enabled = true;
        }

        /// <summary>La página que llega terminó de dibujarse mientras la hoja estaba en el aire.</summary>
        internal void SetArriving(Texture arriving, Color tint, Color blank)
        {
            Set(!_towardLeft, arriving, tint, blank);
        }

        /// <summary>Avance del giro: 0 = sobre el lado del que sale, 1 = sobre el lado al que llega.</summary>
        internal void Pose(float t)
        {
            t = Mathf.Clamp01(t);
            PageTurnShape.Chain(t, _towardLeft, _width, _inner, _tilt, _curlFrom, _curlTo, _reach, _x, _lift);
            float half = _height * 0.5f;
            for (int i = 0; i <= Columns; i++)
            {
                _vertices[i * 2] = new Vector3(_x[i], -half, -_lift[i]);
                _vertices[i * 2 + 1] = new Vector3(_x[i], half, -_lift[i]);
            }
            _mesh.vertices = _vertices;
            _mesh.RecalculateBounds();
            Shade(Mathf.Sin(Mathf.PI * t));
            // El papel se oye cuando la hoja cruza por arriba del lomo.
            if (!_sounded && t > 0.45f)
            {
                _sounded = true;
                if (_audio != null && _clip != null) _audio.PlayOneShot(_clip);
            }
            else if (_sounded && t < 0.2f) _sounded = false;
        }

        internal void End()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        /// <summary>El mismo giro natural, pero solo (botones y pellizcos): más pausado que el rápido.</summary>
        internal IEnumerator PlayNatural(bool towardLeft, Texture leaving, Texture arriving, float width, float height, float inner,
                                         float tilt, float curlFrom, float curlTo, float reach, Color tint, Color blank)
        {
            Begin(towardLeft, leaving, arriving, width, height, inner, tilt, curlFrom, curlTo, reach, tint, blank);
            float elapsed = 0f;
            while (elapsed < NaturalSeconds)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / NaturalSeconds);
                // Arranca con suavidad y cae un poco más rápido, como una hoja soltada del otro lado.
                Pose(u * u * (3f - 2f * u) * 0.75f + u * u * 0.25f);
                yield return null;
            }
            End();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_faceRight != null) Destroy(_faceRight);
            if (_faceLeft != null) Destroy(_faceLeft);
            if (_clip != null) Destroy(_clip);
        }
    }
}
