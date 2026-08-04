using System;
using Guildmaster.Combat.Tape;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Нарезчик боевой ленты на чанки: единый владелец правила «сколько тиков в чанк и что делать, если
    /// кадр не влез». Куда уезжает готовый чанк — не его забота: он отдаёт номер и байты в
    /// <see cref="ChunkSink"/>, а сеть или файл распоряжаются ими сами.
    /// </summary>
    /// <remarks>
    /// <b>Почему отдельный класс.</b> Раздача по сети (<see cref="TapeStreamer"/>) и запись реплея на
    /// диск режут ленту одинаково: тот же размер чанка, та же дельта внутри него, то же деление пополам
    /// при переполнении. Держать это правило в двух местах — завести ему двух владельцев, а расхождение
    /// здесь даёт не ошибку, а тихо битый чанк у приёмника. Поэтому нарезка живёт одна, а разными
    /// остаются только предел размера (его знает транспорт, у файла его нет) и место назначения.
    /// <para><b>Предел приходит параметром, а не полем:</b> у Steam это 512 КБ, у UTP заметно меньше, у
    /// файла — только потолок формата. Насос уважает чужое число, а не держит своё.</para>
    /// </remarks>
    public sealed class TapeChunkPump
    {
        /// <summary>Куда уходит готовый чанк: его номер (для дедупликации у приёмника) и байты.</summary>
        public delegate void ChunkSink(int chunkNumber, ArraySegment<byte> bytes);

        private readonly BattleTape      _tape;
        private readonly TapeChunkWriter _writer = new TapeChunkWriter();
        private readonly int             _ticksPerChunk;

        private int _nextTick;

        public TapeChunkPump(BattleTape tape, int ticksPerChunk = TapeChunkFormat.DefaultTicksPerChunk)
        {
            _tape          = tape ?? throw new ArgumentNullException(nameof(tape));
            _ticksPerChunk = ticksPerChunk > 0 && ticksPerChunk <= 255
                ? ticksPerChunk
                : throw new ArgumentOutOfRangeException(nameof(ticksPerChunk),
                    "смещение тика внутри чанка едет байтом: 1..255");
        }

        /// <summary>Первый тик, который ещё не уехал.</summary>
        public int NextTick => _nextTick;

        /// <summary>
        /// Отдать в <paramref name="sink"/> всё, что уже досчитано ЦЕЛЫМИ чанками. Хвост короче
        /// <see cref="_ticksPerChunk"/> остаётся ждать — неполный чанк уезжает только через
        /// <see cref="Flush"/>, иначе конец боя дробился бы на однотиковые посылки.
        /// </summary>
        /// <param name="readyThroughTick">Последний тик, который приёмнику уже можно видеть, включительно.</param>
        /// <param name="maxBytes">Предел размера чанка. Приходит снаружи — его знает адресат, не насос.</param>
        public void Pump(int readyThroughTick, int maxBytes, ChunkSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            while (readyThroughTick - _nextTick + 1 >= _ticksPerChunk)
                CutChunk(_nextTick, _ticksPerChunk, maxBytes, sink);
        }

        /// <summary>
        /// Дослать хвост неполным чанком — конец боя и любой момент, после которого продолжения не будет.
        /// Без него последние тики боя (исход в их числе) остались бы у отправителя.
        /// </summary>
        public void Flush(int readyThroughTick, int maxBytes, ChunkSink sink)
        {
            Pump(readyThroughTick, maxBytes, sink);

            int rest = readyThroughTick - _nextTick + 1;
            if (rest > 0) CutChunk(_nextTick, rest, maxBytes, sink);
        }

        /// <summary>Новый бой: нумерация тиков и чанков начинается заново.</summary>
        public void Reset() => _nextTick = 0;

        private void CutChunk(int firstTick, int tickCount, int maxBytes, ChunkSink sink)
        {
            int number = _writer.NextChunkNumber;

            if (!_writer.TryWrite(_tape, firstTick, tickCount, maxBytes, out ArraySegment<byte> bytes))
            {
                // Один тик, который не влезает, — это уже не вопрос нарезки: на арене столько юнитов и
                // событий, что кадр не пролезает целиком. Отказ громкий, потому что тихо здесь означало
                // бы «у приёмника просто нет куска боя».
                if (tickCount <= 1)
                    throw new InvalidOperationException(
                        $"кадр тика {firstTick} не влезает в предел {maxBytes} Б — делить дальше нечего");

                // Номер чанка писатель не потратил, поэтому обе половины уедут подряд и без дыры.
                int half = tickCount / 2;
                CutChunk(firstTick, half, maxBytes, sink);
                CutChunk(firstTick + half, tickCount - half, maxBytes, sink);
                return;
            }

            // Пустой срез — не ошибка: в диапазоне не оказалось ни одного записанного кадра (бой ещё не
            // начинался, лента чистилась). Номер чанка при этом тоже не тратится.
            if (bytes.Count == 0)
            {
                _nextTick = firstTick + tickCount;
                return;
            }

            _nextTick = firstTick + tickCount;
            sink(number, bytes);
        }
    }
}
