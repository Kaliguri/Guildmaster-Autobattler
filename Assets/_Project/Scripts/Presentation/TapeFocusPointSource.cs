using System.Collections.Generic;
using Guildmaster.Combat.Tape;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Источник точек фокуса из ЛЕНТЫ: позиции юнитов текущего кадра показа. Нужен повтору — там
    /// симуляция простаивает, юниты живут в ленте, и <see cref="CombatFocusPointSource"/> (тот читает
    /// <c>CombatSimulation.Units</c>) дал бы камере пустоту.
    /// <para>Zero-alloc: переиспользует буфер, перезаполняется в геттере из кадра плейбека.</para>
    /// </summary>
    public sealed class TapeFocusPointSource : IFocusPointSource
    {
        private readonly BattleTapePlayback _playback;
        private readonly List<Vector2> _points = new List<Vector2>(16);

        public TapeFocusPointSource(BattleTapePlayback playback) => _playback = playback;

        public IReadOnlyList<Vector2> FocusPoints
        {
            get
            {
                _points.Clear();
                if (_playback.TryGetFrame(out IReadOnlyList<UnitSnapshot> units))
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
