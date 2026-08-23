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
    /// побеждает» тогда даёт каскад Класс → Персона → Vessel бесплатно: если персона (мементо)
    /// авторит HP через <c>Override</c>, она перекроет классовую базу (обратная совместимость со
    /// старым авторингом); если персона кладёт только <c>Flat</c>/<c>Percent</c> — они лягут
    /// дельтой поверх классовой базы. Source группы — сам <paramref name="config"/>.
    /// </remarks>
    public static class ClassBaseline
    {
        /// <summary>
        /// Добавить классовую базу юнита первой группой. Обязан вызываться ДО стат-блока персоны.
        /// <para>Нет <see cref="UnitData"/> — законный случай: враг-болванка класса не имеет, база не
        /// добавляется. Нет <paramref name="config"/> — юнит уезжает на натуральные дефолты (<c>MaxHP</c> 0);
        /// ровно так шипящаяся сцена показывала «Здоровье 0 / Скорость 0» во всей панели инвентаря, ничего
        /// не сообщая. Теперь говорим вслух — но предупреждением, а не ошибкой: в ИГРЕ этот случай стал
        /// невозможен (<c>ScopeWiring.Require</c> в обоих скоупах + <c>SceneWiringTests</c>), а в тестах
        /// конфиг не подают намеренно, проверяя другое (аудит фолбэков 2026-07-26, п.9).</para>
        /// </summary>
        public static void Apply(Stats stats, UnitData data, ClassBalanceConfig config)
        {
            if (stats == null || data == null) return;
            if (config == null)
            {
                UnityEngine.Debug.LogWarning(
                    $"[ClassBaseline] - нет ClassBalanceConfig: '{data.name}' останется на натуральных дефолтах (MaxHP 0)");
                return;
            }
            stats.AddModifiersFrom(config, config.GetBaseModifiers(data.CombatClass));
        }
    }
}
