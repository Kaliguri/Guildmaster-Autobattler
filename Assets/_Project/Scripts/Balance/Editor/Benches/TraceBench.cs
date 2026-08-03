using System.Collections.Generic;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Разбор ОДНОГО боя: тот же прогон, что в бенчах, но с лентой событий вместо агрегатов
    /// (<see cref="BattleTrace"/>). Бой задаётся выделением в Project.
    /// </summary>
    /// <remarks>
    /// <para>Три формы, потому что три разных вопроса: <b>кит + энкаунтер</b> — «почему этот бой стоит так
    /// дорого» (PvE, главный случай); <b>две реликвии</b> — «почему он проигрывает дуэль, делая всё
    /// правильно»; <b>сценарий</b> — авторский бой, который уже закреплён ассетом.</para>
    /// <para>Отчёт — только Markdown: лента нужна для чтения, а не для спредшита и не для сайта. CSV и JSON
    /// не пишутся намеренно — на сайте эта таблица никому не помогает, а прогон бы засоряла.</para>
    /// </remarks>
    public static class TraceBench
    {
        private const float CapSeconds = 240f;
        private const ulong Seed = 1UL;

        /// <summary>Понятен ли выделенный набор ассетов трейсу (для validate-пункта меню).</summary>
        public static bool CanTrace(Object[] selection) => Parse(selection, out _, out _, out _);

        /// <summary>
        /// Прогнать бой по выделению и записать ленту. Возвращает путь Markdown или null, если из
        /// выделения бой не собирается (тогда в консоль уходит подсказка, что выделить).
        /// </summary>
        public static string RunSelection(Object[] selection)
        {
            if (!Parse(selection, out List<RelicData> relics, out EncounterData encounter,
                    out BalanceScenarioData scenario))
            {
                Debug.LogWarning("[SimBench] Трейс не знает, какой бой прогнать. Выдели в Project одно из: " +
                                 "реликвию + энкаунтер (бой PvE), две реликвии (дуэль) или BalanceScenarioData.");
                return null;
            }

            StatsConfig config = BalanceAssets.LoadStatsConfig();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();

            ulong seed = scenario != null ? scenario.Seed : Seed;
            float capSeconds = scenario != null ? scenario.MaxSeconds : CapSeconds;

            var env = new SimEnvironment(seed, config);
            var tracked = new List<TrackedUnit>();
            string title, setup;

            if (scenario != null)
            {
                ScenarioSides(env, tracked, scenario);
                title = "сценарий «" + scenario.name + "»";
                setup = $"Стороны заданы ассетом (сид {seed}, потолок {capSeconds:0} с).";
            }
            else if (encounter != null)
            {
                RelicData kit = relics.Count > 0 ? relics[0] : null;
                Lineups.SpawnTeam(env, classes, tracked, kit != null ? new[] { kit } : new RelicData[0], 0,
                    Lineups.Squad);
                EncounterFacts facts = EncounterSetup.SpawnEnemies(env, tracked, encounter,
                    EncounterSetup.IndexEnemies());

                title = (kit != null ? kit.name : "штатный отряд") + " против «" + encounter.name + "»";
                setup = $"PvE: отряд игрока (кит в слоте своей роли + эталонные манекены) против энкаунтера " +
                        $"тира {encounter.Tier} — {facts.Enemies} врагов, {facts.EnemyHp:0} HP, " +
                        $"{facts.Threat} очков опасности.";
            }
            else
            {
                Lineups.SpawnTeam(env, classes, tracked, new[] { relics[0] }, 0, Lineups.Solo);
                Lineups.SpawnTeam(env, classes, tracked, new[] { relics[1] }, 1, Lineups.Solo);
                title = relics[0].name + " против " + relics[1].name;
                setup = "Дуэль 1v1 без поддержки.";
            }

            // Писарь подписывается ДО прогона и живёт ровно этот бой.
            var trace = new BattleTrace(env, tracked);
            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome,
                SimBench.TicksFromSeconds(capSeconds));

            return Write(title, setup, report, trace, tracked);
        }

        private static string Write(string title, string setup, BattleReport report, BattleTrace trace,
            List<TrackedUnit> tracked)
        {
            string outcome = report.TimedOut
                ? $"НЕТ ИСХОДА (потолок {report.Seconds:0} с)"
                : report.Outcome.IsWinFor(0) ? "победа отряда игрока"
                : report.Outcome.IsWinFor(1) ? "поражение отряда игрока"
                : "ничья";

            // Итог боя стоит ПЕРЕД лентой: читатель приходит с вопросом «почему так вышло», и ответ
            // «а как вышло» он должен получить в первой же строке, а не после двух тысяч событий.
            string roster = Roster(report, tracked);

            string notes =
                $"**{setup}** **Исход:** {outcome} за {report.Seconds:0.0} с. " + roster +
                " Лента ниже — фактические события боя в порядке случившегося: урон берётся из тех же " +
                "`DamageResult`, что и метрики, длительности эффектов — с учётом сопротивления. " +
                "Колонка «HP цели» — состояние цели СРАЗУ ПОСЛЕ события, поэтому по ней видно не только " +
                "«сколько ударил», но и «сколько это значило». " +
                (trace.Truncated
                    ? "**Лента оборвана по потолку строк** — конец боя в неё не попал; сокращай состав или " +
                      "потолок боя, если нужен финал."
                    : "");

            return ReportWriter.WriteMarkdown("trace_" + Safe(title),
                "SimBench — разбор боя: " + title, BattleTrace.Headers, trace.Rows, notes);
        }

        /// <summary>Кто вышел из боя живым и с каким запасом — короткая сводка в шапке ленты.</summary>
        private static string Roster(BattleReport report, List<TrackedUnit> tracked)
        {
            var alive = new List<string>();
            var dead = new List<string>();
            for (int i = 0; i < report.Units.Count; i++)
            {
                UnitMetric m = report.Units[i];
                string side = m.Team == 0 ? "игрок" : "враг";
                if (m.Died) dead.Add($"{m.DisplayLabel} ({side})");
                else alive.Add($"{m.DisplayLabel} ({side}, {100.0 * m.HpPctLeft:0}%)");
            }

            return "**Выжили:** " + (alive.Count > 0 ? string.Join(", ", alive) : "никто") +
                   ". **Погибли:** " + (dead.Count > 0 ? string.Join(", ", dead) : "никто") + ".";
        }

        private static void ScenarioSides(SimEnvironment env, List<TrackedUnit> tracked,
            BalanceScenarioData scenario)
        {
            SpawnScenarioSide(env, tracked, scenario.TeamA, 0, "A", -3f);
            SpawnScenarioSide(env, tracked, scenario.TeamB, 1, "B", 3f);
        }

        private static void SpawnScenarioSide(SimEnvironment env, List<TrackedUnit> tracked,
            IReadOnlyList<BalanceScenarioData.SideEntry> side, int team, string label, float baseX)
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
                    tracked.Add(new TrackedUnit(env.Real(entry.Unit, entry.Vessel, team, new Vector2(x, y)),
                        label + ":" + entry.Unit.name + "#" + c, entry.Unit.name));
                    row++;
                }
            }
        }

        /// <summary>
        /// Разобрать выделение в один из трёх понятных боёв. Ложь означает «выделено не то» — прогонять
        /// что-то похожее наугад нельзя, разбор боя должен быть о том бое, который заказали.
        /// </summary>
        private static bool Parse(Object[] selection, out List<RelicData> relics, out EncounterData encounter,
            out BalanceScenarioData scenario)
        {
            relics = new List<RelicData>();
            encounter = null;
            scenario = null;

            if (selection == null) return false;

            for (int i = 0; i < selection.Length; i++)
            {
                switch (selection[i])
                {
                    case BalanceScenarioData s: scenario = s; break;
                    case EncounterData e: encounter = e; break;
                    case RelicData r: relics.Add(r); break;
                }
            }

            if (scenario != null) return true;
            if (encounter != null) return true;              // кит опционален: отряд манекенов тоже бой
            return relics.Count == 2;
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
