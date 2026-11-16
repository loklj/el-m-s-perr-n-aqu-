using System;
using System.Collections.Generic;
using FolioXR.Books;

/// <summary>Pruebas de ShelfLayout (lugar de los libros en una fila de la estantería), sin Unity.</summary>
static class LayoutTest
{
    static int checks, fails;
    static void Ok(bool c, string what) { checks++; if (!c) { fails++; Console.WriteLine("FALLA: " + what); } }
    static ShelfLayout.Item Book(string id, float x, float width) => new ShelfLayout.Item { Id = id, X = x, Width = width };
    const float Gap = ShelfLayout.Gap, Eps = 1e-5f;

    /// <summary>¿Ordenada, sin encimar (con su aire) y dentro de la fila?</summary>
    static bool Valid(List<ShelfLayout.Item> row, float length)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (row[i].X < -Eps || row[i].End > length + Eps) return false;
            if (i > 0 && row[i].X < row[i - 1].End + Gap - Eps) return false;
        }
        return true;
    }

    static void Main()
    {
        // Lo que ya está bien no se toca.
        var row = new List<ShelfLayout.Item> { Book("a", 0.10f, 0.03f), Book("b", 0.50f, 0.04f), Book("c", 1.20f, 0.02f) };
        Ok(ShelfLayout.Resolve(row, 3f) && row[0].X == 0.10f && row[1].X == 0.50f && row[2].X == 1.20f, "libros separados: nadie se mueve");

        // Encimados: se separan lo justo.
        row = new List<ShelfLayout.Item> { Book("a", 0.10f, 0.05f), Book("b", 0.12f, 0.05f) };
        Ok(ShelfLayout.Resolve(row, 3f) && Valid(row, 3f) && row[0].Id == "a" && Math.Abs(row[0].X - 0.10f) < Eps &&
           Math.Abs(row[1].X - (0.15f + Gap)) < Eps, "encimados: el segundo se corre a la derecha");

        // Fuera de la fila: vuelven adentro.
        row = new List<ShelfLayout.Item> { Book("a", -0.3f, 0.05f), Book("b", 5f, 0.05f) };
        Ok(ShelfLayout.Resolve(row, 1f) && Valid(row, 1f) && row[0].X == 0f && Math.Abs(row[1].End - 1f) < Eps, "fuera de la fila: vuelven adentro");

        // El libro que se suelta queda donde lo dejaron y aparta a los vecinos hacia los dos lados.
        var a = Book("a", 0.40f, 0.05f); var c = Book("c", 0.46f, 0.05f);
        var dropped = Book("x", 0.42f, 0.06f);
        row = new List<ShelfLayout.Item> { a, c, dropped };
        Ok(ShelfLayout.Resolve(row, 2f, dropped) && Valid(row, 2f) && Math.Abs(dropped.X - 0.42f) < Eps, "soltado entre dos: queda donde lo dejaron");
        Ok(row[0] == a && row[1] == dropped && row[2] == c && a.End <= dropped.X - Gap + Eps && c.X >= dropped.End + Gap - Eps, "soltado entre dos: los vecinos se apartan");

        // Contra el final de una fila casi llena: se corre lo necesario y todo sigue adentro.
        row = new List<ShelfLayout.Item>();
        for (int i = 0; i < 9; i++) row.Add(Book("l" + i, i * 0.105f, 0.1f));
        dropped = Book("x", 0.95f, 0.05f);
        row.Add(dropped);
        Ok(ShelfLayout.Resolve(row, 1f, dropped) && Valid(row, 1f), "fila casi llena: entra corriendo a los demás");
        Ok(Math.Abs(row[row.Count - 1].End - 1f) < 1e-3f, "fila casi llena: llega justo hasta el final");

        // Fila llena: no entra, y se avisa.
        row = new List<ShelfLayout.Item>();
        for (int i = 0; i < 10; i++) row.Add(Book("l" + i, i * 0.1f, 0.096f));
        Ok(ShelfLayout.Resolve(row, 1f), "diez libros que caben justo");
        row.Add(Book("x", 0.5f, 0.05f));
        Ok(!ShelfLayout.Resolve(row, 1f), "el undécimo no cabe");
        Ok(Math.Abs(ShelfLayout.Needed(row) - (10 * 0.096f + 0.05f + 10 * Gap)) < Eps, "largo que hace falta");

        // Primer hueco.
        row = new List<ShelfLayout.Item>();
        Ok(ShelfLayout.FirstGap(row, 1f, 0.05f) == 0f, "fila vacía: al principio");
        Ok(ShelfLayout.FirstGap(row, 0.04f, 0.05f) < 0f, "fila más corta que el libro: no hay hueco");
        row = new List<ShelfLayout.Item> { Book("a", 0f, 0.05f), Book("b", 0.2f, 0.05f) };
        Ok(Math.Abs(ShelfLayout.FirstGap(row, 1f, 0.05f) - (0.05f + Gap)) < Eps, "hueco entre dos libros");
        Ok(Math.Abs(ShelfLayout.FirstGap(row, 1f, 0.2f) - (0.25f + Gap)) < Eps, "libro ancho: después del último");
        row = new List<ShelfLayout.Item> { Book("a", 0.1f, 0.05f) };
        Ok(ShelfLayout.FirstGap(row, 1f, 0.05f) == 0f, "hueco antes del primero");
        Ok(ShelfLayout.FirstGap(row, 0.2f, 0.09f) == 0f && ShelfLayout.FirstGap(row, 0.2f, 0.1f) < 0f, "hueco que alcanza justo, y el que no");
        var extra = Book("n", ShelfLayout.FirstGap(row, 1f, 0.05f), 0.05f);
        ShelfLayout.Insert(row, extra);
        Ok(row[0] == extra && Valid(row, 1f), "el libro nuevo entra en su hueco y la fila queda ordenada");

        // Muchos casos al azar: siempre queda válida, repetir no cambia nada, y el soltado no se mueve si hay lugar.
        var random = new Random(11);
        bool valid = true, stable = true, kept = true, fits = true, fill = true;
        for (int t = 0; t < 4000; t++)
        {
            float length = 0.6f + (float)random.NextDouble() * 2.6f;
            int n = random.Next(0, 40);
            row = new List<ShelfLayout.Item>();
            for (int i = 0; i < n; i++)
                row.Add(Book("b" + i, (float)random.NextDouble() * (length + 0.4f) - 0.2f, 0.016f + (float)random.NextDouble() * 0.044f));
            ShelfLayout.Item pin = n > 0 && random.Next(2) == 0 ? row[random.Next(n)] : null;
            float wanted = pin != null ? Math.Min(Math.Max(pin.X, 0f), length - pin.Width) : 0f;
            bool ok = ShelfLayout.Resolve(row, length, pin);
            if (ok != (ShelfLayout.Needed(row) <= length + 1e-5f)) fits = false;
            if (!ok) continue;
            if (!Valid(row, length)) valid = false;
            var before = row.ConvertAll(item => item.X);
            ShelfLayout.Resolve(row, length);
            for (int i = 0; i < row.Count; i++) if (Math.Abs(row[i].X - before[i]) > 1e-4f) stable = false;
            if (pin != null)
            {
                // Si a su izquierda y a su derecha caben los que quedaron de cada lado, no tuvo por qué moverse.
                int at = row.IndexOf(pin);
                float left = 0f, right = 0f;
                for (int i = 0; i < at; i++) left += row[i].Width + Gap;
                for (int i = at + 1; i < row.Count; i++) right += row[i].Width + Gap;
                if (left <= wanted + 1e-5f && right <= length - (wanted + pin.Width) + 1e-5f && Math.Abs(pin.X - wanted) > 1e-4f) kept = false;
            }
            // Llenar con libros nuevos: cada uno entra en un hueco de verdad.
            for (int k = 0; k < 5; k++)
            {
                float width = 0.02f + (float)random.NextDouble() * 0.04f;
                float x = ShelfLayout.FirstGap(row, length, width);
                if (x < 0f) continue;
                ShelfLayout.Insert(row, Book("n" + k, x, width));
                if (!Valid(row, length)) fill = false;
            }
        }
        Ok(fits, "al azar: cabe exactamente cuando el largo alcanza");
        Ok(valid, "al azar: fila ordenada, sin encimar y dentro");
        Ok(stable, "al azar: acomodar dos veces no mueve nada");
        Ok(kept, "al azar: el libro soltado no se mueve si hay lugar a sus lados");
        Ok(fill, "al azar: los libros nuevos entran en huecos libres");

        Console.WriteLine(fails == 0 ? "OK: " + checks + " comprobaciones del lugar de los libros en la estantería (sin Unity)" : fails + " fallas de " + checks);
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
