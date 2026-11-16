using UnityEngine;

namespace FolioXR.Reading
{
    /// <summary>
    /// Forma de la hoja mientras pasa de un lado al otro llevada por la mano (pedido del usuario, 15 nov 2026:
    /// «quiero que la hoja se mueva a medida que va pasando la mano»). Solo cuentas, sin objetos de Unity, para
    /// poder probarlas fuera del visor (tests/libro).
    /// Se mira el libro en corte: x a lo ancho (lomo en 0, derecha positiva) y «alto» hacia quien lee (el −Z
    /// del libro). La hoja es una cadena de tramos del lomo al borde libre. El avance t va de 0 (apoyada en el
    /// lado del que sale) a 1 (apoyada en el otro).
    /// Como una hoja de papel tomada por el borde: al principio el borde se levanta y el pie, junto al lomo,
    /// sigue acostado; a mitad de camino la hoja queda parada; al final el pie ya descansa del otro lado y el
    /// borde baja último.
    /// </summary>
    internal static class PageTurnShape
    {
        public const int Columns = 24;
        /// <summary>Luz entre la hoja que pasa y las páginas de abajo (evita que se mezclen al dibujar).</summary>
        public const float Clearance = 0.0005f;
        private const float BaseDelay = 0.85f;    // Cuánto se atrasa (y luego adelanta) el pie respecto del borde.
        private static readonly float[] ScratchX = new float[Columns + 1], ScratchLift = new float[Columns + 1];

        /// <summary>Avance del pie de la hoja para un avance t del borde: lento al salir y al llegar.</summary>
        public static float BaseProgress(float t)
        {
            t = Mathf.Clamp01(t);
            return t - BaseDelay * Mathf.Sin(2f * Mathf.PI * t) / (2f * Mathf.PI);
        }

        /// <summary>
        /// Puntos de la hoja (Columns + 1, del lomo al borde libre).
        /// towardLeft: sale de la derecha y cae a la izquierda. width: ancho de página. inner: del lomo al borde
        /// interior de la página. tilt: inclinación de cada mitad del libro abierto (radianes).
        /// curlFrom/curlTo y reach: hundimiento de las páginas junto al lomo (como BookGeometry.Curl) del lado
        /// del que sale y del lado al que llega: en t = 0 y t = 1 la hoja coincide con esas páginas.
        /// </summary>
        public static void Chain(float t, bool towardLeft, float width, float inner, float tilt,
                                 float curlFrom, float curlTo, float reach, float[] x, float[] lift)
        {
            t = Mathf.Clamp01(t);
            float span = Mathf.PI - 2f * tilt;
            float edge = tilt + span * t, foot = tilt + span * BaseProgress(t);
            float segment = width / Columns;
            float sin = Mathf.Sin(tilt), cos = Mathf.Cos(tilt);
            float px = Mathf.Cos(foot) * inner, pl = Mathf.Sin(foot) * inner;
            for (int i = 0; i <= Columns; i++)
            {
                float f = i / (float)Columns;
                // Hacia adentro del libro, perpendicular a cada mitad: (sin, −cos) a la derecha y (−sin, −cos) a la izquierda.
                float from = (BookGeometry.Curl(f, curlFrom, reach) - Clearance) * (1f - t);
                float to = (BookGeometry.Curl(f, curlTo, reach) - Clearance) * t;
                x[i] = px + (from - to) * sin;
                lift[i] = pl - (from + to) * cos;
                if (i == Columns) break;
                // El papel se dobla más cerca del lomo y sigue casi recto hacia el borde.
                float s = (i + 0.5f) / Columns;
                float a = foot + (edge - foot) * (1f - (1f - s) * (1f - s));
                px += Mathf.Cos(a) * segment;
                pl += Mathf.Sin(a) * segment;
            }
            if (!towardLeft)
                for (int i = 0; i <= Columns; i++) x[i] = -x[i];
        }

        /// <summary>Posición a lo ancho del punto de la hoja que está a «along» del borde interior (0 a width).</summary>
        public static float XAt(float t, bool towardLeft, float width, float inner, float tilt,
                                float curlFrom, float curlTo, float reach, float along)
        {
            Chain(t, towardLeft, width, inner, tilt, curlFrom, curlTo, reach, ScratchX, ScratchLift);
            float k = Mathf.Clamp01(along / Mathf.Max(1e-6f, width)) * Columns;
            int i = Mathf.Min(Columns - 1, (int)k);
            return ScratchX[i] + (ScratchX[i + 1] - ScratchX[i]) * (k - i);
        }

        /// <summary>Altura (hacia quien lee) de ese mismo punto.</summary>
        public static float LiftAt(float t, bool towardLeft, float width, float inner, float tilt,
                                   float curlFrom, float curlTo, float reach, float along)
        {
            Chain(t, towardLeft, width, inner, tilt, curlFrom, curlTo, reach, ScratchX, ScratchLift);
            float k = Mathf.Clamp01(along / Mathf.Max(1e-6f, width)) * Columns;
            int i = Mathf.Min(Columns - 1, (int)k);
            return ScratchLift[i] + (ScratchLift[i + 1] - ScratchLift[i]) * (k - i);
        }

        /// <summary>
        /// Avance t con el que el punto de la hoja tomado a «along» del borde interior queda sobre x (la mano).
        /// La posición a lo ancho solo disminuye (o solo aumenta) con t, así que se busca por mitades.
        /// </summary>
        public static float Solve(float x, bool towardLeft, float width, float inner, float tilt,
                                  float curlFrom, float curlTo, float reach, float along)
        {
            float sign = towardLeft ? 1f : -1f;   // Hacia la derecha es el mismo problema reflejado.
            float target = x * sign;
            if (target >= XAt(0f, towardLeft, width, inner, tilt, curlFrom, curlTo, reach, along) * sign) return 0f;
            if (target <= XAt(1f, towardLeft, width, inner, tilt, curlFrom, curlTo, reach, along) * sign) return 1f;
            float low = 0f, high = 1f;
            for (int i = 0; i < 20; i++)
            {
                float middle = (low + high) * 0.5f;
                if (XAt(middle, towardLeft, width, inner, tilt, curlFrom, curlTo, reach, along) * sign > target) low = middle;
                else high = middle;
            }
            return (low + high) * 0.5f;
        }

        // ───────────── Tapa que sigue a la mano (cerrar y abrir) ─────────────

        /// <summary>
        /// Cuán cerrada queda la tapa (0 abierta, 1 cerrada; lineal en su ángulo) cuando el punto tomado a
        /// «along» del lomo queda sobre x. frontSide: lado de la mitad que se pliega con el libro abierto (−1 o +1).
        /// </summary>
        public static float FoldFromX(float x, int frontSide, float along, float tilt)
        {
            float cos = Mathf.Clamp(frontSide * x / Mathf.Max(1e-6f, along), -1f, 1f);
            float angle = Mathf.Acos(cos);
            return Mathf.Clamp01((angle - tilt) / Mathf.Max(1e-6f, Mathf.PI - tilt));
        }

        /// <summary>Inversa de Mathf.SmoothStep(0, 1, t): el t que da y.</summary>
        public static float InverseSmoothStep(float y)
        {
            y = Mathf.Clamp01(y);
            return 0.5f - Mathf.Sin(Mathf.Asin(1f - 2f * y) / 3f);
        }

        /// <summary>Altura máxima a la que puede estar el punto tomado cuando está sobre x (arco alrededor del lomo).</summary>
        public static float ArcHeight(float along, float x) => Mathf.Sqrt(Mathf.Max(0f, along * along - x * x));
    }
}
