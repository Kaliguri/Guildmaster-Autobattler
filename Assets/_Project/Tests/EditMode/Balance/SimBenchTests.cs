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
        public void HpLeft_IsZeroForDead_AndMatchesRemainingForSurvivor()
        {
            var env = new SimEnvironment(1UL, null);
            var tracked = new List<TrackedUnit>
            {
                new TrackedUnit(SyntheticUnits.ImmortalAttacker(0, new Vector2(-1f, 0f), 100f), "atk", "atk"),
                new TrackedUnit(WeakTarget(1, new Vector2(1f, 0f), hp: 50f), "victim", "victim"),
            };

            BattleReport report = SimBench.Drive(env, tracked, RunMode.UntilOutcome, 3600);

            UnitMetric victim = report.Find(1);
            Assert.IsTrue(victim.Died);
            Assert.AreEqual(0.0, victim.HpPctLeft, 1e-9, "У погибшего остаток HP — ноль, а не отрицательный оверкилл");

            UnitMetric attacker = report.Find(0);
            Assert.IsFalse(attacker.Died);
            Assert.AreEqual(1.0, attacker.HpPctLeft, 1e-6, "Нетронутый боец доживает с полным запасом");
            Assert.AreEqual(attacker.MaxHp, attacker.HpLeft, 1e-3, "Абсолютный остаток совпадает с максимумом");
        }

        [Test]
        public void ReferenceAlly_FollowsClassCorridor()
        {
            // Манекены — линейка командных форматов, и мерить ею можно только пока каждый честно
            // изображает рядового бойца своего класса: коридор урона задан явно, остальное берётся из
            // живого ClassBalanceConfig (здесь его нет, поэтому проверяем ту часть, что не от конфига).
            AssertDps(UnitClass.Bruiser, 120f);
            AssertDps(UnitClass.Tank, 60f);
            AssertDps(UnitClass.Assassin, 144f);
            AssertDps(UnitClass.Ranged, 120f);
            AssertDps(UnitClass.Support, 60f);
            AssertDps(UnitClass.Summoner, 60f);

            // Фронт бьёт вплотную, тыл — с восьмёрки: строй формата держится на этой разнице.
            Assert.AreEqual(1f, SyntheticUnits.ReferenceAlly(UnitClass.Tank, null, 0, Vector2.zero)
                .Stats.Get(StatType.AttackRange), 1e-3f);
            Assert.AreEqual(8f, SyntheticUnits.ReferenceAlly(UnitClass.Ranged, null, 0, Vector2.zero)
                .Stats.Get(StatType.AttackRange), 1e-3f);
        }

        private static void AssertDps(UnitClass unitClass, float expected)
        {
            RuntimeUnit ally = SyntheticUnits.ReferenceAlly(unitClass, null, 0, Vector2.zero);
            float dps = ally.Stats.Get(StatType.AutoAttackDamage) * ally.Stats.Get(StatType.AttackSpeed);
            Assert.AreEqual(expected, dps, 1e-3f, $"Манекен класса {unitClass} должен бить по классовому коридору");
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
