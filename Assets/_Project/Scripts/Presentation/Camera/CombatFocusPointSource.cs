using System.Collections.Generic;
using Guildmaster.Combat;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Боевой источник точек фокуса: позиции живых юнитов активной симуляции.
    /// Zero-alloc — переиспользует буфер (перезаполняется в геттере из
    /// <see cref="CombatSimulation.Units"/>, отфильтровывая мёртвых). Только читает
    /// sim, ничего не мутирует.
    /// </summary>
    public sealed class CombatFocusPointSource : IFocusPointSource
    {
        private readonly CombatSimulation _simulation;
        private readonly List<Vector2> _points = new List<Vector2>(16);

        public CombatFocusPointSource(CombatSimulation simulation) => _simulation = simulation;

        public IReadOnlyList<Vector2> FocusPoints
        {
            get
            {
                _points.Clear();
                var units = _simulation.Units;
                for (int i = 0; i < units.Count; i++)
                {
                    if (units[i].IsDead) continue;
                    _points.Add(units[i].Position);
                }
                return _points;
            }
        }
    }
}
