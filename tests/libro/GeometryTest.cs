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
        Console.WriteLine(fails == 0 ? "OK: " + checks + " comprobaciones de la forma del libro (sin Unity)" : fails + " fallas de " + checks);
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
