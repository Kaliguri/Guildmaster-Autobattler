using System.Collections.Generic;
using Guildmaster.Balance.Editor;
using Guildmaster.Combat;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Balance.Tests
{
    /// <summary>
    /// Санити-тесты примитива стенда (<see cref="SimBench.Drive"/>) на синтетиках — герметично, без
    /// зависимости от контент-ассетов. Проверяют плумбинг метрик (урон источника == урон цели), детерминизм
    /// (тот же сид → те же цифры) и корректный съём исхода/смерти. Тесты следуют за игрой: если бой изменится,
    /// правим ожидания под новую механику, а не ослабляем проверку.
    /// </summary>
    public sealed class SimBenchTests
    {
        private const int TenSeconds = 300; // 30 Гц

        private static RuntimeUnit WeakTarget(int team, Vector2 pos, float hp)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("test", new[]
            {
                new StatModifier(StatType.MaxHP, ModifierOp.Flat, hp),
                new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 1f),
            });
            var unit = new RuntimeUnit { Team = team, Stats = stats, Position = pos, PreviousPosition = pos };
            unit.CurrentHP = hp;
            return unit;
        }

        [Test]
        public void ImmortalDummy_TakesExactlyAttackerDamage()
        {
            var env = new SimEnvironment(1UL, null);
            var tracked = new List<TrackedUnit>
            {
                new TrackedUnit(SyntheticUnits.ImmortalAttacker(0, new Vector2(-1f, 0f), 50f), "atk", "atk"),
                new TrackedUnit(SyntheticUnits.ImmortalDummy(1, new Vector2(1f, 0f)), "dummy", "dummy"),
            };

            BattleReport report = SimBench.Drive(env, tracked, RunMode.FixedDuration, TenSeconds);

            UnitMetric attacker = report.Find(0);
            UnitMetric dummy = report.Find(1);
            Assert.Greater(attacker.DamageDealt, 0.0, "Атакующий должен был нанести урон бессмертной цели");
            Assert.AreEqual(attacker.DamageDealt, dummy.DamageTaken, 1e-6,
                "Весь урон источника должен ровно совпасть с уроном, полученным единственной целью");
            Assert.IsFalse(dummy.Died, "Бессмертная цель (1e9 HP) не должна умереть");
        }

        [Test]
        public void SameSeed_ProducesSameMetrics()
        {
            double a = RunDamage(7UL);
            double b = RunDamage(7UL);
            Assert.AreEqual(a, b, 1e-9, "Тот же сид → те же метрики (детерминизм стенда)");
        }

        private static double RunDamage(ulong seed)
        {
            var env = new SimEnvironment(seed, null);
            var tracked = new List<TrackedUnit>
            {
                new TrackedUnit(SyntheticUnits.ImmortalAttacker(0, new Vector2(-1f, 0f), 50f), "atk", "atk"),
                new TrackedUnit(SyntheticUnits.ImmortalDummy(1, new Vector2(1f, 0f)), "dummy", "dummy"),
            };
            BattleReport report = SimBench.Drive(env, tracked, RunMode.FixedDuration, TenSeconds);
            return report.Find(1).DamageTaken;
        }

        [Test]
        public void UntilOutcome_KillableTargetDiesAndOutcomeResolves()
        {
            var env = new SimEnvironment(1UL, null);
            var tracked = new List<TrackedUnit>
            {
                new TrackedUnit(SyntheticUnits.ImmortalAttacker(0, new Vector2(-1f, 0f), 100f), "atk", "atk"),
                new TrackedUnit(WeakTarget(1, new Vector2(1f, 0f), hp: 50f), "victim", "victim"),
            };

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, 3600);

            Assert.IsFalse(report.TimedOut, "Слабая цель должна умереть задолго до потолка");
            Assert.IsTrue(report.Outcome.IsWinFor(0), "Команда атакующего должна победить");
            UnitMetric victim = report.Find(1);
            Assert.IsTrue(victim.Died, "Жертва должна быть помечена мёртвой");
            Assert.Greater(victim.DeathTick, 0, "Тик смерти должен быть зафиксирован");
        }
    }
}
