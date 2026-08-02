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

        /// <summary>
        /// Ассет по имени среди тех типов, из которых трейс умеет собрать бой (реликвия, энкаунтер,
        /// сценарий). Регистр имени не важен; не найдено — <c>null</c>.
        /// </summary>
        /// <remarks>
        /// Живёт здесь, а не у вызывающего, потому что потребителей у резолва двое и они не видят друг
        /// друга: командная строка (<see cref="BalanceCli.Trace"/>) и агентский тул. Копия разошлась бы
        /// молча — например, добавили тип боя в одном месте и не добавили в другом, — и разошлась бы
        /// именно там, где её никто не проверяет глазами.
        /// </remarks>
        public static UnityEngine.Object ResolveTraceAsset(string name)
        {
            foreach (RelicData r in LoadRelics())
                if (string.Equals(r.name, name, System.StringComparison.OrdinalIgnoreCase)) return r;

            foreach (EncounterData e in LoadAll<EncounterData>())
                if (string.Equals(e.name, name, System.StringComparison.OrdinalIgnoreCase)) return e;

            foreach (BalanceScenarioData s in LoadAll<BalanceScenarioData>())
                if (string.Equals(s.name, name, System.StringComparison.OrdinalIgnoreCase)) return s;

            return null;
        }
    }
}
