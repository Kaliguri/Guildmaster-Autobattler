using System.Collections.Generic;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Read-only срез боя для мозга (вики «13» §3.1). Отделяет «решение» от «исполнения»:
    /// мозг читает мир через это представление и НЕ мутирует симуляцию напрямую — отсюда
    /// тестируемость (мок-вью) и детерминизм. Реализуется <c>CombatSimulation</c>.
    /// <para>v1 — минимум, достаточный для линейного таргетинга. Радиус-запросы для
    /// позиционирования/каста (<c>EnemiesInRadius</c>/<c>Allies</c>) приходят с шагами 4/6.</para>
    /// </summary>
    public interface IBattleView
    {
        /// <summary>Все юниты боя (живые и мёртвые — мозг сам фильтрует по <c>IsDead</c>/команде).</summary>
        IReadOnlyList<RuntimeUnit> Units { get; }

        /// <summary>Текущий номер тика — для каденса/стаггера мозга.</summary>
        int CurrentTick { get; }
    }
}
