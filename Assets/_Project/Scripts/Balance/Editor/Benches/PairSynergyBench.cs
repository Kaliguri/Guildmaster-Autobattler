using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Синергия пар: работают ли два кита вместе лучше, чем порознь. Единственный формат, который вообще
    /// умеет задавать этот вопрос — все остальные меряют кит в одиночку, а отряд в рогалике собирается
    /// именно из сочетаний.
    /// </summary>
    /// <remarks>
    /// Мерим не «победил/проиграл» (бой RNG-free, исход дискретен и на паре почти всегда один и тот же),
    /// а НЕПРЕРЫВНУЮ величину — остаток HP команды против одного и того же эталонного противника.
    /// Синергия считается как интеракция: <c>вклад(A+B) − вклад(A) − вклад(B)</c>, где вклад — насколько
    /// команда с этим китом сохраняет больше HP, чем команда из одних манекенов. Плюс — киты усиливают
    /// друг друга, минус — мешают (дерутся за одну цель, ломают чужие условия).
    /// <para>Три размера боя, потому что сочетание может работать только в тесноте или только в толпе:
    /// пара наедине (2v2), пара внутри штатного отряда (4v4) и пара в бою крупнее штатного (6v6).</para>
    /// </remarks>
    public static class PairSynergyBench
    {
        private const float CapSeconds = 240f;
        private const ulong Seed = 1UL;

        private readonly struct Scale
        {
            public readonly Slot[] Lineup;
            public readonly string Label;

            public Scale(Slot[] lineup, string label)
            {
                Lineup = lineup;
                Label = label;
            }
        }

        private static readonly Scale[] Scales =
        {
            new Scale(Lineups.Pair, "2v2"),
            new Scale(Lineups.Squad, "4v4"),
            new Scale(Lineups.Large, "6v6"),
        };

        public static (string csv, string md) Run()
        {
            StatsConfig config = BalanceAssets.LoadStatsConfig();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();
            int cap = SimBench.TicksFromSeconds(CapSeconds);
            List<RelicData> relics = BalanceAssets.LoadRelics();
            int n = relics.Count;

            var headers = new List<string> { "PairA", "PairB" };
            for (int s = 0; s < Scales.Length; s++) headers.Add("Synergy_" + Scales[s].Label);
            headers.Add("SynergyAvg");

            // База и одиночные вклады считаются по разу на масштаб — дальше переиспользуются всеми парами.
            var baseline = new double[Scales.Length];
            var solo = new double[Scales.Length][];
            var none = new RelicData[0];
            for (int s = 0; s < Scales.Length; s++)
            {
                baseline[s] = TeamHp(config, classes, none, Scales[s].Lineup, cap);
                solo[s] = new double[n];
                for (int i = 0; i < n; i++)
                    solo[s][i] = TeamHp(config, classes, new[] { relics[i] }, Scales[s].Lineup, cap) - baseline[s];
            }

            var rows = new List<IReadOnlyList<object>>();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var cells = new List<object> { relics[i].name, relics[j].name };
                    double sum = 0.0;
                    for (int s = 0; s < Scales.Length; s++)
                    {
                        double pair = TeamHp(config, classes, new[] { relics[i], relics[j] }, Scales[s].Lineup, cap)
                                      - baseline[s];
                        double synergy = pair - solo[s][i] - solo[s][j];
                        cells.Add(100.0 * synergy);
                        sum += synergy;
                    }

                    cells.Add(100.0 * sum / Scales.Length);
                    rows.Add(cells);
                }
            }

            // Сортировка по средней синергии: сверху сочетания, ради которых игрок и собирает отряд.
            rows.Sort((a, b) => ((double)b[b.Count - 1]).CompareTo((double)a[a.Count - 1]));

            string notes =
                $"**Синергия пар** (потолок {CapSeconds:0} с/бой). Каждая пара воюет против одного и того же " +
                "эталонного отряда манекенов в трёх размерах боя: наедине (2v2), внутри штатного отряда (4v4) и " +
                "в бою крупнее штатного (6v6). " +
                "Метрика — остаток HP команды в процентных пунктах: сначала считается **вклад** кита " +
                "(насколько команда с ним сохраняет больше HP, чем команда из одних манекенов), затем " +
                "**синергия** = вклад пары минус вклады обоих поодиночке. " +
                "Плюс — киты усиливают друг друга, ноль — просто складываются, минус — мешают " +
                "(дерутся за одну цель, ломают условия друг друга). " +
                "Оговорки: манекен не лечит и не бьёт по площади, поэтому пары с настоящим хилером формат " +
                "занижает; бой RNG-free, так что число — это один детерминированный исход, а не среднее.";

            string csv = ReportWriter.WriteCsv("pair_synergy", headers, rows);
            string md = ReportWriter.WriteMarkdown("pair_synergy", "SimBench — синергия пар", headers, rows, notes);
            ReportWriter.WriteJson("pair_synergy", "SimBench — синергия пар", headers, rows, notes);
            return (csv, md);
        }

        /// <summary>Доля HP, сохранённая командой с этими китами против эталонного отряда манекенов.</summary>
        private static double TeamHp(StatsConfig config, ClassBalanceConfig classes,
            IReadOnlyList<RelicData> heroes, Slot[] lineup, int cap)
        {
            var env = new SimEnvironment(Seed, config);
            var tracked = new List<TrackedUnit>();

            Lineups.SpawnTeam(env, classes, tracked, heroes, 0, lineup);
            Lineups.SpawnTeam(env, classes, tracked, new RelicData[0], 1, lineup);

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);

            double hpLeft = 0.0, maxHp = 0.0;
            for (int i = 0; i < report.Units.Count; i++)
            {
                UnitMetric m = report.Units[i];
                if (m.Team != 0 || m.IsSummon) continue;   // тело — расходник, в запас отряда не входит
                hpLeft += m.HpLeft;
                maxHp += m.MaxHp;
            }

            return maxHp > 0.0 ? hpLeft / maxHp : 0.0;
        }
    }
}
