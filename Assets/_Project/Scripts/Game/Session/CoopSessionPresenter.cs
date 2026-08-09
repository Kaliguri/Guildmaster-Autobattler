using System;
using System.Collections.Generic;
using Guildmaster.Core.Net;
using Guildmaster.Core.Players;
using MessagePipe;
using VContainer.Unity;

namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Сеанс глазами игрока: подключение, отказ, разрыв — всё, о чём кооп обязан сказать вслух.
    /// </summary>
    /// <remarks>
    /// <b>Имя изменилось 09.08.2026 вместе с обязанностями.</b> Класс родился как показ разрыва
    /// (<c>CoopDisconnectPresenter</c>), но сюда же приходит и вход в чужую игру: состояние и причина
    /// уже здесь, а заводить второго слушателя того же события значило бы развести один факт по двум
    /// головам.
    /// <para><b>До этого разрыв был молчаливым с обеих сторон.</b> Гостя уводило в главное меню без единого
    /// слова — экран просто менялся, и «хост вышел» было не отличить от собственного сбоя. Хост же не
    /// узнавал об уходе напарника вовсе: он оставался в игре и понимал это по замершему курсору
    /// (наход. Макса 04.08.2026).</para>
    /// <para><b>Варианты разные, потому что разные роли.</b> Хосту игра остаётся: он продолжает один,
    /// зовёт заново или уходит. Гостю игры больше нет — его забег чужой; он либо уходит в меню, либо
    /// ищет, к кому пойти.</para>
    /// <para><b>«Пригласить» и «Присоединиться» ведут в тот же оверлей Steam, что и кнопки меню</b> —
    /// через <see cref="ICoopSessionControl"/>. Второй способ позвать друга разошёлся бы с первым ровно
    /// в тот день, когда у приглашения появится своё правило.</para>
    /// </remarks>
    public sealed class CoopSessionPresenter : IStartable, IDisposable
    {
        private readonly ICoopSessionControl _coop;
        private readonly ISessionRoster      _roster;
        private readonly Core.Flow.IRunControl _runControl;
        private readonly IPublisher<Core.Flow.NoticeRequest> _notice;
        private readonly IPublisher<Core.Flow.BusyRequest>   _busy;

        private CoopSessionState _lastState;

        /// <summary>Живёт, пока идёт подключение: его отмена и снимает экран ожидания.</summary>
        private System.Threading.CancellationTokenSource _waiting;

        public CoopSessionPresenter(ICoopSessionControl coop, ISessionRoster roster,
                                       Core.Flow.IRunControl runControl,
                                       IPublisher<Core.Flow.NoticeRequest> notice,
                                       IPublisher<Core.Flow.BusyRequest> busy)
        {
            _coop       = coop;
            _roster     = roster;
            _runControl = runControl;
            _notice     = notice;
            _busy       = busy;
        }

        public void Start()
        {
            if (_coop == null) return;

            _lastState = _coop.State;
            _coop.StateChanged += OnStateChanged;
            _coop.PeerLeft     += OnPeerLeft;
            _coop.Invited      += OnInvited;
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
            _coop.Invited      -= OnInvited;
        }

        /// <summary>
        /// Нас зовут в чужую игру, и мы в этот момент играем: спрашиваем прямо в игре.
        /// </summary>
        /// <remarks>
        /// <b>Вопрос, а не действие</b> (заказ Макса 09.08.2026): «Хей, Х зовет тебя в Y,
        /// присоединиться?». Уводить по чужому клику нельзя — человек может быть посреди своего боя.
        /// <para><b>Steam покажет и своё уведомление</b>, отключить его нельзя. Наше окно не заменяет
        /// системное: оно даёт ответить не отрываясь от игры и не искать всплывашку в углу.</para>
        /// <para>Согласие уводит из текущей сессии — вход в чужую игру рвёт свою (это делает
        /// <c>AcceptInvite</c>), поэтому вопрос честно называет цену.</para>
        /// </remarks>
        private void OnInvited(string fromName, ulong fromSteamId)
        {
            string who = string.IsNullOrWhiteSpace(fromName) ? "Напарник" : fromName;

            _notice?.Publish(new Core.Flow.NoticeRequest(
                Core.Flow.NoticeKind.Info,
                titleKey: null, titleFallback: $"{who} зовёт в свою игру",
                bodyKey: "ui.coop.invite.body",
                bodyFallback: "Присоединиться к нему?",
                consequence: _coop.State == CoopSessionState.Offline
                    ? null
                    : "Текущая игра при этом закончится.",
                options: new List<Core.Flow.NoticeOption>
                {
                    new Core.Flow.NoticeOption("ui.coop.invite.accept", "Присоединиться",
                                               () => _coop.AcceptInvite(fromSteamId), primary: true),
                    new Core.Flow.NoticeOption("ui.coop.invite.decline", "Не сейчас", null),
                }));
        }

        /// <summary>У хоста ушёл напарник. Игра продолжается — вопрос только в том, чего хочет хозяин.</summary>
        private void OnPeerLeft(int peerId)
        {
            string name = NameOf(peerId);

            _notice?.Publish(new Core.Flow.NoticeRequest(
                Core.Flow.NoticeKind.Warning,
                titleKey: null, titleFallback: $"{name} отключился",
                bodyKey: "ui.coop.lost.body", bodyFallback: "Напарник потерял связь или вышел из игры.",
                consequence: "Забег продолжается — прогресс сохранён.",
                options: new List<Core.Flow.NoticeOption>
                {
                    new Core.Flow.NoticeOption("ui.coop.lost.continue", "Продолжить", null, primary: true),
                    new Core.Flow.NoticeOption("ui.coop.lost.invite",   "Пригласить", () => _coop.InviteFriend()),
                    new Core.Flow.NoticeOption("ui.coop.lost.to_menu",  "В главное меню",
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

            _notice?.Publish(new Core.Flow.NoticeRequest(
                Core.Flow.NoticeKind.Warning,
                titleKey: null, titleFallback: $"{host} (хост) отключился",
                bodyKey: "ui.coop.lost.host_body",
                bodyFallback: "Игра, к которой вы присоединились, закончилась.",
                consequence: "Ваша гильдия цела — она хранится у вас, а не у хоста.",
                options: new List<Core.Flow.NoticeOption>
                {
                    new Core.Flow.NoticeOption("ui.coop.lost.to_menu", "В главное меню", null, primary: true),
                    new Core.Flow.NoticeOption("ui.coop.lost.join",    "Присоединиться", () => _coop.BrowseFriends()),
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
