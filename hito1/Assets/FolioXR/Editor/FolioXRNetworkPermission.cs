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
        }
    }
}
