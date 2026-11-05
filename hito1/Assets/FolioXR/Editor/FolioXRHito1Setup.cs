// Folio XR - Hito 1
// Menu del Editor: "Folio XR > Crear libro de prueba (Hito 1)" y
// "Folio XR > Comprobar Hito 1". Crea el libro con sus materiales, fija el
// identificador del paquete Android y revisa que esten las piezas del hito.
//
// No depende del SDK de Meta: compila aunque el SDK aun no este instalado.

using System.IO;
using System.Text;
using FolioXR.Reading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FolioXR.EditorTools
{
    public static class FolioXRHito1Setup
    {
        private const string MaterialsFolder = "Assets/FolioXR/Materials";
        private const string PageMaterialPath = MaterialsFolder + "/FolioPagina.mat";
        private const string CoverMaterialPath = MaterialsFolder + "/FolioTapa.mat";
        private const string AndroidPackageId = "com.loklj.folioxr";
        private const string ProductName = "Folio XR";
        private const string JavaPluginPath = "Assets/Plugins/Android/FolioPdfRenderer.java";

        [MenuItem("Folio XR/Crear libro de prueba (Hito 1)", priority = 1)]
        public static void CreateTestBook()
        {
            Material pageMaterial = GetOrCreateMaterial(
                PageMaterialPath,
                new[] { "Universal Render Pipeline/Unlit", "Unlit/Texture" },
                Color.white);
            Material coverMaterial = GetOrCreateMaterial(
                CoverMaterialPath,
                new[] { "Universal Render Pipeline/Lit", "Standard" },
                new Color(0.42f, 0.13f, 0.10f));

            if (pageMaterial == null || coverMaterial == null)
            {
                EditorUtility.DisplayDialog(ProductName,
                    "No se encontraron shaders para los materiales. Revisa que el proyecto use la plantilla Universal 3D.",
                    "OK");
                return;
            }

            var root = new GameObject("Libro de prueba (Folio XR)");
            Undo.RegisterCreatedObjectUndo(root, "Crear libro de prueba");
            root.transform.SetPositionAndRotation(new Vector3(0f, 1.1f, 0.5f), Quaternion.Euler(15f, 0f, 0f));

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;   // flota donde lo dejes; no cae
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var collider = root.AddComponent<BoxCollider>();

            GameObject cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cover.name = "Tapa";
            Object.DestroyImmediate(cover.GetComponent<Collider>());
            cover.transform.SetParent(root.transform, false);
            cover.GetComponent<Renderer>().sharedMaterial = coverMaterial;

            Renderer left = CreatePage("Página izquierda", root.transform, pageMaterial);
            Renderer right = CreatePage("Página derecha", root.transform, pageMaterial);

            var statusObject = new GameObject("Estado");
            statusObject.transform.SetParent(root.transform, false);
            var status = statusObject.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            status.font = font;
            status.fontSize = 64;
            status.characterSize = 0.002f;
            status.anchor = TextAnchor.UpperCenter;
            status.alignment = TextAlignment.Center;
            status.color = new Color(0.92f, 0.92f, 0.92f);
            status.text = "Folio XR - Hito 1";
            if (font != null)
            {
                statusObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            var book = root.AddComponent<FolioBook>();
            book.leftPage = left;
            book.rightPage = right;
            book.cover = cover.transform;
            book.bookCollider = collider;
            book.statusText = status;

            // Ajusta tamaños y posiciones iniciales (lo mismo hace FolioBook al cargar).
            book.ApplyLayout();

            ApplyPlayerSettings();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[FolioXR] Libro de prueba creado. Siguiente paso: clic derecho sobre él > " +
                      "Interaction SDK > Add Grab Interaction (ver docs/HITO1_GUIA.md).");
        }

        [MenuItem("Folio XR/Comprobar Hito 1", priority = 2)]
        public static void CheckMilestone()
        {
            var report = new StringBuilder();
            bool allOk = true;

            Check(report, ref allOk, EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android,
                "Plataforma activa: Android / Meta Quest.",
                "Cambia la plataforma a Meta Quest o Android en File > Build Profiles.");

            Check(report, ref allOk, File.Exists(Path.Combine(Application.streamingAssetsPath, FolioBook.BundledPdfName)),
                "PDF de prueba en Assets/StreamingAssets.",
                "No está Assets/StreamingAssets/" + FolioBook.BundledPdfName + ". Copia la carpeta hito1/Assets de nuevo.");

            Check(report, ref allOk, File.Exists(JavaPluginPath),
                "Plugin Java del PDF presente.",
                "No está " + JavaPluginPath + ". Copia la carpeta hito1/Assets de nuevo.");

            Check(report, ref allOk, Object.FindFirstObjectByType<FolioBook>() != null,
                "Hay un libro de prueba en la escena.",
                "Usa Folio XR > Crear libro de prueba (Hito 1).");

            Check(report, ref allOk, Camera.main != null,
                "Hay una cámara principal (la del Camera Rig de Meta).",
                "No hay cámara con la etiqueta MainCamera. Agrega el bloque Camera Rig de Meta.");

            string packageId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            Check(report, ref allOk, packageId == AndroidPackageId,
                "Identificador del paquete: " + packageId,
                "El identificador del paquete es " + packageId + "; usa Crear libro de prueba para fijarlo en " + AndroidPackageId + ".");

            string message = (allOk ? "Todo listo para compilar.\n\n" : "Faltan pasos:\n\n") + report;
            Debug.Log("[FolioXR] Comprobación del Hito 1:\n" + report);
            EditorUtility.DisplayDialog(ProductName + " - Hito 1", message, "OK");
        }

        private static void Check(StringBuilder report, ref bool allOk, bool ok, string okText, string problemText)
        {
            report.AppendLine((ok ? "OK    " : "FALTA ") + (ok ? okText : problemText));
            if (!ok) allOk = false;
        }

        private static Renderer CreatePage(string name, Transform parent, Material material)
        {
            GameObject page = GameObject.CreatePrimitive(PrimitiveType.Quad);
            page.name = name;
            Object.DestroyImmediate(page.GetComponent<Collider>());
            page.transform.SetParent(parent, false);
            var renderer = page.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static Material GetOrCreateMaterial(string path, string[] shaderNames, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = null;
            foreach (string shaderName in shaderNames)
            {
                shader = Shader.Find(shaderName);
                if (shader != null) break;
            }
            if (shader == null) return null;

            if (!AssetDatabase.IsValidFolder("Assets/FolioXR")) AssetDatabase.CreateFolder("Assets", "FolioXR");
            if (!AssetDatabase.IsValidFolder(MaterialsFolder)) AssetDatabase.CreateFolder("Assets/FolioXR", "Materials");

            var material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void ApplyPlayerSettings()
        {
            string current = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (current != AndroidPackageId)
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidPackageId);
                Debug.Log("[FolioXR] Identificador del paquete Android: " + current + " -> " + AndroidPackageId);
            }
            if (PlayerSettings.productName != ProductName)
            {
                PlayerSettings.productName = ProductName;
            }
        }
    }
}
