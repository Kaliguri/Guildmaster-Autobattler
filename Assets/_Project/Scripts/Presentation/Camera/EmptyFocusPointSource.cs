using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Пустой источник точек фокуса: камера ни за кем не следует. Используется вне боя
    /// (карта/инвентарь, до старта симуляции) — <see cref="CombatFocusTarget"/> тогда
    /// стоит на месте, а активный вид ведёт Overview-камера.
    /// </summary>
    public sealed class EmptyFocusPointSource : IFocusPointSource
    {
        public static readonly EmptyFocusPointSource Instance = new EmptyFocusPointSource();

        private static readonly IReadOnlyList<Vector2> Empty = new Vector2[0];

        public IReadOnlyList<Vector2> FocusPoints => Empty;
    }
}
