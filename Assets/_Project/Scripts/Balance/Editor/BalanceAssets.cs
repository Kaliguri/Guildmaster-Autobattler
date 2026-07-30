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
        /// <summary>
        /// Стат-конфиг, которым играет игра, — через <see cref="GameConfig"/>.
        /// </summary>
        /// <remarks>
        /// Раньше здесь брался «первый по алфавиту ассет типа <c>StatsConfig</c>». Это тихая ловушка:
        /// второй такой ассет (эксперимент, ветка баланса) увёл бы отчёты на конфиг, которым игра не
        /// играет, и расхождение выглядело бы как цифра в отчёте, а не как ошибка. Играющий экземпляр
        /// выбран в `GameConfig`, поэтому бенч спрашивает там же, где спрашивают скоупы.
        /// </remarks>
        public static StatsConfig LoadStatsConfig() => LoadGameConfig()?.Stats;

        /// <inheritdoc cref="LoadStatsConfig"/>
        public static ClassBalanceConfig LoadClassBalanceConfig() => LoadGameConfig()?.ClassBalance;

        private static GameConfig LoadGameConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:GameConfig");
            if (guids.Length == 0)
            {
                UnityEngine.Debug.LogError("[SimBench] GameConfig не найден — брать балансные конфиги негде.");
                return null;
            }
            System.Array.Sort(guids);
            return AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
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
