using System.Collections.Generic;
using Guildmaster.Combat.Abilities;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Единственная точка сборки <see cref="RuntimeUnit"/> из SO-данных.
    /// Шаги сборки (вики «10» §5.2, «6» §3): дефолты из <see cref="StatsConfig"/> → классовая база
    /// (<see cref="ClassBalanceConfig"/>) → видовые скейлы врага (<see cref="SpeciesData"/>) → моды
    /// реликвии → перки сосуда → пассивки
    /// (<see cref="RelicData.GrantedEffects"/> с постоянной длительностью)
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
        private readonly ClassBalanceConfig _classBalance;
        private readonly EffectSystem  _effects;
        private readonly ICombatContext _combat;
        private int _nextId;

        public RuntimeUnitFactory(StatsConfig config, ClassBalanceConfig classBalance,
                                  EffectSystem effects, ICombatContext combat)
        {
            _config       = config;
            _classBalance = classBalance;
            _effects      = effects;
            _combat       = combat;
        }

        /// <summary>
        /// Сбросить счётчик Id (перезапуск боя на месте, dev-R). Без этого счётчик фабрики уезжает от
        /// схемы Id болванчиков (<c>Units.Count+1</c> в харнессе) → коллизия Id → в презентере два юнита
        /// на один ключ, вид одного осиротевает (стоит на месте, HP-бар не реагирует). Раньше это лечил
        /// релоад сцены (новый скоуп → новая фабрика); теперь чистим явно.
        /// </summary>
        public void ResetIds() => _nextId = 0;

        /// <summary>
        /// Создать <see cref="RuntimeUnit"/> из SO-данных. Принимает базовый <see cref="UnitData"/> —
        /// реликвию или врага; сборке всё равно, кто перед ней (вики «13» §3.1).
        /// </summary>
        /// <param name="data">Боевой кит («Чемпион»/враг). null — юнит получит только дефолты StatsConfig.</param>
        /// <param name="vessel">SO «Пилот». null — перки не применяются.</param>
        /// <param name="team">Команда: 0 = союзники, 1 = враги.</param>
        /// <param name="spawnPosition">Начальная позиция на поле боя.</param>
        /// <param name="items">
        /// Предметы юнита (Vessel-скоуп) и баннеры команды (Party-скоуп) — их статовые моды и пассивки
        /// (план 11 §5.5, D1). Применяются как перки: моды до инициализации HP (чтобы +MaxHP поднял старт).
        /// null = без предметов. Активки предметов пока не реализованы (только статы/эффекты).
        /// </param>
        public RuntimeUnit Create(UnitData data, VesselData vessel, int team, Vector2 spawnPosition,
                                  IReadOnlyList<ItemData> items = null)
        {
            var stats = new Stats(_config);

            // Классовая база (2-й уровень каскада) — ПЕРВОЙ группой, до персоны: «последний Override
            // побеждает» даёт каскад Класс → Персона → Vessel, дельты персоны копятся поверх.
            ClassBaseline.Apply(stats, data, _classBalance);

            // Видовые/подвидовые скейлы врага (уровни 3–4) — после класса, до персоны (перемножаются поверх базы).
            EnemyScalers.Apply(stats, data);

            if (data?.Stats != null && data.Stats.Length > 0)
                stats.AddModifiersFrom(data, data.Stats);

            if (vessel?.PerkModifiers != null && vessel.PerkModifiers.Length > 0)
                stats.AddModifiersFrom(vessel, vessel.PerkModifiers);

            // Предметы/баннеры: статовые моды (источник — сам предмет) до HP-init, наравне с перками сосуда.
            if (items != null)
                for (int i = 0; i < items.Count; i++)
                {
                    ItemData item = items[i];
                    if (item?.Mods != null && item.Mods.Length > 0)
                        stats.AddModifiersFrom(item, item.Mods);
                }

            int id = _nextId++;
            var unit = new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentResource  = stats.Get(StatType.StartResource),
                CurrentShield    = 0f,
                Position         = spawnPosition,
                PreviousPosition = spawnPosition,
                Unit             = data,
                Vessel           = vessel,
                // AI (Фаза 3): мозг из профиля кита + фаза стаггера по Id (вики «13» §2.7, §4.1).
                Brain            = new ProfileBrain(data?.Ai),
                BrainPhase       = id % SimConstants.AiTickInterval,
            };

            RegisterPassives(unit, data);
            RegisterItemPassives(unit, items);
            RegisterAbilities(unit, data);

            // CurrentHP — после пассивок: они могли поднять MaxHP, юнит должен стартовать с полным.
            unit.CurrentHP = stats.Get(StatType.MaxHP);

            return unit;
        }

        /// <summary>Наложить пассивные эффекты предметов/баннеров (источник — сам юнит, длительность из Def).</summary>
        private void RegisterItemPassives(RuntimeUnit unit, IReadOnlyList<ItemData> items)
        {
            if (items == null || _effects == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                EffectData[] granted = items[i]?.GrantedEffects;
                if (granted == null) continue;
                for (int j = 0; j < granted.Length; j++)
                    if (granted[j] != null) _effects.Apply(unit, granted[j], unit, _combat);
            }
        }

        /// <summary>Наложить пассивные эффекты кита (источник — сам юнит, длительность из Def, обычно −1).</summary>
        private void RegisterPassives(RuntimeUnit unit, UnitData data)
        {
            EffectData[] passives = data?.GrantedEffects;
            if (passives == null || _effects == null) return;

            for (int i = 0; i < passives.Length; i++)
            {
                if (passives[i] != null) _effects.Apply(unit, passives[i], unit, _combat);
            }
        }

        /// <summary>Собрать рантайм-обёртки активных способностей кита (кулдаун/ресурс).</summary>
        private static void RegisterAbilities(RuntimeUnit unit, UnitData data)
        {
            AbilityData[] abilities = data?.Abilities;
            if (abilities == null) return;

            for (int i = 0; i < abilities.Length; i++)
            {
                if (abilities[i] != null) unit.Abilities.Add(new AbilityRuntime(abilities[i]));
            }
        }
    }
}
