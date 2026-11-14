using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Reading
{
    /// <summary>Medidas de un libro abierto, por lado (0 izquierda, 1 derecha). Todo en metros a escala 1.</summary>
    internal struct BookShape
    {
        public float W, H, Gutter;          // Página y separación central (las hojas casi se tocan).
        public float BlockL, BlockR;        // Grosor de cada bloque de hojas (lo leído queda a un lado).
        public float CurlL, CurlR;          // Cuánto se hunde cada hoja junto al lomo (0 con el libro cerrado).
        public bool Hard;                   // Tapa dura (cartón rígido) o blanda (se dobla con las hojas).
        public float Board, Overhang, Corner, Joint, Reach;

        public float Block(int side) => side < 0 ? BlockL : BlockR;
        public float CurlDepth(int side) => side < 0 ? CurlL : CurlR;

        /// <summary>Profundidad de la cara trasera del bloque donde se apoya la tapa dura (plana).</summary>
        public float HardBack(int side) => Mathf.Max(Block(side), CurlDepth(side) + 0.0004f);
    }

    /// <summary>Malla en listas (sin UnityEngine.Mesh): se puede generar y revisar fuera de Unity.</summary>
    internal sealed class MeshData
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();

        public void Clear() { V.Clear(); UV.Clear(); T.Clear(); }

        /// <summary>
        /// Cuadrilátero a-b-c-d (en orden alrededor del borde) que mira hacia «facing»: si el giro sale al revés
        /// se invierte. Unity muestra la cara cuyo producto (b-a)×(c-a) apunta a la cámara.
        /// </summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-20f) n = Vector3.Cross(c - a, d - a);
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
            if (Vector3.Dot(n, facing) >= 0f)
            {
                T.Add(i); T.Add(i + 1); T.Add(i + 2);
                T.Add(i); T.Add(i + 2); T.Add(i + 3);
            }
            else
            {
                T.Add(i); T.Add(i + 2); T.Add(i + 1);
                T.Add(i); T.Add(i + 3); T.Add(i + 2);
            }
        }

        /// <summary>Cara visible desde los dos lados (lomo fino).</summary>
        public void DoubleQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            Quad(a, b, c, d, ua, ub, uc, ud, n);
            Quad(a, b, c, d, ua, ub, uc, ud, -n);
        }
    }

    /// <summary>
    /// Forma de un libro abierto como uno de papel (pedido del usuario, 14 nov 2026, «que se vea como en Livro»;
    /// geometría propia, sin código ni recursos de nadie). Cada mitad vive en su propio espacio («media»):
    /// lomo en x = 0, la hoja en z = 0 mirando hacia -Z y la tapa detrás (+Z). La mitad izquierda va hacia -X.
    /// La hoja baja hacia el lomo siguiendo Curl; en tapa blanda todo el lado (hojas y tapa) se dobla igual,
    /// en tapa dura la tapa queda plana y solo el bloque se hunde.
    /// </summary>
    internal static class BookGeometry
    {
        public const int PageColumns = 40;
        private const float LineSpacing = 0.00035f;   // Una hoja cada ~0,35 mm en el canto.
        private const float EdgeTiling = 40f;

        /// <summary>Hundimiento hacia +Z a una distancia del lomo f (fracción del ancho de página).</summary>
        public static float Curl(float f, float depth, float reach)
        {
            if (depth <= 0f || reach <= 0f || f >= reach) return 0f;
            float t = 1f - Mathf.Max(0f, f) / reach;
            return depth * t * t;
        }

        public static float PageZ(in BookShape s, int side, float f) => Curl(f, s.CurlDepth(side), s.Reach);

        public static float BlockBack(in BookShape s, int side, float f) =>
            s.Hard ? s.HardBack(side) : PageZ(s, side, f) + s.Block(side);

        /// <summary>Posición en el espacio de la mitad, a una fracción f del ancho de página desde el lomo.</summary>
        public static float X(in BookShape s, int side, float f) => side * (s.Gutter * 0.5f + f * s.W);

        // ───────────── Medidas y poses (las usa BookBody; también se prueban fuera de Unity) ─────────────

        public const float TiltDegrees = 9f;          // Cada mitad sube 9°: el libro abre a unos 162°.
        private const float PerPage = 0.00005f;       // 0,05 mm por página (0,1 mm por hoja).

        /// <summary>
        /// Medidas de un libro: grosor según la cantidad de páginas, repartido según lo leído (a la izquierda en
        /// lectura normal, a la derecha en manga), y tapa dura o blanda.
        /// </summary>
        public static BookShape Measure(float w, float h, float gutter, int pages, int leftIndex, bool rightToLeft, bool hard)
        {
            pages = Mathf.Max(2, pages);
            float read = leftIndex < 0 ? 0.5f : Mathf.Clamp01((leftIndex + 1f) / pages);
            float total = Mathf.Clamp(pages * PerPage, 0.0012f, 0.034f);
            float leftShare = rightToLeft ? 1f - read : read;
            var s = new BookShape
            {
                W = Mathf.Max(0.02f, w), H = Mathf.Max(0.02f, h), Gutter = gutter,
                BlockL = Mathf.Max(0.0004f, total * leftShare), BlockR = Mathf.Max(0.0004f, total * (1f - leftShare)),
                Hard = hard,
                Board = hard ? 0.0024f : 0.0005f,
                Overhang = hard ? 0.003f : 0.0015f,
                Corner = hard ? 0.004f : 0.003f,
                Joint = hard ? 0.0015f : 0f,
                Reach = hard ? 0.24f : 0.27f
            };
            return s;
        }

        /// <summary>Hundimiento de la hoja junto al lomo con el libro abierto.</summary>
        public static float OpenCurl(float block, bool hard) =>
            hard ? Mathf.Min(0.008f, block * 0.75f + 0.001f) : Mathf.Min(0.009f, 0.0055f + block * 0.35f);

        /// <summary>
        /// Giro de cada mitad (grados, hacia quien lee) según cuán cerrado está (fold 0 abierto, 1 cerrado).
        /// Abierto: las dos suben TiltDegrees. Cerrado: la mitad de la portada da la vuelta sobre la otra, que
        /// queda plana mirando a quien lee. rest: lado que no se mueve (+1 derecha, -1 izquierda).
        /// </summary>
        public static void HalfAngles(float fold, int rest, out float left, out float right)
        {
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fold));
            float front = Mathf.Lerp(TiltDegrees, 180f, s), still = Mathf.Lerp(TiltDegrees, 0f, s);
            left = rest > 0 ? front : still;
            right = rest > 0 ? still : front;
        }

        public static Quaternion HalfRotation(int side, float angle) => Quaternion.Euler(0f, side < 0 ? -angle : angle, 0f);

        /// <summary>Lomo entre las dos tapas ya giradas (espacio del libro).</summary>
        public static void SpineFor(MeshData m, in BookShape s, Quaternion rl, Quaternion rr, out Vector3 middle, out Vector3 outward, out float thickness)
        {
            Vector3 p0 = rl * BoardSpineEdge(s, -1, true), p3 = rr * BoardSpineEdge(s, 1, true);
            Vector3 q0 = rl * BoardSpineEdge(s, -1, false), q3 = rr * BoardSpineEdge(s, 1, false);
            Vector3 dirL = rl * Vector3.right, dirR = rr * Vector3.left;
            Vector3 backs = rl * Vector3.forward + rr * Vector3.forward;
            Vector3 bulge = backs.sqrMagnitude > 0.09f ? backs.normalized * (backs.magnitude * 0.5f) * (s.Hard ? 0.004f : 0.0015f) : Vector3.zero;
            Spine(m, p0, p3, dirL, dirR, bulge, s.H * 0.5f + s.Overhang, out middle, out outward, (q0 + q3) * 0.5f);
            thickness = (p3 - p0).magnitude;
        }

        // ───────────── Hoja (espacio de la página: cuadrado unidad, z en metros) ─────────────

        /// <summary>
        /// Malla de una página en [-0,5; 0,5]² que se hunde hacia el lomo. Con shade, solo la franja [f0, f1]
        /// y la coordenada u recorre esa franja (degradado de la sombra del pliegue).
        /// </summary>
        public static void Page(MeshData m, bool left, float f0, float f1, float lift, float depth, float reach, bool shade)
        {
            m.Clear();
            Vector3 toViewer = new Vector3(0f, 0f, -1f);
            for (int i = 0; i < PageColumns; i++)
            {
                float fa = Mathf.Lerp(f0, f1, i / (float)PageColumns), fb = Mathf.Lerp(f0, f1, (i + 1) / (float)PageColumns);
                float xa = left ? 0.5f - fa : fa - 0.5f, xb = left ? 0.5f - fb : fb - 0.5f;
                float za = Curl(fa, depth, reach) + lift, zb = Curl(fb, depth, reach) + lift;
                float ua = shade ? (fa - f0) / Mathf.Max(1e-4f, f1 - f0) : xa + 0.5f;
                float ub = shade ? (fb - f0) / Mathf.Max(1e-4f, f1 - f0) : xb + 0.5f;
                m.Quad(new Vector3(xa, -0.5f, za), new Vector3(xb, -0.5f, zb), new Vector3(xb, 0.5f, zb), new Vector3(xa, 0.5f, za),
                       new Vector2(ua, 0f), new Vector2(ub, 0f), new Vector2(ub, 1f), new Vector2(ua, 1f), toViewer);
            }
        }

        // ───────────── Bloque de hojas (espacio de la mitad) ─────────────

        /// <summary>Canto de las hojas (arriba, abajo, borde libre y fondo del pliegue) y la hoja de papel bajo la página.</summary>
        public static void Block(MeshData m, in BookShape s, int side)
        {
            m.Clear();
            const int steps = 20;
            // La textura del canto trae 16 líneas por vuelta: una línea cada LineSpacing.
            float y = s.H * 0.5f, lines = 1f / (LineSpacing * 16f);
            // Canto del borde libre.
            float xo = X(s, side, 1f), top1 = PageZ(s, side, 1f), back1 = BlockBack(s, side, 1f);
            m.Quad(new Vector3(xo, -y, top1), new Vector3(xo, y, top1), new Vector3(xo, y, back1), new Vector3(xo, -y, back1),
                   new Vector2(0f, top1 * lines), new Vector2(s.H * EdgeTiling, top1 * lines),
                   new Vector2(s.H * EdgeTiling, back1 * lines), new Vector2(0f, back1 * lines), new Vector3(side, 0f, 0f));
            Vector2 paper = new Vector2(0.5f, 0.6f);
            for (int i = 0; i < steps; i++)
            {
                float fa = i / (float)steps, fb = (i + 1) / (float)steps;
                float xa = X(s, side, fa), xb = X(s, side, fb);
                float ta = PageZ(s, side, fa), tb = PageZ(s, side, fb);
                float ba = BlockBack(s, side, fa), bb = BlockBack(s, side, fb);
                for (int end = -1; end <= 1; end += 2)
                {
                    float ye = end * y;
                    m.Quad(new Vector3(xa, ye, ta), new Vector3(xb, ye, tb), new Vector3(xb, ye, bb), new Vector3(xa, ye, ba),
                           new Vector2(fa * s.W * EdgeTiling, ta * lines), new Vector2(fb * s.W * EdgeTiling, tb * lines),
                           new Vector2(fb * s.W * EdgeTiling, bb * lines), new Vector2(fa * s.W * EdgeTiling, ba * lines),
                           new Vector3(0f, end, 0f));
                }
                // Hoja de papel justo detrás de la página: es lo que se ve mientras se pliega o si la página se apaga.
                const float under = 0.0006f;
                m.Quad(new Vector3(xa, -y, ta + under), new Vector3(xb, -y, tb + under), new Vector3(xb, y, tb + under), new Vector3(xa, y, ta + under),
                       paper, paper, paper, paper, new Vector3(0f, 0f, -1f));
            }
            // Fondo del pliegue (lado del lomo).
            float xi = X(s, side, 0f), top0 = PageZ(s, side, 0f), back0 = BlockBack(s, side, 0f);
            m.Quad(new Vector3(xi, -y, top0), new Vector3(xi, y, top0), new Vector3(xi, y, back0), new Vector3(xi, -y, back0),
                   new Vector2(0f, top0 * lines), new Vector2(s.H * EdgeTiling, top0 * lines),
                   new Vector2(s.H * EdgeTiling, back0 * lines), new Vector2(0f, back0 * lines), new Vector3(-side, 0f, 0f));
        }

        // ───────────── Tapa (espacio de la mitad) ─────────────

        private struct Column { public float U, X, Y, Front, Back; }

        /// <summary>Columnas de la tapa desde el lomo hacia el borde, con las esquinas exteriores redondeadas.</summary>
        private static List<Column> BoardColumns(in BookShape s, int side)
        {
            var cols = new List<Column>();
            float length = s.W + s.Overhang - s.Joint;
            float r = Mathf.Clamp(s.Corner, 0f, Mathf.Min(length * 0.4f, s.H * 0.25f));
            float y = s.H * 0.5f + s.Overhang;
            const int straight = 24, corner = 8;
            for (int i = 0; i <= straight; i++) cols.Add(MakeColumn(s, side, (length - r) * i / straight, y));
            for (int k = 1; k <= corner; k++)
            {
                float a = Mathf.PI * 0.5f * k / corner;
                cols.Add(MakeColumn(s, side, length - r + r * Mathf.Sin(a), y - r + r * Mathf.Cos(a)));
            }
            return cols;
        }

        private static Column MakeColumn(in BookShape s, int side, float u, float y)
        {
            float f = Mathf.Clamp01((s.Joint + u) / Mathf.Max(1e-4f, s.W));
            float front = BlockBack(s, side, f);
            return new Column { U = u, X = side * (s.Gutter * 0.5f + s.Joint + u), Y = y, Front = front, Back = front + s.Board };
        }

        /// <summary>Tapa (dura o blanda) con esquinas redondeadas del lado del borde libre.</summary>
        public static void Board(MeshData m, in BookShape s, int side)
        {
            m.Clear();
            List<Column> c = BoardColumns(s, side);
            const float tile = 4f;
            float y = s.H * 0.5f + s.Overhang;
            float length = s.W + s.Overhang - s.Joint;
            float r = Mathf.Clamp(s.Corner, 0f, Mathf.Min(length * 0.4f, s.H * 0.25f));
            Vector2 cornerCenter = new Vector2(side * (s.Gutter * 0.5f + s.Joint + length - r), y - r);
            for (int i = 0; i + 1 < c.Count; i++)
            {
                Column a = c[i], b = c[i + 1];
                // Caras trasera (afuera) y delantera (bajo las hojas).
                m.Quad(new Vector3(a.X, -a.Y, a.Back), new Vector3(b.X, -b.Y, b.Back), new Vector3(b.X, b.Y, b.Back), new Vector3(a.X, a.Y, a.Back),
                       new Vector2(a.X * tile, -a.Y * tile), new Vector2(b.X * tile, -b.Y * tile),
                       new Vector2(b.X * tile, b.Y * tile), new Vector2(a.X * tile, a.Y * tile), new Vector3(0f, 0f, 1f));
                m.Quad(new Vector3(a.X, -a.Y, a.Front), new Vector3(b.X, -b.Y, b.Front), new Vector3(b.X, b.Y, b.Front), new Vector3(a.X, a.Y, a.Front),
                       new Vector2(a.X * tile, -a.Y * tile), new Vector2(b.X * tile, -b.Y * tile),
                       new Vector2(b.X * tile, b.Y * tile), new Vector2(a.X * tile, a.Y * tile), new Vector3(0f, 0f, -1f));
                // Cantos de arriba y abajo (en las esquinas, hacia afuera del arco).
                float midX = (a.X + b.X) * 0.5f, midY = (a.Y + b.Y) * 0.5f, midU = (a.U + b.U) * 0.5f;
                Vector3 outward = midU > length - r ? new Vector3(midX - cornerCenter.x, midY - cornerCenter.y, 0f) : Vector3.up;
                for (int end = -1; end <= 1; end += 2)
                {
                    Vector3 o = new Vector3(outward.x, outward.y * end, 0f);
                    m.Quad(new Vector3(a.X, end * a.Y, a.Front), new Vector3(b.X, end * b.Y, b.Front),
                           new Vector3(b.X, end * b.Y, b.Back), new Vector3(a.X, end * a.Y, a.Back),
                           new Vector2(a.U * tile, 0f), new Vector2(b.U * tile, 0f),
                           new Vector2(b.U * tile, s.Board * tile), new Vector2(a.U * tile, s.Board * tile), o);
                }
            }
            // Borde libre (entre las esquinas) y borde del lomo.
            Column last = c[c.Count - 1], first = c[0];
            m.Quad(new Vector3(last.X, -last.Y, last.Front), new Vector3(last.X, last.Y, last.Front),
                   new Vector3(last.X, last.Y, last.Back), new Vector3(last.X, -last.Y, last.Back),
                   Vector2.zero, new Vector2(last.Y * 2f * tile, 0f), new Vector2(last.Y * 2f * tile, s.Board * tile),
                   new Vector2(0f, s.Board * tile), new Vector3(side, 0f, 0f));
            m.Quad(new Vector3(first.X, -y, first.Front), new Vector3(first.X, y, first.Front),
                   new Vector3(first.X, y, first.Back), new Vector3(first.X, -y, first.Back),
                   Vector2.zero, new Vector2(y * 2f * tile, 0f), new Vector2(y * 2f * tile, s.Board * tile),
                   new Vector2(0f, s.Board * tile), new Vector3(-side, 0f, 0f));
        }

        /// <summary>
        /// Arte de la tapa, de borde a borde sobre la cara de afuera (con las mismas esquinas redondeadas).
        /// spineAtU0: la u = 0 de la imagen queda junto al lomo (si no, en el borde libre).
        /// </summary>
        public static void CoverArt(MeshData m, in BookShape s, int side, bool spineAtU0)
        {
            m.Clear();
            List<Column> c = BoardColumns(s, side);
            float length = s.W + s.Overhang - s.Joint, y = s.H * 0.5f + s.Overhang;
            const float above = 0.00025f;
            for (int i = 0; i + 1 < c.Count; i++)
            {
                Column a = c[i], b = c[i + 1];
                float ua = a.U / length, ub = b.U / length;
                if (!spineAtU0) { ua = 1f - ua; ub = 1f - ub; }
                float va0 = 0.5f - a.Y / (2f * y), va1 = 0.5f + a.Y / (2f * y);
                float vb0 = 0.5f - b.Y / (2f * y), vb1 = 0.5f + b.Y / (2f * y);
                m.Quad(new Vector3(a.X, -a.Y, a.Back + above), new Vector3(b.X, -b.Y, b.Back + above),
                       new Vector3(b.X, b.Y, b.Back + above), new Vector3(a.X, a.Y, a.Back + above),
                       new Vector2(ua, va0), new Vector2(ub, vb0), new Vector2(ub, vb1), new Vector2(ua, va1), new Vector3(0f, 0f, 1f));
            }
        }

        /// <summary>Punto del borde de la tapa junto al lomo (cara de afuera o de adentro), en el espacio de la mitad.</summary>
        public static Vector3 BoardSpineEdge(in BookShape s, int side, bool outside)
        {
            float f = Mathf.Clamp01(s.Joint / Mathf.Max(1e-4f, s.W));
            float front = BlockBack(s, side, f);
            return new Vector3(side * (s.Gutter * 0.5f + s.Joint), 0f, outside ? front + s.Board : front);
        }

        /// <summary>Centro y tamaño de la cara de afuera de la tapa (donde va el título), en el espacio de la mitad.</summary>
        public static Vector3 CoverCenter(in BookShape s, int side, out float width, out float height)
        {
            width = s.W + s.Overhang - s.Joint;
            height = s.H + 2f * s.Overhang;
            float u = width * 0.5f;
            Column c = MakeColumn(s, side, u, height * 0.5f);
            return new Vector3(c.X, 0f, c.Back);
        }

        // ───────────── Lomo (espacio del libro) ─────────────

        /// <summary>Bézier cúbica.</summary>
        public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        /// <summary>
        /// Lomo como una franja curva entre los bordes de las dos tapas (p0 izquierda, p3 derecha, cara de afuera).
        /// dirL/dirR: hacia dónde sale cada tapa por el lomo; bulge empuja la curva hacia afuera del libro.
        /// Devuelve el punto medio y la normal hacia afuera (para el título del lomo).
        /// </summary>
        public static void Spine(MeshData m, Vector3 p0, Vector3 p3, Vector3 dirL, Vector3 dirR, Vector3 bulge,
                                 float halfHeight, out Vector3 middle, out Vector3 outward, Vector3 inside)
        {
            m.Clear();
            const int steps = 14;
            float k = Mathf.Max(0.002f, (p3 - p0).magnitude * 0.55f);
            Vector3 p1 = p0 + dirL * k + bulge, p2 = p3 + dirR * k + bulge;
            Vector3 up = new Vector3(0f, halfHeight, 0f);
            for (int i = 0; i < steps; i++)
            {
                Vector3 a = Bezier(p0, p1, p2, p3, i / (float)steps), b = Bezier(p0, p1, p2, p3, (i + 1) / (float)steps);
                float ua = i / (float)steps, ub = (i + 1) / (float)steps;
                m.DoubleQuad(a - up, b - up, b + up, a + up,
                             new Vector2(ua, 0f), new Vector2(ub, 0f), new Vector2(ub, 4f), new Vector2(ua, 4f));
            }
            middle = Bezier(p0, p1, p2, p3, 0.5f);
            outward = middle - inside;
            outward = outward.sqrMagnitude > 1e-10f ? outward.normalized : new Vector3(0f, 0f, 1f);
        }
    }
}
