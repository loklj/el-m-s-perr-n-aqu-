using System;

namespace FolioXR.Reading
{
    /// <summary>Páginas reales de una apertura. -1 representa papel vacío, nunca una página del archivo.</summary>
    public readonly struct BookSpread
    {
        public readonly int First, Second, Left, Right;
        public BookSpread(int page, int count, bool singleCover, bool rtl)
        {
            if (count <= 0) { First = Second = Left = Right = -1; return; }
            page = Math.Max(0, Math.Min(page, count - 1));
            First = singleCover && page > 0 ? page - (page - 1) % 2 : page - page % 2;
            bool cover = singleCover && First == 0;
            Second = !cover && First < count - 1 ? First + 1 : -1;
            int left = cover ? -1 : First, right = cover ? First : Second;
            Left = rtl ? right : left;
            Right = rtl ? left : right;
        }
        public int Next(int count) => First < 0 ? -1 : Second >= 0
            ? (Second < count - 1 ? Second + 1 : First)
            : (First < count - 1 ? First + 1 : First);
        public int Previous => First < 0 ? -1 : Math.Max(0, First - 2);
    }
}
