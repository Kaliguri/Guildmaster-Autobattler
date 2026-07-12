using System.Collections.Generic;
using Guildmaster.Data.Stats;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Агрегаты статов по когорте юнитов (одного концертного типа) — для stat-context «среднее/разброс рядом»
    /// в Browser и хитмапа в Balance. Строится <see cref="ContentIndex"/>.
    /// </summary>
    public sealed class StatCohort
    {
        public readonly struct Agg
        {
            public readonly float Avg, Median, Min, Max;
            public readonly int Count;
            public Agg(float avg, float median, float min, float max, int count)
            {
                Avg = avg; Median = median; Min = min; Max = max; Count = count;
            }
        }

        private readonly Dictionary<StatType, Agg> _byStat = new Dictionary<StatType, Agg>();

        public int Size { get; internal set; }

        public Agg For(StatType stat) => _byStat.TryGetValue(stat, out var a) ? a : default;

        internal void Set(StatType stat, Agg agg) => _byStat[stat] = agg;
    }
}
