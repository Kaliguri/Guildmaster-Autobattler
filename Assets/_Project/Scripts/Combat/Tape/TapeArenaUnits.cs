using System.Collections.Generic;
using Guildmaster.Core.Arena;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Арена глазами гостя: правду о том, кто где стоит, держит присланный кадр.
    /// </summary>
    /// <remarks>
    /// Своей симуляции у гостя нет намеренно — она затёрла бы ленту пустой ареной, — поэтому
    /// единственное, что у него есть про арену, это кадр показа. Вне боя его наполняет кадр покоя
    /// (десять раз в секунду), в бою — обычные снимки ленты; для рук игрока разницы нет, и знать о
    /// ней потребителю не нужно.
    /// <para><b>Отставание показа здесь — не помеха, а требование.</b> Гость обязан взаимодействовать
    /// ровно с той ареной, которую видит: круг, нарисованный по будущему кадру, стоял бы не под тем
    /// бойцом, который сейчас на экране.</para>
    /// </remarks>
    public sealed class TapeArenaUnits : IArenaUnits
    {
        private readonly IStageFrameSource _frames;
        private readonly List<ArenaUnit>   _buffer = new List<ArenaUnit>(16);

        public TapeArenaUnits(IStageFrameSource frames) => _frames = frames;

        public IReadOnlyList<ArenaUnit> Units
        {
            get
            {
                _buffer.Clear();
                if (!_frames.TryGetFrame(out IReadOnlyList<UnitSnapshot> units, out _)) return _buffer;

                for (int i = 0; i < units.Count; i++) _buffer.Add(Project(units[i]));
                return _buffer;
            }
        }

        public bool TryGet(int id, out ArenaUnit unit)
        {
            unit = default;
            if (!_frames.TryGetFrame(out IReadOnlyList<UnitSnapshot> units, out _)) return false;

            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].Id != id) continue;
                unit = Project(units[i]);
                return true;
            }

            return false;
        }

        private static ArenaUnit Project(in UnitSnapshot s) =>
            new ArenaUnit(s.Id, s.Team, s.Position, s.Size, s.IsDead);
    }
}
