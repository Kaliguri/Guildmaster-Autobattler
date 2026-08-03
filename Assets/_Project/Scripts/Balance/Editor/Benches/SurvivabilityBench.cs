using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Фаза 1 — бенч выживаемости. Кит-жертва стоит против бессмертного эталонного источника урона
    /// (фикс-DPS, физика): «solo» = 1 атакующий, «focus» = 3 атакующих одновременно (фокус-огонь).
    /// Меряем время до смерти (TTD) и поглощённый до смерти урон (EHP-прокси). Атакующие бессмертны —
    /// чтобы мерить чисто стойкость, а не «кто кого перебил». Реген/лайфстил/щиты жертвы учтены (полный сим).
    /// Ограничение: эталон физический single-target (синтетик без кита); школо-зависимая стойкость — отложено.
    /// </summary>
    public static class SurvivabilityBench
    {
        /// <summary>Урон эталонного атакующего. Internal — по нему <see cref="BalanceNorms"/> считает норму TTD.</summary>
        internal const float RefDps = 60f;
        private const float CapSeconds = 240f;
        private const ulong Seed = 1UL;

        public static (string csv, string md) Run()
        {
            StatsConfig config = BalanceAssets.LoadStatsConfig();
            int cap = SimBench.TicksFromSeconds(CapSeconds);
            List<RelicData> relics = BalanceAssets.LoadRelics();

            var headers = new List<string>
            {
                "Relic", "TTD_solo", "EHP_solo", "HpLeft_solo%", "TTD_focus3", "EHP_focus3", "HpLeft_focus3%",
            };
            var table = new List<IReadOnlyList<object>>();

            foreach (RelicData relic in relics)
            {
                (double ttd1, double ehp1, double hp1) = RunSurvival(config, relic, attackers: 1, cap);
                (double ttd3, double ehp3, double hp3) = RunSurvival(config, relic, attackers: 3, cap);
                table.Add(new object[]
                {
                    relic.name,
                    ttd1 < 0 ? (object)"survived" : ttd1,
                    ehp1,
                    100.0 * hp1,
                    ttd3 < 0 ? (object)"survived" : ttd3,
                    ehp3,
                    100.0 * hp3,
                });
            }

            string notes =
                $"**Бенч выживаемости** (эталон {RefDps:0} DPS/атакующий, физика; потолок {CapSeconds:0} с). " +
                "TTD — время до смерти, сек («survived» = пережил потолок). EHP — суммарно поглощённый урон до смерти. " +
                "HpLeft% — остаток HP на конец прогона: у погибших 0, у выживших показывает, насколько хватило запаса. " +
                "focus3 = трое атакующих (фокус-огонь). Эталон физ-single-target: школо-зависимая стойкость отложена. " +
                $"**Кит, собирающий живучесть с союзников** («Каменная десятина»), в этой линзе мерится с заменой " +
                $"сделки на её результат: пассивка снята, запас +{SoloTitheHpBonus * 100f:0}%, старт с " +
                $"{SoloTitheStartHp * 100f:0}% максимума (решение Макса 03.08). Без этого он садится на свой " +
                "стартовый процент HP, собирать дань не с кого — и число говорило бы о линзе, а не о ките.";

            string csv = ReportWriter.WriteCsv("bench_survivability", headers, table);
            string md = ReportWriter.WriteMarkdown("bench_survivability", "SimBench — выживаемость (Фаза 1)", headers, table, notes);
            ReportWriter.WriteJson("bench_survivability", "SimBench — выживаемость (Фаза 1)", headers, table, notes);
            return (csv, md);
        }

        private static (double ttd, double ehp, double hpLeft) RunSurvival(
            StatsConfig config, RelicData relic, int attackers, int cap)
        {
            var env = new SimEnvironment(Seed, config);
            var tracked = new List<TrackedUnit>();

            for (int a = 0; a < attackers; a++)
            {
                var pos = new Vector2(-2.2f - a * 0.5f, (a - (attackers - 1) * 0.5f) * 0.6f);
                tracked.Add(new TrackedUnit(SyntheticUnits.ImmortalAttacker(0, pos, RefDps), "attacker" + a, "attacker"));
            }

            int victimIndex = tracked.Count;
            var victim = env.Real(relic, null, 1, new Vector2(2f, 0f));
            CompensateSoloTithe(env, victim);
            tracked.Add(new TrackedUnit(victim, relic.name, relic.name));

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, cap);
            UnitMetric vm = report.Find(victimIndex);
            if (vm == null) return (-1, 0, 0);

            double ttd = vm.Died ? vm.DeathTick / (double)SimConstants.TickRate : -1;
            return (ttd, vm.DamageTaken, vm.HpPctLeft);
        }

        /// <summary>Прибавка к запасу вместо несостоявшейся сделки, доля максимума (решение Макса 03.08).</summary>
        private const float SoloTitheHpBonus = 0.2f;

        /// <summary>С каким наполнением такой кит начинает соло-замер, доля максимума.</summary>
        private const float SoloTitheStartHp = 0.7f;

        /// <summary>
        /// Кит, чья живучесть по замыслу собирается С СОЮЗНИКОВ, в соло-линзе мерится не тем, чем живёт:
        /// «Каменная десятина» сажает носителя на 1% HP и добирает недостающее данью с команды, а в этом
        /// бенче команды нет — он просто умирает на первом тике, и число говорит о линзе, а не о ките
        /// (та же ловушка, что была с Друидом и его сценарием свалки).
        /// <para>Поэтому в СОЛО сделка заменяется её результатом: пассивка снимается, запас растёт на
        /// <see cref="SoloTitheHpBonus"/>, старт — <see cref="SoloTitheStartHp"/> максимума. Числа
        /// дизайнерские (решение Макса 03.08.2026), а не выведенные из формулы дани: они отвечают на
        /// вопрос «сколько он стоит, когда сделка СОСТОЯЛАСЬ», а не «сколько дани он собрал бы».</para>
        /// </summary>
        /// <remarks>
        /// Ищем по КОМПОНЕНТУ, а не по имени кита: второй такой же получит компенсацию сам, без правки
        /// стенда. Правка живёт только здесь — в командных форматах и энкаунтерах сделка состоится
        /// по-настоящему, и подменять там нечего.
        /// </remarks>
        private static void CompensateSoloTithe(SimEnvironment env, RuntimeUnit victim)
        {
            RuntimeEffect tithe = null;
            List<RuntimeEffect> effects = victim.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
            {
                IEffectComponent[] comps = effects[i].Def != null ? effects[i].Def.Components : null;
                if (comps == null) continue;

                for (int c = 0; c < comps.Length; c++)
                {
                    if (comps[c] is not StoneTitheComponent) continue;
                    tithe = effects[i];
                    break;
                }
                if (tithe != null) break;
            }

            if (tithe == null) return;

            // Снимаем ДО первого тика: иначе десятина успеет посадить носителя на свой стартовый процент,
            // и компенсация окажется прибавкой к трупу.
            env.Effects.Remove(victim, tithe, env.Sim);

            victim.Stats.AddModifiersFrom("solo-tithe-compensation", new[]
            {
                new StatModifier(StatType.MaxHP, ModifierOp.PercentMult, SoloTitheHpBonus),
            }, deferred: false);

            victim.CurrentHP = victim.Stats.Get(StatType.MaxHP) * SoloTitheStartHp;
        }
    }
}
