using System;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace Guildmaster.Net.Transport
{
    /// <summary>
    /// Транспорт поверх Steam Networking Sockets: хост поднимает relay-сокет, гость подключается к нему
    /// по SteamId хозяина лобби. Никакого высокоуровневого netcode между нами и Steam нет.
    /// </summary>
    /// <remarks>
    /// <b>Почему напрямую, без NGO/Mirror/FishNet</b> (решение Макса 02.08.2026). Высокоуровневые
    /// библиотеки продают репликацию сетевых объектов, RPC и спавн — у нас нет ни одного сетевого
    /// объекта и не планируется: бой раздаётся лентой чанками, состояние забега — логом команд,
    /// присутствие — своим пакетом. Оставалось бы платить версионной зависимостью за перенос байтов и
    /// одобрение подключения, а и то, и другое у нас уже написано (конверт с каналом, отпечаток
    /// контента).
    /// <para><b>Relay, а не прямое соединение.</b> `CreateRelaySocket`/`ConnectRelay` идут через Steam
    /// Datagram Relay: IP игроков не раскрывается, NAT проходится, а трафик едет по бэкбону Valve.
    /// Для P2P это бесплатно любому партнёру Steam — договариваться нужно только про SDR для
    /// выделенных серверов, которых у нас нет.</para>
    /// <para><b>Свой id гость узнаёт не отсюда.</b> Транспорт переносит байты и не знает, что такое
    /// рукопожатие; номер пира гостю назначает хост первым сообщением сессии и кладёт его сюда через
    /// <see cref="SetLocalPeerId"/>. Иначе транспорт пришлось бы учить протоколу, который живёт выше.</para>
    /// </remarks>
    public sealed class SteamNetTransport : INetTransport, IDisposable
    {
        /// <summary>Виртуальный порт Steam-сокета. Одно приложение — один порт, разводить нечего.</summary>
        public const int VirtualPort = 0;

        /// <summary>
        /// Предел надёжного сообщения у Steam (<c>k_cbMaxSteamNetworkingSocketsMessageSizeSend</c>).
        /// Сверх него отправка возвращает отказ, поэтому размер проверяет наш код — ниже нас его не
        /// проверяет никто.
        /// </summary>
        public const int MaxMessageBytes = 512 * 1024;

        private readonly Queue<Incoming>          _inbox   = new Queue<Incoming>();
        private readonly Dictionary<uint, int>    _peerByConnection = new Dictionary<uint, int>();
        private readonly Dictionary<int, Connection> _connectionByPeer = new Dictionary<int, Connection>();

        private HostSocket      _socket;   // мы хост
        private GuestConnection _client;   // мы гость
        private int             _nextPeerId = NetPeer.HostPeerId + 1;

        /// <summary>
        /// Номер живого подключения. Растёт на каждом <see cref="Shutdown"/>, и каждый сокет запоминает
        /// тот, при котором родился.
        /// </summary>
        /// <remarks>
        /// <b>Steam досказывает за закрытым соединением.</b> Колбэки приходят не тогда, когда мы закрыли
        /// сокет, а на ближайшем <c>RunCallbacks</c> — то есть уже после того, как на его месте подняли
        /// новое. Без поколения такой хвост неотличим от настоящего события: он попадает в очередь и
        /// выходит наружу как факт про ЖИВУЮ сессию.
        /// <para>Стоило это двух симптомов сразу (прогон вдвоём 08.08.2026). Гость, поднявший свой
        /// хостинг, получал «пир 0 отключился» от прошлого соединения — а ноль в новой роли означает его
        /// самого, и состав сеанса вычёркивал хозяина: «нас нет в составе сеанса (наш номер 0,
        /// участников 1)». Второй раз тот же хвост убивал подключение к чужому лобби: <c>CoopSession</c>
        /// видел разрыв в состоянии <c>Connecting</c> и валил новую сессию с текстом «Хост не ответил»,
        /// хотя хост отвечал исправно.</para>
        /// <para><b>Почему не хватило снять <c>Owner</c></b> у закрываемого сокета: это лечит только тот
        /// случай, когда объект соединения новый. Поколение отвечает на вопрос «из какой сессии это
        /// событие», а он и есть настоящий, — и отвечает одинаково для всех трёх видов событий.</para>
        /// </remarks>
        private int _generation;

        private readonly struct Incoming
        {
            public readonly int    From;
            public readonly byte[] Payload;   // null = событие состава
            public readonly bool   Connected;

            public Incoming(int from, byte[] payload, bool connected)
            {
                From      = from;
                Payload   = payload;
                Connected = connected;
            }
        }

        public bool IsRunning => _socket != null || _client != null;
        public bool IsHost    => _socket != null;

        // Соединение гостя подтверждено хостом. Само наличие _client значит лишь «мы постучались»:
        // Steam ведёт соединение через relay, и между ConnectRelay и OnConnected проходит время.
        private bool _clientConnected;

        // Буфер ответа на «кто уже подключён»: пересобирается на месте, наружу отдаётся только
        // для чтения — список участников спрашивают на старте сеанса, а не в тике.
        private readonly List<int> _peers = new List<int>(4);

        public IReadOnlyList<int> ConnectedPeers
        {
            get
            {
                _peers.Clear();

                if (IsHost)
                {
                    foreach (KeyValuePair<int, Connection> pair in _connectionByPeer) _peers.Add(pair.Key);
                }
                else if (_clientConnected)
                {
                    _peers.Add(NetPeer.HostPeerId);
                }

                return _peers;
            }
        }

        /// <summary>Наш номер в сессии. У хоста — ноль; гостю его сообщает хост при рукопожатии.</summary>
        public int LocalPeerId { get; private set; } = NetPeer.NoPeer;

        public int MaxReliableMessageBytes => MaxMessageBytes;

        public event Action<int> PeerConnected;
        public event Action<int> PeerDisconnected;
        public event Action<int, ArraySegment<byte>> MessageReceived;

        /// <summary>
        /// Поднять relay-сокет. Возвращает false, если Steam не запущен — это внешний отказ, и он должен
        /// быть виден игроку, а не заглажен.
        /// </summary>
        public bool StartHost()
        {
            if (IsRunning) return false;
            if (!SteamClient.IsValid)
            {
                Debug.LogError("[SteamNetTransport] Steam не запущен — сессию не поднять");
                return false;
            }

            // Доступ к релею запрашивается заранее: первое соединение иначе ждёт, пока Steam выберет
            // маршрут, и выглядит это как «подключение висит».
            SteamNetworkingUtils.InitRelayNetworkAccess();

            _socket = SteamNetworkingSockets.CreateRelaySocket<HostSocket>(VirtualPort);
            if (_socket == null)
            {
                Debug.LogError("[SteamNetTransport] Steam не создал relay-сокет");
                return false;
            }

            _socket.Owner      = this;
            _socket.Generation = _generation;
            LocalPeerId        = NetPeer.HostPeerId;
            return true;
        }

        /// <summary>Подключиться к хозяину лобби по его SteamId.</summary>
        public bool Connect(ulong hostSteamId)
        {
            if (IsRunning) return false;
            if (!SteamClient.IsValid)
            {
                Debug.LogError("[SteamNetTransport] Steam не запущен — подключаться нечем");
                return false;
            }

            SteamNetworkingUtils.InitRelayNetworkAccess();

            _client = SteamNetworkingSockets.ConnectRelay<GuestConnection>(hostSteamId, VirtualPort);
            if (_client == null)
            {
                Debug.LogError("[SteamNetTransport] Steam не открыл соединение к хосту");
                return false;
            }

            _client.Owner      = this;
            _client.Generation = _generation;
            return true;
        }

        /// <summary>
        /// Принять свой номер от хоста. Зовётся сессией по рукопожатию — до него гость своего номера не
        /// знает и знать не может.
        /// </summary>
        public void SetLocalPeerId(int peerId) => LocalPeerId = peerId;

        public void Send(int peerId, ArraySegment<byte> payload, NetDelivery delivery)
        {
            if (!IsRunning) return;
            Guard(payload, delivery);

            if (IsHost)
            {
                if (_connectionByPeer.TryGetValue(peerId, out Connection connection))
                    connection.SendMessage(payload.Array, payload.Offset, payload.Count, Kind(delivery));
                return;
            }

            // У гостя собеседник ровно один — хост; кому бы он ни адресовал, дорога одна.
            _client.Connection.SendMessage(payload.Array, payload.Offset, payload.Count, Kind(delivery));
        }

        public void SendToAll(ArraySegment<byte> payload, NetDelivery delivery)
        {
            if (!IsRunning) return;
            Guard(payload, delivery);

            if (!IsHost)
            {
                _client.Connection.SendMessage(payload.Array, payload.Offset, payload.Count, Kind(delivery));
                return;
            }

            foreach (KeyValuePair<int, Connection> pair in _connectionByPeer)
                pair.Value.SendMessage(payload.Array, payload.Offset, payload.Count, Kind(delivery));
        }

        /// <summary>
        /// Прокачать Steam и раздать пришедшее. Приём и доставка наружу разведены сознательно: контракт
        /// шва обещает, что события случаются только здесь, и на этом обещании держится воспроизводимость
        /// тестов.
        /// </summary>
        public void Poll()
        {
            _socket?.Receive();
            _client?.Receive();

            while (_inbox.Count > 0)
            {
                Incoming item = _inbox.Dequeue();

                if (item.Payload == null)
                {
                    if (item.Connected) PeerConnected?.Invoke(item.From);
                    else                PeerDisconnected?.Invoke(item.From);
                    continue;
                }

                MessageReceived?.Invoke(item.From, new ArraySegment<byte>(item.Payload));
            }
        }

        public void Shutdown()
        {
            // Поколение растёт ПЕРВЫМ: Steam может дёрнуть OnDisconnected прямо из Close, и событие о
            // закрытии уже не должно считаться событием живой сессии.
            _generation++;

            _socket?.Close();
            _client?.Close();
            _socket = null;
            _client = null;

            _peerByConnection.Clear();
            _connectionByPeer.Clear();
            _inbox.Clear();
            _nextPeerId      = NetPeer.HostPeerId + 1;
            LocalPeerId      = NetPeer.NoPeer;
            _clientConnected = false;
        }

        public void Dispose() => Shutdown();

        // Надёжное едет Reliable — Steam фрагментирует его сам; присутствие идёт Unreliable и обязано
        // влезать в MTU (фрагментированное ненадёжное теряется целиком).
        private static SendType Kind(NetDelivery delivery) =>
            delivery == NetDelivery.Reliable ? SendType.Reliable : SendType.Unreliable;

        private void Guard(ArraySegment<byte> payload, NetDelivery delivery)
        {
            if (delivery == NetDelivery.Reliable && payload.Count > MaxMessageBytes)
                throw new ArgumentOutOfRangeException(nameof(payload),
                    $"сообщение {payload.Count} Б больше предела Steam ({MaxMessageBytes} Б): " +
                    "сверх него отправка отказывает, и отказ этот никто не читает");
        }

        // ── события сокета ───────────────────────────────────────────────────────

        /// <summary>
        /// Событие пришло от той сессии, что живёт сейчас? Хвост закрытого сокета сюда доезжает
        /// исправно — см. <see cref="_generation"/>.
        /// </summary>
        private bool IsStale(int generation) => generation != _generation;

        private void HandleGuestConnected(int generation, Connection connection)
        {
            if (IsStale(generation)) return;

            int peerId = _nextPeerId++;
            _peerByConnection[connection.Id] = peerId;
            _connectionByPeer[peerId]        = connection;
            _inbox.Enqueue(new Incoming(peerId, null, connected: true));
        }

        private void HandleGuestDisconnected(int generation, Connection connection)
        {
            if (IsStale(generation)) return;
            if (!_peerByConnection.TryGetValue(connection.Id, out int peerId)) return;

            _peerByConnection.Remove(connection.Id);
            _connectionByPeer.Remove(peerId);
            _inbox.Enqueue(new Incoming(peerId, null, connected: false));
        }

        private void HandleHostConnected(int generation)
        {
            if (IsStale(generation)) return;

            _clientConnected = true;
            _inbox.Enqueue(new Incoming(NetPeer.HostPeerId, null, connected: true));
        }

        private void HandleHostDisconnected(int generation)
        {
            if (IsStale(generation)) return;

            _clientConnected = false;
            _inbox.Enqueue(new Incoming(NetPeer.HostPeerId, null, connected: false));
        }

        // Буфер Steam живёт только внутри колбэка, поэтому копируем: без копии подписчик прочитал бы
        // уже перезаписанную память — баг, который выглядит как порча данных в сети.
        private void HandleMessage(int generation, int from, IntPtr data, int size)
        {
            if (IsStale(generation)) return;
            if (size <= 0) return;

            var bytes = new byte[size];
            System.Runtime.InteropServices.Marshal.Copy(data, bytes, 0, size);
            _inbox.Enqueue(new Incoming(from, bytes, connected: false));
        }

        private int PeerOf(Connection connection) =>
            _peerByConnection.TryGetValue(connection.Id, out int peerId) ? peerId : NetPeer.NoPeer;

        /// <summary>Сокет хоста: принимает подключения и раскладывает сообщения по номерам пиров.</summary>
        private sealed class HostSocket : SocketManager
        {
            public SteamNetTransport Owner;

            /// <summary>Поколение, при котором сокет подняли. Хвост от прошлого сюда и приходит.</summary>
            public int Generation;

            // Принимаем всех: кто здесь чужой, решает рукопожатие уровнем выше — оно знает про версию
            // сборки и отпечаток контента, а сокет про них не знает ничего.
            public override void OnConnecting(Connection connection, ConnectionInfo info)
            {
                base.OnConnecting(connection, info);
                connection.Accept();
            }

            public override void OnConnected(Connection connection, ConnectionInfo info)
            {
                base.OnConnected(connection, info);
                Owner?.HandleGuestConnected(Generation, connection);
            }

            public override void OnDisconnected(Connection connection, ConnectionInfo info)
            {
                base.OnDisconnected(connection, info);
                Owner?.HandleGuestDisconnected(Generation, connection);
            }

            public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size,
                                           long messageNum, long recvTime, int channel)
            {
                base.OnMessage(connection, identity, data, size, messageNum, recvTime, channel);
                if (Owner == null) return;

                int peerId = Owner.PeerOf(connection);
                if (peerId != NetPeer.NoPeer) Owner.HandleMessage(Generation, peerId, data, size);
            }
        }

        /// <summary>Соединение гостя: единственный собеседник — хост.</summary>
        private sealed class GuestConnection : ConnectionManager
        {
            public SteamNetTransport Owner;

            /// <summary>Поколение, при котором соединение открыли. См. <see cref="_generation"/>.</summary>
            public int Generation;

            public override void OnConnected(ConnectionInfo info)
            {
                base.OnConnected(info);
                Owner?.HandleHostConnected(Generation);
            }

            public override void OnDisconnected(ConnectionInfo info)
            {
                base.OnDisconnected(info);
                Owner?.HandleHostDisconnected(Generation);
            }

            public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
            {
                base.OnMessage(data, size, messageNum, recvTime, channel);
                Owner?.HandleMessage(Generation, NetPeer.HostPeerId, data, size);
            }
        }
    }
}
