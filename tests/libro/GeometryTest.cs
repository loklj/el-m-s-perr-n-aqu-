using System;
using System.Collections.Generic;
using UnityEngine;
using FolioXR.Reading;

/// <summary>Pruebas de la forma del libro (BookGeometry) sin Unity: caras, portada, lomo y grosores.</summary>
static class GeometryTest
{
    static int checks, fails;
    static void Ok(bool c, string what) { checks++; if (!c) { fails++; Console.WriteLine("FALLA: " + what); } }

    static Vector3 Normal(MeshData m, int t) =>
        Vector3.Cross(m.V[m.T[t + 1]] - m.V[m.T[t]], m.V[m.T[t + 2]] - m.V[m.T[t]]);

    static bool AllFacing(MeshData m, Func<Vector3, Vector3> map, Vector3 dir, float minDot)
    {
        for (int t = 0; t < m.T.Count; t += 3)
        {
            Vector3 n = Vector3.Cross(map(m.V[m.T[t + 1]]) - map(m.V[m.T[t]]), map(m.V[m.T[t + 2]]) - map(m.V[m.T[t]]));
            if (n.sqrMagnitude < 1e-16f) continue;
            if (Vector3.Dot(n.normalized, dir) < minDot) return false;
        }
        return true;
    }

    static bool Finite(MeshData m)
    {
        foreach (var v in m.V) if (float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z)) return false;
        return true;
    }

    /// <summary>¿La u del arte crece hacia la derecha de quien mira desde -Z (x del libro)?</summary>
    static float USlope(MeshData m, Func<Vector3, Vector3> map)
    {
        float sx = 0, su = 0, sxx = 0, sxu = 0; int n = m.V.Count;
        for (int i = 0; i < n; i++) { float x = map(m.V[i]).x, u = m.UV[i].x; sx += x; su += u; sxx += x * x; sxu += x * u; }
        return (n * sxu - sx * su) / Math.Max(1e-12f, n * sxx - sx * sx);
    }

    /// <summary>Hoja que sigue a la mano (PageTurnShape): nace y termina sobre las páginas, y la mano la gobierna sin saltos.</summary>
    static void PageTurn()
    {
        const int n = PageTurnShape.Columns;
        const float w = 0.17f, inner = 0.001f, reach = 0.27f, curlA = 0.006f, curlB = 0.008f;
        float tilt = BookGeometry.TiltDegrees * Mathf.Deg2Rad, sin = Mathf.Sin(tilt), cos = Mathf.Cos(tilt);
        var x = new float[n + 1]; var lift = new float[n + 1];
        var x2 = new float[n + 1]; var lift2 = new float[n + 1];
        foreach (bool towardLeft in new[] { true, false })
        {
            string name = towardLeft ? " (hacia la izquierda)" : " (hacia la derecha)";
            float side = towardLeft ? 1f : -1f;   // Lado del que sale.
            // En t = 0 la hoja coincide con la página del lado del que sale; en t = 1, con la del otro.
            for (int end = 0; end <= 1; end++)
            {
                PageTurnShape.Chain(end, towardLeft, w, inner, tilt, curlA, curlB, reach, x, lift);
                float half = end == 0 ? side : -side, curl = end == 0 ? curlA : curlB;
                bool flat = true;
                for (int i = 0; i <= n; i++)
                {
                    float f = i / (float)n;
                    float along = half * x[i] * cos + lift[i] * sin, depth = half * x[i] * sin - lift[i] * cos;
                    if (Math.Abs(along - (inner + f * w)) > 2e-5f) flat = false;
                    if (Math.Abs(depth - (BookGeometry.Curl(f, curl, reach) - PageTurnShape.Clearance)) > 2e-5f) flat = false;
                }
                Ok(flat, "la hoja " + (end == 0 ? "sale de" : "llega a") + " la forma de la página" + name);
            }
            // Mientras pasa: no se estira, no atraviesa las páginas y avanza siempre hacia el otro lado.
            bool length = true, above = true, forward = true, finite = true;
            float lastGrab = float.MaxValue;
            for (int k = 0; k <= 200; k++)
            {
                float t = k / 200f;
                PageTurnShape.Chain(t, towardLeft, w, inner, tilt, 0f, 0f, reach, x, lift);
                for (int i = 0; i < n; i++)
                {
                    float dx = x[i + 1] - x[i], dl = lift[i + 1] - lift[i];
                    if (Math.Abs(Math.Sqrt(dx * dx + dl * dl) - w / n) > 1e-5) length = false;
                }
                PageTurnShape.Chain(t, towardLeft, w, inner, tilt, curlA, curlB, reach, x, lift);
                for (int i = 0; i <= n; i++)
                {
                    if (float.IsNaN(x[i] + lift[i])) finite = false;
                    // Altura de la página de abajo en ese x (cada mitad sube con la V y se hunde junto al lomo).
                    float floor = Math.Abs(x[i]) * sin / cos - Math.Max(curlA, curlB) / cos;
                    if (lift[i] < floor - 1e-4f) above = false;
                }
                float grab = side * PageTurnShape.XAt(t, towardLeft, w, inner, tilt, curlA, curlB, reach, 0.8f * w);
                if (grab > lastGrab + 1e-6f) forward = false;
                lastGrab = grab;
            }
            Ok(length, "la hoja no se estira ni se encoge" + name);
            Ok(above, "la hoja no atraviesa las páginas" + name);
            Ok(forward, "el punto tomado avanza siempre hacia el otro lado" + name);
            Ok(finite, "la hoja no tiene puntos inválidos" + name);
            // La mano: de la posición del punto tomado se recupera el avance.
            bool solved = true;
            foreach (float along in new[] { 0.5f * w, 0.8f * w, w })
                for (int k = 0; k <= 40; k++)
                {
                    float t = k / 40f;
                    float at = PageTurnShape.XAt(t, towardLeft, w, inner, tilt, curlA, curlB, reach, along);
                    float back = PageTurnShape.Solve(at, towardLeft, w, inner, tilt, curlA, curlB, reach, along);
                    // Cerca de los extremos la hoja casi no se mueve a lo ancho: se compara por posición.
                    float again = PageTurnShape.XAt(back, towardLeft, w, inner, tilt, curlA, curlB, reach, along);
                    if (Math.Abs(again - at) > 2e-5f) solved = false;
                    if (t > 0.1f && t < 0.9f && Math.Abs(back - t) > 2e-3f) solved = false;
                }
            Ok(solved, "de la mano se recupera el avance de la hoja" + name);
            Ok(PageTurnShape.Solve(side * 2f * w, towardLeft, w, inner, tilt, curlA, curlB, reach, 0.8f * w) == 0f, "mano más afuera que la hoja: no se levanta" + name);
            Ok(PageTurnShape.Solve(-side * 2f * w, towardLeft, w, inner, tilt, curlA, curlB, reach, 0.8f * w) == 1f, "mano pasada al otro lado: hoja apoyada" + name);
            float middle = PageTurnShape.Solve(0f, towardLeft, w, inner, tilt, curlA, curlB, reach, 0.8f * w);
            Ok(Math.Abs(middle - 0.5f) < 0.02f, "mano sobre el lomo: hoja a mitad de camino" + name + ": " + middle);
            // A mitad de camino la hoja está parada sobre el lomo.
            PageTurnShape.Chain(0.5f, towardLeft, w, inner, tilt, 0f, 0f, reach, x, lift);
            Ok(Math.Abs(x[n]) < 1e-4f && lift[n] > 0.95f * w, "a mitad de camino la hoja queda parada" + name);
            // Al empezar, el borde ya se levantó y el pie sigue casi acostado (como una hoja tomada por el borde).
            PageTurnShape.Chain(0.2f, towardLeft, w, inner, tilt, 0f, 0f, reach, x, lift);
            float foot = (float)Math.Atan2(lift[1] - lift[0], side * (x[1] - x[0])), edge = (float)Math.Atan2(lift[n] - lift[n - 1], side * (x[n] - x[n - 1]));
            Ok(edge > foot + 0.2f && foot < tilt + 0.3f, "al empezar se levanta primero el borde" + name);
        }
        // Reflejo: hacia la derecha es el mismo giro espejado.
        PageTurnShape.Chain(0.37f, true, w, inner, tilt, curlA, curlB, reach, x, lift);
        PageTurnShape.Chain(0.37f, false, w, inner, tilt, curlA, curlB, reach, x2, lift2);
        bool mirror = true;
        for (int i = 0; i <= n; i++) if (Math.Abs(x[i] + x2[i]) > 1e-6f || Math.Abs(lift[i] - lift2[i]) > 1e-6f) mirror = false;
        Ok(mirror, "hacia la derecha es el mismo giro espejado");
        // El pie se atrasa al salir y se adelanta al llegar, sin retroceder nunca.
        bool steady = true;
        for (int k = 1; k <= 100; k++) if (PageTurnShape.BaseProgress(k / 100f) < PageTurnShape.BaseProgress((k - 1) / 100f)) steady = false;
        Ok(steady && PageTurnShape.BaseProgress(0f) == 0f && Math.Abs(PageTurnShape.BaseProgress(1f) - 1f) < 1e-6f, "el pie de la hoja nunca retrocede");
        Ok(PageTurnShape.BaseProgress(0.25f) < 0.25f && PageTurnShape.BaseProgress(0.75f) > 0.75f, "el pie se atrasa al salir y se adelanta al llegar");
        // Tapa en la mano.
        foreach (int front in new[] { -1, 1 })
        {
            float along = 0.8f * w;
            Ok(PageTurnShape.FoldFromX(front * along * cos, front, along, tilt) < 1e-3f, "tapa: mano donde estaba = abierto");
            Ok(PageTurnShape.FoldFromX(-front * along, front, along, tilt) > 0.999f, "tapa: mano del otro lado = cerrado");
            float half = PageTurnShape.FoldFromX(0f, front, along, tilt);
            Ok(Math.Abs(half - (Mathf.PI * 0.5f - tilt) / (Mathf.PI - tilt)) < 1e-4f, "tapa: mano sobre el lomo = tapa parada");
            bool rising = true;
            for (int k = 1; k <= 50; k++)
                if (PageTurnShape.FoldFromX(front * along * (1f - 2f * k / 50f), front, along, tilt) <
                    PageTurnShape.FoldFromX(front * along * (1f - 2f * (k - 1) / 50f), front, along, tilt) - 1e-6f) rising = false;
            Ok(rising, "tapa: se cierra a medida que la mano cruza");
            // El ángulo que pide la mano es el que BookGeometry da a la mitad que se pliega.
            float s = 0.37f;
            BookGeometry.HalfAngles(PageTurnShape.InverseSmoothStep(s), -front, out float la, out float ra);
            float angle = front < 0 ? la : ra, expected = BookGeometry.TiltDegrees + (180f - BookGeometry.TiltDegrees) * s;
            Ok(Math.Abs(angle - expected) < 0.05f, "tapa: el pliegue sigue el ángulo de la mano: " + angle + " / " + expected);
        }
        bool inverse = true;
        for (int k = 0; k <= 50; k++)
            if (Math.Abs(PageTurnShape.InverseSmoothStep(Mathf.SmoothStep(0f, 1f, k / 50f)) - k / 50f) > 1e-3f) inverse = false;
        Ok(inverse, "el suavizado del pliegue se deshace bien");
        Ok(Math.Abs(PageTurnShape.ArcHeight(0.1f, 0.06f) - 0.08f) < 1e-6f && PageTurnShape.ArcHeight(0.1f, 0.2f) == 0f, "altura del arco del punto tomado");
    }

    static void Main()
    {
        var m = new MeshData();
        // Medidas: un cómic de 26 páginas es fino; una novela de 400, gruesa; lo leído queda a un lado.
        var comic = BookGeometry.Measure(0.17f, 0.26f, 0.002f, 26, 10, false, false);
        Ok(Math.Abs(comic.BlockL + comic.BlockR - 0.0013f) < 0.0003f, "grosor del cómic ~1,3 mm: " + (comic.BlockL + comic.BlockR));
        var novel = BookGeometry.Measure(0.14f, 0.21f, 0.002f, 400, 120, false, true);
        Ok(Math.Abs(novel.BlockL + novel.BlockR - 0.020f) < 0.0005f, "grosor de la novela ~20 mm");
        Ok(novel.BlockL < novel.BlockR, "en lectura normal lo leído (poco) queda a la izquierda");
        var manga = BookGeometry.Measure(0.13f, 0.19f, 0.002f, 200, 60, true, false);
        Ok(manga.BlockR < manga.BlockL, "en manga lo leído queda a la derecha");
        Ok(novel.Hard && !comic.Hard && novel.Board > comic.Board, "tapa dura más gruesa que la blanda");

        // Poses: abierto en V; cerrado, la portada da la vuelta sobre la otra mitad.
        BookGeometry.HalfAngles(0f, 1, out float l0, out float r0);
        Ok(l0 == BookGeometry.TiltDegrees && r0 == BookGeometry.TiltDegrees, "abierto: las dos mitades en V");
        BookGeometry.HalfAngles(1f, 1, out float l1, out float r1);
        Ok(Math.Abs(l1 - 180f) < 1e-3f && Math.Abs(r1) < 1e-3f, "cerrado (normal): la izquierda gira 180°");
        BookGeometry.HalfAngles(1f, -1, out float l2, out float r2);
        Ok(Math.Abs(r2 - 180f) < 1e-3f && Math.Abs(l2) < 1e-3f, "cerrado (manga): la derecha gira 180°");
        Vector3 outerRight = BookGeometry.HalfRotation(1, 9f) * new Vector3(1, 0, 0);
        Vector3 outerLeft = BookGeometry.HalfRotation(-1, 9f) * new Vector3(-1, 0, 0);
        Ok(outerRight.z < -0.1f && outerLeft.z < -0.1f, "abierto: los bordes libres vienen hacia quien lee (-Z)");

        foreach (var shape0 in new[] { comic, novel, manga })
        {
            var s = shape0;
            s.CurlL = BookGeometry.OpenCurl(s.BlockL, s.Hard);
            s.CurlR = BookGeometry.OpenCurl(s.BlockR, s.Hard);
            string kind = s.Hard ? "dura" : "blanda";
            for (int side = -1; side <= 1; side += 2)
            {
                BookGeometry.Page(m, side < 0, 0f, 1f, 0f, s.CurlDepth(side), s.Reach, false);
                Ok(Finite(m) && AllFacing(m, v => v, new Vector3(0, 0, -1), 0.5f), "la hoja mira hacia quien lee (" + kind + ")");
                Ok(m.V.Exists(v => v.z > s.CurlDepth(side) * 0.99f) && m.V.Exists(v => Math.Abs(v.z) < 1e-6f), "la hoja se hunde junto al lomo");
                BookGeometry.Page(m, side < 0, 0f, 0.36f, -0.0004f, s.CurlDepth(side), s.Reach, true);
                Ok(AllFacing(m, v => v, new Vector3(0, 0, -1), 0.5f), "la sombra del pliegue mira hacia quien lee");
                BookGeometry.CoverArt(m, s, side, true);
                Ok(Finite(m) && AllFacing(m, v => v, new Vector3(0, 0, 1), 0.3f), "el arte mira hacia afuera (+Z de su mitad)");
                BookGeometry.Board(m, s, side);
                Ok(Finite(m) && m.T.Count > 0, "tapa armada (" + kind + ")");
                float minX = float.MaxValue, maxX = float.MinValue;
                foreach (var v in m.V) { minX = Math.Min(minX, Math.Abs(v.x)); maxX = Math.Max(maxX, Math.Abs(v.x)); }
                Ok(Math.Abs(maxX - (s.Gutter * 0.5f + s.W + s.Overhang)) < 1e-4f, "la tapa sobresale lo justo del borde");
                BookGeometry.Block(m, s, side);
                Ok(Finite(m), "bloque de hojas armado");
                float fore = BookGeometry.BlockBack(s, side, 1f) - BookGeometry.PageZ(s, side, 1f);
                Ok(Math.Abs(fore - (s.Hard ? s.HardBack(side) : s.Block(side))) < 1e-5f, "el canto mide el grosor del bloque");
                Ok(BookGeometry.BlockBack(s, side, 0f) + 1e-6f >= BookGeometry.PageZ(s, side, 0f), "la tapa queda detrás de la hoja hundida");
            }
        }

        // Libro cerrado: la portada mira a quien lee y no sale espejada; el lomo queda del lado correcto.
        foreach (bool rtl in new[] { false, true })
        {
            var s = BookGeometry.Measure(0.17f, 0.26f, 0.002f, 26, 10, rtl, false);
            int rest = rtl ? -1 : 1, front = -rest;
            BookGeometry.HalfAngles(1f, rest, out float la, out float ra);
            Quaternion rl = BookGeometry.HalfRotation(-1, la), rr = BookGeometry.HalfRotation(1, ra);
            Quaternion rf = front < 0 ? rl : rr, rb = front < 0 ? rr : rl;
            BookGeometry.CoverArt(m, s, front, !rtl);
            Ok(AllFacing(m, v => rf * v, new Vector3(0, 0, -1), 0.9f), "cerrado: la portada mira a quien lee" + (rtl ? " (manga)" : ""));
            Ok(USlope(m, v => rf * v) > 0f, "cerrado: la portada no sale espejada" + (rtl ? " (manga)" : ""));
            BookGeometry.CoverArt(m, s, -front, rtl);
            Ok(AllFacing(m, v => rb * v, new Vector3(0, 0, 1), 0.9f), "cerrado: la contraportada mira hacia atrás");
            // Desde atrás (+Z) la derecha de quien mira es -X: la u debe crecer hacia -X.
            Ok(USlope(m, v => rb * v) < 0f, "cerrado: la contraportada no sale espejada" + (rtl ? " (manga)" : ""));
            BookGeometry.SpineFor(m, s, rl, rr, out Vector3 mid, out Vector3 outward, out float thick);
            Ok(rtl ? outward.x > 0.9f : outward.x < -0.9f, "cerrado: el lomo queda del lado del pliegue" + (rtl ? " (manga)" : ""));
            Ok(Math.Abs(thick - (s.Block(-1) + s.Block(1) + 2f * s.Board)) < 0.001f, "cerrado: el lomo mide lo que el libro cerrado");
            BookGeometry.HalfAngles(0f, rest, out la, out ra);
            BookGeometry.SpineFor(m, s, BookGeometry.HalfRotation(-1, la), BookGeometry.HalfRotation(1, ra), out mid, out outward, out thick);
            Ok(outward.z > 0.9f, "abierto: el lomo queda por detrás");
        }
        PageTurn();
        Console.WriteLine(fails == 0 ? "OK: " + checks + " comprobaciones de la forma del libro (sin Unity)" : fails + " fallas de " + checks);
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
