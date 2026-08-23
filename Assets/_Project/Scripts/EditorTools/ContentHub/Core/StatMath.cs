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
        /// <summary>Собрать эффективный стат-блок мементо/врага поверх дефолтов конфига и классовой базы.</summary>
        public static Stats BuildEffective(UnitData data, StatsConfig config, ClassBalanceConfig classConfig = null)
            => EffectiveStats.Build(data, config, classConfig);

        /// <summary>«Бумажный» DPS автоатаки без цели: урон × атак/сек × эфф. наносимого урона (броня/митигейт цели не учитываются).</summary>
        public static float AutoAttackDps(IStatReader stats)
        {
            if (stats == null) return 0f;
            float dmg = stats.Get(StatType.AutoAttackDamage);
            float aps = AttackTiming.AttacksPerSecond(stats.Get(StatType.AttackSpeed));
            float dealt = stats.Get(StatType.DamageDealtEff);
            return dmg * aps * dealt;
        }
    }
}
