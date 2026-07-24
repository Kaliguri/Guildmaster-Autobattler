using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Применение классовой стат-базы (2-й уровень каскада) к <see cref="Stats"/> — ЕДИНСТВЕННОЕ
    /// место, откуда классовая база попадает в сборку. И бой (<c>RuntimeUnitFactory</c>), и
    /// Content Hub (<c>StatMath</c>) зовут это, чтобы значения совпадали по построению («таблица
    /// не врёт»).
    /// </summary>
    /// <remarks>
    /// Класс-база добавляется ПЕРВОЙ группой — ДО стат-блока персоны. Правило «последний Override
    /// побеждает» тогда даёт каскад Класс → Персона → Vessel бесплатно: если персона (реликвия)
    /// авторит HP через <c>Override</c>, она перекроет классовую базу (обратная совместимость со
    /// старым авторингом); если персона кладёт только <c>Flat</c>/<c>Percent</c> — они лягут
    /// дельтой поверх классовой базы. Source группы — сам <paramref name="config"/>.
    /// </remarks>
    public static class ClassBaseline
    {
        /// <summary>
        /// Добавить классовую базу юнита первой группой. No-op, если конфиг или данные не заданы
        /// (враг-болванка без <see cref="UnitData"/> → падает на дефолты <c>StatsConfig</c>, как раньше).
        /// Обязан вызываться ДО добавления стат-блока персоны.
        /// </summary>
        public static void Apply(Stats stats, UnitData data, ClassBalanceConfig config)
        {
            if (stats == null || data == null || config == null) return;
            stats.AddModifiersFrom(config, config.GetBaseModifiers(data.CombatClass));
        }
    }
}
