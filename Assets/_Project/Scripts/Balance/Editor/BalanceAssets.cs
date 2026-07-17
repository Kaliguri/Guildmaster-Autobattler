using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEditor;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Загрузка контент-ассетов для бенчей — напрямую через <see cref="AssetDatabase"/> (editor-only тул,
    /// рантайм-<c>IContentDatabase</c>/DI не поднимаем). Порядок стабилен (по пути) для воспроизводимых отчётов.
    /// </summary>
    internal static class BalanceAssets
    {
        public static StatsConfig LoadStatsConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:StatsConfig");
            if (guids.Length == 0) return null;
            System.Array.Sort(guids);
            return AssetDatabase.LoadAssetAtPath<StatsConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        public static List<T> LoadAll<T>() where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            var paths = new List<string>(guids.Length);
            for (int i = 0; i < guids.Length; i++) paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            paths.Sort(System.StringComparer.Ordinal);

            var result = new List<T>(paths.Count);
            for (int i = 0; i < paths.Count; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(paths[i]);
                if (asset != null) result.Add(asset);
            }
            return result;
        }

        public static List<RelicData> LoadRelics() => LoadAll<RelicData>();
        public static List<EnemyData> LoadEnemies() => LoadAll<EnemyData>();
    }
}
