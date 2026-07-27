using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Замена в живом отряде: штатная четвёрка из РЕАЛЬНЫХ китов против своего зеркала, и в одной из копий
    /// боец своей роли заменён на испытуемого. Отвечает на вопрос, который игрок задаёт себе каждый забег:
    /// «кого ставить на эту роль».
    /// </summary>
    /// <remarks>
    /// Зеркало — намеренно: одинаковые отряды дают ровный ноль, поэтому весь сдвиг результата приписывается
    /// ровно одной замене, без поправок на силу соперника. Штатный отряд собирается не вручную: на каждую
    /// роль берётся кит с МЕДИАННЫМ вкладом (насколько отряд с ним держится лучше отряда из манекенов).
    /// Медиана, а не лучший и не худший — чтобы фон был обычным отрядом, а не командой мечты, в которой
    /// любая замена смотрится провалом.
    /// </remarks>
    public static class SquadSwapBench
    {
        private const float CapSeconds = 240f;
        private const ulong Seed = 1UL;

        public static (string csv, string md) Run()
        {
            StatsConfig config = BalanceAssets.LoadStatsConfig();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();
            int cap = SimBench.TicksFromSeconds(CapSeconds);
            List<RelicData> relics = BalanceAssets.LoadRelics();
            Slot[] lineup = Lineups.Squad;

            // 1. Вклад каждого кита в штатный отряд — им же выбираем медианного представителя роли.
            var contribution = new Dictionary<RelicData, double>();
            double baseline = TeamHp(config, classes, new RelicData[0], lineup, cap);
            foreach (RelicData relic in relics)
                contribution[relic] = TeamHp(config, classes, new[] { relic }, lineup, cap) - baseline;

            // 2. Штатный отряд: медианный кит на каждую роль строя (роль без китов остаётся манекеном).
            var squad = new List<RelicData>();
            var squadByRole = new Dictionary<UnitClass, RelicData>();
            foreach (Slot slot in lineup)
            {
                if (squadByRole.ContainsKey(slot.Role)) continue;
                RelicData pick = MedianOfRole(relics, contribution, slot.Role);
                if (pick == null) continue;
                squadByRole[slot.Role] = pick;
                squad.Add(pick);
            }

            // 3. Каждый кит по очереди встаёт вместо штатного бойца своей роли.
            var headers = new List<string>
            {
                "Relic", "Role", "Replaces", "Outcome", "TeamHpLeft%", "EnemyHpLeft%", "Delta", "Seconds",
            };
            var rows = new List<IReadOnlyList<object>>();

            foreach (RelicData relic in relics)
            {
                UnitClass role = Lineups.SlotRole(relic.CombatClass);
                squadByRole.TryGetValue(role, out RelicData incumbent);

                var challenger = new List<RelicData>(squad);
                int at = incumbent != null ? challenger.IndexOf(incumbent) : -1;
                if (at >= 0) challenger[at] = relic;
                else challenger.Add(relic);   // роли нет в строю — кит просто входит в отряд

                SwapResult r = RunSwap(config, classes, challenger, squad, lineup, cap);
                rows.Add(new object[]
                {
                    relic.name,
                    role.ToString(),
                    incumbent != null ? incumbent.name : "—",
                    r.Outcome,
                    100.0 * r.TeamHp,
                    100.0 * r.EnemyHp,
                    100.0 * r.Delta,
                    r.Seconds,
                });
            }

            rows.Sort((a, b) => ((double)b[6]).CompareTo((double)a[6]));

            var squadNames = new List<string>();
            foreach (RelicData r in squad) squadNames.Add(r.name);

            string notes =
                $"**Замена в живом отряде** (штатный размер игры — четвёрка; потолок {CapSeconds:0} с/бой). " +
                $"Штатный отряд: **{string.Join(", ", squadNames)}** — на каждую роль взят кит с МЕДИАННЫМ " +
                "вкладом, чтобы фон был обычным отрядом, а не командой мечты. " +
                "Бой идёт против зеркала этого же отряда, в котором заменён ровно один боец, поэтому весь " +
                "сдвиг — цена замены. **Delta** — разница остатков HP команд в процентных пунктах: плюс " +
                "означает, что с этим китом отряд крепче штатного, минус — слабее. У самого штатного бойца " +
                "дельта нулевая по построению (он дерётся сам с собой). " +
                "Оговорка: медиана считается вкладом в отряд из манекенов, поэтому роли, где кит один, " +
                "попадают в состав без выбора.";

            string csv = ReportWriter.WriteCsv("squad_swap", headers, rows);
            string md = ReportWriter.WriteMarkdown("squad_swap", "SimBench — замена в живом отряде (4v4)",
                headers, rows, notes);
            ReportWriter.WriteJson("squad_swap", "SimBench — замена в живом отряде (4v4)", headers, rows, notes);
            return (csv, md);
        }

        private readonly struct SwapResult
        {
            public readonly string Outcome;
            public readonly double TeamHp;
            public readonly double EnemyHp;
            public readonly double Delta;
            public readonly double Seconds;

            public SwapResult(string outcome, double teamHp, double enemyHp, double delta, double seconds)
            {
                Outcome = outcome;
                TeamHp = teamHp;
                EnemyHp = enemyHp;
                Delta = delta;
                Seconds = seconds;
            }
        }

        /// <summary>
        /// Прогон замены ДВАЖДЫ — испытуемый отряд слева и справа — с усреднением дельты.
        /// </summary>
        /// <remarks>
        /// Одиночный прогон здесь врёт: сим не симметричен, и зеркальный бой двух ОДИНАКОВЫХ отрядов
        /// заканчивается уверенной победой левой стороны (поймано замером: 59.7% против нуля). Порядок
        /// обработки юнитов даёт левым право первого удара, дальше преимущество лавинообразно растёт.
        /// Играть обе стороны и усреднять — то же лекарство, что уже стоит в дуэльной матрице: перекос
        /// стороны входит в обе половины с разными знаками и схлопывается.
        /// </remarks>
        private static SwapResult RunSwap(StatsConfig config, ClassBalanceConfig classes,
            IReadOnlyList<RelicData> challenger, IReadOnlyList<RelicData> incumbents, Slot[] lineup, int cap)
        {
            (string outcomeL, double teamL, double enemyL, double secondsL) =
                RunOnce(config, classes, challenger, incumbents, lineup, cap);
            (string outcomeR, double teamR, double enemyR, double secondsR) =
                RunOnce(config, classes, incumbents, challenger, lineup, cap);

            // Слева испытуемый — команда 0; справа он уже команда 1, поэтому дельта берётся с другой стороны.
            double delta = 0.5 * ((teamL - enemyL) + (enemyR - teamR));
            string outcome = outcomeL == Mirror(outcomeR) ? outcomeL : outcomeL + "/" + Mirror(outcomeR);

            return new SwapResult(outcome, 0.5 * (teamL + enemyR), 0.5 * (enemyL + teamR),
                delta, 0.5 * (secondsL + secondsR));
        }

        private static string Mirror(string outcome) => outcome switch
        {
            "W" => "L",
            "L" => "W",
            _ => outcome,
        };

        private static (string outcome, double teamHp, double enemyHp, double seconds) RunOnce(
            StatsConfig config, ClassBalanceConfig classes, IReadOnlyList<RelicData> left,
            IReadOnlyList<RelicData> right, Slot[] lineup, int cap)
        {
            var env = new SimEnvironment(Seed, config);
            var tracked = new List<TrackedUnit>();

            Lineups.SpawnTeam(env, classes, tracked, left, 0, lineup);
            Lineups.SpawnTeam(env, classes, tracked, right, 1, lineup);

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);

            string outcome = report.TimedOut ? "timeout"
                : report.Outcome.IsWinFor(0) ? "W"
                : report.Outcome.IsWinFor(1) ? "L"
                : "D";

            return (outcome, TeamHpOf(report, 0), TeamHpOf(report, 1), report.Seconds);
        }

        private static double TeamHpOf(BattleReport report, int team)
        {
            double hpLeft = 0.0, maxHp = 0.0;
            for (int i = 0; i < report.Units.Count; i++)
            {
                UnitMetric m = report.Units[i];
                if (m.Team != team) continue;
                hpLeft += m.HpLeft;
                maxHp += m.MaxHp;
            }

            return maxHp > 0.0 ? hpLeft / maxHp : 0.0;
        }

        /// <summary>Кит роли с медианным вкладом. При чётном числе кандидатов берётся верхний из середины.</summary>
        private static RelicData MedianOfRole(List<RelicData> relics,
            Dictionary<RelicData, double> contribution, UnitClass role)
        {
            var ofRole = new List<RelicData>();
            foreach (RelicData relic in relics)
                if (Lineups.SlotRole(relic.CombatClass) == role) ofRole.Add(relic);

            if (ofRole.Count == 0) return null;
            ofRole.Sort((a, b) => contribution[a].CompareTo(contribution[b]));
            return ofRole[ofRole.Count / 2];
        }

        private static double TeamHp(StatsConfig config, ClassBalanceConfig classes,
            IReadOnlyList<RelicData> heroes, Slot[] lineup, int cap)
        {
            var env = new SimEnvironment(Seed, config);
            var tracked = new List<TrackedUnit>();

            Lineups.SpawnTeam(env, classes, tracked, heroes, 0, lineup);
            Lineups.SpawnTeam(env, classes, tracked, new RelicData[0], 1, lineup);

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);
            return TeamHpOf(report, 0);
        }
    }
}
