using UnityEngine;

namespace Guildmaster.Presentation.Map
{
    /// <summary>Состояние узла карты для отрисовки (смысл переносится из <c>MapGraph</c> USS-классов).</summary>
    public enum MapNodeVisualState
    {
        /// <summary>Недоступен с текущей позиции: тускло, выбором не берётся.</summary>
        Locked = 0,
        /// <summary>Доступен для входа: подсвечен, выбирается кликом.</summary>
        Available = 1,
        /// <summary>Текущая позиция игрока.</summary>
        Current = 2,
        /// <summary>Пройден.</summary>
        Cleared = 3,
    }

    /// <summary>
    /// Один узел на отрисовку: ТОПОЛОГИЯ (этаж + ряд) и как его показать. Мировых координат здесь нет —
    /// раскладку (шаг, центрирование ряда, разброс) целиком считает слой карты, потому что это его дело.
    /// <para>Намеренно не знает про <c>MapNode</c>/<c>MapNodeType</c>: <c>Guildmaster.Presentation</c> не
    /// ссылается на <c>Guildmaster.Guild</c>, и расширять связность ради карты незачем. Конвертацию делает
    /// слой склейки (<c>Game</c>), который видит оба мира.</para>
    /// </summary>
    public readonly struct MapNodeVisual
    {
        /// <summary>Id узла — то, что вернётся наружу при клике, и основа стабильного разброса.</summary>
        public readonly string Id;

        /// <summary>Этаж (индекс колонки) от старта.</summary>
        public readonly int Floor;

        /// <summary>Индекс внутри этажа, сверху вниз.</summary>
        public readonly int Row;

        /// <summary>Состояние — задаёт подсветку и выбираемость.</summary>
        public readonly MapNodeVisualState State;

        /// <summary>
        /// Вид узла — имя его типа (Battle/Elite/Boss/...). Как он выглядит, решает <see cref="MapIconSet"/>:
        /// здесь передаётся только ключ, потому что семантику типов держит Game, а картинки — Presentation.
        /// </summary>
        public readonly string Kind;

        public MapNodeVisual(string id, int floor, int row, MapNodeVisualState state, string kind)
        {
            Id    = id;
            Floor = floor;
            Row   = row;
            State = state;
            Kind  = kind;
        }
    }
}
