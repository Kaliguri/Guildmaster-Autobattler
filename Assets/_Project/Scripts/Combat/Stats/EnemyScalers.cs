using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Применение видовых/подвидовых скейлов врага (уровни 3–4 каскада) к <see cref="Stats"/> —
    /// ЕДИНСТВЕННОЕ место, откуда они попадают в сборку. И бой (<c>RuntimeUnitFactory</c>), и
    /// Content Hub (<c>StatMath</c>) зовут это, чтобы значения совпадали по построению.
    /// </summary>
    /// <remarks>
    /// Порядок каскада: класс ПЕРВОЙ группой (<see cref="ClassBaseline"/>), затем Вид, затем Подвид,
    /// затем стат-блок юнита. Скейлы обычно <c>PercentMult</c> и перемножаются поверх классовой базы
    /// (Гоблины: MaxHP ×0.4, MoveSpeed ×1.1). No-op для не-врагов и врагов без назначенного вида.
    /// Обязан вызываться ПОСЛЕ <see cref="ClassBaseline"/> и ДО стат-блока персоны.
    /// </remarks>
    public static class EnemyScalers
    {
        public static void Apply(Stats stats, UnitData data)
        {
            if (stats == null || !(data is EnemyData enemy)) return;

            if (enemy.Species != null)
                stats.AddModifiersFrom(enemy.Species, enemy.Species.Scalers);

            if (enemy.Subspecies != null)
                stats.AddModifiersFrom(enemy.Subspecies, enemy.Subspecies.Scalers);
        }
    }
}
