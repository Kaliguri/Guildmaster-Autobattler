using System;
using Guildmaster.Core.Players;
using Guildmaster.Data.Definitions;
using VContainer.Unity;

namespace Guildmaster.Game.Activity
{
    /// <summary>
    /// Заказывает составу сеанса раздачу сторон под открывшуюся площадку: врозь в PvP, всех вместе в
    /// кампании и на Ристалище.
    /// </summary>
    /// <remarks>
    /// <b>Заказывает мероприятие, а решает сеанс</b> — и это не лишнее звено. Кому принадлежит вторая
    /// сторона, знает только площадка (<see cref="ActivitySetup.Opposition"/>); кто эти стороны займёт
    /// — только сеанс, потому что участников знает соединение. Спроси состав у площадки напрямую, и он
    /// полез бы вниз по скоупам за объектом, который умрёт раньше него.
    /// <para><b>Делить участников надо ровно там, где вторая сторона игрокова.</b> Пока это выводилось
    /// из запрета трогать чужих, кампания разводила союзников по разным сторонам — и гость получал во
    /// владение монстров вместо своего отряда (наход. Макса 07.08.2026).</para>
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

        public void Start() => _roster?.SplitBetweenSides(_setup.SidesAreDealt);

        public void Dispose() => _roster?.SplitBetweenSides(false);
    }
}
