using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Источник точек фокуса экшн-камеры (вики «16» §5): позиции сущностей, которые
    /// камера держит в кадре. Развязывает <see cref="CombatFocusTarget"/> от боевой
    /// симуляции — в бою точки идут от живых юнитов (<see cref="CombatFocusPointSource"/>),
    /// вне боя источник пуст (<see cref="EmptyFocusPointSource"/>), камера ни за кем не
    /// следует. Презентация смотрит на sim, sim на камеру — никогда.
    /// </summary>
    public interface IFocusPointSource
    {
        /// <summary>
        /// Позиции живых сущностей в кадре. Пусто → камера не следует ни за чем.
        /// Список может переиспользоваться реализацией: читать по значению в тот же
        /// кадр, ссылку между кадрами не кэшировать.
        /// </summary>
        IReadOnlyList<Vector2> FocusPoints { get; }
    }
}
