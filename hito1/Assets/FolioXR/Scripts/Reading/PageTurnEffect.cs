using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FolioXR.Reading
{
    /// <summary>
    /// Encargo 8 (escrito por Claude). Hoja que pasa de un lado al otro del libro, curvada por código.
    /// Vive en el mismo padre que las páginas: lomo en x = 0, páginas hacia -Z.
    /// Primera mitad del giro: muestra la página que se va; segunda mitad: la que llega.
    /// </summary>
    internal sealed class PageTurnEffect : MonoBehaviour
    {
        private const int Columns = 12;
        private const float Seconds = 0.35f, Lag = 0.75f;
        private Mesh _mesh;
        private Material _material;
        private MeshRenderer _renderer;
        private readonly Vector3[] _vertices = new Vector3[(Columns + 1) * 2];
        private AudioSource _audio;
        private AudioClip _clip;

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
            _material = new Material(source) { name = "FolioXR hoja que pasa" };
            _material.SetFloat("_Cull", (float)CullMode.Off);
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
            _mesh.triangles = triangles;
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
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

        /// <param name="towardLeft">La hoja sale de la derecha y cae a la izquierda.</param>
        /// <param name="inner">Distancia del lomo al borde interior de la página (medio margen central).</param>
        /// <param name="tilt">Inclinación de cada mitad del libro abierto (radianes): la hoja sale y llega en esa V.</param>
        internal IEnumerator Play(bool towardLeft, Texture first, Texture second, float width, float height, float inner,
                                  float tilt = 0f)
        {
            _renderer.enabled = true;
            if (_audio != null && _clip != null) _audio.PlayOneShot(_clip);
            float elapsed = 0f;
            while (elapsed < Seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Seconds));
                // tilt = sobre la mitad derecha, π - tilt = sobre la izquierda (el libro abre en V).
                float angle = Mathf.Lerp(tilt, Mathf.PI - tilt, towardLeft ? t : 1f - t);
                bool onLeft = angle > Mathf.PI * 0.5f;
                bool secondHalf = towardLeft == onLeft;
                Texture texture = secondHalf ? second : first;
                _material.mainTexture = texture != null ? texture : Texture2D.whiteTexture;
                // Sobre la izquierda la hoja se ve por detrás: se espeja para que el texto quede derecho.
                _material.mainTextureScale = new Vector2(onLeft ? -1f : 1f, 1f);
                _material.mainTextureOffset = new Vector2(onLeft ? 1f : 0f, 0f);
                Shape(angle, towardLeft, width, height, inner);
                yield return null;
            }
            _renderer.enabled = false;
        }

        private void Shape(float angle, bool towardLeft, float width, float height, float inner)
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

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
            if (_clip != null) Destroy(_clip);
        }
    }
}
