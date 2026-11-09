using UnityEngine;

namespace FolioXR.Tools
{
    /// <summary>Consulta de la Tanda D, también con el cono falso. No crea tinta ni trazos.</summary>
    public static class UltravioletLight
    {
        private static TripleFlashlight _source;
        private static readonly float ConeCosine = Mathf.Cos(17.5f * Mathf.Deg2Rad);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _source = null;
        }
        internal static void Register(TripleFlashlight source)
        {
            _source = source;
        }
        internal static void Unregister(TripleFlashlight source)
        {
            if (_source == source)
                _source = null;
        }

        public static bool Reveals(Vector3 point)
        {
            if (_source == null || !_source.isActiveAndEnabled || !_source.Powered ||
                _source.Mode != FlashlightMode.Ultraviolet || _source.Emitter == null)
                return false;
            Transform emitter = _source.Emitter;
            Vector3 delta = point - emitter.position;
            float distance = delta.magnitude;
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0.001f || distance >= 3f)
                return false;
            if (Vector3.Dot(emitter.forward, delta / distance) < ConeCosine)
                return false;
            // El propio soporte del punto puede ser el impacto final: tolerancia de 1 mm.
            return !Physics.Raycast(emitter.position, delta / distance, distance - 0.001f, ~0,
                                    QueryTriggerInteraction.Ignore);
        }
    }
}
