using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace Guildmaster.Net.Transport
{
    /// <summary>
    /// Наш шов транспорта поверх Netcode for GameObjects: байты едут именованными сообщениями, а
    /// доставка наружу случается только в <see cref="Poll"/>.
    /// </summary>
    /// <remarks>
    /// <b>Одно имя сообщения на всё.</b> NGO различает потоки по строковому имени, но у нас поверх уже
    /// есть свой канал в первом байте (<see cref="NetEnvelope"/>). Второй способ разводить потоки
    /// означал бы два владельца одного правила и рассинхрон при добавлении канала.
    /// <para><b>Почему очередь, а не прямой проброс.</b> NGO поднимает колбэк, когда ему удобно — внутри
    /// своего апдейта. Контракт <see cref="INetTransport"/> обещает другое: события приходят только в
    /// <c>Poll</c>. Обещание не косметическое — на нём держится воспроизводимость тестов, где два узла
    /// живут в одном процессе и шагают вызовом метода.</para>
    /// <para><b>Буфер читателя живёт только внутри колбэка</b>, поэтому байты копируются на приёме. Без
    /// копии подписчик прочитал бы уже перезаписанную память — баг, который выглядит как порча данных в
    /// сети и ищется неделями.</para>
    /// <para><b>Себе не шлём.</b> <c>SendNamedMessageToAll</c> у хоста включает и его собственного
    /// клиента, а наш контракт — «всем, кроме себя»; иначе хост получал бы эхо каждого своего чанка.</para>
    /// </remarks>
    public sealed class NgoTransport : INetTransport, IDisposable
    {
        /// <summary>Имя именованного сообщения. Одно на все каналы — канал внутри конверта.</summary>
        public const string MessageName = "gm";

        private readonly NetworkManager _manager;

        private readonly Queue<Incoming> _inbox = new Queue<Incoming>();
        private readonly List<ulong>     _targets = new List<ulong>(8);

        private bool _handlerRegistered;
        private int  _fragmentLimit;

        private readonly struct Incoming
        {
            public readonly int    From;
            public readonly byte[] Payload;    // null = событие состава
            public readonly bool   Connected;

            public Incoming(int from, byte[] payload, bool connected)
            {
                From      = from;
                Payload   = payload;
                Connected = connected;
            }
        }

        public NgoTransport(NetworkManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));

            _manager.OnClientConnectedCallback  += HandleConnected;
            _manager.OnClientDisconnectCallback += HandleDisconnected;
            _manager.OnServerStarted            += RegisterHandler;
            _manager.OnClientStarted            += RegisterHandler;

            if (_manager.IsListening) RegisterHandler();
        }

        public bool IsRunning   => _manager != null && _manager.IsListening;
        public int  LocalPeerId => _manager != null ? (int)_manager.LocalClientId : NetPeer.NoPeer;
        public bool IsHost      => _manager != null && _manager.IsServer;

        /// <summary>
        /// Предел надёжного сообщения берётся у NGO (<c>MaximumFragmentedMessageSize</c>), а не
        /// назначается нами: он зависит от транспорта и настроек проекта, и «примерно знать» его нельзя —
        /// сверх предела сообщение не уезжает.
        /// </summary>
        /// <remarks>
        /// <b>Готча:</b> до старта сети это свойство NGO бросает <c>NullReferenceException</c> — оно
        /// спрашивает менеджер сообщений, а тот создаётся вместе с сессией. Поэтому значение снимается
        /// один раз при старте, а до него отдаётся предел ненадёжной посылки: он заведомо проходит, и
        /// отправлять в это время всё равно некому.
        /// </remarks>
        public int MaxReliableMessageBytes => IsRunning && _fragmentLimit > 0
            ? _fragmentLimit
            : SafeLimitBeforeStart;

        /// <summary>Предел, которым пользуемся, пока сеть не поднята: MTU-безопасная посылка UTP.</summary>
        private const int SafeLimitBeforeStart = 1200;

        public event Action<int> PeerConnected;
        public event Action<int> PeerDisconnected;
        public event Action<int, ArraySegment<byte>> MessageReceived;

        public void Send(int peerId, ArraySegment<byte> payload, NetDelivery delivery)
        {
            if (!IsRunning) return;

            using var writer = new FastBufferWriter(payload.Count, Allocator.Temp);
            writer.WriteBytesSafe(payload.Array, payload.Count, payload.Offset);
            _manager.CustomMessagingManager.SendNamedMessage(
                MessageName, (ulong)peerId, writer, Deliver(delivery));
        }

        public void SendToAll(ArraySegment<byte> payload, NetDelivery delivery)
        {
            if (!IsRunning) return;

            _targets.Clear();
            if (IsHost)
            {
                IReadOnlyList<ulong> ids = _manager.ConnectedClientsIds;
                for (int i = 0; i < ids.Count; i++)
                    if (ids[i] != _manager.LocalClientId) _targets.Add(ids[i]);
            }
            else
            {
                // Гость знает ровно одного собеседника: сервер. Остальные для него — за хостом.
                _targets.Add(NetworkManager.ServerClientId);
            }

            if (_targets.Count == 0) return;

            using var writer = new FastBufferWriter(payload.Count, Allocator.Temp);
            writer.WriteBytesSafe(payload.Array, payload.Count, payload.Offset);
            _manager.CustomMessagingManager.SendNamedMessage(
                MessageName, _targets, writer, Deliver(delivery));
        }

        public void Poll()
        {
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
            UnregisterHandler();
            if (_manager != null && _manager.IsListening) _manager.Shutdown();
            _inbox.Clear();
        }

        public void Dispose()
        {
            UnregisterHandler();

            if (_manager == null) return;
            _manager.OnClientConnectedCallback  -= HandleConnected;
            _manager.OnClientDisconnectCallback -= HandleDisconnected;
            _manager.OnServerStarted            -= RegisterHandler;
            _manager.OnClientStarted            -= RegisterHandler;
        }

        // Надёжное едет ФРАГМЕНТИРУЕМЫМ: чанк ленты заведомо больше MTU, а нефрагментируемое надёжное
        // сверх MTU транспорт просто не отправит.
        private static NetworkDelivery Deliver(NetDelivery delivery) => delivery == NetDelivery.Reliable
            ? NetworkDelivery.ReliableFragmentedSequenced
            : NetworkDelivery.Unreliable;

        private void RegisterHandler()
        {
            if (_handlerRegistered || _manager?.CustomMessagingManager == null) return;

            _manager.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, HandleNamedMessage);
            _handlerRegistered = true;

            // Снимаем предел ровно здесь: сеть уже поднята, значит менеджер сообщений существует и
            // свойство отвечает вместо того, чтобы бросать.
            _fragmentLimit = _manager.MaximumFragmentedMessageSize;
        }

        private void UnregisterHandler()
        {
            if (!_handlerRegistered || _manager?.CustomMessagingManager == null) return;

            _manager.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
            _handlerRegistered = false;
        }

        private void HandleNamedMessage(ulong sender, FastBufferReader payload)
        {
            int length = payload.Length - payload.Position;
            if (length <= 0) return;

            var bytes = new byte[length];
            payload.ReadBytesSafe(ref bytes, length);
            _inbox.Enqueue(new Incoming((int)sender, bytes, connected: false));
        }

        // Своё подключение событием пира не считаем: «пир пришёл» — это про собеседника, а про себя
        // отвечает IsRunning. Иначе гость сообщил бы сам о себе, и подписчики завели бы себе двойника.
        private void HandleConnected(ulong clientId)
        {
            if (clientId == _manager.LocalClientId) return;
            _inbox.Enqueue(new Incoming((int)clientId, null, connected: true));
        }

        private void HandleDisconnected(ulong clientId)
        {
            if (clientId == _manager.LocalClientId && !IsHost)
            {
                // Гостя отключили от хоста: собеседник, которого он потерял, — хост.
                _inbox.Enqueue(new Incoming(NetPeer.HostPeerId, null, connected: false));
                return;
            }

            if (clientId == _manager.LocalClientId) return;
            _inbox.Enqueue(new Incoming((int)clientId, null, connected: false));
        }
    }
}
