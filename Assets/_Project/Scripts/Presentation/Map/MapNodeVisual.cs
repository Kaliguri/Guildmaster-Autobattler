using UnityEngine;

namespace Guildmaster.Presentation.Map
{
    /// <summary>Состояние узла карты для отрисовки (смысл переносится из <c>MapGraph</c> USS-классов).</summary>
    public enum MapNodeVisualState
    {
        /// <summary>Недоступен с текущей позиции: тускло, кликом не берётся.</summary>
        Locked = 0,
        /// <summary>Доступен для входа: подсвечен, кликабелен.</summary>
        Available = 1,
        /// <summary>Текущая позиция игрока.</summary>
        Current = 2,
        /// <summary>Пройден.</summary>
        Cleared = 3,
    }

    /// <summary>
    /// Данные одного узла для отрисовки в мире. Намеренно НЕ знает про <c>MapNode</c>/<c>MapNodeType</c>:
    /// <c>Guildmaster.Presentation</c> не ссылается на <c>Guildmaster.Guild</c>, и расширять связность ради
    /// карты незачем. Конвертацию делает слой склейки (<c>Game</c>), который видит оба мира.
    /// </summary>
    public readonly struct MapNodeVisual
    {
        /// <summary>Id узла — то, что вернётся наружу при клике.</summary>
        public readonly string Id;

        /// <summary>Позиция в мире (уже разложенная: сетка (col,row) умножена на шаг и смещена в зону карты).</summary>
        public readonly Vector2 Position;

        /// <summary>Состояние — задаёт подсветку и кликабельность.</summary>
        public readonly MapNodeVisualState State;

        /// <summary>Цвет узла (по типу — семантику типов держит Game, не Presentation).</summary>
        public readonly Color Color;

        public MapNodeVisual(string id, Vector2 position, MapNodeVisualState state, Color color)
        {
            Id       = id;
            Position = position;
            State    = state;
            Color    = color;
        }
    }
}
