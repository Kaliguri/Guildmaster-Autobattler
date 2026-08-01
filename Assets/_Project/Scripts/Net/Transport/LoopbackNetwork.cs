using System;
using System.Collections.Generic;

namespace Guildmaster.Net.Transport
{
    /// <summary>
    /// Сеть из нескольких узлов в ОДНОМ процессе, без NGO, сокетов и сцен: хост и гости обмениваются
    /// байтами через очереди, доставка случается только в <see cref="INetTransport.Poll"/>.
    /// <para><b>Это главный инструмент отладки коопа, а не заглушка для тестов.</b> Внешние
    /// свидетельства называют самой дорогой частью мультиплеера не синхронизацию геймплея, а сессию и
    /// отладку — «поднять два инстанса и воспроизвести баг в одиночку занимает вечность». Здесь два
    /// клиента живут в одном EditMode-тесте, и баг воспроизводится вызовом метода.</para>
    /// </summary>
    /// <remarks>
    /// Порядок доставки строго FIFO на пару «отправитель → получатель», и это ЕДИНСТВЕННОЕ обещание
    /// loopback'а. Задержку, потерю, дубли и переупорядочивание добавляет <see cref="ChaosTransport"/> —
    /// иначе идеальный канал прятал бы ровно те баги, ради которых шов и делался.
    /// </remarks>
    public sealed class LoopbackNetwork
    {
        private readonly Dictionary<int, Node> _nodes = new Dictionary<int, Node>();
        private int _nextPeerId = NetPeer.HostPeerId;

        /// <summary>Создать узел. Первый созданный становится хостом.</summary>
        public INetTransport CreateNode()
        {
            int id = _nextPeerId++;
            var node = new Node(this, id, isHost: id == NetPeer.HostPeerId);
            _nodes.Add(id, node);

            // Соединение объявляется обеим сторонам, как в настоящем транспорте: новый узел узнаёт про
            // всех, кто уже есть, а они — про него. Иначе гость, подключившийся вторым, не увидел бы
            // первого, и «кто в сессии» зависело бы от порядка входа.
            foreach (KeyValuePair<int, Node> pair in _nodes)
            {
                if (pair.Key == id) continue;
                pair.Value.EnqueueConnect(id);
                node.EnqueueConnect(pair.Key);
            }

            return node;
        }

        /// <summary>Прокачать все узлы разом — типичный шаг теста «прошёл кадр у всех».</summary>
        public void PollAll()
        {
            // Копия списка: обработчик события вправе создать узел или уронить соединение.
            var snapshot = new List<Node>(_nodes.Count);
            foreach (Node node in _nodes.Values) snapshot.Add(node);
            for (int i = 0; i < snapshot.Count; i++) snapshot[i].Poll();
        }

        private void Deliver(int from, int to, byte[] payload)
        {
            if (_nodes.TryGetValue(to, out Node node)) node.EnqueueMessage(from, payload);
        }

        private void DeliverToAll(int from, byte[] payload)
        {
            foreach (KeyValuePair<int, Node> pair in _nodes)
            {
                if (pair.Key == from) continue;
                pair.Value.EnqueueMessage(from, payload);
            }
        }

        private void Drop(int id)
        {
            if (!_nodes.Remove(id)) return;
            foreach (Node node in _nodes.Values) node.EnqueueDisconnect(id);
        }

        private sealed class Node : INetTransport
        {
            // Что пришло: либо сообщение, либо смена состава. Одна очередь на всё, потому что порядок
            // между «пир ушёл» и «его последнее сообщение» — это тоже поведение сети, и разводить их по
            // двум очередям значило бы решить за неё, что случилось раньше.
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

            private readonly LoopbackNetwork _net;
            private readonly Queue<Incoming> _inbox = new Queue<Incoming>();

            private bool _running = true;

            public Node(LoopbackNetwork net, int peerId, bool isHost)
            {
                _net        = net;
                LocalPeerId = peerId;
                IsHost      = isHost;
            }

            public bool IsRunning  => _running;
            public int  LocalPeerId { get; }
            public bool IsHost      { get; }

            /// <summary>
            /// Тот же предел, что у релизного Steam-транспорта (512 КБ на надёжное сообщение). Loopback
            /// технически вынес бы любой размер — и именно поэтому обязан врать одинаково с релизом:
            /// шов, где в тестах проходит то, что в игре уезжает в тишину, хуже отсутствующего.
            /// </summary>
            public int MaxReliableMessageBytes => 512 * 1024;

            public event Action<int> PeerConnected;
            public event Action<int> PeerDisconnected;
            public event Action<int, ArraySegment<byte>> MessageReceived;

            public void Send(int peerId, ArraySegment<byte> payload, NetDelivery delivery)
            {
                if (!_running) return;
                Guard(payload, delivery);
                _net.Deliver(LocalPeerId, peerId, Copy(payload));
            }

            public void SendToAll(ArraySegment<byte> payload, NetDelivery delivery)
            {
                if (!_running) return;
                Guard(payload, delivery);
                _net.DeliverToAll(LocalPeerId, Copy(payload));
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
                if (!_running) return;
                _running = false;
                _net.Drop(LocalPeerId);
            }

            public void EnqueueMessage(int from, byte[] payload) =>
                _inbox.Enqueue(new Incoming(from, payload, connected: false));

            public void EnqueueConnect(int peer) =>
                _inbox.Enqueue(new Incoming(peer, null, connected: true));

            public void EnqueueDisconnect(int peer) =>
                _inbox.Enqueue(new Incoming(peer, null, connected: false));

            // Превышение предела — громкий отказ, а не молчаливая потеря: на релизе это сообщение
            // проглотил бы Steam, и искать причину пришлось бы по отсутствию картинки у гостя.
            private void Guard(ArraySegment<byte> payload, NetDelivery delivery)
            {
                if (delivery == NetDelivery.Reliable && payload.Count > MaxReliableMessageBytes)
                    throw new ArgumentOutOfRangeException(nameof(payload),
                        $"сообщение {payload.Count} Б больше предела надёжной посылки " +
                        $"({MaxReliableMessageBytes} Б): чанк обязан резаться до отправки");
            }

            // Копия обязательна: у отправителя буфер переиспользуемый (FastBufferWriter), и без копии
            // получатель прочитал бы уже перезаписанные байты — баг, который выглядит как порча данных
            // в сети и ищется неделю.
            private static byte[] Copy(ArraySegment<byte> payload)
            {
                var bytes = new byte[payload.Count];
                Array.Copy(payload.Array, payload.Offset, bytes, 0, payload.Count);
                return bytes;
            }
        }
    }
}
