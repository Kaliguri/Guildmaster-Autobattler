using System.Collections.Generic;
using Guildmaster.Balance;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Прогон кастом-сценария (<see cref="BalanceScenarioData"/>): состав A vs состав B до исхода/потолка,
    /// с per-unit метриками. Единственный бенч, работающий по авторенному SO, — для бесспоке-боёв «состав
    /// против состава», которые ГД хочет закрепить. Расстановка — простыми рядами по сторонам.
    /// </summary>
    public static class ScenarioBench
    {
        public static (string csv, string md) Run(BalanceScenarioData scenario)
        {
            StatsConfig config = BalanceAssets.LoadStatsConfig();
            var env = new SimEnvironment(scenario.Seed, config);
            var tracked = new List<TrackedUnit>();

            SpawnSide(env, tracked, scenario.TeamA, team: 0, sideLabel: "A", baseX: -3f);
            SpawnSide(env, tracked, scenario.TeamB, team: 1, sideLabel: "B", baseX: 3f);

            int cap = SimBench.TicksFromSeconds(scenario.MaxSeconds);
            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);

            var headers = new List<string>
            {
                "Side", "Unit", "DmgDealt", "DmgAutoPhys", "DmgAutoMagic", "DmgAbility", "DmgDoT", "DmgVuln",
                "DmgSelf", "DmgTaken", "Healing", "Died", "DeathSec", "HpLeft%",
            };
            var table = new List<IReadOnlyList<object>>();
            foreach (UnitMetric m in report.Units)
            {
                table.Add(new object[]
                {
                    m.Team == 0 ? "A" : "B",
                    m.Label,
                    m.DamageDealt,
                    m.DamageAutoPhysical,
                    m.DamageAutoMagical,
                    m.DamageAbility,
                    m.DamagePeriodic,
                    m.DamageFromVulnerability,
                    m.SelfDamage,
                    m.DamageTaken,
                    m.HealingDone,
                    m.Died ? "yes" : "no",
                    m.Died ? (object)(m.DeathTick / (double)SimConstants.TickRate) : "-",
                    100.0 * m.HpPctLeft,
                });
            }

            string result = report.TimedOut ? "TIMEOUT" : report.Outcome.ToString();
            string notes = $"**Сценарий:** {scenario.name}. **Исход:** {result} за {report.Seconds:0.0} с. " +
                           "Per-unit метрики боя A (team 0) vs B (team 1). DmgVuln — сколько урона добавили " +
                           "уязвимости цели («Угли»); это часть DmgDealt, а не добавка к нему. " +
                           "HpLeft% — остаток HP на конец боя.";

            string csv = ReportWriter.WriteCsv("scenario_" + Safe(scenario.name), headers, table);
            string md = ReportWriter.WriteMarkdown("scenario_" + Safe(scenario.name),
                "SimBench — сценарий «" + scenario.name + "»", headers, table, notes);
            return (csv, md);
        }

        private static void SpawnSide(SimEnvironment env, List<TrackedUnit> tracked,
            IReadOnlyList<BalanceScenarioData.SideEntry> side, int team, string sideLabel, float baseX)
        {
            int row = 0;
            for (int e = 0; e < side.Count; e++)
            {
                BalanceScenarioData.SideEntry entry = side[e];
                if (entry.Unit == null) continue;
                int count = Mathf.Max(1, entry.Count);
                for (int c = 0; c < count; c++)
                {
                    float x = baseX + (team == 0 ? -1f : 1f) * (row / 4) * 1.0f;
                    float y = ((row % 4) - 1.5f) * 1.2f;
                    var u = env.Real(entry.Unit, entry.Vessel, team, new Vector2(x, y));
                    tracked.Add(new TrackedUnit(u, sideLabel + ":" + entry.Unit.name + "#" + c, entry.Unit.name));
                    row++;
                }
            }
        }

        private static string Safe(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars);
        }
    }
}
