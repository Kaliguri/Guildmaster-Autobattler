using UnityEditor;
using UnityEditor.Presets;

namespace Guildmaster.PaletteRemap
{
    /// <summary>
    /// Applies <c>BonePartSprite.preset</c> to textures under
    /// <c>Assets/_Project/Art/Sprites/Bone Animations/</c> on first import.
    /// Re-exports that overwrite PNG keep existing .meta (PPU/pivot/etc.).
    /// </summary>
    internal sealed class BonePartSpritePostprocessor : AssetPostprocessor
    {
        private const string FolderPrefix = "Assets/_Project/Art/Sprites/Bone Animations/";
        private const string PresetPath = "Assets/_Project/Art/Sprites/Bone Animations/BonePartSprite.preset";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(FolderPrefix, System.StringComparison.OrdinalIgnoreCase))
                return;
            if (!assetImporter.importSettingsMissing)
                return;

            var preset = AssetDatabase.LoadAssetAtPath<Preset>(PresetPath);
            if (preset == null)
                return;

            preset.ApplyTo(assetImporter);
        }
    }
}
