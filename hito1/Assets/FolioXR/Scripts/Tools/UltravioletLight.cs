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

        public static bool Reveals(Vector3 point) => Reveals(point, null);

        /// <summary>
        /// Revisión de Claude: el soporte de la tinta puede ser una caja con grosor (por ejemplo, el libro),
        /// y la superficie dibujada queda dentro de ella. El rayo chocaría primero con su cara y la
        /// tinta nunca se vería. Si el primer impacto es el colisionador de soporte, cuenta como iluminado.
        /// </summary>
        public static bool Reveals(Vector3 point, Collider support)
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
            if (!Physics.Raycast(emitter.position, delta / distance, out RaycastHit hit, distance - 0.001f, ~0,
                                 QueryTriggerInteraction.Ignore))
                return true;
            return support != null && hit.collider == support;
        }
    }
}
