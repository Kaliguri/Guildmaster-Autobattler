using System;
using System.Collections.Generic;
using Guildmaster.Core.Net;
using Guildmaster.Core.Players;
using MessagePipe;
using VContainer.Unity;

namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Разрыв связи глазами игрока: кто пропал, что это значит и что делать дальше.
    /// </summary>
    /// <remarks>
    /// <b>До этого разрыв был молчаливым с обеих сторон.</b> Гостя уводило в главное меню без единого
    /// слова — экран просто менялся, и «хост вышел» было не отличить от собственного сбоя. Хост же не
    /// узнавал об уходе напарника вовсе: он оставался в игре и понимал это по замершему курсору
    /// (наход. Макса 04.08.2026).
    /// <para><b>Варианты разные, потому что разные роли.</b> Хосту игра остаётся: он продолжает один,
    /// зовёт заново или уходит. Гостю игры больше нет — его забег чужой; он либо уходит в меню, либо
    /// ищет, к кому пойти.</para>
    /// <para><b>«Пригласить» и «Присоединиться» ведут в тот же оверлей Steam, что и кнопки меню</b> —
    /// через <see cref="ICoopSessionControl"/>. Второй способ позвать друга разошёлся бы с первым ровно
    /// в тот день, когда у приглашения появится своё правило.</para>
    /// </remarks>
    public sealed class CoopDisconnectPresenter : IStartable, IDisposable
    {
        private readonly ICoopSessionControl _coop;
        private readonly ISessionRoster      _roster;
        private readonly Core.Flow.IRunControl _runControl;
        private readonly IPublisher<PeerLostRequest> _pub;

        private CoopSessionState _lastState;

        public CoopDisconnectPresenter(ICoopSessionControl coop, ISessionRoster roster,
                                       Core.Flow.IRunControl runControl,
                                       IPublisher<PeerLostRequest> pub)
        {
            _coop       = coop;
            _roster     = roster;
            _runControl = runControl;
            _pub        = pub;
        }

        public void Start()
        {
            if (_coop == null) return;

            _lastState = _coop.State;
            _coop.StateChanged += OnStateChanged;
            _coop.PeerLeft     += OnPeerLeft;
        }

        public void Dispose()
        {
            if (_coop == null) return;

            _coop.StateChanged -= OnStateChanged;
            _coop.PeerLeft     -= OnPeerLeft;
        }

        /// <summary>У хоста ушёл напарник. Игра продолжается — вопрос только в том, чего хочет хозяин.</summary>
        private void OnPeerLeft(int peerId)
        {
            string name = NameOf(peerId);

            _pub?.Publish(new PeerLostRequest(
                title: $"{name} отключился",
                body: "Напарник потерял связь или вышел из игры.",
                consequence: "Забег продолжается — прогресс сохранён.",
                options: new List<PeerLostOption>
                {
                    new PeerLostOption("ui.coop.lost.continue", "Продолжить", null, primary: true),
                    new PeerLostOption("ui.coop.lost.invite",   "Пригласить", () => _coop.InviteFriend()),
                    new PeerLostOption("ui.coop.lost.to_menu",  "В главное меню",
                                       () => _runControl?.RequestReturnToMainMenu()),
                }));
        }

        /// <summary>
        /// У гостя кончилась сессия. Показываем только уход хоста: отказ по версии и несостоявшееся
        /// соединение — это про вход, и о них говорит тот, кто вход и затевал.
        /// </summary>
        private void OnStateChanged(CoopSessionState state)
        {
            CoopSessionState was = _lastState;
            _lastState = state;

            if (state != CoopSessionState.Offline) return;
            if (was != CoopSessionState.Connected) return;          // мы и не были в чужой игре
            if (_coop.EndReason != CoopEndReason.HostLeft) return;

            string host = NameOf(NetPeerIds.Host);

            _pub?.Publish(new PeerLostRequest(
                title: $"{host} (хост) отключился",
                body: "Игра, к которой вы присоединились, закончилась.",
                consequence: "Ваша гильдия цела — она хранится у вас, а не у хоста.",
                options: new List<PeerLostOption>
                {
                    new PeerLostOption("ui.coop.lost.to_menu", "В главное меню", null, primary: true),
                    new PeerLostOption("ui.coop.lost.join",    "Присоединиться", () => _coop.BrowseFriends()),
                }));
        }

        /// <summary>
        /// Имя по номеру пира. Состав сеанса к этому моменту уже мог схлопнуться — тогда говорим
        /// «Напарник»: безымянное сообщение честнее выдуманного имени.
        /// </summary>
        private string NameOf(int peerId)
        {
            IReadOnlyList<SessionPlayer> players = _roster?.Players;
            for (int i = 0; players != null && i < players.Count; i++)
                if (players[i].Id == peerId && !string.IsNullOrEmpty(players[i].Name))
                    return players[i].Name;

            return "Напарник";
        }
    }

    /// <summary>Номера пиров, о которых знает показ. Сеть их владелец, но UI не ссылается на сеть.</summary>
    internal static class NetPeerIds
    {
        public const int Host = 0;
    }
}
