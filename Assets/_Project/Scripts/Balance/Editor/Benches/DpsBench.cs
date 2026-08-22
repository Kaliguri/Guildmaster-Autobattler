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
                "Relic", "DPS_solo", "DPS_summons", "DPS_with_summons", "DPS_aoe", "AoE_ratio",
                "ControlSec", "ControlScore", "ControlShare%", "DmgControlled%",
                "AutoPhys%", "AutoMagic%", "Ability%", "DoT%", "React%", "Vuln%", "SelfDmg%",
                "aoe_AutoPhys%", "aoe_AutoMagic%", "aoe_Ability%", "aoe_DoT%", "aoe_React%",
            };
            var table = new List<IReadOnlyList<object>>();

            foreach (RelicData relic in relics)
            {
                BattleReport soloReport = RunDps(config, relic, aoe: false, cap);
                UnitMetric a = soloReport.Find(0);
                double solo = a != null && soloReport.Seconds > 0 ? a.DamageDealt / soloReport.Seconds : 0.0;

                // Урон армии — своей колонкой рядом с личным: у призывателя личный DPS отвечает на
                // вопрос «чем он машет», а на вопрос «сколько он стоит» отвечает только сумма.
                SummonRollup soloArmy = soloReport.Summons(0);
                double summonDps = soloReport.Seconds > 0 ? soloArmy.DamageDealt / soloReport.Seconds : 0.0;

                BattleReport aoeReport = RunDps(config, relic, aoe: true, cap);
                UnitMetric aa = aoeReport.Find(0);
                double aoe = aa != null && aoeReport.Seconds > 0 ? aa.DamageDealt / aoeReport.Seconds : 0.0;

                double ratio = solo > 1e-6 ? aoe / solo : 0.0;
                double total = a != null ? a.DamageDealt : 0.0;
                double Share(double part) => total > 1e-6 ? 100.0 * part / total : 0.0;

                // Разбивка AoE-прогона считается отдельно от solo: у кита, чья способность требует
                // нескольких целей, в solo она вообще не кастуется — и её доля там ноль не потому, что
                // способность слаба, а потому, что её не было. Без этих колонок вопрос «сколько в AoE от
                // взрыва, а сколько от яда» упирался в итоговое число (Друид, разбор 2026-07-28).
                double aoeTotal = aa != null ? aa.DamageDealt : 0.0;
                double AoeShare(double part) => aoeTotal > 1e-6 ? 100.0 * part / aoeTotal : 0.0;

                table.Add(new object[]
                {
                    relic.name, solo, summonDps, solo + summonDps, aoe, ratio,
                    a?.ControlSecondsDealt ?? 0.0,
                    a?.ControlScore ?? 0.0,
                    soloReport.Seconds > 0 ? 100.0 * (a?.ControlSecondsDealt ?? 0.0) / soloReport.Seconds : 0.0,
                    Share(a?.DamageOnControlled ?? 0.0),
                    Share(a?.DamageAutoPhysical ?? 0.0),
                    Share(a?.DamageAutoMagical ?? 0.0),
                    Share(a?.DamageAbility ?? 0.0),
                    Share(a?.DamagePeriodic ?? 0.0),
                    Share(a?.DamageReactive ?? 0.0),
                    Share(a?.DamageFromVulnerability ?? 0.0),
                    Share(a?.SelfDamage ?? 0.0),
                    AoeShare(aa?.DamageAutoPhysical ?? 0.0),
                    AoeShare(aa?.DamageAutoMagical ?? 0.0),
                    AoeShare(aa?.DamageAbility ?? 0.0),
                    AoeShare(aa?.DamagePeriodic ?? 0.0),
                    AoeShare(aa?.DamageReactive ?? 0.0),
                });
            }

            string notes =
                $"**DPS-бенч**: урон/сек до убийства эталонной цели HP={DummyHp:0} (или до потолка {CapSeconds:0} с). " +
                $"DPS_solo — одна цель; DPS_aoe — кластер {ClusterSize} целей (для AoE-китов выше, ratio>1). " +
                "**DPS_summons** — урон призванных тел, **DPS_with_summons** — сумма: с классовой нормой " +
                "сравнивается именно она, личный DPS призывателя отвечает лишь на вопрос «чем он машет сам». " +
                "Проценты разбивки считаются от ЛИЧНОГО урона — тела бьют своим китом, и мешать их доли с " +
                "долями хозяина значило бы складывать разные разбивки в одну. " +
                "Первые пять «%» — разбивка нанесённого урона (solo-прогон), в сумме 100: авто-атака физикой, " +
                "авто-атака магией (расщеплённый кит бьёт одной атакой в две школы), способность, DoT, ответка. " +
                "**Vuln%** стоит особняком и в сумму НЕ входит: это доля общего урона, добавленная уязвимостями цели " +
                "(«Угли»), она уже сидит внутри строк выше — показывает, сколько кит выигрывает от собственного разгона. " +
                "**SelfDmg%** — плата кита собственным HP, в долях от нанесённого по врагу: в сумму тоже не входит, " +
                "потому что это цена, а не вклад. " +
                "Колонки **aoe_\\*** — та же разбивка, но по AoE-прогону: у кита, чья способность требует нескольких " +
                "целей, в solo она не кастуется вовсе, и её доля там ноль по отсутствию, а не по слабости. " +
                "Сравнивать solo- и aoe-доли имеет смысл только с оглядкой на AoE_ratio — знаменатели разные. " +
                "**Контроль — четыре колонки, и они не про урон.** ControlSec — сколько секунд контроля кит " +
                "наложил на цель (сон, оглушение, заморозка, подброс), **ControlScore** — те же секунды, " +
                "взвешенные ценой эффекта (1.0 полный запрет, 0.5 частичный, 0.166 замедление), " +
                "ControlShare% — какую долю боя цель простояла под ним, DmgControlled% — доля урона, " +
                "попавшая в это окно. Сравнивать китов надо по СЧЁТУ, а не по секундам: четыре секунды " +
                "замедления и четыре секунды сна — это одни и те же секунды и вшестеро разная цена. " +
                "Кит, у которого DPS ниже нормы, а DmgControlled% под сотню, — не слабый: он просто " +
                "меряется не тем. Пожиратель снов по своей карточке в открытом размене не бьётся вовсе " +
                "(`docs/balance-issues.md` §BAL-032). " +
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
