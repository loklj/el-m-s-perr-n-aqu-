using System;
using System.Collections.Generic;

namespace FolioXR.Books
{
    /// <summary>
    /// Lugar de cada libro en una fila de la estantería (pedido del usuario, 15 nov 2026: «las estanterías deben
    /// estar vacías, para que los usuarios vayan dejando los libros como quieran»). Solo cuentas, sin Unity,
    /// para probarlas aparte (tests/estanteria). Cada libro ocupa de X a X + Width a lo largo de la fila, en
    /// metros desde su extremo izquierdo.
    /// </summary>
    internal static class ShelfLayout
    {
        /// <summary>Aire entre un libro y el siguiente.</summary>
        public const float Gap = 0.004f;

        public sealed class Item
        {
            public string Id;
            public float X, Width;
            public float End => X + Width;
        }

        /// <summary>Largo que ocupan esos libros bien juntos.</summary>
        public static float Needed(IReadOnlyList<Item> items)
        {
            float total = 0f;
            foreach (Item item in items) total += item.Width;
            return total + Gap * Math.Max(0, items.Count - 1);
        }

        /// <summary>
        /// Deja los libros de una fila dentro de ella y sin encimarse, moviéndolos lo menos posible; la lista
        /// queda ordenada de izquierda a derecha. pinned (el que se acaba de soltar) se queda donde lo dejaron
        /// y aparta a los vecinos; solo se corre si la fila no da para otra cosa. false: no caben.
        /// </summary>
        public static bool Resolve(List<Item> items, float length, Item pinned = null)
        {
            int n = items.Count;
            if (n == 0) return true;
            if (Needed(items) > length + 1e-5f) return false;
            items.Sort((a, b) =>
            {
                int byCenter = (a.X + a.Width * 0.5f).CompareTo(b.X + b.Width * 0.5f);
                return byCenter != 0 ? byCenter : string.CompareOrdinal(a.Id, b.Id);
            });
            foreach (Item item in items) item.X = Math.Min(Math.Max(item.X, 0f), Math.Max(0f, length - item.Width));
            int at = pinned != null ? items.IndexOf(pinned) : -1;
            if (at >= 0)
            {
                for (int i = at + 1; i < n; i++) items[i].X = Math.Max(items[i].X, items[i - 1].End + Gap);
                for (int i = at - 1; i >= 0; i--) items[i].X = Math.Min(items[i].X, items[i + 1].X - Gap - items[i].Width);
            }
            // Hacia la derecha desde el inicio de la fila y, si se pasan del final, de vuelta hacia la izquierda.
            float next = 0f;
            foreach (Item item in items)
            {
                item.X = Math.Max(item.X, next);
                next = item.End + Gap;
            }
            float end = length;
            for (int i = n - 1; i >= 0; i--)
            {
                items[i].X = Math.Max(0f, Math.Min(items[i].X, end - items[i].Width));
                end = items[i].X - Gap;
            }
            return true;
        }

        /// <summary>
        /// Primer hueco de la fila (ya ordenada y sin encimar) donde cabe un libro de ese ancho: X de su borde
        /// izquierdo, o un número negativo si no hay.
        /// </summary>
        public static float FirstGap(IReadOnlyList<Item> sorted, float length, float width)
        {
            float x = 0f;
            foreach (Item item in sorted)
            {
                if (item.X - Gap - x >= width - 1e-6f) return x;
                x = Math.Max(x, item.End + Gap);
            }
            return length - x >= width - 1e-6f ? x : -1f;
        }

        /// <summary>Pone un libro en la lista conservando el orden de izquierda a derecha.</summary>
        public static void Insert(List<Item> sorted, Item item)
        {
            int i = 0;
            while (i < sorted.Count && sorted[i].X <= item.X) i++;
            sorted.Insert(i, item);
        }
    }
}
