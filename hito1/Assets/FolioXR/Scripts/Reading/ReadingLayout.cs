using System;
using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Reading
{
    /// <summary>Preferencias comunes a las fuentes HTML; independientes de Día/Sepia/Noche.</summary>
    public static class ReadingLayout
    {
        public const string FontKey = "folioxr.readingFont", LineKey = "folioxr.lineSpacing", MarginKey = "folioxr.pageMargin";
        public static int Font => Mathf.Clamp(PlayerPrefs.GetInt(FontKey, 0), 0, 1);
        public static string FontName => Font == 0 ? "Gentium" : "Atkinson";
        public static float LineSpacing { get { float v = PlayerPrefs.GetFloat(LineKey, 1.4f); return v == 1.2f || v == 1.8f ? v : 1.4f; } }
        public static int Margin { get { int v = PlayerPrefs.GetInt(MarginKey, 48); return v == 24 || v == 72 ? v : 48; } }
    }

    [Serializable]
    public sealed class ContentsEntry
    {
        public string title;
        public int page, depth;
    }
    [Serializable]
    public sealed class ContentsList
    {
        public string message;
        public List<ContentsEntry> entries = new List<ContentsEntry>();
    }
}
