using System;
using Guildmaster.Core.Players;
using Guildmaster.Data.Definitions;
using VContainer.Unity;

namespace Guildmaster.Game.Activity
{
    /// <summary>
    /// Заказывает составу сеанса раздачу сторон под открывшуюся площадку: одна сторона в кампании, две
    /// в PvP.
    /// </summary>
    /// <remarks>
    /// <b>Заказывает мероприятие, а решает сеанс</b> — и это не лишнее звено. Сколько у площадки сторон,
    /// знает только она сама (<see cref="ActivitySetup.OwnUnitsOnly"/>); кто эти стороны займёт — только
    /// сеанс, потому что участников знает соединение. Спроси состав у площадки напрямую, и он полез бы
    /// вниз по скоупам за объектом, который умрёт раньше него.
    /// <para><b>Закрываясь, площадка возвращает всех в одну сторону.</b> Иначе после PvP-матча игроки
    /// разошлись бы по командам навсегда и перестали видеть курсоры друг друга во дворе — там, где
    /// противников нет вовсе.</para>
    /// </remarks>
    public sealed class ActivitySideAssignment : IStartable, IDisposable
    {
        private readonly ISessionRoster _roster;
        private readonly ActivitySetup  _setup;

        public ActivitySideAssignment(ISessionRoster roster, ActivitySetup setup)
        {
            _roster = roster;
            _setup  = setup;
        }

        public void Start() => _roster?.AssignSides(_setup.OwnUnitsOnly ? 2 : 1);

        public void Dispose() => _roster?.AssignSides(1);
    }
}
