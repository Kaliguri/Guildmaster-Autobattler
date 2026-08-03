using System;
using System.Collections.Generic;
using Guildmaster.Combat.Tape;
using Guildmaster.Net.Transport;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Хостовая раздача боевой ленты: режет готовые тики на чанки, шлёт их гостям и повторяет
    /// потерянные по запросу.
    /// </summary>
    /// <remarks>
    /// <b>Раздача идёт потоком, а не «весь бой перед показом».</b> Окно снимков в ленте — двенадцать
    /// секунд; сим, уехавший до конца боя, вытеснил бы начало, и раздавать было бы нечего. Поэтому сим
    /// держит обычный лаг показа, а чанки уезжают по мере готовности: длина боя перестаёт значить
    /// что-либо, битрейт постоянный, память гостя не растёт.
    /// <para><b>Готовность решает вызывающий.</b> Стример не спрашивает симуляцию про текущий тик — ему
    /// передают «досчитано включительно по такой-то тик». Так он тестируется без симуляции вовсе, а
    /// правило «не раздавать то, что игрок ещё не должен видеть» остаётся у того, кто его знает.</para>
    /// <para><b>История отправленного нужна для повтора.</b> Чанк самодостаточен, поэтому повтор — это
    /// буквально те же байты; кольцо на <see cref="DefaultHistoryChunks"/> чанков покрывает разрыв
    /// заметно длиннее окна снимков, а что старше — доедет опорным снимком, когда он появится.</para>
    /// </remarks>
    public sealed class TapeStreamer : IDisposable
    {
        /// <summary>Сколько отправленных чанков держим на случай запроса повтора.</summary>
        public const int DefaultHistoryChunks = 32;

        private readonly INetTransport   _transport;
        private readonly BattleTape      _tape;
        private readonly TapeChunkWriter _writer = new TapeChunkWriter();
        private readonly int             _ticksPerChunk;
        private readonly int             _historyChunks;

        // Номер чанка → его байты. Копия обязательна: писатель отдаёт переиспользуемый буфер.
        private readonly Dictionary<int, byte[]> _history = new Dictionary<int, byte[]>();
        private readonly Queue<int>              _historyOrder = new Queue<int>();

        private byte[] _envelope;
        private int    _nextTick;

        public TapeStreamer(INetTransport transport, BattleTape tape,
                            int ticksPerChunk = TapeChunkFormat.DefaultTicksPerChunk,
                            int historyChunks = DefaultHistoryChunks)
        {
            _transport     = transport ?? throw new ArgumentNullException(nameof(transport));
            _tape          = tape      ?? throw new ArgumentNullException(nameof(tape));
            _ticksPerChunk = ticksPerChunk > 0 && ticksPerChunk <= 255
                ? ticksPerChunk
                : throw new ArgumentOutOfRangeException(nameof(ticksPerChunk), "смещение тика в чанке едет байтом: 1..255");
            _historyChunks = Math.Max(1, historyChunks);

            _transport.MessageReceived += HandleMessage;
        }

        /// <summary>Первый тик, который ещё не уехал.</summary>
        public int NextTick => _nextTick;

        /// <summary>Сколько чанков отправлено с начала боя (повторы не считаются).</summary>
        public int SentChunkCount { get; private set; }

        /// <summary>Сколько повторов отдано по запросу — метрика качества канала, видна в dev-панели.</summary>
        public int ResentChunkCount { get; private set; }

        /// <summary>
        /// Отправить всё, что уже досчитано целыми чанками. Хвост короче
        /// <see cref="_ticksPerChunk"/> остаётся ждать: неполный чанк уезжает только через
        /// <see cref="Flush"/>, иначе конец боя дробился бы на однотиковые посылки.
        /// </summary>
        /// <param name="readyThroughTick">Последний тик, который гостям уже можно видеть, включительно.</param>
        public void Pump(int readyThroughTick)
        {
            while (readyThroughTick - _nextTick + 1 >= _ticksPerChunk)
                SendChunk(_nextTick, _ticksPerChunk);
        }

        /// <summary>
        /// Дослать хвост неполным чанком — конец боя и любой момент, после которого продолжения не
        /// будет. Без него последние тики боя (исход в их числе) остались бы у хоста.
        /// </summary>
        public void Flush(int readyThroughTick)
        {
            Pump(readyThroughTick);

            int rest = readyThroughTick - _nextTick + 1;
            if (rest > 0) SendChunk(_nextTick, rest);
        }

        /// <summary>Новый бой: нумерация тиков и чанков начинается заново, история сбрасывается.</summary>
        public void Reset()
        {
            _nextTick = 0;
            _history.Clear();
            _historyOrder.Clear();
            SentChunkCount   = 0;
            ResentChunkCount = 0;
        }

        /// <summary>Отписаться от транспорта. Зовётся вместе с концом сессии.</summary>
        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void SendChunk(int firstTick, int tickCount)
        {
            int number = _writer.NextChunkNumber;
            ArraySegment<byte> bytes = _writer.Write(_tape, firstTick, tickCount);

            // Пустой срез — это не ошибка: в диапазоне не оказалось ни одного записанного кадра
            // (бой ещё не начинался, лента чистилась). Номер чанка при этом НЕ тратится — писатель
            // увеличивает его только когда пишет, — поэтому у гостя не появляется вечная дыра.
            if (bytes.Count == 0)
            {
                _nextTick = firstTick + tickCount;
                return;
            }

            // Предел спрашиваем у ТРАНСПОРТА, а не берём из своей константы: у Steam это 512 КБ, у UTP —
            // MaximumFragmentedMessageSize, и он заметно меньше нашего потолка чанка. Сообщение сверх
            // предела Steam роняет молча (транспорт не читает его отказ), так что проверить обязаны мы.
            int limit = _transport.MaxReliableMessageBytes - NetEnvelope.HeaderBytes;
            if (bytes.Count > limit)
            {
                _writer.DiscardLast();

                // Один тик, который не влезает, — это уже не вопрос нарезки: на арене столько юнитов и
                // событий, что кадр не пролезает в сеть целиком. Отказ громкий, потому что тихо здесь
                // означало бы «у гостя просто нет куска боя».
                if (tickCount <= 1)
                    throw new InvalidOperationException(
                        $"кадр тика {firstTick} весит {bytes.Count} Б при пределе {limit} Б — " +
                        "делить дальше нечего");

                int half = tickCount / 2;
                SendChunk(firstTick, half);
                SendChunk(firstTick + half, tickCount - half);
                return;
            }

            _nextTick = firstTick + tickCount;
            Remember(number, bytes);
            _transport.SendToAll(NetEnvelope.Wrap(NetChannel.TapeChunk, bytes, ref _envelope), NetDelivery.Reliable);
            SentChunkCount++;
        }

        private void Remember(int number, ArraySegment<byte> bytes)
        {
            var copy = new byte[bytes.Count];
            Array.Copy(bytes.Array, bytes.Offset, copy, 0, bytes.Count);

            _history[number] = copy;
            _historyOrder.Enqueue(number);

            while (_historyOrder.Count > _historyChunks)
                _history.Remove(_historyOrder.Dequeue());
        }

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.TapeResend) return;
            if (payload.Count < sizeof(int)) return;

            int number = BitConverter.ToInt32(payload.Array, payload.Offset);
            if (!_history.TryGetValue(number, out byte[] bytes)) return; // вытеснен — доедет опорным снимком

            _transport.Send(from,
                NetEnvelope.Wrap(NetChannel.TapeChunk, new ArraySegment<byte>(bytes), ref _envelope),
                NetDelivery.Reliable);
            ResentChunkCount++;
        }
    }
}
