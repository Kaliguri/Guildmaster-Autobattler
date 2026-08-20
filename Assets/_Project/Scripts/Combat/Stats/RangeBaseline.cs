using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Применение ступени дальности авто-атаки — ЕДИНСТВЕННОЕ место, откуда
    /// <see cref="StatType.AttackRange"/> попадает в сборку юнита.
    /// </summary>
    /// <remarks>
    /// Стоит рядом с <see cref="ClassBaseline"/> и по той же причине: у числа один владелец, и бой с
    /// витриной обязаны показывать одно. Разница в том, что дистанцию задаёт не класс, а сам юнит —
    /// танк бывает и с копьём, и с кулаками, поэтому ступень живёт в <see cref="UnitData"/>.
    /// <para>Кладётся ДО стат-блока персоны, как и классовая база. Свой <c>AttackRange</c> в стат-блоке
    /// перекрыл бы ступень и вернул бы ровно ту болезнь, ради которой ступени заводились, — поэтому
    /// такой авторинг запрещён тестом, а не соглашением.</para>
    /// </remarks>
    public static class RangeBaseline
    {
        /// <summary>
        /// Добавить дистанцию ступени отдельной группой. Нет данных или конфига — не трогаем ничего:
        /// юнит останется на дефолте <see cref="StatsConfig"/>, как и до появления ступеней.
        /// </summary>
        public static void Apply(Stats stats, UnitData data, StatsConfig config)
        {
            if (stats == null || data == null || config == null) return;

            float range = config.RangeOf(data.RangeBand) * (1f + data.RangeAdjustPct);
            stats.AddModifiersFrom(config, new[]
            {
                new StatModifier(StatType.AttackRange, ModifierOp.Override, range),
            });
        }
    }
}
