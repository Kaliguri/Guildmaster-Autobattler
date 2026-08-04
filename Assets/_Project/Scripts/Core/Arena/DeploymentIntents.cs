using UnityEngine;

namespace Guildmaster.Core.Arena
{
    /// <summary>
    /// «Поставь этого бойца сюда» — то, что игрок сделал руками, ещё не будучи применённым.
    /// </summary>
    /// <remarks>
    /// <b>Почему намерение, а не прямая правка.</b> Руки игрока есть у обоих участников сеанса, а
    /// право менять арену — только у хозяина. Раньше расстановка двигала бойца сама и лишь потом
    /// сообщала об этом забегу; такой путь у гостя не воспроизводится в принципе — двигать ему
    /// нечего, симуляции у него нет. Намерение же одинаково у обоих: хозяин исполняет своё сразу,
    /// гость отправляет своё по сети и ждёт кадра, в котором боец уже стоит.
    /// <para>Это тот же приём, которым выбор узла карты стал командой: клик — заявка, а не правка.
    /// Разбор — журнал <c>2026-08-04-a-click-is-a-command-not-a-wakeup</c>.</para>
    /// </remarks>
    public readonly struct UnitMoveIntent
    {
        /// <summary>Кого двигаем — id бойца на арене.</summary>
        public readonly int UnitId;

        /// <summary>Куда: точка уже проверена зоной расстановки и перекрытием на стороне рук.</summary>
        public readonly Vector2 Position;

        /// <summary>
        /// Чьи это руки. Владелец арены обязан спросить, распоряжается ли этот участник той стороной,
        /// которую двигают: без автора он мог бы проверить только собственное право — то есть чужое
        /// намерение принимал бы как своё.
        /// </summary>
        public readonly int PlayerId;

        public UnitMoveIntent(int unitId, Vector2 position, int playerId)
        {
            UnitId   = unitId;
            Position = position;
            PlayerId = playerId;
        }
    }

    /// <summary>
    /// «Покажи снаряжение этого бойца» — дабл-клик по фигурке.
    /// </summary>
    /// <remarks>
    /// Экран снаряжения открывается не отсюда: ему нужны надетый кит и сосуд, а их знает владелец
    /// состава, а не руки. Руки говорят только, по кому щёлкнули.
    /// </remarks>
    public readonly struct OpenLoadoutIntent
    {
        public readonly int UnitId;

        public OpenLoadoutIntent(int unitId) => UnitId = unitId;
    }
}
