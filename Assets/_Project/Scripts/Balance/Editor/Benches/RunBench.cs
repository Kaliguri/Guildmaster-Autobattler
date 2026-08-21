using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Линза ЗАБЕГА: не «прошёл ли бой», а «сколько ран отряд наберёт за акт». Гоняет цепочку боёв по
    /// настоящему маршруту акта — карта генерируется тем же <see cref="MapGenerator"/>, что и в игре, —
    /// и считает смерти по узлам, раскладывая их в слоты ран по правилам ГДД.
    /// </summary>
    /// <remarks>
    /// <para><b>Зачем отдельно от <see cref="EncounterBench"/>.</b> Тот меряет один бой и отвечает
    /// «прошёл, вот цена». Но истощение забега у нас держат раны за смерти
    /// (<c>gdd/30-run-meta/injuries-mettle</c>, решено 2026-08-21), а раны копятся ЧЕРЕЗ бои: вопрос
    /// «годятся ли ёмкости 3/2/1» одним боем не решается в принципе. HP между боями не переносится —
    /// каждый бой начинается с полного запаса, и это не упрощение стенда, а механика игры.</para>
    ///
    /// <para><b>Три прогона на один маршрут, и это главное.</b> Норма Макса (2026-08-21): смертность
    /// должна приходить от попытки выполнить квест, а рядовой бой при правильной расстановке смертей
    /// давать не должен — но при неправильной обязан. Одно число «смертей на бой» на это не отвечает,
    /// поэтому маршрут проходится трижды: штатным отрядом, кривым составом «под квест»
    /// (<see cref="Lineups.MeleeOnly"/>) и штатным составом в вывернутой расстановке
    /// (<see cref="Lineups.SquadInverted"/>). Разница между первым и вторым — цена квеста; между первым
    /// и третьим — цена ошибки в расстановке. Настраиваются они порознь.</para>
    ///
    /// <para><b>Отряд собирается из настоящих реликвий, а не манекенов</b> — см.
    /// <see cref="PickHeroes"/>: эталонный боец не умеет ни лечить, ни кастовать, и отряд из таких
    /// болванок сравнивает суммы урона вместо ролей.</para>
    ///
    /// <para><b>Чего бенч НЕ делает.</b> Раны здесь только считаются, но на бой не влияют: их эффектов
    /// в движке пока нет вовсе. То есть отчёт отвечает «сколько ран набежит», а не «каково играть
    /// ранеными» — второй вопрос откроется, когда раны появятся в коде.</para>
    /// </remarks>
    public static class RunBench
    {
        /// <summary>Сколько маршрутов акта прогоняется. Карта роллится, поэтому одного мало.</summary>
        private const int Routes = 8;

        /// <summary>Тот же потолок боя, что во всех форматах стенда.</summary>
        private const float CapSeconds = 240f;

        /// <summary>База сидов. Маршрут и каждый бой получают свой производный сид — детерминированно.</summary>
        private const ulong SeedBase = 1UL;

        /// <summary>
        /// Лист ран одного бойца — тонкая обёртка над ИГРОВЫМ каскадом <see cref="InjuryCascade"/>.
        /// </summary>
        /// <remarks>
        /// Своей модели правила здесь нет намеренно. Первая версия бенча несла собственную копию
        /// каскада (игрового кода ран тогда ещё не было), и это ровно тот случай, когда две копии
        /// расходятся молча: стенд продолжал бы показывать числа игры, в которую мы не играем.
        /// Ёмкости слотов тоже спрашиваются у каскада, а не объявляются рядом.
        /// </remarks>
        private struct WoundSheet
        {
            private InjurySlots _slots;

            /// <summary>Слоты кончились: боец выбыл из забега и ран больше не принимает.</summary>
            public bool Retired;

            public int Bruises => _slots.Bruises;
            public int Wounds  => _slots.Wounds;
            public int Maims   => _slots.Maimings;

            /// <summary>Есть ли ещё место под мелкую — по нему бенч выбирает, кому отдать рану.</summary>
            public bool HasFreeBruiseSlot => !Retired && _slots.Free(InjuryGrade.Bruise) > 0;

            /// <summary>
            /// Положить мелкую рану и вернуть ступень, которая легла на самом деле; <c>null</c> —
            /// класть было некуда и боец выбыл из забега.
            /// </summary>
            public InjuryGrade? Add()
            {
                if (Retired) return null;

                InjuryOutcome outcome = InjuryCascade.Resolve(_slots, InjuryGrade.Bruise);
                if (outcome.Retired)
                {
                    Retired = true;
                    return null;
                }

                _slots = _slots.With(outcome.Grade);
                return outcome.Grade;
            }
        }

        /// <summary>Режим прохождения маршрута — чем именно отличается отряд.</summary>
        private readonly struct Mode
        {
            public readonly string Key;
            public readonly string Title;
            public readonly Slot[] Lineup;

            public Mode(string key, string title, Slot[] lineup)
            {
                Key = key;
                Title = title;
                Lineup = lineup;
            }
        }

        private static readonly Mode[] Modes =
        {
            new Mode("reference", "Штатный отряд", Lineups.Squad),
            new Mode("quest",     "Кривой состав (квест «только ближники»)", Lineups.MeleeOnly),
            new Mode("misplaced", "Кривая расстановка (танк в тылу)", Lineups.SquadInverted),
        };

        /// <summary>Итог одного боя на узле маршрута.</summary>
        private readonly struct NodeResult
        {
            public readonly bool Cleared;
            public readonly int Fallen;
            public readonly double TeamHpPct;
            public readonly bool TimedOut;

            public NodeResult(bool cleared, int fallen, double teamHpPct, bool timedOut)
            {
                Cleared = cleared;
                Fallen = fallen;
                TeamHpPct = teamHpPct;
                TimedOut = timedOut;
            }
        }

        /// <summary>Накопленное по режиму за все маршруты.</summary>
        private sealed class ModeTally
        {
            public int Battles;
            public int Cleared;
            public int Deaths;
            public double HpCostSum;
            public int TimedOut;

            /// <summary>Раны по ступеням, суммарно за все маршруты.</summary>
            public int Bruises, Wounds, Maims, Retired;

            /// <summary>Сколько маршрутов дошло до конца, не потеряв ни одного бойца из забега.</summary>
            public int RoutesIntact;

            /// <summary>На каком по счёту бою впервые забились все мелкие слоты (сумма по маршрутам, где забились).</summary>
            public int FirstOverflowSum;
            public int FirstOverflowRoutes;

            /// <summary>Смерти по позиции боя в маршруте — кривая, ради которой всё затевалось.</summary>
            public readonly List<int> DeathsByIndex = new List<int>();
            public readonly List<int> BattlesByIndex = new List<int>();

            public void CountAt(int index, int deaths)
            {
                while (DeathsByIndex.Count <= index) { DeathsByIndex.Add(0); BattlesByIndex.Add(0); }
                DeathsByIndex[index] += deaths;
                BattlesByIndex[index]++;
            }
        }

        public static (string csv, string md) Run()
        {
            StatsConfig config = BalanceAssets.LoadStatsConfig();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();
            Dictionary<string, EnemyData> enemiesById = EncounterSetup.IndexEnemies();
            int cap = SimBench.TicksFromSeconds(CapSeconds);

            Dictionary<EncounterTier, List<EncounterData>> pools = PoolsByTier(enemiesById);
            var missing = new List<string>();
            if (Count(pools, EncounterTier.Common) == 0) missing.Add("рядовых");
            if (Count(pools, EncounterTier.Elite) == 0) missing.Add("элитных");
            if (Count(pools, EncounterTier.Finalist) == 0) missing.Add("боссов");

            if (Count(pools, EncounterTier.Common) == 0)
            {
                const string empty = "Рядовых энкаунтеров нет ни одного — маршрут собрать не из чего.";
                Debug.LogWarning("[SimBench] RunBench: " + empty);
                return ("", "# Забег по акту\n\n" + empty + "\n");
            }

            MapGenConfig mapConfig = BalanceAssets.LoadActConfig() is ActConfig act
                ? act.ToGenConfig()
                : new MapGenConfig().Validated();

            var tallies = new Dictionary<string, ModeTally>();
            var heroesByMode = new Dictionary<string, RelicData[]>();
            List<RelicData> relics = BalanceAssets.LoadRelics();
            var thinRoles = new List<string>();

            foreach (Mode m in Modes)
            {
                tallies[m.Key] = new ModeTally();
                heroesByMode[m.Key] = PickHeroes(m.Lineup, relics, thinRoles);
            }

            for (int r = 0; r < Routes; r++)
            {
                // Маршрут один и тот же для всех трёх режимов — иначе сравнивались бы разные акты,
                // а не разные отряды.
                List<MapNodeType> route = BuildRoute(mapConfig, SeedBase + (ulong)r);

                foreach (Mode mode in Modes)
                    WalkRoute(config, classes, enemiesById, pools, cap, mode, route,
                        tallies[mode.Key], (ulong)r, heroesByMode[mode.Key]);
            }

            return WriteReports(tallies, pools, missing, thinRoles, heroesByMode);
        }

        // --- Маршрут ---

        /// <summary>
        /// Сгенерировать акт и пройти его от Старта до Босса, вернув типы боевых узлов по порядку.
        /// </summary>
        /// <remarks>
        /// Ход по графу спрашивается у <see cref="MapTraversal"/> — того же владельца, что и в игре:
        /// свой обход здесь означал бы, что стенд ходит по карте иначе, чем игрок. «?»-узлы считаются
        /// НЕбоевыми (в игре бой выпадает из них примерно в пятой части случаев), поэтому настоящий акт
        /// чуть тяжелее замеренного — допущение названо в отчёте.
        /// </remarks>
        private static List<MapNodeType> BuildRoute(MapGenConfig mapConfig, ulong seed)
        {
            IRngService rng = new XorShiftRng(seed);
            MapState map = MapGenerator.Generate(rng, mapConfig);
            var battles = new List<MapNodeType>();

            for (int guard = 0; guard < 64; guard++)
            {
                IReadOnlyList<MapNode> next = MapTraversal.AvailableNext(map);
                if (next == null || next.Count == 0) break;

                MapNode step = next[rng.NextInt(0, next.Count)];
                if (!MapTraversal.Advance(map, step.Id)) break;

                if (step.Type is MapNodeType.Battle or MapNodeType.Elite or MapNodeType.Boss)
                    battles.Add(step.Type);

                if (MapTraversal.IsActComplete(map)) break;
            }

            return battles;
        }

        /// <summary>Пройти маршрут одним режимом: бой за боем, копя смерти в листы ран отряда.</summary>
        private static void WalkRoute(StatsConfig config, ClassBalanceConfig classes,
            Dictionary<string, EnemyData> enemiesById, Dictionary<EncounterTier, List<EncounterData>> pools,
            int cap, Mode mode, List<MapNodeType> route, ModeTally tally, ulong routeSeed,
            IReadOnlyList<RelicData> heroes)
        {
            var sheets = new WoundSheet[mode.Lineup.Length];
            int firstOverflowAt = -1;

            for (int i = 0; i < route.Count; i++)
            {
                EncounterTier tier = TierOf(route[i]);
                EncounterData encounter = Pick(pools, tier, routeSeed, i);
                if (encounter == null) continue;

                // Сид боя разный на каждом узле: один сид на весь маршрут дал бы N копий одного боя.
                ulong seed = SeedBase + routeSeed * 1000UL + (ulong)i;
                NodeResult res = RunNode(config, classes, enemiesById, encounter, cap, mode.Lineup,
                    heroes, seed);

                tally.Battles++;
                if (res.Cleared) tally.Cleared++;
                if (res.TimedOut) tally.TimedOut++;
                tally.HpCostSum += 1.0 - res.TeamHpPct;
                tally.Deaths += res.Fallen;
                tally.CountAt(i, res.Fallen);

                // Раны кладём тем, кто лёг. Кто именно — бенч не знает (метрика даёт число павших),
                // поэтому раздаём по кругу: для вопроса «когда забьются слоты» важно их число, а не имя.
                for (int d = 0; d < res.Fallen; d++)
                {
                    int target = PickWoundTarget(sheets, d);
                    if (target < 0) break;

                    InjuryGrade? grade = sheets[target].Add();
                    switch (grade)
                    {
                        case InjuryGrade.Bruise:  tally.Bruises++; break;
                        case InjuryGrade.Wound:   tally.Wounds++;  break;
                        case InjuryGrade.Maiming: tally.Maims++;   break;
                        case null:                tally.Retired++; break;
                    }

                    if (grade != InjuryGrade.Bruise && firstOverflowAt < 0) firstOverflowAt = i + 1;
                }
            }

            bool intact = true;
            for (int s = 0; s < sheets.Length; s++) if (sheets[s].Retired) intact = false;
            if (intact) tally.RoutesIntact++;

            if (firstOverflowAt > 0)
            {
                tally.FirstOverflowSum += firstOverflowAt;
                tally.FirstOverflowRoutes++;
            }
        }

        /// <summary>
        /// Кому достанется рана. Сперва тем, у кого ещё свободны мелкие слоты — иначе один невезучий
        /// боец собрал бы весь каскад, и замер показал бы выбывание там, где его в игре не будет.
        /// </summary>
        private static int PickWoundTarget(WoundSheet[] sheets, int nth)
        {
            for (int s = 0; s < sheets.Length; s++)
            {
                int idx = (nth + s) % sheets.Length;
                if (sheets[idx].HasFreeBruiseSlot) return idx;
            }

            for (int s = 0; s < sheets.Length; s++)
            {
                int idx = (nth + s) % sheets.Length;
                if (!sheets[idx].Retired) return idx;
            }

            return -1;
        }

        // --- Один бой ---

        private static NodeResult RunNode(StatsConfig config, ClassBalanceConfig classes,
            Dictionary<string, EnemyData> enemiesById, EncounterData encounter, int cap, Slot[] lineup,
            IReadOnlyList<RelicData> heroes, ulong seed)
        {
            var env = new SimEnvironment(seed, config);
            var tracked = new List<TrackedUnit>();

            Lineups.SpawnTeam(env, classes, tracked, heroes, 0, lineup);
            EncounterSetup.SpawnEnemies(env, tracked, encounter, enemiesById);

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);

            double hpLeft = 0.0, maxHp = 0.0;
            int fallen = 0;
            for (int i = 0; i < report.Units.Count; i++)
            {
                UnitMetric m = report.Units[i];
                if (m.Team != 0 || m.IsSummon) continue;

                hpLeft += m.HpLeft;
                maxHp += m.MaxHp;
                if (m.Died) fallen++;
            }

            bool cleared = !report.TimedOut && report.Outcome.IsWinFor(0);
            return new NodeResult(cleared, fallen, maxHp > 0.0 ? hpLeft / maxHp : 0.0, report.TimedOut);
        }

        /// <summary>
        /// Собрать отряд НАСТОЯЩИХ реликвий под строй — по одной на слот, по совпадению роли.
        /// </summary>
        /// <remarks>
        /// <b>Манекенами здесь мерить нельзя, и это стоило одного ложного вывода.</b> Эталонный боец
        /// (<c>SyntheticUnits.ReferenceAlly</c>) — чистые статы без единой способности: «поддержка» из
        /// него не лечит, она просто стоит с низкой нормой DPS. Отряд из таких манекенов сравнивает не
        /// роли, а суммы урона, и первый прогон 2026-08-21 честно показал, что четыре ближника проходят
        /// акт «дешевле» штатной четвёрки — вывод, целиком порождённый инструментом.
        /// <para>Реликвии берутся детерминированно (порядок ассетов), <c>relic.base</c> исключён: он
        /// заведомо слабейший в ростере и мерил бы не отряд, а свою болванку. Роль без реликвии
        /// закрывается манекеном по-прежнему — и такие роли перечисляются в отчёте, потому что молчаливый
        /// манекен в строю снова сделал бы замер ложным.</para>
        /// </remarks>
        private static RelicData[] PickHeroes(Slot[] lineup, List<RelicData> relics, List<string> thinRoles)
        {
            var picked = new List<RelicData>(lineup.Length);
            var used = new HashSet<string>();

            for (int s = 0; s < lineup.Length; s++)
            {
                RelicData found = null;
                for (int r = 0; r < relics.Count; r++)
                {
                    RelicData relic = relics[r];
                    if (relic == null || string.IsNullOrEmpty(relic.Id)) continue;
                    if (relic.Id == ContentIds.BaseRelic) continue;
                    if (used.Contains(relic.Id)) continue;
                    if (Lineups.SlotRole(relic.CombatClass) != lineup[s].Role) continue;

                    found = relic;
                    break;
                }

                if (found == null)
                {
                    string role = lineup[s].Role.ToString();
                    if (!thinRoles.Contains(role)) thinRoles.Add(role);
                    continue;
                }

                picked.Add(found);
                used.Add(found.Id);
            }

            return picked.ToArray();
        }

        // --- Пулы энкаунтеров ---

        private static Dictionary<EncounterTier, List<EncounterData>> PoolsByTier(
            Dictionary<string, EnemyData> enemiesById)
        {
            var pools = new Dictionary<EncounterTier, List<EncounterData>>();
            List<EncounterData> all = BalanceAssets.LoadEncounters();

            for (int i = 0; i < all.Count; i++)
            {
                EncounterData e = all[i];
                if (e == null || e.Tier == EncounterTier.Special) continue;   // Special на карте не спавнится
                if (!EncounterSetup.IsPlayable(e, enemiesById)) continue;

                if (!pools.TryGetValue(e.Tier, out List<EncounterData> pool))
                    pools[e.Tier] = pool = new List<EncounterData>();
                pool.Add(e);
            }

            return pools;
        }

        private static int Count(Dictionary<EncounterTier, List<EncounterData>> pools, EncounterTier tier)
            => pools.TryGetValue(tier, out List<EncounterData> pool) ? pool.Count : 0;

        private static EncounterTier TierOf(MapNodeType type) => type switch
        {
            MapNodeType.Elite => EncounterTier.Elite,
            MapNodeType.Boss  => EncounterTier.Finalist,
            _                 => EncounterTier.Common,
        };

        /// <summary>
        /// Энкаунтер нужного тира. Пул пуст — откатываемся на ступень ниже и говорим об этом вслух:
        /// босса акта в проекте пока нет вовсе, и молчаливая подмена рядовым боем выдала бы замер
        /// лёгкого акта за замер настоящего.
        /// </summary>
        private static EncounterData Pick(Dictionary<EncounterTier, List<EncounterData>> pools,
            EncounterTier tier, ulong routeSeed, int index)
        {
            if (!pools.TryGetValue(tier, out List<EncounterData> pool) || pool.Count == 0)
            {
                if (tier == EncounterTier.Finalist) return Pick(pools, EncounterTier.Elite, routeSeed, index);
                if (tier == EncounterTier.Elite) return Pick(pools, EncounterTier.Common, routeSeed, index);
                return null;
            }

            // Детерминированный выбор без своего RNG: индекс узла и сид маршрута задают его однозначно.
            int pick = (int)((routeSeed * 31UL + (ulong)index) % (ulong)pool.Count);
            return pool[pick];
        }


        // --- Отчёты ---

        /// <summary>
        /// Два отчёта из одного прогона: <c>run_act</c> — что акт спрашивает у трёх разных отрядов,
        /// <c>run_curve</c> — как смертность идёт по ходу маршрута. Возвращает пути (контракт шага круга:
        /// бенч пишет файлы сам, наружу отдаёт, куда написал).
        /// </summary>
        private static (string csv, string md) WriteReports(Dictionary<string, ModeTally> tallies,
            Dictionary<EncounterTier, List<EncounterData>> pools, List<string> missing,
            List<string> thinRoles, Dictionary<string, RelicData[]> heroesByMode)
        {
            string[] headers =
            {
                "Отряд", "Боёв", "Прошёл%", "СмертейНаБой", "ЦенаБоя%HP",
                "Мелких", "Средних", "Тяжёлых", "Выбыло", "МаршрутовБезПотерь%", "ПервоеПереполнениеНаБою",
            };

            var rows = new List<IReadOnlyList<object>>();
            foreach (Mode m in Modes)
            {
                ModeTally t = tallies[m.Key];
                if (t.Battles == 0) continue;

                rows.Add(new object[]
                {
                    m.Title,
                    t.Battles,
                    t.Cleared * 100.0 / t.Battles,
                    t.Deaths / (double)t.Battles,
                    t.HpCostSum * 100.0 / t.Battles,
                    t.Bruises, t.Wounds, t.Maims, t.Retired,
                    t.RoutesIntact * 100.0 / Routes,
                    t.FirstOverflowRoutes > 0 ? (object)(t.FirstOverflowSum / (double)t.FirstOverflowRoutes) : "—",
                });
            }

            string notes = Notes(pools, missing, thinRoles, heroesByMode);
            string csv = ReportWriter.WriteCsv("run_act", headers, rows);
            string md = ReportWriter.WriteMarkdown("run_act", "Забег по акту: сколько ран стоит проход",
                headers, rows, notes);
            ReportWriter.WriteJson("run_act", "Забег по акту: сколько ран стоит проход", headers, rows, notes);

            WriteCurve(tallies);
            return (csv, md);
        }

        /// <summary>Кривая смертности по позиции боя в маршруте — растёт акт или стоит на месте.</summary>
        private static void WriteCurve(Dictionary<string, ModeTally> tallies)
        {
            int longest = 0;
            foreach (Mode m in Modes)
                if (tallies[m.Key].DeathsByIndex.Count > longest) longest = tallies[m.Key].DeathsByIndex.Count;
            if (longest == 0) return;

            var headers = new List<string> { "Отряд" };
            for (int i = 0; i < longest; i++) headers.Add("бой " + (i + 1));

            var rows = new List<IReadOnlyList<object>>();
            foreach (Mode m in Modes)
            {
                ModeTally t = tallies[m.Key];
                if (t.Battles == 0) continue;

                var row = new List<object> { m.Title };
                for (int i = 0; i < longest; i++)
                {
                    bool has = i < t.BattlesByIndex.Count && t.BattlesByIndex[i] > 0;
                    row.Add(has ? (object)(t.DeathsByIndex[i] / (double)t.BattlesByIndex[i]) : "—");
                }
                rows.Add(row);
            }

            const string notes =
                "Столбцы — ПОРЯДКОВЫЙ НОМЕР боя на маршруте, а не этаж карты: маршруты роллятся, и на " +
                "одном этаже боя может не быть вовсе. Правые столбцы опираются на меньшее число " +
                "маршрутов, чем левые, — короткий маршрут до них просто не доходит.";

            ReportWriter.WriteCsv("run_curve", headers, rows);
            ReportWriter.WriteMarkdown("run_curve", "Забег: смертей на бой по ходу маршрута",
                headers, rows, notes);
            ReportWriter.WriteJson("run_curve", "Забег: смертей на бой по ходу маршрута",
                headers, rows, notes);
        }

        private static string Notes(Dictionary<EncounterTier, List<EncounterData>> pools,
            List<string> missing, List<string> thinRoles, Dictionary<string, RelicData[]> heroesByMode)
        {
            var sb = new StringBuilder();
            sb.AppendLine("**Линза забега, а не боя.** Маршрут генерируется тем же `MapGenerator`, что и в " +
                          "игре, и проходится боем за боем. HP между боями НЕ переносится (механика игры, " +
                          "не упрощение стенда) — истощение держат **раны за смерти**, " +
                          "`gdd/30-run-meta/injuries-mettle`.");
            sb.AppendLine();
            sb.AppendLine($"Маршрутов: **{Routes}**, каждый пройден тремя отрядами. Слоты ран — " +
                          $"**{InjuryCascade.BruiseSlots}** мелких, **{InjuryCascade.WoundSlots}** средних, " +
                          $"**{InjuryCascade.MaimingSlots}** тяжёлая; " +
                          "переполнение поднимает ступень, переполнение тяжёлой уводит бойца из забега.");
            sb.AppendLine();
            sb.AppendLine("**Как читать.** Норма (решение Макса 2026-08-21): смертность должна приходить от " +
                          "попытки выполнить квест, а не от фона. У строки «Штатный отряд» смертей на бой " +
                          "должно быть **около нуля**, у двух других — заметно больше. Если штатный отряд " +
                          "гибнет наравне с кривым, акт наказывает не за то.");
            sb.AppendLine();
            sb.AppendLine("**Цена квеста** — разница смертности между «Штатным отрядом» и «Кривым составом». " +
                          "**Цена ошибки в расстановке** — разница между «Штатным отрядом» и «Кривой " +
                          "расстановкой». Две разные ручки, крутятся порознь.");
            sb.AppendLine();
            sb.AppendLine("**Кто дерётся.** Каждый слот строя закрыт НАСТОЯЩЕЙ реликвией своей роли " +
                          "(`relic.base` исключён). Это не мелочь: эталонный манекен способностей не имеет " +
                          "вовсе — «поддержка» из него не лечит, — и отряд из манекенов сравнивал бы не " +
                          "роли, а суммы урона.");
            sb.AppendLine();

            foreach (Mode m in Modes)
            {
                RelicData[] hs = heroesByMode[m.Key];
                var names = new List<string>(hs.Length);
                for (int i = 0; i < hs.Length; i++) names.Add(hs[i].name);
                sb.AppendLine($"- **{m.Title}:** {(names.Count > 0 ? string.Join(", ", names) : "—")}");
            }
            sb.AppendLine();

            if (thinRoles.Count > 0)
            {
                sb.AppendLine($"**Ролей без реликвии: {string.Join(", ", thinRoles)}** — их слоты закрыты " +
                              "манекенами, и по этим ролям замер занижен.");
                sb.AppendLine();
            }

            sb.AppendLine("**Слепые пятна.** Раны не влияют на бой — их эффектов в движке нет, так что " +
                          "настоящий акт будет тяжелее замеренного. «?»-узлы считаются небоевыми, а в игре " +
                          "бой выпадает из них примерно в пятой части случаев. Состав фиксированный (по " +
                          "первой реликвии на роль) — это одна точка, а не срез по ростеру. Кто именно " +
                          "лёг, бенч не знает: раны раздаются по кругу, начиная с тех, у кого свободны " +
                          "мелкие слоты.");
            sb.AppendLine();

            if (missing.Count > 0)
            {
                sb.AppendLine($"**Пулы пусты: {string.Join(", ", missing)}.** Такие узлы закрываются " +
                              "энкаунтером ступенью ниже — то есть замеренный акт ЛЕГЧЕ задуманного.");
                sb.AppendLine();
            }

            sb.AppendLine($"Пулы энкаунтеров: рядовых **{Count(pools, EncounterTier.Common)}**, элитных " +
                          $"**{Count(pools, EncounterTier.Elite)}**, боссов " +
                          $"**{Count(pools, EncounterTier.Finalist)}**.");
            return sb.ToString();
        }
    }
}
