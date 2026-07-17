using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Фаза 2 — round-robin дуэли всех реликвий 1v1 + рейтинг Bradley-Terry. Каждая упорядоченная пара
    /// (i слева vs j справа) гоняется отдельно → каждая неупорядоченная пара сыграна дважды (обе стороны),
    /// что усредняет возможное преимущество левой/правой позиции. Выдаёт сводку (Wins/WinRate/Strength) и
    /// матрицу матчапов. Оговорка: бой RNG-free → исходы детерминированы (1/0), win-rate дискретный.
    /// </summary>
    public static class DuelMatrixBench
    {
        private const float CapSeconds = 120f;
        private const ulong Seed = 1UL;

        public static (string csv, string md) Run()
        {
            StatsConfig config = BalanceAssets.LoadStatsConfig();
            int cap = SimBench.TicksFromSeconds(CapSeconds);
            List<RelicData> relics = BalanceAssets.LoadRelics();
            int n = relics.Count;

            var wins = New(n);   // для Bradley-Terry: дробные очки (ничья = 0.5)
            var games = New(n);
            var wCount = new int[n];
            var lCount = new int[n];
            var dCount = new int[n];

            // outcome[i][j]: результат для i, когда i слева vs j справа ("W"/"L"/"D").
            var outcome = new string[n][];
            for (int i = 0; i < n; i++) { outcome[i] = new string[n]; for (int j = 0; j < n; j++) outcome[i][j] = "-"; }

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double leftScore = RunDuel(config, relics[i], relics[j], cap); // очко i (левого): 1/0.5/0

                    outcome[i][j] = leftScore > 0.75 ? "W" : leftScore < 0.25 ? "L" : "D";
                    wins[i][j] += leftScore;
                    wins[j][i] += 1.0 - leftScore;
                    games[i][j] += 1.0;
                    games[j][i] += 1.0;

                    if (leftScore > 0.75) { wCount[i]++; lCount[j]++; }
                    else if (leftScore < 0.25) { lCount[i]++; wCount[j]++; }
                    else { dCount[i]++; dCount[j]++; }
                }
            }

            double[] strength = BradleyTerry.Fit(n, wins, games);

            // --- Сводка (сортировка по силе) ---
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => strength[b].CompareTo(strength[a]));

            var sumHeaders = new List<string> { "Rank", "Relic", "Wins", "Losses", "Draws", "WinRate", "BTStrength" };
            var sumTable = new List<IReadOnlyList<object>>();
            int rank = 1;
            foreach (int i in order)
            {
                int total = wCount[i] + lCount[i] + dCount[i];
                double winRate = total > 0 ? (wCount[i] + 0.5 * dCount[i]) / total : 0.0;
                sumTable.Add(new object[] { rank++, relics[i].name, wCount[i], lCount[i], dCount[i], winRate, strength[i] });
            }

            string sumNotes =
                $"**Round-robin дуэли** (потолок {CapSeconds:0} с/бой, каждая пара — обе стороны). " +
                "WinRate учитывает ничьи как 0.5. BTStrength (Bradley-Terry) — относительная сила, норм. к 1. " +
                "Сейчас бой RNG-free → исходы детерминированы; рейтинг ближе к топологическому порядку. " +
                "Дуэль 1v1 не отражает СОЧЕТАНИЯ — это ранг в вакууме, не приговор балансу.";
            string sumCsv = ReportWriter.WriteCsv("duel_ranking", sumHeaders, sumTable);
            string sumMd = ReportWriter.WriteMarkdown("duel_ranking", "SimBench — рейтинг дуэлей (Фаза 2)", sumHeaders, sumTable, sumNotes);

            // --- Матрица матчапов (строка = левый, столбец = правый) ---
            var matrixHeaders = new List<string> { "Left \\ Right" };
            for (int j = 0; j < n; j++) matrixHeaders.Add(relics[j].name);
            var matrixTable = new List<IReadOnlyList<object>>();
            for (int i = 0; i < n; i++)
            {
                var cells = new List<object> { relics[i].name };
                for (int j = 0; j < n; j++) cells.Add(outcome[i][j]);
                matrixTable.Add(cells);
            }
            ReportWriter.WriteCsv("duel_matrix", matrixHeaders, matrixTable);
            ReportWriter.WriteMarkdown("duel_matrix", "SimBench — матрица матчапов (Фаза 2)", matrixHeaders, matrixTable,
                "Ячейка [строка i, столбец j] — исход для i (левого) против j (правого): W/L/D.");

            return (sumCsv, sumMd);
        }

        /// <summary>Прогон одной дуэли. Возвращает очко ЛЕВОГО (relicLeft): 1 победа, 0.5 ничья/timeout, 0 поражение.</summary>
        private static double RunDuel(StatsConfig config, RelicData relicLeft, RelicData relicRight, int cap)
        {
            var env = new SimEnvironment(Seed, config);
            var tracked = new List<TrackedUnit>
            {
                new TrackedUnit(env.Real(relicLeft, null, 0, new Vector2(-3f, 0f)), relicLeft.name, relicLeft.name),
                new TrackedUnit(env.Real(relicRight, null, 1, new Vector2(3f, 0f)), relicRight.name, relicRight.name),
            };

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);
            if (report.TimedOut || report.Outcome.IsDraw) return 0.5;
            if (report.Outcome.IsWinFor(0)) return 1.0;
            if (report.Outcome.IsWinFor(1)) return 0.0;
            return 0.5;
        }

        private static double[][] New(int n)
        {
            var a = new double[n][];
            for (int i = 0; i < n; i++) a[i] = new double[n];
            return a;
        }
    }
}
