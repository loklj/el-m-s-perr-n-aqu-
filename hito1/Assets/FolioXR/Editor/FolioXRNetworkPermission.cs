using UnityEditor;

namespace FolioXR.EditorTools
{
    /// <summary>
    /// La carga desde el PC por Wi-Fi (encargo 12) abre un servidor local en Java: el APK necesita el
    /// permiso INTERNET aunque Unity no detecte uso de red en los scripts. Se fija solo al cargar el Editor.
    /// </summary>
    [InitializeOnLoad]
    internal static class FolioXRNetworkPermission
    {
        static FolioXRNetworkPermission()
        {
            if (!PlayerSettings.Android.forceInternetPermission)
                PlayerSettings.Android.forceInternetPermission = true;
            // Catálogos OPDS caseros (Calibre-Web, Kavita, Komga) suelen usar http:// en la red local:
            // sin esto Android bloquea el tráfico sin cifrar (usesCleartextTraffic).
            if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            EnableSystemKeyboard();
        }

        /// <summary>
        /// Teclado del sistema para nombrar libros (TouchScreenKeyboard): en Quest exige la opción
        /// «Requires System Keyboard» del OVRProjectConfig. Por reflexión, para no romper la compilación
        /// si una versión del SDK cambia el nombre del campo.
        /// </summary>
        private static void EnableSystemKeyboard()
        {
            try
            {
                System.Type type = System.Type.GetType("OVRProjectConfig, Oculus.VR.Editor") ??
                                   System.Type.GetType("OVRProjectConfig, Oculus.VR");
                if (type == null) return;
                object config = type.GetProperty("CachedProjectConfig")?.GetValue(null);
                System.Reflection.FieldInfo field = type.GetField("requiresSystemKeyboard");
                if (config == null || field == null || (bool)field.GetValue(config)) return;
                field.SetValue(config, true);
                type.GetMethod("CommitProjectConfig")?.Invoke(null, new[] { config });
                UnityEngine.Debug.Log("[FolioXR] Activado «Requires System Keyboard» para nombrar libros.");
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning("[FolioXR] No se pudo activar el teclado del sistema: " + e.Message);
            }
        }
    }
}
