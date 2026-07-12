using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Эффективные статы юнита ТЕМ ЖЕ путём, что и бой (контракт «таблица не врёт»): переиспользует
    /// <see cref="Stats"/> из сим-кода, а не переписывает формулу. <c>RuntimeUnitFactory.Create</c> делает
    /// ровно то же — <c>new Stats(config)</c> + <c>AddModifiersFrom(data, data.Stats)</c> — поэтому значения
    /// совпадают по построению.
    /// <para>НЕ включает статы от пассивных <c>GrantedEffects</c> (те применяются через EffectSystem в бою) —
    /// хаб показывает SO-слой модификаторов, достаточный для сравнения баланса.</para>
    /// </summary>
    public static class StatMath
    {
        /// <summary>Собрать эффективный стат-блок реликвии/врага поверх дефолтов конфига.</summary>
        public static Stats BuildEffective(UnitData data, StatsConfig config)
        {
            var stats = new Stats(config);
            if (data != null && data.Stats != null && data.Stats.Length > 0)
                stats.AddModifiersFrom(data, data.Stats);
            return stats;
        }

        /// <summary>Атак/сек с учётом тиковой квантизации сима (через <see cref="AttackTiming"/>), не сырой AttackSpeed.</summary>
        public static float AttacksPerSecond(float attackSpeed)
        {
            int interval = AttackTiming.IntervalTicks(attackSpeed);
            if (interval <= 0 || interval == int.MaxValue) return 0f;
            return (float)SimConstants.TickRate / interval;
        }

        /// <summary>«Бумажный» DPS автоатаки без цели: урон × атак/сек × эфф. наносимого урона (броня/митигейт цели не учитываются).</summary>
        public static float AutoAttackDps(IStatReader stats)
        {
            if (stats == null) return 0f;
            float dmg = stats.Get(StatType.AutoAttackDamage);
            float aps = AttacksPerSecond(stats.Get(StatType.AttackSpeed));
            float dealt = stats.Get(StatType.DamageDealtEff);
            return dmg * aps * dealt;
        }
    }
}
