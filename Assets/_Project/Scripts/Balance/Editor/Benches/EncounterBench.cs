using System.Collections.Generic;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// PvE-линза стенда: отряд игрока против АВТОРЕННЫХ энкаунтеров (<see cref="EncounterData"/>), а не
    /// против зеркального ростера. Отвечает на вопрос, который в игре действительно есть: «прошёл ли бой,
    /// какой ценой и за сколько», — и делает это дважды, с двух сторон.
    /// </summary>
    /// <remarks>
    /// <para>Зачем отдельно от круговых форматов: Guildmaster — PvE-рогалик, игрок дерётся с энкаунтерами.
    /// Круговой винрейт «реликвия против реликвии» отвечает на вопрос, которого в игре нет, поэтому он
    /// остаётся грубой линзой «кто вообще жив», а PvE-цена боя меряется здесь.</para>
    /// <para>Два отчёта из одного прогона: <c>encounter_kits</c> — что энкаунтеры говорят о ките,
    /// <c>encounter_difficulty</c> — что киты говорят об энкаунтере. Второй нужен ровно так же, как первый:
    /// балансировать приходится обе стороны, а сложность боя — свойство состава врагов, не кита.</para>
    /// <para>Норм PvE (сколько HP команды «должен» стоить бой каждого тира) на 2026-07-29 НЕ существует:
    /// это дизайнерские числа, их назначает Макс. Пока отчёт даёт факт и прямо говорит, что коридора нет,
    /// а роль подручного эталона играет строка «Пустого сосуда» — цена боя обычным отрядом.</para>
    /// </remarks>
    public static class EncounterBench
    {
        // Тот же потолок, что в командных форматах: длинные бои упирались в 120 с и копили ничьи,
        // которые ничего не сообщают.
        private const float CapSeconds = 240f;
        private const ulong Seed = 1UL;

        /// <summary>Сколько имён провалившихся китов печатать в таблице сложности, прежде чем сократить.</summary>
        private const int FailedNamesShown = 4;

        /// <summary>Итог одного прохода «кит + отряд против энкаунтера».</summary>
        private readonly struct Attempt
        {
            public readonly bool Cleared;
            public readonly bool TimedOut;
            public readonly double Seconds;

            /// <summary>Остаток HP отряда игрока на конец боя, доля [0,1] — «чем заплатили».</summary>
            public readonly double TeamHpPct;

            /// <summary>Сколько бойцов отряда (из четырёх) легло.</summary>
            public readonly int Fallen;

            public readonly bool HeroDied;

            /// <summary>Метрика самого испытуемого кита за бой — все пять корзин.</summary>
            public readonly UnitMetric Hero;

            /// <summary>Вся его армия за бой, свёрнутая в одну строку. У кита без призывов — нули.</summary>
            public readonly SummonRollup Army;

            /// <summary>Длительность боя в тиках — знаменатель аптайма армии.</summary>
            public readonly int DurationTicks;

            public Attempt(bool cleared, bool timedOut, double seconds, double teamHpPct, int fallen,
                bool heroDied, UnitMetric hero, SummonRollup army, int durationTicks)
            {
                Cleared = cleared;
                TimedOut = timedOut;
                Seconds = seconds;
                TeamHpPct = teamHpPct;
                Fallen = fallen;
                HeroDied = heroDied;
                Hero = hero;
                Army = army;
                DurationTicks = durationTicks;
            }
        }

        public static (string csv, string md) Run()
        {
            StatsConfig config = BalanceAssets.LoadStatsConfig();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();
            int cap = SimBench.TicksFromSeconds(CapSeconds);
            float overtimeStart = SimTuning.Default.OvertimeStartSeconds;

            List<RelicData> relics = BalanceAssets.LoadRelics();
            List<EncounterData> encounters = BalanceAssets.LoadAll<EncounterData>();
            Dictionary<string, EnemyData> enemiesById = EncounterSetup.IndexEnemies();

            int kits = relics.Count;
            int encCount = encounters.Count;

            // Последний ряд — КОНТРОЛЬ: тот же отряд без испытуемого (четыре эталонных манекена). Без него
            // «дорого» и «дёшево» не с чем сравнить: цена боя есть свойство энкаунтера, и вопрос про кита
            // звучит как «он делает бой дешевле или дороже, чем его отсутствие».
            int rows = kits + 1;
            int control = kits;

            // [ряд][энкаунтер] — прямоугольник попыток. Хранится целиком: из него собираются оба отчёта,
            // и агрегировать по строкам и по столбцам надо одни и те же числа.
            var grid = new Attempt[rows][];
            for (int k = 0; k < rows; k++) grid[k] = new Attempt[encCount];

            var facts = new EncounterFacts[encCount];
            var playable = new bool[encCount];

            for (int e = 0; e < encCount; e++)
            {
                playable[e] = EncounterSetup.IsPlayable(encounters[e], enemiesById);
                if (!playable[e]) continue;

                // Статика энкаунтера пишется на каждой попытке одним и тем же значением: состав врагов от
                // испытуемого кита не зависит, и отдельный проход ради неё был бы вторым способом её узнать.
                for (int k = 0; k < rows; k++)
                    grid[k][e] = RunAttempt(config, classes, k == control ? null : relics[k], encounters[e],
                        enemiesById, cap, out facts[e]);
            }

            string csv = WriteKitReport(relics, encounters, grid, playable, overtimeStart, out string md);
            WriteDifficultyReport(relics, encounters, grid, facts, playable);
            return (csv, md);
        }

        // --- Отчёт 1: что энкаунтеры говорят о ките ---

        private static string WriteKitReport(List<RelicData> relics, List<EncounterData> encounters,
            Attempt[][] grid, bool[] playable, float overtimeStart, out string md)
        {
            var headers = new List<string>
            {
                "Rank", "Relic", "Class", "Cleared", "Fights", "ClearRate%", "HpCostOnClear%",
                "AvgFightSec", "Timeout%", "Overtime%", "HeroDeaths%", "FallenOnClear",
                "AvgDmgDealt", "HealDone", "ControlSec",
                "SummonDmg", "DmgWithSummons", "SummonTanked", "SummonsAlive", "FirstSummonSec",
            };

            var rows = new List<(double clearRate, double hpCost, IReadOnlyList<object> cells)>();

            // На один ряд больше, чем китов: последний — контроль «отряд без кита».
            for (int k = 0; k < grid.Length; k++)
            {
                bool isControl = k >= relics.Count;
                int fights = 0, clears = 0, timeouts = 0, overtimes = 0, heroDeaths = 0, fallenOnClear = 0;
                double hpOnClear = 0.0, secondsSum = 0.0, dmg = 0.0, heal = 0.0, control = 0.0;
                int secondsCount = 0;

                // Армия считается по тем же боям, что и кит: сумма урона тел, сумма перехваченного ими
                // урона и аптайм. Рампа берётся средней только по боям, где призыв вообще случился —
                // иначе кит, не успевший призвать в коротком бою, занижал бы себе секунду появления.
                double summonDmg = 0.0, summonTanked = 0.0, summonAlive = 0.0, firstSpawnSum = 0.0;
                int firstSpawnCount = 0;

                for (int e = 0; e < encounters.Count; e++)
                {
                    if (!playable[e]) continue;

                    // Служебные наборы (тир Special — «на карте сам не спавнится») в агрегат не идут:
                    // проходимость должна отвечать за бои, которые игрок реально встретит, а пара
                    // манекенов проходится всегда и задирала бы её всем одинаково.
                    if (encounters[e].Tier == EncounterTier.Special) continue;

                    Attempt a = grid[k][e];
                    fights++;

                    if (a.TimedOut) timeouts++;
                    else { secondsSum += a.Seconds; secondsCount++; }
                    if (a.TimedOut || a.Seconds >= overtimeStart) overtimes++;
                    if (a.HeroDied) heroDeaths++;

                    if (a.Cleared)
                    {
                        clears++;
                        hpOnClear += a.TeamHpPct;
                        fallenOnClear += a.Fallen;
                    }

                    if (a.Hero != null)
                    {
                        dmg += a.Hero.DamageDealt;
                        heal += a.Hero.HealingDone;
                        control += a.Hero.ControlSecondsDealt;

                        summonDmg += a.Army.DamageDealt;
                        summonTanked += a.Army.DamageTaken;
                        summonAlive += a.Army.AvgAlive(a.DurationTicks);
                        if (a.Army.FirstSpawnSeconds >= 0.0)
                        {
                            firstSpawnSum += a.Army.FirstSpawnSeconds;
                            firstSpawnCount++;
                        }
                    }
                }

                if (fights == 0) continue;

                double clearRate = (double)clears / fights;
                // Цена платится только там, где бой пройден: в провале остаток HP говорит о том, как
                // именно отряд лёг, а не о цене прохождения. Поэтому цена считается по пройденным.
                double hpCost = clears > 0 ? 100.0 * (1.0 - hpOnClear / clears) : 100.0;

                rows.Add((clearRate, hpCost, new object[]
                {
                    0,
                    isControl ? "(контроль: отряд без кита)" : relics[k].name,
                    isControl ? "—" : relics[k].CombatClass.ToString(),
                    clears, fights, 100.0 * clearRate, hpCost,
                    secondsCount > 0 ? secondsSum / secondsCount : 0.0,
                    100.0 * timeouts / fights, 100.0 * overtimes / fights, 100.0 * heroDeaths / fights,
                    clears > 0 ? (double)fallenOnClear / clears : 0.0,
                    dmg / fights, heal / fights, control / fights,
                    summonDmg / fights, (dmg + summonDmg) / fights, summonTanked / fights,
                    summonAlive / fights,
                    firstSpawnCount > 0 ? firstSpawnSum / firstSpawnCount : -1.0,
                }));
            }

            // Сортировка по PvE-смыслу: сначала кто чаще проходит, при равенстве — кто платит меньше.
            rows.Sort((x, y) => x.clearRate == y.clearRate
                ? x.hpCost.CompareTo(y.hpCost)
                : y.clearRate.CompareTo(x.clearRate));

            var table = new List<IReadOnlyList<object>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var cells = new List<object>(rows[i].cells);
                cells[0] = i + 1;
                table.Add(cells);
            }

            int skipped = 0;
            for (int e = 0; e < playable.Length; e++) if (!playable[e]) skipped++;

            string notes =
                "**PvE-линза: отряд игрока против авторенных энкаунтеров.** Кит занимает слот своей роли в " +
                "штатном отряде (Танк, Брузер, дальник, поддержка), остальные три слота закрывают эталонные " +
                "манекены своих классов — вопрос тот же, что в формате 4v4, но заданный об энкаунтере, а не " +
                "о зеркальном ростере. Враги стоят там, где их поставил автор энкаунтера. " +
                $"Прогнано {encounters.Count - skipped} из {encounters.Count} энкаунтеров" +
                (skipped > 0 ? $" (пропущено {skipped}: в составе есть враг, которого нет в ассетах — " +
                               "неполный состав мерить нельзя, это дало бы число о другом бое)" : "") + ". " +
                "**ClearRate%** — процент пройденных боёв: главная PvE-метрика вместо винрейта. " +
                "**HpCostOnClear%** — сколько HP отряда стоила ПОБЕДА (100% минус остаток), считается только " +
                "по пройденным боям: в проигранном остаток HP говорит о том, как отряд лёг, а не о цене. " +
                "**FallenOnClear** — сколько бойцов из четырёх легло в среднем в пройденном бою: победа с " +
                "тремя трупами и победа без потерь стоят по-разному, и HpCost их не различает. " +
                "**Строка «контроль: отряд без кита»** — тот же отряд из четырёх эталонных манекенов, без " +
                "испытуемого. Читать её как ПОТОЛОК, а не как среднего игрока: манекен бьёт классовую норму " +
                "ровно и никогда не тратит время на смену цели, тогда как живой кит на этом теряет темп " +
                "(замер 29.07: Следопыт 136.5 DPS в вакууме против 108 в бою, Криомант 153.9 против 96). " +
                "Поэтому кит НИЖЕ контроля — не приговор киту, а мера того, сколько стоит переприцеливание; " +
                "приговор — только систематический отрыв вниз при сравнимом темпе. " +
                "**HeroDeaths%** — как часто погибал сам испытуемый, независимо от исхода. " +
                "**AvgFightSec** — по разрешившимся боям; потолок в среднее не входит. " +
                $"**Timeout%** — доля боёв, упёршихся в потолок {CapSeconds:0} с (это не ничья, а отсутствие " +
                $"исхода). **Overtime%** — доля боёв, доехавших до порога овертайма ({overtimeStart:0} с). " +
                "Дальше — что кит делал: урон, лечение и секунды контроля в среднем за бой. " +
                "**Призывы разнесены на три взгляда:** **AvgDmgDealt** — урон САМОГО кита, **SummonDmg** — " +
                "урон его тел, **DmgWithSummons** — сумма, и сравнивать с классовой нормой надо именно её. " +
                "**SummonTanked** — урон, принятый телами: это перехваченные удары, вклад призывателя в " +
                "живучесть отряда, невидимый в его собственном HP. **SummonsAlive** — среднее число живых " +
                "тел за бой, а не число вызовов: призыватель единственный стартует пустым, и без этой шкалы " +
                "«набрал восемь к сороковой секунде» неотличимо от «держал три весь бой». " +
                "**FirstSummonSec** — когда появилось первое тело (−1 = не призвал ни разу), мера рампы. " +
                "Сами тела в отряд НЕ засчитываются: их смерти не идут в FallenOnClear, а их HP — в " +
                "HpCostOnClear, иначе кит с армией платил бы за бой дешевле, просто разбавив отряд расходниками. " +
                "В агрегат по киту НЕ входят энкаунтеры тира Special (служебные, «на карте не спавнятся» — " +
                "там же живут тренировочные наборы); в таблице сложности они есть, потому что она о боях, " +
                "а не о ростере. " +
                "**Нормы PvE не заданы** (сколько HP команды должен стоить рядовой бой, элита, финалист) — " +
                "это дизайнерские числа, и до их назначения таблица даёт факт, а не отклонение. Подручный " +
                "эталон — строка «Пустого сосуда»: цена боя обычным отрядом без особых механик. " +
                "Слепые пятна: один состав отряда и одна расстановка на энкаунтер (дисперсии нет), " +
                "предметы и уровни Сосуда не участвуют — все замеры сняты на стартовых статах.";

            string csv = ReportWriter.WriteCsv("encounter_kits", headers, table);
            md = ReportWriter.WriteMarkdown("encounter_kits",
                "SimBench — энкаунтеры: цена боя по китам", headers, table, notes);
            ReportWriter.WriteJson("encounter_kits",
                "SimBench — энкаунтеры: цена боя по китам", headers, table, notes);
            return csv;
        }

        // --- Отчёт 2: что киты говорят об энкаунтере ---

        private static void WriteDifficultyReport(List<RelicData> relics, List<EncounterData> encounters,
            Attempt[][] grid, EncounterFacts[] facts, bool[] playable)
        {
            var headers = new List<string>
            {
                "Encounter", "Tier", "Enemies", "Threat", "EnemyHP", "ClearRate", "AvgHpCostOnClear%",
                "AvgFightSec", "Timeout%", "ControlClear", "ControlHpCost%", "FailedBy",
            };

            var table = new List<IReadOnlyList<object>>();

            for (int e = 0; e < encounters.Count; e++)
            {
                EncounterData enc = encounters[e];
                if (!playable[e])
                {
                    table.Add(new object[]
                    {
                        enc.name, enc.Tier.ToString(), 0, 0, 0.0, null, null, null, null, null, null,
                        "не прогнан: враг не найден",
                    });
                    continue;
                }

                int fights = 0, clears = 0, timeouts = 0, secondsCount = 0;
                double hpOnClear = 0.0, secondsSum = 0.0;
                var failed = new List<string>();

                for (int k = 0; k < relics.Count; k++)
                {
                    Attempt a = grid[k][e];
                    fights++;
                    if (a.TimedOut) timeouts++;
                    else { secondsSum += a.Seconds; secondsCount++; }

                    if (a.Cleared) { clears++; hpOnClear += a.TeamHpPct; }
                    else failed.Add(relics[k].name);
                }

                // Контроль — последний ряд сетки: тот же бой без испытуемого кита. «Проходится и без кита»
                // говорит о бое больше, чем любая колонка про китов: значит он ничего не спрашивает.
                Attempt ctrl = grid[grid.Length - 1][e];

                table.Add(new object[]
                {
                    enc.name, enc.Tier.ToString(), facts[e].Enemies, facts[e].Threat, facts[e].EnemyHp,
                    fights > 0 ? (double)clears / fights : 0.0,
                    clears > 0 ? 100.0 * (1.0 - hpOnClear / clears) : 100.0,
                    secondsCount > 0 ? secondsSum / secondsCount : 0.0,
                    fights > 0 ? 100.0 * timeouts / fights : 0.0,
                    ctrl.Cleared ? "да" : ctrl.TimedOut ? "нет исхода" : "нет",
                    ctrl.Cleared ? 100.0 * (1.0 - ctrl.TeamHpPct) : (object)null,
                    Names(failed),
                });
            }

            const string notes =
                "**Тот же прогон, вид со стороны врага:** сложность энкаунтера как свойство состава, а не " +
                "кита. Каждый энкаунтер пройден всем ростером по очереди (кит + три эталонных манекена). " +
                "**Threat** — сумма «очков опасности», которые автор проставил врагам вручную: это ЕДИНСТВЕННАЯ " +
                "заявленная оценка сложности, и расхождение с фактическим ClearRate/HpCost читается как " +
                "«оценка врёт». **EnemyHP** — суммарный запас врагов, знаменатель для разговора о TTK. " +
                "**ClearRate** — сколько китов ростера прошли бой (контроль в эту долю не входит); " +
                "**FailedBy** — кто именно не прошёл. **ControlClear / ControlHpCost%** — прошёл ли бой отряд " +
                "БЕЗ испытуемого кита и какой ценой: «да» при низкой цене значит, что бой не спрашивает " +
                "ничего и китам в нём нечего показывать. " +
                "Читать вместе: провал одного-двух китов на элите — норма (не всякий кит для всякого боя), " +
                "провал всех — вопрос к энкаунтеру. Обратное тоже верно: ClearRate 100% при нулевой цене " +
                "значит, что бой ничего не спрашивает.";

            ReportWriter.WriteCsv("encounter_difficulty", headers, table);
            ReportWriter.WriteMarkdown("encounter_difficulty",
                "SimBench — энкаунтеры: сложность боёв", headers, table, notes);
            ReportWriter.WriteJson("encounter_difficulty",
                "SimBench — энкаунтеры: сложность боёв", headers, table, notes);
        }

        // --- Прогон одного боя ---

        /// <summary>
        /// Один проход. <paramref name="relic"/> == null — контрольный прогон: отряд собирается целиком из
        /// эталонных манекенов, и в нём нет испытуемого (метрика кита остаётся пустой).
        /// </summary>
        private static Attempt RunAttempt(StatsConfig config, ClassBalanceConfig classes, RelicData relic,
            EncounterData encounter, Dictionary<string, EnemyData> enemiesById, int cap,
            out EncounterFacts facts)
        {
            var env = new SimEnvironment(Seed, config);
            var tracked = new List<TrackedUnit>();

            // Игрок — team 0 (Lineups зеркалит его на отрицательный X), враги — team 1 по якорям автора.
            int[] heroes = Lineups.SpawnTeam(env, classes, tracked,
                relic != null ? new[] { relic } : System.Array.Empty<RelicData>(), 0, Lineups.Squad);
            int hero = heroes.Length > 0 ? heroes[0] : -1;
            facts = EncounterSetup.SpawnEnemies(env, tracked, encounter, enemiesById);

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);

            double hpLeft = 0.0, maxHp = 0.0;
            int fallen = 0;
            UnitMetric heroMetric = null;
            for (int i = 0; i < report.Units.Count; i++)
            {
                UnitMetric m = report.Units[i];
                // Призванные тела — не бойцы отряда: их смерть не потеря, их HP не запас прочности.
                // Иначе кит с армией платил бы за бой «дешевле», просто разбавив отряд расходниками.
                if (m.Team != 0 || m.IsSummon) continue;

                hpLeft += m.HpLeft;
                maxHp += m.MaxHp;
                if (m.Died) fallen++;
                if (m.Id == hero) heroMetric = m;
            }

            SummonRollup army = hero >= 0 ? report.Summons(hero) : default;

            bool cleared = !report.TimedOut && report.Outcome.IsWinFor(0);
            return new Attempt(cleared, report.TimedOut, report.Seconds,
                maxHp > 0.0 ? hpLeft / maxHp : 0.0, fallen,
                heroMetric != null && heroMetric.Died, heroMetric, army, report.DurationTicks);
        }

        private static string Names(List<string> names)
        {
            if (names.Count == 0) return "";
            if (names.Count <= FailedNamesShown) return string.Join(", ", names);
            return string.Join(", ", names.GetRange(0, FailedNamesShown)) +
                   $" и ещё {names.Count - FailedNamesShown}";
        }
    }
}
