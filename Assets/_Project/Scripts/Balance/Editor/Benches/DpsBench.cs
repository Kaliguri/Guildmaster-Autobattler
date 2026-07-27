using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Фаза 1 — DPS-бенч. Каждый кит бьёт (а) одну бессмертную цель [solo] и (б) кластер бессмертных
    /// целей [aoe]; меряем нанесённый урон/сек. Ratio = aoe/solo &gt; 1 → кит реально размазывает по AoE.
    /// Метрика чувствительна к расстановке (см. simbench.md) — это относительный инструмент, не абсолют.
    /// </summary>
    public static class DpsBench
    {
        private const float CapSeconds = 20f;
        private const float DummyHp = 3000f;
        private const int ClusterSize = 5;
        private const ulong Seed = 1UL;

        public static (string csv, string md) Run()
        {
            StatsConfig config = BalanceAssets.LoadStatsConfig();
            int cap = SimBench.TicksFromSeconds(CapSeconds);
            List<RelicData> relics = BalanceAssets.LoadRelics();

            var headers = new List<string>
            {
                "Relic", "DPS_solo", "DPS_aoe", "AoE_ratio",
                "AutoPhys%", "AutoMagic%", "Ability%", "DoT%", "React%", "Vuln%", "SelfDmg%",
            };
            var table = new List<IReadOnlyList<object>>();

            foreach (RelicData relic in relics)
            {
                BattleReport soloReport = RunDps(config, relic, aoe: false, cap);
                UnitMetric a = soloReport.Find(0);
                double solo = a != null && soloReport.Seconds > 0 ? a.DamageDealt / soloReport.Seconds : 0.0;

                BattleReport aoeReport = RunDps(config, relic, aoe: true, cap);
                UnitMetric aa = aoeReport.Find(0);
                double aoe = aa != null && aoeReport.Seconds > 0 ? aa.DamageDealt / aoeReport.Seconds : 0.0;

                double ratio = solo > 1e-6 ? aoe / solo : 0.0;
                double total = a != null ? a.DamageDealt : 0.0;
                double Share(double part) => total > 1e-6 ? 100.0 * part / total : 0.0;

                table.Add(new object[]
                {
                    relic.name, solo, aoe, ratio,
                    Share(a?.DamageAutoPhysical ?? 0.0),
                    Share(a?.DamageAutoMagical ?? 0.0),
                    Share(a?.DamageAbility ?? 0.0),
                    Share(a?.DamagePeriodic ?? 0.0),
                    Share(a?.DamageReactive ?? 0.0),
                    Share(a?.DamageFromVulnerability ?? 0.0),
                    Share(a?.SelfDamage ?? 0.0),
                });
            }

            string notes =
                $"**DPS-бенч**: урон/сек до убийства эталонной цели HP={DummyHp:0} (или до потолка {CapSeconds:0} с). " +
                $"DPS_solo — одна цель; DPS_aoe — кластер {ClusterSize} целей (для AoE-китов выше, ratio>1). " +
                "Первые пять «%» — разбивка нанесённого урона (solo-прогон), в сумме 100: авто-атака физикой, " +
                "авто-атака магией (расщеплённый кит бьёт одной атакой в две школы), способность, DoT, ответка. " +
                "**Vuln%** стоит особняком и в сумму НЕ входит: это доля общего урона, добавленная уязвимостями цели " +
                "(«Угли»), она уже сидит внутри строк выше — показывает, сколько кит выигрывает от собственного разгона. " +
                "**SelfDmg%** — плата кита собственным HP, в долях от нанесённого по врагу: в сумму тоже не входит, " +
                "потому что это цена, а не вклад. " +
                "Фикс-HP цели (не 1e9) — чтобы механики «% от HP» не взрывали цифру. Чувствительно к расстановке; " +
                "wind-up первых кадров занижает DPS. Способности/on-hit учтены (полный сим). DPS=0 — кит не бьёт цель (напр. хилер).";

            string csv = ReportWriter.WriteCsv("bench_dps", headers, table);
            string md = ReportWriter.WriteMarkdown("bench_dps", "SimBench — DPS-бенч (Фаза 1)", headers, table, notes);
            ReportWriter.WriteJson("bench_dps", "SimBench — DPS-бенч (Фаза 1)", headers, table, notes);
            return (csv, md);
        }

        private static BattleReport RunDps(StatsConfig config, RelicData relic, bool aoe, int cap)
        {
            var env = new SimEnvironment(Seed, config);
            var tracked = new List<TrackedUnit>
            {
                new TrackedUnit(env.Real(relic, null, 0, new Vector2(0f, 0f)), relic.name, relic.name),
            };

            if (!aoe)
            {
                tracked.Add(new TrackedUnit(SyntheticUnits.ReferenceDummy(1, new Vector2(1.2f, 0f), DummyHp), "dummy", "dummy"));
            }
            else
            {
                Vector2[] cluster =
                {
                    new Vector2(1.2f, 0f), new Vector2(1.7f, 0.45f), new Vector2(1.7f, -0.45f),
                    new Vector2(2.2f, 0.3f), new Vector2(2.2f, -0.3f),
                };
                for (int k = 0; k < ClusterSize; k++)
                    tracked.Add(new TrackedUnit(SyntheticUnits.ReferenceDummy(1, cluster[k], DummyHp), "dummy" + k, "dummy"));
            }

            // UntilOutcome: бой кончается, когда цель(и) мертвы → Seconds = время до убийства (или потолок).
            return SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);
        }
    }
}
