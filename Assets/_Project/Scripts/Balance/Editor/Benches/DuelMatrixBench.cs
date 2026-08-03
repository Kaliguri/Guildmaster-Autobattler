using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
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
    /// 1v1 (кит в вакууме), 3v3 и 4v4. Командные форматы существуют потому, что дуэль не умеет мерить
    /// китов, чья работа — не убивать: хилер, баффер и держатель фронта в вакууме всегда выглядят мусором.</para>
    /// </summary>
    public static class DuelMatrixBench
    {
        // 240 с: на строе из пяти ролей бои упирались в прежний потолок 120 и копили ничьи, которые
        // ничего не сообщают — «не добил» и «равны» выглядели одинаково.
        private const float CapSeconds = 240f;
        private const ulong Seed = 1UL;

        /// <summary>Строй стороны и подписи отчёта.</summary>
        private readonly struct Format
        {
            public readonly Slot[] Lineup;
            public readonly string BaseName;
            public readonly string Title;
            public readonly string Blurb;

            public Format(Slot[] lineup, string baseName, string title, string blurb)
            {
                Lineup = lineup;
                BaseName = baseName;
                Title = title;
                Blurb = blurb;
            }
        }

        private static readonly Format Solo = new Format(Lineups.Solo,
            "duel", "дуэли 1v1",
            "Кит против кита без поддержки. Ранг в вакууме: китов, чья работа не в убийстве, занижает по построению.");

        private static readonly Format Trio = new Format(Lineups.Trio,
            "trio_duel", "тройки 3v3",
            "Неполный отряд: кит и двое рядовых. Промежуточная линза между дуэлью и настоящим боем.");

        private static readonly Format Squad = new Format(Lineups.Squad,
            "squad_duel", "отряды 4v4",
            "ОТРЯД ИГРЫ: Танк, Брузер, дальник и поддержка — тот состав, который игрок реально выставляет. " +
            "Кит ЗАМЕЩАЕТ рядового своей роли, поэтому вопрос звучит прямо: он лучше или хуже обычного " +
            "представителя этой роли? Это главный формат оценки, остальные — вспомогательные.");

        public static (string csv, string md) Run() => RunFormat(Solo);
        public static (string csv, string md) RunTrio() => RunFormat(Trio);
        public static (string csv, string md) RunSquad() => RunFormat(Squad);

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

            // Что кит делал в бою — все пять корзин метрик, накопленные по боям. Отдельных массивов
            // на каждое число не заводим: копим в тот же UnitMetric, из которого читаем.
            var acc = new UnitMetric[n];
            for (int i = 0; i < n; i++) acc[i] = new UnitMetric();

            // Время боя. Отвечает на вопрос «какой у нас вообще TTK» — до сих пор стенд его не выводил
            // вовсе, и разговор о правиле анти-затягивания вёлся без единого замеренного числа.
            // Таймауты считаем отдельно: включать потолок в среднее нельзя, он занижает и врёт.
            var secondsSum = new double[n];
            var secondsCount = new int[n];
            var timeouts = new int[n];
            var allSeconds = new List<double>();

            // Сколько боёв доехало до овертайма. Порог читаем из того же снапшота тюнинга, что и бой —
            // иначе отчёт мерил бы одно, а симуляция жила по другому.
            var overtimeHits = new int[n];
            float overtimeStart = SimTuning.Default.OvertimeStartSeconds;

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    SideResult left, right;
                    double leftScore = RunBattle(config, classes, relics[i], relics[j], format, cap,
                        out left, out right, out double seconds, out bool timedOut);

                    // Время боя копим по обоим участникам: «сколько длится бой С ЭТИМ китом» — свойство
                    // пары, и кит отвечает за него независимо от того, победил он или проиграл.
                    if (timedOut) { timeouts[i]++; timeouts[j]++; }
                    else
                    {
                        secondsSum[i] += seconds; secondsCount[i]++;
                        secondsSum[j] += seconds; secondsCount[j]++;
                        allSeconds.Add(seconds);
                    }

                    // Овертайм — предохранитель, и он обязан оставаться редким. Если доля боёв,
                    // доехавших до порога, начнёт расти — это сигнал о TTK, а не повод крутить рампу.
                    if (timedOut || seconds >= overtimeStart) { overtimeHits[i]++; overtimeHits[j]++; }

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

                    Accumulate(acc[i], left.Hero);
                    Accumulate(acc[j], right.Hero);
                }
            }

            double[] strength = BradleyTerry.Fit(n, wins, games);

            // Сводка по формату целиком: медиана честнее среднего — пара затянувшихся боёв тянет
            // среднее вверх и создаёт впечатление, что «бои у нас долгие», хотя долгих ровно две штуки.
            allSeconds.Sort();
            double MedianSec(double p) => allSeconds.Count > 0
                ? allSeconds[Mathf.Min(allSeconds.Count - 1, (int)(allSeconds.Count * p))]
                : 0.0;
            int totalFights = n * (n - 1);
            int totalTimeouts = (totalFights - allSeconds.Count);

            // --- Сводка (сортировка по силе) ---
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => strength[b].CompareTo(strength[a]));

            var sumHeaders = new List<string>
            {
                "Rank", "Relic", "Wins", "Losses", "Draws", "WinRate", "TeamHpOnWin%", "HeroSurvival%",
                "AvgFightSec", "Timeout%", "Overtime%",
                "AvgDmgDealt", "AvgDmgTaken", "React%", "BTStrength",
                "HealTaken", "Mitigated", "Evaded",
                "ControlSec", "ControlCount", "ControlTakenSec",
                "Debuffs", "DebuffSec", "Dots",
                "HealDone", "Buffs", "BuffSec", "Cleanses",
            };
            var sumTable = new List<IReadOnlyList<object>>();
            int rank = 1;
            foreach (int i in order)
            {
                int total = wCount[i] + lCount[i] + dCount[i];
                double winRate = total > 0 ? (wCount[i] + 0.5 * dCount[i]) / total : 0.0;
                double avgHp = hpWinCount[i] > 0 ? 100.0 * hpWin[i] / hpWinCount[i] : 0.0;
                double surv = fights[i] > 0 ? 100.0 * survived[i] / fights[i] : 0.0;
                UnitMetric a = acc[i];
                double reactShare = a.DamageDealt > 1e-6 ? 100.0 * a.DamageReactive / a.DamageDealt : 0.0;

                // Всё «за бой» — средние: боёв у китов поровну, но делить всё равно надо, иначе число
                // растёт от размера ростера и отчёты разных прогонов перестают сравниваться.
                double Avg(double v) => fights[i] > 0 ? v / fights[i] : 0.0;

                double avgSec = secondsCount[i] > 0 ? secondsSum[i] / secondsCount[i] : 0.0;
                double timeoutPct = fights[i] > 0 ? 100.0 * timeouts[i] / fights[i] : 0.0;

                sumTable.Add(new object[]
                {
                    rank++, relics[i].name, wCount[i], lCount[i], dCount[i], winRate, avgHp, surv,
                    avgSec, timeoutPct, fights[i] > 0 ? 100.0 * overtimeHits[i] / fights[i] : 0.0,
                    Avg(a.DamageDealt), Avg(a.DamageTaken), reactShare, strength[i],
                    Avg(a.HealingReceived), Avg(a.DamageMitigated), Avg(a.HitsEvaded),
                    Avg(a.ControlSecondsDealt), Avg(a.ControlAppliedCount), Avg(a.ControlSecondsTaken),
                    Avg(a.DebuffsApplied), Avg(a.DebuffSecondsDealt), Avg(a.DotsApplied),
                    Avg(a.HealingDone), Avg(a.BuffsGranted), Avg(a.BuffSecondsGranted), Avg(a.CleansesDone),
                });
            }

            string sumNotes =
                $"**Формат: {format.Title}.** {format.Blurb} " +
                $"**TTK формата:** медиана боя **{MedianSec(0.5):0.0} с** " +
                $"(четверть боёв короче {MedianSec(0.25):0.0} с, четверть длиннее {MedianSec(0.75):0.0} с, " +
                $"самый долгий разрешившийся {MedianSec(1.0):0.0} с); " +
                $"не разрешилось за потолок **{totalTimeouts} из {totalFights}** боёв " +
                $"({(totalFights > 0 ? 100.0 * totalTimeouts / totalFights : 0):0}%). " +
                $"Round-robin (потолок {CapSeconds:0} с/бой, каждая пара — обе стороны). " +
                "WinRate учитывает ничьи как 0.5. **TeamHpOnWin%** — средний остаток HP команды в ВЫИГРАННЫХ боях: " +
                "запас победы (80% = размазал не заметив, 10% = вытянул на последних каплях). " +
                "**HeroSurvival%** — как часто сам испытуемый доживал до конца, независимо от исхода. " +
                "**AvgFightSec** — средняя длительность боёв с этим китом, ТОЛЬКО по разрешившимся: потолок в " +
                "среднее не входит, иначе затянутые бои выглядели бы короче, чем есть. **Timeout%** — доля боёв, " +
                $"упёршихся в потолок {CapSeconds:0} с; такой бой не «ничья по силе», а отсутствие исхода. " +
                "Читать вместе: высокий Timeout% при коротком AvgFightSec значит «либо быстро, либо никогда». " +
                $"**Overtime%** — доля боёв, доехавших до порога овертайма ({overtimeStart:0} с). Овертайм — " +
                "предохранитель для клинча сустейна, и обязан оставаться редким: его рост читать как сигнал о " +
                "TTK, а не как повод крутить рампу урона. " +
                "**AvgDmgDealt / AvgDmgTaken** — сколько кит в среднем за бой наносил и поглощал: отвечает на " +
                "вопрос «он много бьёт или долго живёт». **React%** — какая доля его урона пришла ответкой " +
                "(шипы), то есть не выбиралась им вовсе. " +
                "BTStrength (Bradley-Terry) — относительная сила, норм. к 1. " +
                "Дальше — что кит делает помимо урона, всё в среднем ЗА БОЙ. " +
                "**Чем не умер:** HealTaken (полученное лечение), Mitigated (срезано бронёй), Evaded (уклонений). " +
                "**Контроль:** ControlSec/ControlCount — секунды и число наложенных контролей, " +
                "ControlTakenSec — сколько контроля съел сам. " +
                "**Проклятия:** Debuffs/DebuffSec — наложенные дебаффы и их секунды, Dots — отдельно яд и горение. " +
                "**Утилита:** HealDone (вылечено), Buffs/BuffSec (бафы союзникам), Cleanses (снято чужих дебаффов со своих). " +
                "Нули у всей корзины значат, что кит этим не занимается, — это факт о ките, а не пробел в замере. " +
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
            ReportWriter.WriteJson(format.BaseName + "_ranking",
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
            const string matrixNotes =
                "Ячейка [строка i, столбец j] — исход для i (левого) против j (правого): W/L/D. " +
                "Число рядом с W/D — остаток HP команды левого на конец боя, %: цена победы. " +
                "У L он всегда 0 и не пишется.";
            ReportWriter.WriteMarkdown(format.BaseName + "_matrix",
                "SimBench — матрица матчапов, " + format.Title, matrixHeaders, matrixTable, matrixNotes);
            ReportWriter.WriteJson(format.BaseName + "_matrix",
                "SimBench — матрица матчапов, " + format.Title, matrixHeaders, matrixTable, matrixNotes);

            return (sumCsv, sumMd);
        }

        /// <summary>Итог одной стороны боя: запас команды и что сделал сам испытуемый.</summary>
        private readonly struct SideResult
        {
            public readonly double TeamHpPct;
            public readonly bool HeroAlive;

            /// <summary>Метрика самого испытуемого за этот бой — целиком, все пять корзин.</summary>
            public readonly UnitMetric Hero;

            public SideResult(double teamHpPct, bool heroAlive, UnitMetric hero)
            {
                TeamHpPct = teamHpPct;
                HeroAlive = heroAlive;
                Hero = hero;
            }
        }

        /// <summary>Сложить метрику одного боя в накопитель кита. Только суммируемые величины: HP и
        /// флаги смерти живут отдельно, складывать их бессмысленно.</summary>
        private static void Accumulate(UnitMetric total, UnitMetric one)
        {
            if (one == null) return;

            total.DamageDealt += one.DamageDealt;
            total.DamageTaken += one.DamageTaken;
            total.DamageReactive += one.DamageReactive;

            total.HealingReceived += one.HealingReceived;
            total.DamageMitigated += one.DamageMitigated;
            total.HitsEvaded += one.HitsEvaded;

            total.ControlSecondsDealt += one.ControlSecondsDealt;
            total.ControlAppliedCount += one.ControlAppliedCount;
            total.ControlSecondsTaken += one.ControlSecondsTaken;

            total.DebuffsApplied += one.DebuffsApplied;
            total.DebuffSecondsDealt += one.DebuffSecondsDealt;
            total.DotsApplied += one.DotsApplied;

            total.HealingDone += one.HealingDone;
            total.BuffsGranted += one.BuffsGranted;
            total.BuffSecondsGranted += one.BuffSecondsGranted;
            total.CleansesDone += one.CleansesDone;
        }

        /// <summary>
        /// Прогон одного боя. Возвращает очко ЛЕВОГО: 1 победа, 0.5 ничья/timeout, 0 поражение — и итоги
        /// обеих сторон, чтобы было видно не только «кто», но и «насколько» и «какой ценой».
        /// </summary>
        private static double RunBattle(StatsConfig config, ClassBalanceConfig classes,
            RelicData relicLeft, RelicData relicRight, Format format, int cap,
            out SideResult left, out SideResult right, out double seconds, out bool timedOut)
        {
            var env = new SimEnvironment(Seed, config);
            var tracked = new List<TrackedUnit>();

            int heroLeft = Lineups.SpawnTeam(env, classes, tracked, new[] { relicLeft }, 0, format.Lineup)[0];
            int heroRight = Lineups.SpawnTeam(env, classes, tracked, new[] { relicRight }, 1, format.Lineup)[0];

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);

            seconds = report.Seconds;
            timedOut = report.TimedOut;
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
            UnitMetric hero = null;
            for (int i = 0; i < report.Units.Count; i++)
            {
                UnitMetric m = report.Units[i];
                if (m.Team != team) continue;
                hpLeft += m.HpLeft;
                maxHp += m.MaxHp;
                if (m.Id == heroId) hero = m;
            }

            return new SideResult(maxHp > 0.0 ? hpLeft / maxHp : 0.0, hero != null && !hero.Died, hero);
        }

        private static double[][] New(int n)
        {
            var a = new double[n][];
            for (int i = 0; i < n; i++) a[i] = new double[n];
            return a;
        }
    }
}
