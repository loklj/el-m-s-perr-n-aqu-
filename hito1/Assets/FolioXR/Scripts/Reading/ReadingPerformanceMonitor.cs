using System;
using UnityEngine;

namespace FolioXR.Reading
{
    /// <summary>Sin reservas por cuadro: muestras preasignadas y envío cada dos segundos.</summary>
    public sealed class ReadingPerformanceMonitor : MonoBehaviour
    {
        private readonly float[] _frames = new float[256];
        private int _count;
        private float _seconds;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<ReadingPerformanceMonitor>() != null) return;
            var host = new GameObject("FolioXR rendimiento");
            DontDestroyOnLoad(host); host.AddComponent<ReadingPerformanceMonitor>();
        }
        private void Awake() { ReadingDiagnostics.Configure(); }
        private void Update()
        {
            float delta = Time.unscaledDeltaTime;
            if (delta <= 0) return;
            _frames[_count++] = delta * 1000f; _seconds += delta;
            if (_seconds < 2f && _count < _frames.Length) return;
            // Una reserva cada 2 s, únicamente al exportar diagnóstico.
            float[] frames = Array.Empty<float>();
            if (ReadingDiagnostics.Enabled) { frames = new float[_count]; Array.Copy(_frames, frames, _count); }
            ReadingDiagnostics.Sample(frames, _count / _seconds);
            _count = 0; _seconds = 0;
        }
        private void OnApplicationPause(bool paused) { if (paused) ReadingDiagnostics.Flush(); }
        private void OnApplicationQuit() { ReadingDiagnostics.Flush(); }
    }
}
