using System;
using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Reading
{
    /// <summary>Telemetría acotada. No guarda títulos ni rutas de libros en el CSV.</summary>
    public static class ReadingDiagnostics
    {
        public static bool Enabled => PlayerPrefs.GetInt("folioxr.diagnostics", 0) != 0;
        private static readonly List<Texture2D> Pages = new List<Texture2D>(16);
#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaClass _java;
        private static AndroidJavaClass Java => _java ?? (_java = new AndroidJavaClass("com.loklj.folioxr.FolioPerformance"));
#endif
        public static void Configure()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Java.CallStatic("configure", Application.persistentDataPath, Enabled);
#endif
        }
        public static void Toggle()
        {
            PlayerPrefs.SetInt("folioxr.diagnostics", Enabled ? 0 : 1);
            PlayerPrefs.Save(); Configure(); Flush();
        }
        public static void Record(string metric, double value)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Enabled) Java.CallStatic("record", metric, value);
#endif
        }
        public static void Track(Texture2D texture) { if (texture != null && !Pages.Contains(texture)) Pages.Add(texture); }
        public static long GraphicsBytes()
        {
            long bytes = 0;
            for (int i = Pages.Count - 1; i >= 0; i--)
                if (Pages[i] == null) Pages.RemoveAt(i);
                else bytes += (long)Pages[i].width * Pages[i].height * 4 * 4 / 3; // RGBA + mipmaps.
            return bytes;
        }
        public static void Sample(float[] frameMs, float fps)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Java.CallStatic("setGraphicsBytes", Math.Max(GraphicsBytes(), UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver()));
            if (Enabled) { Java.CallStatic("frames", frameMs); Record("fps", fps); }
#endif
        }
        public static void Flush()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Java.CallStatic("flush");
#endif
        }
    }

}
