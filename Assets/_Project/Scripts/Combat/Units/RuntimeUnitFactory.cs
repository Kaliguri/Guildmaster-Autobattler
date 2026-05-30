using Guildmaster.Combat.Abilities;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Единственная точка сборки <see cref="RuntimeUnit"/> из SO-данных.
    /// Шаги сборки (вики «10» §5.2, «6» §3): дефолты из <see cref="StatsConfig"/> → моды реликвии
    /// → таланты сосуда → пассивки (<see cref="RelicData.GrantedEffects"/> с постоянной длительностью)
    /// → активки (<see cref="AbilityRuntime"/>) → ресурс (<see cref="StatType.StartResource"/>)
    /// → <c>CurrentHP = Get(MaxHP)</c>.
    /// </summary>
    /// <remarks>
    /// Пассивки применяются <b>до</b> инициализации <c>CurrentHP</c>: пассив на +MaxHP должен поднять
    /// и стартовое здоровье. Поэтому фабрике нужны <see cref="EffectSystem"/> и боевой контекст —
    /// наложение пассивки зовёт <c>OnApply</c> компонентов (вики «12» §6, шаг 9).
    /// </remarks>
    public sealed class RuntimeUnitFactory
    {
        private readonly StatsConfig   _config;
        private readonly EffectSystem  _effects;
        private readonly ICombatContext _combat;
        private int _nextId;

        public RuntimeUnitFactory(StatsConfig config, EffectSystem effects, ICombatContext combat)
        {
            _config  = config;
            _effects = effects;
            _combat  = combat;
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

            var unit = new RuntimeUnit
            {
                Id               = _nextId++,
                Team             = team,
                Stats            = stats,
                CurrentResource  = stats.Get(StatType.StartResource),
                CurrentShield    = 0f,
                Position         = spawnPosition,
                PreviousPosition = spawnPosition,
                Relic            = relic,
                Vessel           = vessel,
            };

            RegisterPassives(unit, relic);
            RegisterAbilities(unit, relic);

            // CurrentHP — после пассивок: они могли поднять MaxHP, юнит должен стартовать с полным.
            unit.CurrentHP = stats.Get(StatType.MaxHP);

            return unit;
        }

        /// <summary>Наложить пассивные эффекты реликвии (источник — сам юнит, длительность из Def, обычно −1).</summary>
        private void RegisterPassives(RuntimeUnit unit, RelicData relic)
        {
            EffectData[] passives = relic?.GrantedEffects;
            if (passives == null || _effects == null) return;

            for (int i = 0; i < passives.Length; i++)
            {
                if (passives[i] != null) _effects.Apply(unit, passives[i], unit, _combat);
            }
        }

        /// <summary>Собрать рантайм-обёртки активных способностей реликвии (кулдаун/ресурс).</summary>
        private static void RegisterAbilities(RuntimeUnit unit, RelicData relic)
        {
            AbilityData[] abilities = relic?.Abilities;
            if (abilities == null) return;

            for (int i = 0; i < abilities.Length; i++)
            {
                if (abilities[i] != null) unit.Abilities.Add(new AbilityRuntime(abilities[i]));
            }
        }
    }
}
