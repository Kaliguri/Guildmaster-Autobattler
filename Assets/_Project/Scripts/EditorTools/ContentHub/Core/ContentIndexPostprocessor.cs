using UnityEditor;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Инвалидирует <see cref="ContentIndex"/> при изменении .asset-ов (импорт/удаление/перемещение).
    /// Пересборка ленива (на следующем доступе), поэтому это дёшево.
    /// </summary>
    internal sealed class ContentIndexPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (Touches(imported) || Touches(deleted) || Touches(moved))
                ContentIndex.Invalidate();
        }

        private static bool Touches(string[] paths)
        {
            foreach (var p in paths)
                if (p.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
