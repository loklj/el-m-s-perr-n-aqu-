using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FolioXR.EditorTools
{
    /// <summary>
    /// En Android no se puede listar StreamingAssets, así que antes de cada compilación se escribe
    /// Licencias/indice.txt con los archivos de licencia que haya; lo lee «Licencias de terceros».
    /// </summary>
    public sealed class FolioXRLicenseIndex : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Write();

        [MenuItem("Folio XR/Actualizar índice de licencias")]
        public static void Write()
        {
            string folder = Path.Combine(Application.dataPath, "StreamingAssets", "Licencias");
            if (!Directory.Exists(folder)) return;
            string[] files = Directory.GetFiles(folder, "*.txt")
                .Select(Path.GetFileName)
                .Where(name => name != "indice.txt")
                .OrderBy(name => name, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();
            File.WriteAllText(Path.Combine(folder, "indice.txt"),
                "# Generado por FolioXRLicenseIndex al compilar. Un archivo por línea.\n" + string.Join("\n", files) + "\n");
            Debug.Log("[FolioXR] Índice de licencias: " + files.Length + " archivos.");
        }
    }
}
