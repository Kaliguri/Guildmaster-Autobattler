using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Фаза 2 — round-robin бои всех реликвий + рейтинг Bradley-Terry. Каждая упорядоченная пара
    /// (i слева vs j справа) гоняется отдельно → каждая неупорядоченная пара сыграна дважды (обе стороны),
    /// что усредняет возможное преимущество левой/правой позиции. Выдаёт сводку (Wins/WinRate/Strength) и
    /// матрицу матчапов. Оговорка: бой RNG-free → исходы детерминированы (1/0), win-rate дискретный.
    /// <para>Три формата — один и тот же прогон с разным числом союзных манекенов вокруг испытуемого:
    /// 1v1 (кит в вакууме), 3v3 и 5v5. Командные форматы существуют потому, что дуэль не умеет мерить
    /// китов, чья работа — не убивать: хилер, баффер и держатель фронта в вакууме всегда выглядят мусором.</para>
    /// </summary>
    public static class DuelMatrixBench
    {
        private const float CapSeconds = 120f;
        private const ulong Seed = 1UL;

        /// <summary>
        /// Слот боевого строя: какую роль он занимает и где стоит (координаты для команды 0, для команды 1
        /// зеркалятся по X). Кит встаёт в слот СВОЕГО класса, вытесняя оттуда манекен, — так формат
        /// отвечает на вопрос «этот кит лучше или хуже рядового представителя своей роли».
        /// </summary>
        private readonly struct Slot
        {
            public readonly UnitClass Role;
            public readonly Vector2 Pos;

            public Slot(UnitClass role, float x, float y)
            {
                Role = role;
                Pos = new Vector2(x, y);
            }
        }

        /// <summary>Строй стороны и подписи отчёта.</summary>
        private readonly struct Format
        {
            public readonly Slot[] Lineup;

            /// <summary>
            /// true — кит занимает место манекена своей роли (размер команды = длине строя);
            /// false — кит добавляется к строю сверху (размер команды на единицу больше).
            /// </summary>
            public readonly bool HeroTakesSlot;

            public readonly string BaseName;
            public readonly string Title;
            public readonly string Blurb;

            public Format(Slot[] lineup, bool heroTakesSlot, string baseName, string title, string blurb)
            {
                Lineup = lineup;
                HeroTakesSlot = heroTakesSlot;
                BaseName = baseName;
                Title = title;
                Blurb = blurb;
            }
        }

        // Глубина строя: фронт стоит вплотную к противнику, тыл — за спинами.
        private const float FrontX = 2.2f;
        private const float BackX = 4.4f;

        private static readonly Format Solo = new Format(
            new[] { new Slot(UnitClass.Bruiser, FrontX, 0f) }, heroTakesSlot: true,
            "duel", "дуэли 1v1",
            "Кит против кита без поддержки. Ранг в вакууме: китов, чья работа не в убийстве, занижает по построению.");

        private static readonly Format Team = new Format(
            new[]
            {
                new Slot(UnitClass.Bruiser, FrontX, 0f),
                new Slot(UnitClass.Ranged, BackX, 0f),
            }, heroTakesSlot: false,
            "team_duel", "командные дуэли 3v3",
            "Тройка: манекен-Брузер держит фронт, манекен-дальник бьёт из-за спины, третьим встаёт испытуемый кит.");

        private static readonly Format SuperTeam = new Format(
            new[]
            {
                new Slot(UnitClass.Tank, FrontX, -1.2f),
                new Slot(UnitClass.Bruiser, FrontX, 0f),
                new Slot(UnitClass.Assassin, FrontX, 1.2f),
                new Slot(UnitClass.Ranged, BackX, -0.6f),
                new Slot(UnitClass.Support, BackX, 0.6f),
            }, heroTakesSlot: true,
            "super_team_duel", "командные дуэли 5v5",
            "Полный отряд из пяти ролей: Танк, Брузер, Убийца, дальник и поддержка. Кит ЗАМЕЩАЕТ манекен своего " +
            "класса, поэтому вопрос звучит прямо: он лучше или хуже рядового представителя своей роли?");

        public static (string csv, string md) Run() => RunFormat(Solo);
        public static (string csv, string md) RunTeam() => RunFormat(Team);
        public static (string csv, string md) RunSuperTeam() => RunFormat(SuperTeam);

        private static (string csv, string md) RunFormat(Format format)
        {
            StatsConfig config = BalanceAssets.LoadStatsConfig();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();
            int cap = SimBench.TicksFromSeconds(CapSeconds);
            List<RelicData> relics = BalanceAssets.LoadRelics();
            int n = relics.Count;

            var wins = New(n);   // для Bradley-Terry: дробные очки (ничья = 0.5)
            var games = New(n);
            var wCount = new int[n];
            var lCount = new int[n];
            var dCount = new int[n];

            // outcome[i][j]: результат для i, когда i слева vs j справа ("W 62%"/"L"/"D 40%").
            var outcome = new string[n][];
            for (int i = 0; i < n; i++) { outcome[i] = new string[n]; for (int j = 0; j < n; j++) outcome[i][j] = "-"; }

            // Остаток HP команды-победителя: сумма и счётчик по каждому киту. У проигравшей стороны он
            // всегда 0, поэтому копим только победы — «насколько уверенно», а не «выиграл ли».
            var hpWin = new double[n];
            var hpWinCount = new int[n];

            // Сколько раз сам испытуемый дожил до конца боя (в командных форматах он может выжить в
            // проигранном бою и погибнуть в выигранном — это отдельный факт от исхода).
            var survived = new int[n];
            var fights = new int[n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    SideResult left, right;
                    double leftScore = RunBattle(config, classes, relics[i], relics[j], format, cap, out left, out right);

                    outcome[i][j] = leftScore > 0.75 ? $"W {100.0 * left.TeamHpPct:0}%"
                                  : leftScore < 0.25 ? "L"
                                  : $"D {100.0 * left.TeamHpPct:0}%";

                    wins[i][j] += leftScore;
                    wins[j][i] += 1.0 - leftScore;
                    games[i][j] += 1.0;
                    games[j][i] += 1.0;

                    if (leftScore > 0.75) { wCount[i]++; lCount[j]++; hpWin[i] += left.TeamHpPct; hpWinCount[i]++; }
                    else if (leftScore < 0.25) { lCount[i]++; wCount[j]++; hpWin[j] += right.TeamHpPct; hpWinCount[j]++; }
                    else { dCount[i]++; dCount[j]++; }

                    fights[i]++; fights[j]++;
                    if (left.HeroAlive) survived[i]++;
                    if (right.HeroAlive) survived[j]++;
                }
            }

            double[] strength = BradleyTerry.Fit(n, wins, games);

            // --- Сводка (сортировка по силе) ---
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => strength[b].CompareTo(strength[a]));

            var sumHeaders = new List<string>
            {
                "Rank", "Relic", "Wins", "Losses", "Draws", "WinRate", "TeamHpOnWin%", "HeroSurvival%", "BTStrength",
            };
            var sumTable = new List<IReadOnlyList<object>>();
            int rank = 1;
            foreach (int i in order)
            {
                int total = wCount[i] + lCount[i] + dCount[i];
                double winRate = total > 0 ? (wCount[i] + 0.5 * dCount[i]) / total : 0.0;
                double avgHp = hpWinCount[i] > 0 ? 100.0 * hpWin[i] / hpWinCount[i] : 0.0;
                double surv = fights[i] > 0 ? 100.0 * survived[i] / fights[i] : 0.0;
                sumTable.Add(new object[]
                {
                    rank++, relics[i].name, wCount[i], lCount[i], dCount[i], winRate, avgHp, surv, strength[i],
                });
            }

            string sumNotes =
                $"**Формат: {format.Title}.** {format.Blurb} " +
                $"Round-robin (потолок {CapSeconds:0} с/бой, каждая пара — обе стороны). " +
                "WinRate учитывает ничьи как 0.5. **TeamHpOnWin%** — средний остаток HP команды в ВЫИГРАННЫХ боях: " +
                "запас победы (80% = размазал не заметив, 10% = вытянул на последних каплях). " +
                "**HeroSurvival%** — как часто сам испытуемый доживал до конца, независимо от исхода. " +
                "BTStrength (Bradley-Terry) — относительная сила, норм. к 1. " +
                "Бой RNG-free → исходы детерминированы; рейтинг ближе к топологическому порядку. " +
                (format.Lineup.Length > 1
                    ? "Союзники — эталонные манекены своих классов (HP, скорость и броня из ClassBalanceConfig, " +
                      "урон по классовому коридору), одинаковые у обеих сторон: разницу даёт только испытуемый кит. " +
                      "Манекен не умеет ни лечить, ни бить по площади — «поддержка» в строю это слабый стрелок, " +
                      "поэтому вклад настоящего хилера формат скорее занижает."
                    : "Дуэль 1v1 не отражает СОЧЕТАНИЯ — это ранг в вакууме, не приговор балансу.");

            string sumCsv = ReportWriter.WriteCsv(format.BaseName + "_ranking", sumHeaders, sumTable);
            string sumMd = ReportWriter.WriteMarkdown(format.BaseName + "_ranking",
                "SimBench — рейтинг, " + format.Title, sumHeaders, sumTable, sumNotes);

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
            ReportWriter.WriteCsv(format.BaseName + "_matrix", matrixHeaders, matrixTable);
            ReportWriter.WriteMarkdown(format.BaseName + "_matrix",
                "SimBench — матрица матчапов, " + format.Title, matrixHeaders, matrixTable,
                "Ячейка [строка i, столбец j] — исход для i (левого) против j (правого): W/L/D. " +
                "Число рядом с W/D — остаток HP команды левого на конец боя, %: цена победы. " +
                "У L он всегда 0 и не пишется.");

            return (sumCsv, sumMd);
        }

        /// <summary>Итог одной стороны боя: запас команды и судьба самого испытуемого.</summary>
        private readonly struct SideResult
        {
            public readonly double TeamHpPct;
            public readonly bool HeroAlive;

            public SideResult(double teamHpPct, bool heroAlive)
            {
                TeamHpPct = teamHpPct;
                HeroAlive = heroAlive;
            }
        }

        /// <summary>
        /// Прогон одного боя. Возвращает очко ЛЕВОГО: 1 победа, 0.5 ничья/timeout, 0 поражение — и итоги
        /// обеих сторон, чтобы было видно не только «кто», но и «насколько» и «какой ценой».
        /// </summary>
        private static double RunBattle(StatsConfig config, ClassBalanceConfig classes,
            RelicData relicLeft, RelicData relicRight, Format format, int cap,
            out SideResult left, out SideResult right)
        {
            var env = new SimEnvironment(Seed, config);
            var tracked = new List<TrackedUnit>();

            int heroLeft = SpawnSide(env, classes, tracked, relicLeft, team: 0, format);
            int heroRight = SpawnSide(env, classes, tracked, relicRight, team: 1, format);

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);

            left = SideOf(report, team: 0, heroLeft);
            right = SideOf(report, team: 1, heroRight);

            double score = 0.5;
            if (!report.TimedOut && !report.Outcome.IsDraw)
            {
                if (report.Outcome.IsWinFor(0)) score = 1.0;
                else if (report.Outcome.IsWinFor(1)) score = 0.0;
            }

            return score;
        }

        private static SideResult SideOf(BattleReport report, int team, int heroId)
        {
            double hpLeft = 0.0, maxHp = 0.0;
            bool heroAlive = false;
            for (int i = 0; i < report.Units.Count; i++)
            {
                UnitMetric m = report.Units[i];
                if (m.Team != team) continue;
                hpLeft += m.HpLeft;
                maxHp += m.MaxHp;
                if (m.Id == heroId) heroAlive = !m.Died;
            }

            return new SideResult(maxHp > 0.0 ? hpLeft / maxHp : 0.0, heroAlive);
        }

        /// <summary>
        /// Развернуть сторону по строю формата: манекены занимают свои роли, кит встаёт на место своего
        /// класса (или добавляется рядом, если формат не предполагает замещения). Возвращает индекс кита
        /// в <paramref name="tracked"/>, он же его Id — SimBench раздаёт Id по порядку списка.
        /// </summary>
        private static int SpawnSide(SimEnvironment env, ClassBalanceConfig classes, List<TrackedUnit> tracked,
            RelicData relic, int team, Format format)
        {
            float side = team == 0 ? -1f : 1f;   // сторона арены: команда 0 слева, команда 1 справа
            Slot[] lineup = format.Lineup;

            // Какой слот уступает место киту. Призыватель делит слот с поддержкой: обе роли стоят в тылу
            // и в бою участвуют не автоатакой, а тем, что вокруг них происходит.
            UnitClass heroRole = relic.CombatClass == UnitClass.Summoner ? UnitClass.Support : relic.CombatClass;
            int takenSlot = -1;
            if (format.HeroTakesSlot)
            {
                for (int s = 0; s < lineup.Length; s++)
                    if (lineup[s].Role == heroRole) { takenSlot = s; break; }

                // Роли кита в строю нет (строй короче списка классов) — тогда он вытесняет ближайшего
                // по линии боя: фронтовой кит меняет Брузера, тыловой — дальника.
                if (takenSlot < 0)
                {
                    UnitClass fallback = IsFrontline(heroRole) ? UnitClass.Bruiser : UnitClass.Ranged;
                    for (int s = 0; s < lineup.Length; s++)
                        if (lineup[s].Role == fallback) { takenSlot = s; break; }
                    if (takenSlot < 0) takenSlot = 0;
                }
            }

            for (int s = 0; s < lineup.Length; s++)
            {
                if (s == takenSlot) continue;
                Slot slot = lineup[s];
                tracked.Add(new TrackedUnit(
                    SyntheticUnits.ReferenceAlly(slot.Role, classes, team, new Vector2(side * slot.Pos.x, slot.Pos.y)),
                    slot.Role.ToString().ToLowerInvariant(), "ally"));
            }

            // Место кита: занятый слот либо, если формат его не двигает, свободная линия своего эшелона.
            Vector2 heroPos = takenSlot >= 0
                ? lineup[takenSlot].Pos
                : new Vector2(IsFrontline(heroRole) ? FrontX : BackX, 1.2f);

            int heroIndex = tracked.Count;
            tracked.Add(new TrackedUnit(env.Real(relic, null, team, new Vector2(side * heroPos.x, heroPos.y)),
                relic.name, relic.name));
            return heroIndex;
        }

        private static bool IsFrontline(UnitClass unitClass)
            => unitClass is UnitClass.Tank or UnitClass.Bruiser or UnitClass.Assassin;

        private static double[][] New(int n)
        {
            var a = new double[n][];
            for (int i = 0; i < n; i++) a[i] = new double[n];
            return a;
        }
    }
}
