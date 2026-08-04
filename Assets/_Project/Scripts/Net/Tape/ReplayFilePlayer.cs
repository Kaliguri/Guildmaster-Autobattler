using System;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Воспроизводит бой из файла повтора — третий наполнитель ленты после живого сима и гостя по сети.
    /// Симуляции нет: читает записи файла и кладёт их в ту же <see cref="BattleTape"/>, из которой рисует
    /// весь показ. Показ не знает и не должен знать, что ленту наполнил файл, а не арена.
    /// </summary>
    /// <remarks>
    /// <b>Кормим по мере показа, а не всё разом.</b> Окно снимков ленты — двенадцать секунд; влить весь
    /// бой сразу значило бы затереть его начало прежде, чем показ до него дойдёт. Поэтому каждый тик
    /// подаём ровно столько записей, чтобы фронт ленты держался на опережение впереди момента показа
    /// (<c>ViewTick</c>) — тот же лаг, что у гостя и у хоста.
    /// <para><b>Порядок записей в файле = порядок во времени:</b> паспорт юнита лежит перед чанком, что
    /// его впервые несёт (писатель так и писал — по спавну, до готовности тика). Поэтому линейное чтение
    /// всегда регистрирует бойца раньше, чем его рисуют.</para>
    /// <para><b>Неизвестный id контента — пропуск бойца, а не отказ боя.</b> Запись переживает балансные
    /// правки даром, но не удаление контента: если id из файла в реестре не нашёлся, рисовать этого
    /// юнита нечем — пропускаем его и играем остальное (как <c>BattleRosterIntake</c>). Битый или
    /// новее-нашего чанк — иное: дальше поток недостоверен, воспроизведение останавливается.</para>
    /// </remarks>
    public sealed class ReplayFilePlayer : ITickable
    {
        // Запас поверх опережения: держим фронт на один чанк дальше, чем строго нужно показу, чтобы на
        // границе чанка показ не упёрся в неподанный кадр.
        private const int FeedMarginTicks = TapeChunkFormat.DefaultTicksPerChunk;

        private readonly Guildmaster.Net.NetByteReader _stream;
        private readonly BattleTape         _tape;
        private readonly BattleTapePlayback _playback;
        private readonly TapeChunkReader    _reader;
        private readonly BattleUnitRegistry _registry;
        private readonly IContentDatabase   _content;

        private bool _exhausted;

        /// <summary>Заголовок файла: имя боя, версия, сид. Пусто при неуспешной загрузке.</summary>
        public ReplayFile.Header Header { get; }

        /// <summary>Вердикт открытия. Не <see cref="ReplayLoadResult.Ok"/> — плеер молчит, ленту не трогает.</summary>
        public ReplayLoadResult LoadResult { get; }

        public ReplayFilePlayer(byte[] fileBytes, BattleTape tape, BattleTapePlayback playback,
                                TapeChunkReader reader, BattleUnitRegistry registry, IContentDatabase content)
        {
            _tape     = tape     ?? throw new ArgumentNullException(nameof(tape));
            _playback = playback ?? throw new ArgumentNullException(nameof(playback));
            _reader   = reader   ?? throw new ArgumentNullException(nameof(reader));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _content  = content;

            if (fileBytes == null || fileBytes.Length == 0)
            {
                LoadResult = ReplayLoadResult.Missing;
                _exhausted = true;
                return;
            }

            _stream    = new Guildmaster.Net.NetByteReader(new ArraySegment<byte>(fileBytes));
            LoadResult = ReplayFile.TryReadHeader(_stream, out ReplayFile.Header header);
            Header     = header;

            if (LoadResult != ReplayLoadResult.Ok)
            {
                Debug.LogError($"[ReplayFilePlayer] - файл повтора не открыть: {LoadResult}. Фона не будет.");
                _exhausted = true;
            }
        }

        public void Tick()
        {
            if (LoadResult != ReplayLoadResult.Ok) return;

            _playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);

            // Пока показ ещё не пошёл, фронт вперёд на весь lookahead НЕ гоним. Иначе к первому кадру
            // показа лента уже прокормлена на десять секунд, и старт (FrontTick − lookahead) уезжает
            // мимо завязки — показ начинается с уже сошедшихся команд, а не с их расстановки. Живой
            // бой этого не делает: продюсер не разгоняет сим впереди несуществующего показа
            // (HasFullLead=true до старта), поэтому показ идёт с первого тика. Держим ту же симметрию —
            // до старта кормим только на первый кадр, а lookahead набираем уже по ходу показа.
            int feedTarget = _playback.IsPlaying
                ? _playback.ViewTick + BattleTapePlayback.LookaheadTicks + FeedMarginTicks
                : _tape.OldestTick + FeedMarginTicks;
            FeedUpTo(feedTarget);
        }

        /// <summary>
        /// Подать записи из потока, пока фронт ленты не дойдёт до <paramref name="targetFrontTick"/> или
        /// файл не кончится. Выделено из <see cref="Tick"/>, чтобы проверяться без часов показа.
        /// </summary>
        public void FeedUpTo(int targetFrontTick)
        {
            while (!_exhausted && _tape.FrontTick < targetFrontTick)
            {
                if (!_stream.HasMore) { _exhausted = true; return; }

                byte tag;
                try
                {
                    tag = _stream.ReadByte();
                    if (tag == ReplayFile.Record.Roster) ReadRoster();
                    else if (tag == ReplayFile.Record.Chunk) { if (!ReadChunk()) return; }
                    else { FailStream($"неизвестный тег записи {tag}"); return; }
                }
                catch (InvalidOperationException e)
                {
                    FailStream("файл обрезан: " + e.Message);
                    return;
                }
            }
        }

        private void ReadRoster()
        {
            int    id        = _stream.ReadInt();
            int    team      = _stream.ReadByte();
            string contentId = _stream.ReadString();

            UnitData definition = null;
            if (!string.IsNullOrEmpty(contentId) && _content != null && !_content.TryGet(contentId, out definition))
            {
                // Балансные правки записи не трогают, а вот удаление контента — трогает: рисовать нечем.
                // Пропускаем бойца, играем остальное. Это ожидаемая деградация сквозь версии, не поломка.
                Debug.LogWarning($"[ReplayFilePlayer] - в повторе юнит '{contentId}', которого нет в " +
                                 "реестре контента → бойца в кадре не будет (запись из другой версии).");
                return;
            }

            _registry.RegisterRemote(id, definition, team);
        }

        /// <summary>Прочитать чанк в ленту. <c>false</c> — поток дальше недостоверен, воспроизведение стоп.</summary>
        private bool ReadChunk()
        {
            int len = _stream.ReadInt();
            ArraySegment<byte> chunk = _stream.ReadBytes(len);

            TapeChunkStatus status = _reader.Read(chunk);
            if (status == TapeChunkStatus.Ok || status == TapeChunkStatus.Duplicate) return true;

            FailStream($"чанк отвергнут ({status}): {_reader.LastError}");
            return false;
        }

        private void FailStream(string reason)
        {
            _exhausted = true;
            Debug.LogError($"[ReplayFilePlayer] - {reason}. Дальше повтор не играется.");
        }
    }
}
