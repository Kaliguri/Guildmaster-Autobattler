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
        /// <summary>
        /// Сколько чанков хост держит для повторной отправки. <see cref="KeepWholeBattle"/> — весь бой.
        /// </summary>
        /// <remarks>
        /// <b>По умолчанию хранится ВЕСЬ бой</b> (решение Макса 04.08.2026), а не хвост в тридцать две
        /// секунды. Причина не в потерях пакетов — тех и правда хватало, — а в подключении посреди боя:
        /// гость получает ленту с нуля и потому встаёт в общую нумерацию без дыр, может отмотать назад
        /// и имеет на руках полный бой. Отвергнута альтернатива «начинать отсчёт с первого полученного
        /// чанка»: дешевле в байтах, но лишает гостя ленты и перемотки.
        /// <para>Цена — память: чанк это секунда боя, порядка трёх килобайт; бой на три минуты
        /// обходится в полмегабайта и живёт ровно до <see cref="Reset"/>, то есть до следующего боя.</para>
        /// </remarks>
        public const int DefaultHistoryChunks = KeepWholeBattle;

        /// <summary>Не выбрасывать ничего: история равна бою целиком.</summary>
        public const int KeepWholeBattle = int.MaxValue;

        private readonly INetTransport _transport;
        private readonly TapeChunkPump _pump;
        private readonly int           _historyChunks;

        // Номер чанка → его байты. Копия обязательна: писатель отдаёт переиспользуемый буфер.
        private readonly Dictionary<int, byte[]> _history = new Dictionary<int, byte[]>();
        private readonly Queue<int>              _historyOrder = new Queue<int>();

        private byte[] _envelope;

        public TapeStreamer(INetTransport transport, BattleTape tape,
                            int ticksPerChunk = TapeChunkFormat.DefaultTicksPerChunk,
                            int historyChunks = DefaultHistoryChunks)
        {
            _transport     = transport ?? throw new ArgumentNullException(nameof(transport));
            _pump          = new TapeChunkPump(tape, ticksPerChunk);
            _historyChunks = Math.Max(1, historyChunks);

            _transport.MessageReceived += HandleMessage;
        }

        /// <summary>Первый тик, который ещё не уехал.</summary>
        public int NextTick => _pump.NextTick;

        /// <summary>Сколько чанков отправлено с начала боя (повторы не считаются).</summary>
        public int SentChunkCount { get; private set; }

        /// <summary>Сколько повторов отдано по запросу — метрика качества канала, видна в dev-панели.</summary>
        public int ResentChunkCount { get; private set; }

        /// <summary>
        /// Отправить всё, что уже досчитано целыми чанками. Хвост короче размера чанка остаётся ждать:
        /// неполный чанк уезжает только через <see cref="Flush"/>, иначе конец боя дробился бы на
        /// однотиковые посылки.
        /// </summary>
        /// <param name="readyThroughTick">Последний тик, который гостям уже можно видеть, включительно.</param>
        public void Pump(int readyThroughTick) => _pump.Pump(readyThroughTick, ChunkLimit(), Send);

        /// <summary>
        /// Дослать хвост неполным чанком — конец боя и любой момент, после которого продолжения не
        /// будет. Без него последние тики боя (исход в их числе) остались бы у хоста.
        /// </summary>
        public void Flush(int readyThroughTick) => _pump.Flush(readyThroughTick, ChunkLimit(), Send);

        /// <summary>
        /// Переслать кадр покоя — состояние арены, пока симуляция стоит (расстановка, площадка).
        /// Границу отправленного не двигает: см. <see cref="TapeChunkPump.Resend"/>.
        /// </summary>
        public void Resend(int tick) => _pump.Resend(tick, ChunkLimit(), Send);

        /// <summary>Новый бой: нумерация тиков и чанков начинается заново, история сбрасывается.</summary>
        public void Reset()
        {
            _pump.Reset();
            _history.Clear();
            _historyOrder.Clear();
            // Догрузка принадлежит ПРОШЛОМУ бою: дошли эти чанки гостю или нет, они больше не про то,
            // что сейчас на арене, а номера в новом бою начинаются заново и совпадут с чужими.
            _backfill.Clear();
            SentChunkCount   = 0;
            ResentChunkCount = 0;
        }

        /// <summary>Отписаться от транспорта. Зовётся вместе с концом сессии.</summary>
        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        /// <summary>
        /// Предел размера чанка спрашиваем у ТРАНСПОРТА, а не берём из своей константы: у Steam это
        /// 512 КБ, у UTP — MaximumFragmentedMessageSize, и он заметно меньше. Сообщение сверх предела
        /// Steam роняет молча (транспорт не читает его отказ), так что проверить обязаны мы. Потолок
        /// формата тоже участвует — берём меньшее из двух.
        /// </summary>
        private int ChunkLimit() => Math.Min(_transport.MaxReliableMessageBytes - NetEnvelope.HeaderBytes,
                                             TapeChunkFormat.MaxChunkBytes);

        // Готовый чанк уходит гостям и ложится в историю на случай запроса повтора — та же операция, что
        // раньше стояла хвостом SendChunk, теперь отданная насосу как приёмник.
        private void Send(int number, ArraySegment<byte> bytes)
        {
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

        /// <summary>
        /// Отдать очередную порцию догрузки — бой с начала тому, кто подключился посреди него.
        /// </summary>
        /// <remarks>
        /// <b>Порциями, а не залпом.</b> Бой на три минуты — это под две сотни надёжных сообщений, и
        /// высыпать их в очередь Steam одним кадром значит выбрать худший момент: ровно этот, когда
        /// соединение только поднялось. Отдаём по нескольку за кадр — показ у всех на паузе, спешить
        /// некуда.
        /// </remarks>
        public void PumpBackfill(int maxPerCall = BackfillChunksPerCall)
        {
            for (int sent = 0; sent < maxPerCall && _backfill.Count > 0; )
            {
                (int peer, int number) = _backfill.Peek();
                _backfill.Dequeue();

                if (!_history.TryGetValue(number, out byte[] bytes)) continue; // такого чанка и не было

                _transport.Send(peer,
                    NetEnvelope.Wrap(NetChannel.TapeChunk, new ArraySegment<byte>(bytes), ref _envelope),
                    NetDelivery.Reliable);
                ResentChunkCount++;
                sent++;
            }
        }

        /// <summary>Сколько чанков догрузки ещё не ушло — по нему видно, ждать ли гостя.</summary>
        public int BackfillRemaining => _backfill.Count;

        /// <summary>За один вызов — столько чанков, то есть примерно восемь секунд боя за кадр.</summary>
        public const int BackfillChunksPerCall = 8;

        private readonly Queue<(int Peer, int Number)> _backfill = new Queue<(int, int)>();

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.TapeResend) return;

            // Пустая просьба — «пришли бой с начала»: так просит тот, кто подключился, когда бой уже
            // идёт. Ставим ему в очередь всё, что храним, по возрастанию номера: приёмник складывает
            // ленту по номерам и от порядка не зависит, но человеку, который смотрит на догрузку,
            // естественнее видеть бой прибывающим с начала.
            if (payload.Count == 0)
            {
                var numbers = new List<int>(_history.Keys);
                numbers.Sort();
                for (int i = 0; i < numbers.Count; i++) _backfill.Enqueue((from, numbers[i]));
                return;
            }

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
