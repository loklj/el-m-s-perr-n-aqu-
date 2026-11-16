using System.IO;
using UnityEditor.Android;

namespace FolioXR.EditorTools
{
    /// <summary>
    /// «Download solo» (16 nov 2026): para importar sin preguntar los libros que la persona baja con el
    /// navegador del visor, el APK declara el permiso de acceso a todos los archivos. La persona lo concede
    /// (o no) en los ajustes del visor; sin él, la app sigue funcionando con el selector de archivos.
    /// Se agrega al manifiesto que Unity genera en cada compilación.
    /// </summary>
    internal sealed class FolioXRStoragePermission : IPostGenerateGradleAndroidProject
    {
        private const string Permission = "android.permission.MANAGE_EXTERNAL_STORAGE";

        public int callbackOrder => 90;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            try
            {
                string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
                if (!File.Exists(manifest)) { UnityEngine.Debug.LogWarning("[FolioXR] Sin manifiesto en " + manifest); return; }
                string text = File.ReadAllText(manifest);
                if (text.Contains(Permission)) return;
                int end = text.LastIndexOf("</manifest>", System.StringComparison.Ordinal);
                if (end < 0) return;
                text = text.Insert(end, "  <uses-permission android:name=\"" + Permission + "\" />\n");
                File.WriteAllText(manifest, text);
                UnityEngine.Debug.Log("[FolioXR] Permiso de archivos agregado al manifiesto (Download solo).");
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning("[FolioXR] No se pudo agregar el permiso de archivos: " + e.Message);
            }
        }
    }
}
