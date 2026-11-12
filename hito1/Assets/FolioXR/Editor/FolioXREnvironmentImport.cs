using UnityEditor;
using UnityEngine;

namespace FolioXR.EditorTools
{
    /// <summary>
    /// Ajustes de importación de las texturas de entornos horneados (Resources/Entornos): tamaño completo
    /// (Unity las reduce a 2048 por defecto), ASTC en Android y mipmaps. Sin esto se verían borrosas.
    /// </summary>
    public sealed class FolioXREnvironmentImport : AssetPostprocessor
    {
        public override uint GetVersion() { return 2; }

        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Resources/Entornos/")) return;
            var importer = (TextureImporter)assetImporter;
            bool sky = path.EndsWith("cielo.jpg");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapModeU = sky ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = sky ? 2 : 8;
            importer.maxTextureSize = 8192;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 8192;
            // El terreno y el follaje aguantan más compresión que la casa y el cielo.
            android.format = path.EndsWith("cerca_luz.jpg") ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
        }
    }
}
