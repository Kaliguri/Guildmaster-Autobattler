using System;
using System.IO;
using Guildmaster.Combat.Tape;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Пишет бой в файл повтора: заголовок при рождении, дальше паспорта юнитов и чанки ленты в потоке —
    /// в том же порядке, в каком кооп слал бы их по сети. Транспорта не знает: чанки режет тот же
    /// <see cref="TapeChunkPump"/>, что и раздача, а уезжают они в буфер, а не в сокет.
    /// </summary>
    /// <remarks>
    /// <b>Запись потоковая, а не «весь бой в конце».</b> Окно снимков в ленте — двенадцать секунд; сим,
    /// уехавший вперёд, вытеснил бы начало боя раньше, чем мы его сохраним. Поэтому <see cref="Pump"/>
    /// зовётся каждый кадр записи (по последний досчитанный тик), а <see cref="Flush"/> — на конце боя,
    /// чтобы хвост с исходом не остался в ленте. Тот же ритм, что у <c>BattleTapeBroadcast</c>.
    /// <para><b>Паспорт пишется при спавне</b> (<see cref="AddUnit"/> из <c>OnUnitSpawned</c>), до чанка,
    /// который этого юнита впервые несёт — как по сети. Порядок в файле = порядок вызовов, поэтому
    /// воспроизведение всегда знает, кто это, раньше, чем рисует его.</para>
    /// </remarks>
    public sealed class ReplayFileWriter
    {
        private readonly Guildmaster.Net.NetByteWriter _bytes = new Guildmaster.Net.NetByteWriter(16384);
        private readonly TapeChunkPump _pump;

        /// <summary>Сколько паспортов и чанков записано — метрика для dev-лога.</summary>
        public int UnitCount  { get; private set; }
        public int ChunkCount { get; private set; }

        public ReplayFileWriter(BattleTape tape, in ReplayFile.Header header)
        {
            _pump = new TapeChunkPump(tape ?? throw new ArgumentNullException(nameof(tape)));
            ReplayFile.WriteHeader(_bytes, in header);
        }

        /// <summary>Готовые байты файла (живут до следующей записи). Для сохранения — <see cref="Save"/>.</summary>
        public ArraySegment<byte> Written => _bytes.WrittenSegment;

        /// <summary>Паспорт вышедшего на арену юнита. Пустой <paramref name="contentId"/> законен: так
        /// пишутся болванчики без определения (показ берёт умолчания).</summary>
        public void AddUnit(int id, int team, string contentId)
        {
            _bytes.WriteByte(ReplayFile.Record.Roster);
            _bytes.WriteInt(id);
            _bytes.WriteByte((byte)team);
            _bytes.WriteString(contentId);
            UnitCount++;
        }

        /// <summary>Записать всё, что досчитано целыми чанками, по <paramref name="readyThroughTick"/> включительно.</summary>
        public void Pump(int readyThroughTick) => _pump.Pump(readyThroughTick, TapeChunkFormat.MaxChunkBytes, WriteChunk);

        /// <summary>Дописать хвост неполным чанком — конец боя, где едет исход.</summary>
        public void Flush(int readyThroughTick) => _pump.Flush(readyThroughTick, TapeChunkFormat.MaxChunkBytes, WriteChunk);

        /// <summary>
        /// Сохранить файл на диск атомарно: сначала во временный, затем подменой — оборванная запись не
        /// оставит полуфайла на месте живого. Каталог создаётся, если его нет.
        /// </summary>
        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            ArraySegment<byte> seg = _bytes.WrittenSegment;
            string temp = path + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                file.Write(seg.Array, seg.Offset, seg.Count);

            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private void WriteChunk(int number, ArraySegment<byte> chunk)
        {
            _bytes.WriteByte(ReplayFile.Record.Chunk);
            _bytes.WriteInt(chunk.Count);
            // Номер чанка в файл НЕ пишем: в потоке чанки лежат по порядку и без дыр (в отличие от сети,
            // где номер нужен для дедупликации повторов). Дельта самодостаточна внутри чанка, склейка —
            // просто чтение подряд.
            for (int i = 0; i < chunk.Count; i++) _bytes.WriteByte(chunk.Array[chunk.Offset + i]);
            ChunkCount++;
        }
    }
}
