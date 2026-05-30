using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Единственная точка сборки <see cref="RuntimeUnit"/> из SO-данных.
    /// Шаги сборки: дефолты из <see cref="StatsConfig"/> → моды реликвии
    /// → (таланты сосуда — Фаза 2) → инициализация CurrentHP = Get(MaxHP)
    /// (вики «10» §5.2, «6» §3).
    /// </summary>
    public sealed class RuntimeUnitFactory
    {
        private readonly StatsConfig _config;
        private int _nextId;

        public RuntimeUnitFactory(StatsConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Создать <see cref="RuntimeUnit"/> из SO-данных.
        /// </summary>
        /// <param name="relic">SO «Чемпион». null — юнит получит только дефолты StatsConfig.</param>
        /// <param name="vessel">SO «Пилот». null — таланты не применяются.</param>
        /// <param name="team">Команда: 0 = союзники, 1 = враги.</param>
        /// <param name="spawnPosition">Начальная позиция на поле боя.</param>
        public RuntimeUnit Create(RelicData relic, VesselData vessel, int team, Vector2 spawnPosition)
        {
            var stats = new Stats(_config);

            if (relic?.Stats != null && relic.Stats.Length > 0)
                stats.AddModifiersFrom(relic, relic.Stats);

            if (vessel?.TalentModifiers != null && vessel.TalentModifiers.Length > 0)
                stats.AddModifiersFrom(vessel, vessel.TalentModifiers);

            return new RuntimeUnit
            {
                Id               = _nextId++,
                Team             = team,
                Stats            = stats,
                CurrentHP        = stats.Get(StatType.MaxHP),
                CurrentResource  = 0f,
                CurrentShield    = 0f,
                Position         = spawnPosition,
                PreviousPosition = spawnPosition,
                Relic            = relic,
                Vessel           = vessel,
            };
        }
    }
}
