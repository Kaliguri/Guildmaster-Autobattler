using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>Статика энкаунтера: во что он оценён автором и сколько в нём мяса.</summary>
    internal readonly struct EncounterFacts
    {
        public readonly int Enemies;
        public readonly int Threat;
        public readonly double EnemyHp;

        public EncounterFacts(int enemies, int threat, double enemyHp)
        {
            Enemies = enemies;
            Threat = threat;
            EnemyHp = enemyHp;
        }
    }

    /// <summary>
    /// Сборка вражеской стороны из <see cref="EncounterData"/> для стенда: разрешение <c>enemy.*</c> id по
    /// ассетам, проверка полноты состава и сам спавн. Живёт отдельно от бенчей, потому что энкаунтер
    /// разворачивают уже двое — PvE-бенч и трейс одного боя.
    /// </summary>
    /// <remarks>
    /// В бою тем же занимается <c>EncounterLoader</c> через рантайм-<c>IContentDatabase</c>; здесь
    /// editor-путь по <c>AssetDatabase</c> (как весь остальной стенд), но расстановку копий обе стороны
    /// спрашивают у одного владельца — <see cref="EncounterUnit.PositionOf"/>.
    /// </remarks>
    internal static class EncounterSetup
    {
        /// <summary>Все враги проекта по их content id.</summary>
        public static Dictionary<string, EnemyData> IndexEnemies()
        {
            List<EnemyData> all = BalanceAssets.LoadEnemies();
            var byId = new Dictionary<string, EnemyData>(all.Count);
            for (int i = 0; i < all.Count; i++)
                if (!string.IsNullOrEmpty(all[i].Id)) byId[all[i].Id] = all[i];
            return byId;
        }

        /// <summary>
        /// Мерить можно только ПОЛНЫЙ состав: если хоть один id не разрешается в ассет, энкаунтер
        /// пропускается целиком. Иначе стенд тихо мерил бы облегчённый бой и рапортовал о нём как о
        /// задуманном.
        /// </summary>
        public static bool IsPlayable(EncounterData encounter, Dictionary<string, EnemyData> enemiesById)
        {
            IReadOnlyList<EncounterUnit> units = encounter.Units;
            if (units == null || units.Count == 0)
            {
                Debug.LogWarning($"[SimBench] энкаунтер «{encounter.name}»: пустой состав — пропущен.");
                return false;
            }

            for (int i = 0; i < units.Count; i++)
            {
                string id = units[i].EnemyId;
                if (string.IsNullOrEmpty(id) || !enemiesById.ContainsKey(id))
                {
                    Debug.LogWarning($"[SimBench] энкаунтер «{encounter.name}»: враг «{id}» не найден в " +
                                     "ассетах — энкаунтер пропущен целиком (неполный состав мерить нельзя).");
                    return false;
                }
            }

            return true;
        }

        /// <summary>Развернуть вражескую сторону (team 1) по авторенным якорям и вернуть её статику.</summary>
        public static EncounterFacts SpawnEnemies(SimEnvironment env, List<TrackedUnit> tracked,
            EncounterData encounter, Dictionary<string, EnemyData> enemiesById)
        {
            int count = 0, threat = 0;
            double hp = 0.0;

            IReadOnlyList<EncounterUnit> units = encounter.Units;
            for (int i = 0; units != null && i < units.Count; i++)
            {
                EncounterUnit u = units[i];
                if (!enemiesById.TryGetValue(u.EnemyId ?? string.Empty, out EnemyData enemy)) continue;

                for (int c = 0; c < u.Count; c++)
                {
                    var unit = env.Real(enemy, null, 1, u.PositionOf(c));
                    tracked.Add(new TrackedUnit(unit, enemy.name + "#" + c, "enemy"));
                    hp += unit.Stats.Get(StatType.MaxHP);
                    count++;
                    threat += enemy.ThreatPoints;
                }
            }

            return new EncounterFacts(count, threat, hp);
        }
    }
}
