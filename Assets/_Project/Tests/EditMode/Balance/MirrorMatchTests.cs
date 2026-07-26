using System.Collections.Generic;
using Guildmaster.Balance.Editor;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Balance.Tests
{
    /// <summary>
    /// Зеркальные бои: две ОДИНАКОВЫЕ команды, отражённые по оси. Ни один такой бой не должен
    /// заканчиваться уверенной победой одной из сторон — иначе у симуляции есть встроенное преимущество
    /// стороны, и тогда врут все замеры стенда, а в игре одна из команд системно сильнее другой.
    /// </summary>
    /// <remarks>
    /// Сторож заведён после того, как замена в отряде показала зеркальный бой со счётом 59.7% против нуля.
    /// Тест намеренно живёт на синтетиках: если перекос ловится уже на них, причина в ядре боя, а не в ките.
    /// </remarks>
    public sealed class MirrorMatchTests
    {
        private const int Cap = 240 * 30;   // 240 с при 30 Гц — тот же потолок, что у бенчей

        /// <summary>Допуск: команды могут разойтись на считанные проценты HP, но не на исход боя.</summary>
        private const double AllowedHpGap = 0.10;

        [Test]
        public void Mirror_OneOnOne_EndsEven()
        {
            AssertMirrorIsEven(new[]
            {
                new Slot(UnitClass.Bruiser, 1f, 0f),
            });
        }

        [Test]
        public void Mirror_Squad_EndsEven()
        {
            AssertMirrorIsEven(Lineups.Squad);
        }

        /// <summary>
        /// Зеркало КАЖДОГО реального кита против самого себя. Манекены симметричны по построению, а кит
        /// несёт способности, ресурс и мозги — если сторона решает исход, ломается именно здесь.
        /// </summary>
        [Test]
        public void Mirror_EachRelicAgainstItself_EndsEven([ValueSource(nameof(RelicNames))] string relicName)
        {
            List<RelicData> relics = BalanceAssets.LoadRelics();
            RelicData relic = relics.Find(r => r.name == relicName);
            Assert.IsNotNull(relic, $"Реликвия {relicName} не найдена");

            var env = new SimEnvironment(1UL, BalanceAssets.LoadStatsConfig());
            var tracked = new List<TrackedUnit>();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();
            var lineup = new[] { new Slot(relic.CombatClass, 2.2f, 0f) };

            Lineups.SpawnTeam(env, classes, tracked, new[] { relic }, 0, lineup);
            Lineups.SpawnTeam(env, classes, tracked, new[] { relic }, 1, lineup);

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, Cap);
            double left = TeamHp(report, 0);
            double right = TeamHp(report, 1);

            Assert.AreEqual(left, right, AllowedHpGap,
                $"{relicName} против самого себя: слева осталось {left:P1} HP, справа {right:P1}");
        }

        /// <summary>
        /// Зеркало из ЧЕТЫРЁХ РАЗНЫХ реальных китов — ровно тот бой, на котором замена в отряде показала
        /// 59.7% против нуля. Отдельно от предыдущих: там симметричны и бойцы, и роли, а здесь роли разные,
        /// и сломаться может именно их взаимодействие.
        /// </summary>
        [Test]
        public void Mirror_RealSquad_EndsEven()
        {
            List<RelicData> relics = BalanceAssets.LoadRelics();
            var squad = new List<RelicData>();
            foreach (string name in new[] { "Defender", "FlameSwordsman", "Cryomancer", "LightShepherd" })
            {
                RelicData relic = relics.Find(r => r.name == name);
                Assert.IsNotNull(relic, $"Реликвия {name} не найдена");
                squad.Add(relic);
            }

            var env = new SimEnvironment(1UL, BalanceAssets.LoadStatsConfig());
            var tracked = new List<TrackedUnit>();
            ClassBalanceConfig classes = BalanceAssets.LoadClassBalanceConfig();

            Lineups.SpawnTeam(env, classes, tracked, squad, 0, Lineups.Squad);
            Lineups.SpawnTeam(env, classes, tracked, squad, 1, Lineups.Squad);

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, Cap);
            double left = TeamHp(report, 0);
            double right = TeamHp(report, 1);

            Assert.AreEqual(left, right, AllowedHpGap,
                $"Зеркальный отряд из четырёх разных китов: слева осталось {left:P1} HP, справа {right:P1}");
        }

        private static IEnumerable<string> RelicNames()
        {
            List<RelicData> relics = BalanceAssets.LoadRelics();
            var names = new List<string>();
            foreach (RelicData r in relics) names.Add(r.name);
            return names;
        }

        private static void AssertMirrorIsEven(Slot[] lineup)
        {
            var env = new SimEnvironment(1UL, null);
            var tracked = new List<TrackedUnit>();
            var noHeroes = new RelicData[0];

            Lineups.SpawnTeam(env, null, tracked, noHeroes, 0, lineup);
            Lineups.SpawnTeam(env, null, tracked, noHeroes, 1, lineup);

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, Cap);

            double left = TeamHp(report, 0);
            double right = TeamHp(report, 1);

            Assert.AreEqual(left, right, AllowedHpGap,
                $"Зеркальный бой не должен давать преимущество стороне: слева осталось {left:P1} HP, " +
                $"справа {right:P1}. Разрыв означает, что порядок обработки (или что-то ещё, зависящее от " +
                "того, кто заспавнен первым) решает исход за бойцов.");
        }

        private static double TeamHp(BattleReport report, int team)
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
    }
}
