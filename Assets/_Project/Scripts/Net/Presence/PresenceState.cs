using UnityEngine;

namespace Guildmaster.Net.Presence
{
    /// <summary>
    /// Что игрок делает прямо сейчас: где курсор, куда он движется, на что наведён и что держит.
    /// <para><b>HARD: присутствие эфемерно.</b> Оно не пишется в лог команд, не влияет на
    /// <c>RunState</c>, не сохраняется и не читается ни одной механикой. Как только от жеста начинает
    /// что-то зависеть — перехват вещи, бросок кубика, — он перестаёт быть присутствием и становится
    /// командой со своим номером и подтверждением хоста.</para>
    /// <para>Инвариант держит не дисциплина, а сборки: <c>Guildmaster.Net</c> не ссылается на
    /// <c>Guildmaster.Guild</c>, поэтому дороги отсюда в состояние забега физически нет.</para>
    /// <para><b>Скорость едет вместе с позицией</b> не для механики, а для интерполяции: по паре
    /// «точка + скорость» приёмник строит кривую Эрмита, и чужой курсор не ломается на изломах, как
    /// ломался бы при линейном сглаживании.</para>
    /// </summary>
    public readonly struct PresenceState
    {
        /// <summary>Чей курсор.</summary>
        public readonly int PlayerId;

        /// <summary>Номер пакета отправителя. Канал ненадёжный: по номеру приёмник отбрасывает опоздавших.</summary>
        public readonly ushort Sequence;

        /// <summary>Курсор в мировых координатах.</summary>
        public readonly Vector2 Cursor;

        /// <summary>Скорость курсора, мировых единиц в секунду.</summary>
        public readonly Vector2 Velocity;

        /// <summary>Id того, на что наведён курсор, или <see cref="Nothing"/>.</summary>
        public readonly int HoveredId;

        /// <summary>Id того, что курсор держит (тащит), или <see cref="Nothing"/>.</summary>
        public readonly int HeldId;

        /// <summary>«Ни на что» — и для наведения, и для удержания.</summary>
        public const int Nothing = -1;

        /// <summary>
        /// Жест, который игрок показывает прямо сейчас: <see cref="Core.Players.PlayerGesture"/>.
        /// </summary>
        /// <remarks>
        /// <b>Живёт в присутствии, а не своим каналом,</b> и это следует из его природы: жест ничего не
        /// меняет в игре и потеря пакета стоит ровно одной незамеченной иконки. Присутствие уже
        /// раздаётся хозяином всем своим и отбирается по сторонам — своему каналу пришлось бы повторить
        /// и раздачу, и отбор, то есть завести второго владельца правила «кому это видно».
        /// <para>Держится отправителем несколько пакетов подряд: канал ненадёжный, и один-единственный
        /// пакет с жестом мог бы не доехать вовсе.</para>
        /// </remarks>
        public readonly Core.Players.PlayerGesture Gesture;

        public PresenceState(int playerId, ushort sequence, Vector2 cursor, Vector2 velocity,
            int hoveredId = Nothing, int heldId = Nothing, Core.Players.PlayerGesture gesture = Core.Players.PlayerGesture.None)
        {
            PlayerId  = playerId;
            Sequence  = sequence;
            Cursor    = cursor;
            Velocity  = velocity;
            HoveredId = hoveredId;
            HeldId    = heldId;
            Gesture   = gesture;
        }

        /// <summary>Держит ли игрок что-нибудь — чужое «в руках» видно всем, это и есть мягкая заявка.</summary>
        public bool IsHolding => HeldId != Nothing;

        public override string ToString() =>
            $"p{PlayerId}#{Sequence} @{Cursor}" +
            (HoveredId != Nothing ? $" →{HoveredId}" : string.Empty) +
            (IsHolding ? $" ✋{HeldId}" : string.Empty);
    }
}
