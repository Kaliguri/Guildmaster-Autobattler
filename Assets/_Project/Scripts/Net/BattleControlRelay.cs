using System;
using Guildmaster.Net.Transport;

namespace Guildmaster.Net
{
    /// <summary>
    /// Общая пауза боя: чей угодно интент, решение хоста, одно состояние на всех — и видно, кто нажал.
    /// </summary>
    /// <remarks>
    /// <b>Пауза — свойство ПОКАЗА, а не симуляции.</b> Соло уже устроено так: тумблер владеет
    /// <c>TimeScaleService</c>, а <c>CombatSimulation.SetPaused</c> — другой факт («сим заморожен
    /// сценарием»). В коопе иначе и быть не может: у гостя симуляции нет вовсе, он смотрит ленту.
    /// Поэтому релей не трогает бой — он объявляет состояние, а применяет его тот, кто владеет показом.
    /// <para><b>Чем это отличается от предшественника.</b> Прежний <c>NetworkCommandRelay</c> ставил
    /// команду паузы с будущим номером тика и рассылал её всем, чтобы «все применили на одном тике» —
    /// это lockstep-модель, которую мы отвергли 19.06.2026: клиенты свою симуляцию не тикают, и
    /// применять им нечего. Сама очередь команд симуляции снесена 02.08.2026 — пользователей у неё в
    /// игре не осталось ни одного. Заодно он был <c>NetworkBehaviour</c>, то есть требовал NGO и сцены —
    /// а весь кооп-код у нас обязан жить над <see cref="INetTransport"/> и тестироваться на loopback.</para>
    /// <para><b>Отклик оптимистичный</b> (решение 6 ТЗ кооп-вертикали): нажавший видит паузу сразу, не
    /// дожидаясь полного RTT, а подтверждение хоста перезаписывает состояние. Для паузы это безопасно —
    /// хост в ней не отказывает, — а ощущение «кнопка отвечает» стоит дороже строгости.</para>
    /// </remarks>
    public sealed class BattleControlRelay
    {
        private const byte KindIntent = 0; // гость → хост: «хочу поставить/снять»
        private const byte KindState  = 1; // хост → всем: «вот как есть»

        private readonly INetTransport _transport;
        private readonly byte[]        _payload = new byte[6];

        private byte[] _envelope;

        public BattleControlRelay(INetTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.MessageReceived += HandleMessage;
        }

        /// <summary>Стоит ли бой сейчас.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>Кто поставил текущую паузу. <see cref="NetPeer.NoPeer"/> — никто, бой идёт.</summary>
        public int PausedBy { get; private set; } = NetPeer.NoPeer;

        /// <summary>
        /// Состояние сменилось: пауза и её автор. Подписчик — владелец показа (в игре
        /// <c>TimeScaleService</c> через адаптер, в тесте — сам тест). Событие поднимается и на своё
        /// действие тоже: у паузы один путь применения, а не два.
        /// </summary>
        public event Action<bool, int> PauseChanged;

        /// <summary>Локальный интент игрока: поставить или снять паузу.</summary>
        public void RequestPause(bool paused)
        {
            // Оптимистично применяем у себя в любом случае, а дальше расходятся роли: хост объявляет
            // состояние всем, гость просит хоста об этом.
            Apply(paused, _transport.LocalPeerId);

            if (_transport.IsHost) BroadcastState();
            else                   Send(NetPeer.HostPeerId, KindIntent, paused, _transport.LocalPeerId);
        }

        /// <summary>Новый бой: пауза не переносится через границу боя.</summary>
        public void Reset()
        {
            IsPaused = false;
            PausedBy = NetPeer.NoPeer;
        }

        /// <summary>Отписаться от транспорта.</summary>
        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.BattleControl || payload.Count < 6) return;

            byte kind   = payload.Array[payload.Offset];
            bool paused = payload.Array[payload.Offset + 1] != 0;
            int  author = BitConverter.ToInt32(payload.Array, payload.Offset + 2);

            if (kind == KindIntent)
            {
                // Интент принимает только хост: у гостя нет права решать за сессию, и молча применить
                // чужую просьбу значило бы завести второй источник правды о состоянии боя.
                if (!_transport.IsHost) return;

                // Автор — тот, кто прислал, а не тот, кого он назвал: иначе подмена автора стоила бы
                // одного байта, а «видно, кто нажал» — дизайн-требование, а не украшение.
                if (Apply(paused, from)) BroadcastState();
                return;
            }

            // Состояние объявляет только хост. Оно перезаписывает оптимистичный локальный отклик —
            // в том числе автора: нажали двое, победил тот, чей интент дошёл первым.
            if (from != NetPeer.HostPeerId) return;
            Apply(paused, author);
        }

        private bool Apply(bool paused, int author)
        {
            int nextAuthor = paused ? author : NetPeer.NoPeer;
            if (IsPaused == paused && PausedBy == nextAuthor) return false;

            IsPaused = paused;
            PausedBy = nextAuthor;
            PauseChanged?.Invoke(IsPaused, PausedBy);
            return true;
        }

        private void BroadcastState()
        {
            Write(KindState, IsPaused, PausedBy);
            _transport.SendToAll(
                NetEnvelope.Wrap(NetChannel.BattleControl, new ArraySegment<byte>(_payload), ref _envelope),
                NetDelivery.Reliable);
        }

        private void Send(int peer, byte kind, bool paused, int author)
        {
            Write(kind, paused, author);
            _transport.Send(peer,
                NetEnvelope.Wrap(NetChannel.BattleControl, new ArraySegment<byte>(_payload), ref _envelope),
                NetDelivery.Reliable);
        }

        private void Write(byte kind, bool paused, int author)
        {
            _payload[0] = kind;
            _payload[1] = (byte)(paused ? 1 : 0);
            BitConverter.GetBytes(author).CopyTo(_payload, 2);
        }
    }
}
