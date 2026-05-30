using System;
using System.Collections.Generic;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Помечает юнитов с HP ≤ 0 как мёртвых, удаляет их из <see cref="SpatialHash"/>,
    /// публикует событие <see cref="OnUnitDied"/>.
    /// </summary>
    public sealed class DeathSystem
    {
        /// <summary>Событие: юнит погиб. Слушатели — CombatSimulation (для проверки win/lose) и Presentation.</summary>
        public event Action<RuntimeUnit> OnUnitDied;

        /// <summary>Обработать смерти за текущий тик.</summary>
        public void Tick(List<RuntimeUnit> units, SpatialHash spatialHash)
        {
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead || unit.CurrentHP > 0f) continue;

                unit.IsDead = true;
                unit.CurrentTarget = null;
                spatialHash.Remove(unit);
                OnUnitDied?.Invoke(unit);
            }
        }
    }
}
