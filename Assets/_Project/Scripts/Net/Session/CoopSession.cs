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
        private readonly SteamNetTransport _transport;
        private readonly SteamLobbyService _lobby;
        private readonly CoopHandshake     _handshake;

        public CoopSession(SteamNetTransport transport, SteamLobbyService lobby, CoopHandshake handshake)
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
            // Гость: соединение с хостом есть — представляемся. «В сессии» мы станем на его ответ.
            if (!_transport.IsHost && peerId == NetPeer.HostPeerId) _handshake?.SayHello();
        }

        private void HandlePeerDisconnected(int peerId)
        {
            // У хоста уход гостя сессию не кончает: он остаётся хостом, пусть и в одиночестве.
            if (_transport.IsHost) return;
            if (peerId != NetPeer.HostPeerId) return;

            if (State == CoopSessionState.Connecting) Fail(CoopEndReason.ConnectionFailed, "Хост не ответил");
            else                                      Fail(CoopEndReason.HostLeft, "Хост завершил игру");
        }

        private void HandleApproved(int myPeerId)
        {
            _transport.SetLocalPeerId(myPeerId);
            Set(CoopSessionState.Connected);
        }

        private void HandleRejected(string reason) => Fail(CoopEndReason.Rejected, reason);

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
            State = state;
            Raise();
        }

        // Лобби поднялось или закрылось. Само состояние сессии от этого не меняется — меняется
        // CanInvite, а экран узнаёт о нём тем же событием (см. ICoopSessionControl.StateChanged).
        private void HandleLobbyChanged() => Raise();

        private void Raise() => StateChanged?.Invoke(State);
    }
}
