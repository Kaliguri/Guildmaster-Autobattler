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
        private readonly IPublisher<Core.Flow.NoticeRequest> _notice;
        private readonly IPublisher<Core.Flow.BusyRequest>   _busy;

        private CoopSessionState _lastState;

        /// <summary>Живёт, пока идёт подключение: его отмена и снимает экран ожидания.</summary>
        private System.Threading.CancellationTokenSource _waiting;

        public CoopDisconnectPresenter(ICoopSessionControl coop, ISessionRoster roster,
                                       Core.Flow.IRunControl runControl,
                                       IPublisher<PeerLostRequest> pub,
                                       IPublisher<Core.Flow.NoticeRequest> notice,
                                       IPublisher<Core.Flow.BusyRequest> busy)
        {
            _coop       = coop;
            _roster     = roster;
            _runControl = runControl;
            _pub        = pub;
            _notice     = notice;
            _busy       = busy;
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
            // Ожидание снимаем раньше подписок: иначе экран переживёт того, кто его заказывал.
            _waiting?.Cancel();
            _waiting?.Dispose();
            _waiting = null;

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
        /// Сеанс сменил состояние: показываем ожидание на входе, уход хоста — диалогом, а
        /// несостоявшийся вход — сообщением.
        /// </summary>
        /// <remarks>
        /// <b>Про несостоявшийся вход раньше не говорил НИКТО.</b> Здесь стояло «о нём скажет тот, кто
        /// вход затевал», но затевающий молчал, и игрок видел только то, что игра не началась: «Щас не
        /// понятно, что произошло, почему не смогли подключиться к пвп» (Макс, 08.08.2026). Место для
        /// этого ровно тут — сюда уже приходит и состояние, и причина.
        /// </remarks>
        private void OnStateChanged(CoopSessionState state)
        {
            CoopSessionState was = _lastState;
            _lastState = state;

            ShowWaitingWhileConnecting(state);

            if (state != CoopSessionState.Offline) return;

            // Вход не состоялся: об этом говорим ВСЕГДА, даже когда причина скучная. Молчание здесь
            // читается как поломка игры, а не как отказ соединения.
            if (was == CoopSessionState.Connecting)
            {
                ReportFailedJoin();
                return;
            }

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
        /// Пока идёт подключение — показываем ожидание; вышли из него — снимаем.
        /// </summary>
        /// <remarks>
        /// Ожидание длится секунды (relay Valve выбирает маршрут), и всё это время не происходило
        /// ничего видимого: игрок жал кнопку повторно, не зная, засчиталось ли первое нажатие (наход.
        /// Макса 08.08.2026, «Не хватает UI загрузки»). Срок держит этот токен, а не сам экран.
        /// </remarks>
        private void ShowWaitingWhileConnecting(CoopSessionState state)
        {
            if (state == CoopSessionState.Connecting)
            {
                if (_waiting != null) return;   // уже ждём: второе окно поверх первого — это мигание

                _waiting = new System.Threading.CancellationTokenSource();
                _busy?.Publish(new Core.Flow.BusyRequest(
                    "ui.coop.connecting", "Подключение к игре напарника...", _waiting.Token));
                return;
            }

            if (_waiting == null) return;

            _waiting.Cancel();
            _waiting.Dispose();
            _waiting = null;
        }

        /// <summary>
        /// Сказать, что вход не состоялся, и назвать причину словами системы.
        /// </summary>
        /// <remarks>
        /// <b>Заголовок описывает исход, а не диагноз.</b> Сообщение «Хост не ответил» приходило и
        /// тогда, когда хост отвечал исправно, — оно назначалось по СОСТОЯНИЮ, а не по причине разрыва
        /// (разбор 08.08.2026). Поэтому здесь: что случилось — наше, отчего — от того, кто отказал,
        /// строкой без перевода.
        /// </remarks>
        private void ReportFailedJoin()
        {
            string details = _coop?.EndMessage;

            (string key, string text) = _coop?.EndReason switch
            {
                CoopEndReason.Rejected =>
                    ("ui.coop.join_rejected", "Хозяин игры не принял подключение."),
                CoopEndReason.LocalRequest =>
                    (null, (string)null),   // ушли сами — говорить не о чем
                _ =>
                    ("ui.coop.join_failed", "Подключиться к игре не удалось."),
            };

            if (key == null) return;

            _notice?.Publish(new Core.Flow.NoticeRequest(
                Core.Flow.NoticeKind.Error,
                titleKey: "ui.coop.join_failed_title", titleFallback: "Не удалось подключиться",
                bodyKey: key, bodyFallback: text,
                details: details));
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
