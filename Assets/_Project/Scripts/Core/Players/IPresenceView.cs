using UnityEngine;

namespace Guildmaster.Core.Players
{
    /// <summary>Чужой курсор в том виде, в каком его рисуют.</summary>
    public readonly struct RemoteCursor
    {
        /// <summary>Чей курсор.</summary>
        public readonly int PlayerId;

        /// <summary>Где он в мире — уже сглаженный, готовый к отрисовке.</summary>
        public readonly Vector2 Position;

        /// <summary>Держит ли игрок что-то в руке: вид курсора меняется, это видно всем.</summary>
        public readonly bool IsHolding;

        /// <summary>
        /// На что он навёл курсор, или <see cref="Nothing"/>. Наведение публично: пока напарник держит
        /// курсор на бойце, у остальных этот боец подсвечен его мейн-цветом.
        /// </summary>
        /// <remarks>
        /// «Пинг без клика» и самый дешёвый способ убрать половину голосового трафика («да вон тот,
        /// лучник… нет, другой лучник») — принято 30.07.2026, <c>gdd/50-modes-ux/coop/presence</c>
        /// §Правила слоя. Ехало по проводу с самого начала, но наружу не отдавалось и потому не
        /// рисовалось.
        /// </remarks>
        public readonly int HoveredId;

        /// <summary>«Ни на что» — то же значение, что у присутствия на проводе.</summary>
        public const int Nothing = -1;

        public RemoteCursor(int playerId, Vector2 position, bool isHolding, int hoveredId = Nothing)
        {
            PlayerId  = playerId;
            Position  = position;
            IsHolding = isHolding;
            HoveredId = hoveredId;
        }
    }

    /// <summary>
    /// Чужие курсоры, которые нам МОЖНО видеть.
    /// </summary>
    /// <remarks>
    /// <b>Отбор по стороне сделан не здесь.</b> Пакет присутствия режет по командам хост, ещё до
    /// отправки (решение Макса 03.08.2026): курсор противника не должен доезжать до нас вовсе. Не
    /// нарисовать пришедшее — вежливая просьба, а не тайна, и в PvP она выдала бы чужой строй любому,
    /// кто заглянет в память клиента.
    /// <para>Свой курсор сюда не попадает: его рисует система, а не сеть.</para>
    /// </remarks>
    public interface IPresenceView
    {
        /// <summary>Сколько чужих курсоров сейчас видно.</summary>
        int Count { get; }

        /// <summary>Курсор по порядковому номеру в списке видимых.</summary>
        RemoteCursor this[int index] { get; }
    }
}
