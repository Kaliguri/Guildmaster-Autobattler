using System;
using System.Text;
using Guildmaster.Data;
using Guildmaster.Net.Transport;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Guildmaster.Net.Session
{
    /// <summary>Где сейчас находится кооп-сессия.</summary>
    public enum CoopSessionState
    {
        /// <summary>Играем одни.</summary>
        Offline = 0,

        /// <summary>Мы хост, ждём гостей.</summary>
        Hosting,

        /// <summary>Гость: соединение поднимается.</summary>
        Connecting,

        /// <summary>Гость: соединение установлено.</summary>
        Connected,
    }

    /// <summary>Почему сессия кончилась. Игроку показывается текстом, поэтому причина именована.</summary>
    public enum CoopEndReason
    {
        None = 0,

        /// <summary>Сами вышли.</summary>
        LocalRequest,

        /// <summary>Хост ушёл — сессия кончается для всех (решение 01.08.2026).</summary>
        HostLeft,

        /// <summary>Хост отказал: другая версия сборки или другой контент.</summary>
        Rejected,

        /// <summary>Не достучались вовсе.</summary>
        ConnectionFailed,
    }

    /// <summary>
    /// Кооп-сессия: поднять хост, войти гостем, пережить разрыв и честно кончиться, когда ушёл хост.
    /// </summary>
    /// <remarks>
    /// <b>Миграции хоста не существует.</b> Гильдия живёт у хоста (дизайн), поэтому его уход — конец
    /// сессии, а не повод выбрать нового авторитета. Это осознанный отказ от самой дорогой и самой
    /// багованной подсистемы сетевого кода: гости уносят открытия в свои профили и возвращаются к себе.
    /// <para><b>Рукопожатие держит отпечаток контента, а не только версию сборки.</b> У нас data-driven
    /// контент на строковых id и живой поток правок в SO; чанк ленты несёт эти id, и неизвестный id на
    /// приёме роняет показ, а не «слегка расходит картинку». NGO сверяет свой <c>NetworkConfig</c> и про
    /// наш контент не знает ничего.</para>
    /// <para><b>Отказ приходит текстом.</b> Молчаливый разрыв на рукопожатии выглядит для игрока как
    /// «не работает интернет», и разбираться в этом он будет не с патчем, а с отзывом.</para>
    /// </remarks>
    public sealed class CoopSession : IDisposable
    {
        /// <summary>Порт по умолчанию для прямого подключения в dev-сборках.</summary>
        public const ushort DefaultPort = 7777;

        private readonly NetworkManager     _manager;
        private readonly ContentFingerprint _fingerprint;

        public CoopSession(NetworkManager manager, ContentFingerprint fingerprint)
        {
            _manager     = manager ?? throw new ArgumentNullException(nameof(manager));
            _fingerprint = fingerprint;

            _manager.OnClientDisconnectCallback += HandleDisconnect;
        }

        /// <summary>Текущее состояние.</summary>
        public CoopSessionState State { get; private set; } = CoopSessionState.Offline;

        /// <summary>Почему кончилась прошлая сессия.</summary>
        public CoopEndReason EndReason { get; private set; } = CoopEndReason.None;

        /// <summary>Текст отказа для экрана. Пусто, если отказа не было.</summary>
        public string EndMessage { get; private set; } = string.Empty;

        /// <summary>Состояние сменилось — экран перерисовывается по этому событию.</summary>
        public event Action<CoopSessionState> StateChanged;

        /// <summary>Поднять хост. Возвращает false, если NGO не стартовал (порт занят, транспорт не настроен).</summary>
        public bool StartHost(ushort port = DefaultPort)
        {
            if (State != CoopSessionState.Offline) return false;

            Configure("0.0.0.0", port, listenAll: true);

            // Одобряет только хост, и обязательно ДО старта: NGO спрашивает колбэк уже на первом
            // подключении, а поставленный позже он для него не существует.
            _manager.NetworkConfig.ConnectionApproval = true;
            _manager.ConnectionApprovalCallback       = Approve;

            if (!_manager.StartHost())
            {
                Fail(CoopEndReason.ConnectionFailed, "Не удалось поднять сессию");
                return false;
            }

            EndReason = CoopEndReason.None;
            EndMessage = string.Empty;
            Set(CoopSessionState.Hosting);
            return true;
        }

        /// <summary>Войти к хосту по адресу.</summary>
        public bool Join(string address, ushort port = DefaultPort)
        {
            if (State != CoopSessionState.Offline) return false;

            Configure(address, port, listenAll: false);

            _manager.NetworkConfig.ConnectionApproval = true;
            _manager.NetworkConfig.ConnectionData     = Encode(_fingerprint);

            if (!_manager.StartClient())
            {
                Fail(CoopEndReason.ConnectionFailed, "Не удалось подключиться");
                return false;
            }

            Set(CoopSessionState.Connecting);
            _manager.OnClientConnectedCallback += HandleConnected;
            return true;
        }

        /// <summary>Выйти самому. У хоста это конец сессии для всех.</summary>
        public void Leave()
        {
            if (State == CoopSessionState.Offline) return;

            EndReason  = CoopEndReason.LocalRequest;
            EndMessage = string.Empty;
            Stop();
        }

        public void Dispose()
        {
            _manager.OnClientDisconnectCallback -= HandleDisconnect;
            _manager.OnClientConnectedCallback  -= HandleConnected;
        }

        // ── рукопожатие ──────────────────────────────────────────────────────────

        private void Approve(NetworkManager.ConnectionApprovalRequest request,
                             NetworkManager.ConnectionApprovalResponse response)
        {
            // Себя хост одобряет без разговоров: свой контент с собой всегда совпадает, а гонять его
            // через кодирование значило бы завести шанс отказать самому себе.
            if (request.ClientNetworkId == _manager.LocalClientId)
            {
                response.Approved = true;
                return;
            }

            if (!TryDecode(request.Payload, out ContentFingerprint theirs))
            {
                response.Approved = false;
                response.Reason   = "Непонятное рукопожатие: другая версия игры";
                return;
            }

            if (!_fingerprint.Matches(theirs))
            {
                response.Approved = false;
                response.Reason   = _fingerprint.DescribeMismatch(theirs);
                return;
            }

            response.Approved       = true;
            response.CreatePlayerObject = false; // игроков-объектов у нас нет: всё едет сообщениями
        }

        private static byte[] Encode(in ContentFingerprint print) =>
            Encoding.UTF8.GetBytes(
                $"{print.ContentHash}|{print.ContentCount}|{print.SchemaVersion}|{print.GameVersion}");

        private static bool TryDecode(byte[] payload, out ContentFingerprint print)
        {
            print = default;
            if (payload == null || payload.Length == 0) return false;

            string[] parts = Encoding.UTF8.GetString(payload).Split('|');
            if (parts.Length != 4) return false;

            if (!ulong.TryParse(parts[0], out ulong hash))   return false;
            if (!int.TryParse(parts[1], out int count))      return false;
            if (!int.TryParse(parts[2], out int schema))     return false;

            print = new ContentFingerprint(hash, count, schema, parts[3]);
            return true;
        }

        // ── жизнь соединения ─────────────────────────────────────────────────────

        private void HandleConnected(ulong clientId)
        {
            if (clientId != _manager.LocalClientId) return;
            Set(CoopSessionState.Connected);
        }

        private void HandleDisconnect(ulong clientId)
        {
            if (_manager.IsServer)
            {
                // У хоста уход гостя сессию не кончает: он остаётся хостом, пусть и в одиночестве.
                return;
            }

            if (clientId != _manager.LocalClientId) return;

            // Причина отказа приходит от хоста строкой; пустая означает обычный разрыв или уход хоста.
            string reason = _manager.DisconnectReason;
            if (!string.IsNullOrEmpty(reason)) Fail(CoopEndReason.Rejected, reason);
            else if (State == CoopSessionState.Connecting) Fail(CoopEndReason.ConnectionFailed, "Хост не ответил");
            else Fail(CoopEndReason.HostLeft, "Хост завершил игру");
        }

        private void Configure(string address, ushort port, bool listenAll)
        {
            var utp = _manager.GetComponent<UnityTransport>();
            if (utp == null)
            {
                Debug.LogError("[CoopSession] на NetworkManager нет UnityTransport — сессию не поднять");
                return;
            }

            // Слушаем на всех интерфейсах у хоста и стучимся по адресу у гостя. Разводить это по двум
            // полям обязательно: слушать по адресу гостя хост не может, а гость по 0.0.0.0 не достучится.
            utp.SetConnectionData(listenAll ? "127.0.0.1" : address, port, listenAll ? "0.0.0.0" : null);
        }

        private void Fail(CoopEndReason reason, string message)
        {
            EndReason  = reason;
            EndMessage = message;
            Stop();
        }

        private void Stop()
        {
            _manager.OnClientConnectedCallback -= HandleConnected;
            if (_manager.IsListening) _manager.Shutdown();
            Set(CoopSessionState.Offline);
        }

        private void Set(CoopSessionState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
