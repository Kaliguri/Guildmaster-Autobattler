using System;
using Guildmaster.Core.Net;
using Guildmaster.Data;
using Guildmaster.Net.Transport;
using UnityEngine;

namespace Guildmaster.Net.Session
{
    /// <summary>
    /// Кооп-сессия: создать игру одним кликом, позвать друга, пережить разрыв и честно кончиться, когда
    /// ушёл хост.
    /// </summary>
    /// <remarks>
    /// <b>Только Steam</b> (решение Макса 02.08.2026): создание — один клик без названия и настроек,
    /// вход — исключительно по приглашению. Списка комнат и подключения по адресу не существует; лобби
    /// у нас не комната, а адрес, по которому Steam ведёт приглашённого.
    /// <para><b>Миграции хоста нет.</b> Гильдия живёт у хоста, поэтому его уход — конец сессии, а не
    /// повод выбирать нового авторитета. Это осознанный отказ от самой дорогой и самой багованной
    /// подсистемы сетевого кода: гости уносят открытия в свои профили и возвращаются к себе.</para>
    /// <para><b>«Подключились» и «в сессии» — разные события.</b> Между ними рукопожатие: версия сборки
    /// и отпечаток контента. Без проверки расхождение контента всплыло бы не отказом, а сломанным
    /// показом боя — лента несёт строковые id, и неизвестный id роняет картинку.</para>
    /// </remarks>
    public sealed class CoopSession : ICoopSessionControl, IDisposable
    {
        // Транспорт — ЗА ШВОМ, а не конкретный Steam: подъём сессии и вход к другу теперь часть
        // интерфейса, и потому подключение, разрыв и приём приглашения проверяются петлёй в одном
        // процессе. Пока здесь стоял конкретный тип, всё это можно было увидеть только вживую вдвоём.
        private readonly Transport.INetTransport _transport;
        // Комната — тоже за швом, по той же причине, что и транспорт: гостевая половина сеанса иначе
        // не отыгрывается нигде, кроме живого прогона вдвоём.
        private readonly ICoopLobby _lobby;
        private readonly CoopHandshake     _handshake;

        // ПАРАМЕТРЫ — тоже за швом, и это не косметика: тип поля можно сузить до интерфейса, а
        // параметр оставить конкретным, и всё скомпилируется — инъекция при этом продолжит подавать
        // Steam-транспорт даже там, где в контейнере выбрана петля (моя ошибка 05.08.2026).
        public CoopSession(Transport.INetTransport transport, ICoopLobby lobby, CoopHandshake handshake)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _lobby     = lobby;
            _handshake = handshake;

            _transport.PeerConnected    += HandlePeerConnected;
            _transport.PeerDisconnected += HandlePeerDisconnected;

            if (_handshake != null)
            {
                _handshake.Approved += HandleApproved;
                _handshake.Rejected += HandleRejected;
                // Хозяйскую половину рукопожатия не слушал никто, и «гость принят» не оставляло следа
                // нигде: хозяин узнавал о напарнике только тогда, когда тот отваливался.
                _handshake.GuestApproved += HandleGuestApproved;
                _handshake.GuestRejected += HandleGuestRejected;
            }

            if (_lobby != null)
            {
                _lobby.JoinRequested += HandleJoinRequested;
                _lobby.LobbyChanged  += HandleLobbyChanged;
            }
        }

        public CoopSessionState State { get; private set; } = CoopSessionState.Offline;

        public CoopEndReason EndReason { get; private set; } = CoopEndReason.None;

        public string EndMessage { get; private set; } = string.Empty;

        public event Action<CoopSessionState> StateChanged;

        /// <summary>
        /// Есть ли кого звать: сессия поднята и лобби создано. Кнопка приглашения гаснет сама, если
        /// Steam не запущен, — это внешний отказ, и прятать его нельзя.
        /// </summary>
        public bool CanInvite => State != CoopSessionState.Offline && (_lobby?.HasLobby ?? false);

        /// <summary>Создать игру: relay-сокет плюс лобби, по которому придёт приглашённый.</summary>
        public bool StartHost()
        {
            if (State != CoopSessionState.Offline) return false;

            Log("поднимаю сессию: сокет плюс лобби");

            if (!_transport.StartHost())
            {
                Fail(CoopEndReason.ConnectionFailed, "Не удалось поднять сессию — проверь, запущен ли Steam");
                return false;
            }

            EndReason  = CoopEndReason.None;
            EndMessage = string.Empty;
            Set(CoopSessionState.Hosting);

            _lobby?.CreateLobby();
            return true;
        }

        /// <summary>Позвать друга оверлеем Steam — единственный вход для игрока.</summary>
        public void InviteFriend() => _lobby?.OpenInviteOverlay();

        /// <summary>Steam на связи: клиент запущен и поднят.</summary>
        public bool IsSteamReady => _lobby?.IsSteamReady ?? false;

        /// <summary>Открыть список друзей — оттуда входят в чужую игру.</summary>
        public void BrowseFriends() => _lobby?.OpenFriendsOverlay();

        /// <summary>Выйти. У хоста это конец сессии для всех.</summary>
        public void Leave()
        {
            if (State == CoopSessionState.Offline) return;

            EndReason  = CoopEndReason.LocalRequest;
            EndMessage = string.Empty;
            Stop();
        }

        public void Dispose()
        {
            _transport.PeerConnected    -= HandlePeerConnected;
            _transport.PeerDisconnected -= HandlePeerDisconnected;

            if (_handshake != null)
            {
                _handshake.Approved -= HandleApproved;
                _handshake.Rejected -= HandleRejected;
                _handshake.GuestApproved -= HandleGuestApproved;
                _handshake.GuestRejected -= HandleGuestRejected;
            }

            if (_lobby != null)
            {
                _lobby.JoinRequested -= HandleJoinRequested;
                _lobby.LobbyChanged  -= HandleLobbyChanged;
            }
        }

        // ── вход по приглашению ──────────────────────────────────────────────────

        // Steam зовёт нас в чужое лобби: у нас есть SteamId хозяина, а значит и адрес relay-сокета.
        private void HandleJoinRequested(ulong lobbyId, ulong hostSteamId)
        {
            Log($"Steam зовёт в лобби {lobbyId} к хозяину {hostSteamId}; наше состояние {State}");

            if (State != CoopSessionState.Offline) Stop();

            if (!_transport.Connect(hostSteamId))
            {
                Fail(CoopEndReason.ConnectionFailed, "Не удалось открыть соединение с хостом");
                return;
            }

            EndReason  = CoopEndReason.None;
            EndMessage = string.Empty;
            Set(CoopSessionState.Connecting);
        }

        private void HandlePeerConnected(int peerId)
        {
            // Канал «сеанс» существовал, но в него не писал никто: включив его на разборе, игрок видел
            // пустой лог и заключал, что диагностика не работает (наход. Макса 07.08.2026). Здесь —
            // единственное место, где видны обе половины входа: и соединение, и рукопожатие.
            Log($"соединение с пиром {peerId} поднято (мы {(_transport.IsHost ? "хозяин" : "гость")}, " +
                $"состояние {State})");

            // Гость: соединение с хостом есть — представляемся. «В сессии» мы станем на его ответ.
            if (!_transport.IsHost && peerId == NetPeer.HostPeerId) _handshake?.SayHello();
        }

        private static void Log(string message) =>
            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Session, message);

        public event Action<int> PeerLeft;

        private void HandlePeerDisconnected(int peerId)
        {
            Log($"соединение с пиром {peerId} разорвано (состояние {State})");

            // У хоста уход гостя сессию не кончает: он остаётся хостом, пусть и в одиночестве. Но
            // молчать об этом нельзя — он не должен узнавать о потере напарника по тому, что курсор
            // перестал двигаться.
            if (_transport.IsHost) { PeerLeft?.Invoke(peerId); return; }
            if (peerId != NetPeer.HostPeerId) return;

            // Текст описывает НАБЛЮДАЕМОЕ, а не догадку о чужой стороне. «Хост не ответил» здесь стояло
            // до 09.08.2026 и врало: разрыв на этапе подключения приходил и тогда, когда хост отвечал
            // исправно — например, эхом закрытого соединения прошлой сессии. Диагноз назначался по
            // состоянию, а игрок читал его как факт (разбор прогона 08.08.2026).
            if (State == CoopSessionState.Connecting)
                Fail(CoopEndReason.ConnectionFailed, "Соединение закрылось, не дойдя до рукопожатия");
            else
                Fail(CoopEndReason.HostLeft, "Соединение с хозяином игры закрыто");
        }

        private void HandleApproved(int myPeerId)
        {
            Log($"рукопожатие прошло: наш номер в сеансе — {myPeerId}");
            _transport.SetLocalPeerId(myPeerId);
            Set(CoopSessionState.Connected);
        }

        private void HandleRejected(string reason)
        {
            Log($"хозяин отказал: {reason}");
            Fail(CoopEndReason.Rejected, reason);
        }

        /// <summary>
        /// Гость прошёл проверку версии и контента — с этой секунды он участник сеанса.
        /// </summary>
        /// <remarks>
        /// Своего события наружу отсюда НЕ поднимается, и это осознанно: приход игрока игре показывает
        /// панель участников, которая читает состав сеанса сама. Второй путь к тому же факту завёл бы
        /// слушателя, которому нечего делать, — а диалог, как на уходе, здесь был бы прямо вреден:
        /// уход требует решения («продолжить, позвать, уйти»), приход не требует ничего и прерывал бы
        /// игру ради новости.
        /// </remarks>
        private void HandleGuestApproved(int peerId) =>
            Log($"гость {peerId} принят: версия и контент сошлись");

        private void HandleGuestRejected(int peerId, string reason) =>
            Log($"гостю {peerId} отказано: {reason}");

        // ── общее ────────────────────────────────────────────────────────────────

        private void Fail(CoopEndReason reason, string message)
        {
            EndReason  = reason;
            EndMessage = message;
            Debug.LogWarning($"[CoopSession] сессия кончилась: {reason} — {message}");
            Stop();
        }

        private void Stop()
        {
            _transport.Shutdown();
            _lobby?.LeaveLobby();
            Set(CoopSessionState.Offline);
        }

        private void Set(CoopSessionState state)
        {
            if (State == state) return;

            Log($"состояние сеанса: {State} → {state}");
            State = state;
            Raise();
        }

        // Лобби поднялось или закрылось. Само состояние сессии от этого не меняется — меняется
        // CanInvite, а экран узнаёт о нём тем же событием (см. ICoopSessionControl.StateChanged).
        private void HandleLobbyChanged() => Raise();

        private void Raise() => StateChanged?.Invoke(State);
    }
}
