using Guildmaster.Combat;
using Guildmaster.Combat.Commands;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Детерминизм симуляции: одинаковый сид + одинаковый лог команд → одинаковый checksum
    /// после N тиков (вики «10» §10).
    /// </summary>
    public sealed class CombatSimulationTests
    {
        private const ulong Seed    = 42UL;
        private const int   Ticks   = 120;

        private static CombatSimulation BuildSim(ulong seed)
        {
            var rng = new XorShiftRng(seed);
            return new CombatSimulation(
                rng,
                CombatTestValues.ArmorK,
                new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(),
                new AbilitySystem(),
                new MovementSystem(),
                new AutoAttackSystem(),
                new ProjectileSystem(),
                new DeathSystem(),
                new EffectSystem(),
                new RegenSystem());
        }

        private static RuntimeUnit MakeMeleeUnit(int team, float x)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 500f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat,  50f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat,   1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat,   1.5f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat,   3f),
            });
            return new RuntimeUnit
            {
                Team             = team,
                Stats            = stats,
                CurrentHP        = 500f,
                Position         = new Vector2(x, 0f),
                PreviousPosition = new Vector2(x, 0f),
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
        }

        [Test]
        public void SameSeedAndCommands_ProduceSameChecksum()
        {
            var simA = BuildSim(Seed);
            var simB = BuildSim(Seed);

            PopulateSim(simA);
            PopulateSim(simB);

            for (int t = 0; t < Ticks; t++)
            {
                simA.Tick(SimConstants.TickDelta);
                simB.Tick(SimConstants.TickDelta);
            }

            Assert.AreEqual(simA.ComputeChecksum(), simB.ComputeChecksum(),
                $"После {Ticks} тиков checksum расходится при одинаковом сиде");
        }

        // ВНИМАНИЕ: в Фазе 1 симуляция НЕ потребляет RNG (нет крита/разброса урона),
        // поэтому ход боя при разных сидах идентичен. Этот тест проверяет лишь, что
        // checksum включает состояние RNG и различает сиды. Когда в Фазе 2 рандом войдёт
        // в пайплайн — заменить на проверку реального расхождения боя.
        [Test]
        public void DifferentSeeds_ProduceDifferentChecksums_ViaRngState()
        {
            var simA = BuildSim(Seed);
            var simB = BuildSim(Seed + 1UL);

            PopulateSim(simA);
            PopulateSim(simB);

            for (int t = 0; t < Ticks; t++)
            {
                simA.Tick(SimConstants.TickDelta);
                simB.Tick(SimConstants.TickDelta);
            }

            Assert.AreNotEqual(simA.ComputeChecksum(), simB.ComputeChecksum(),
                "Checksum должен различать разные сиды (через снапшот состояния RNG)");
        }

        [Test]
        public void PauseCommand_StopsTick()
        {
            var sim = BuildSim(Seed);
            PopulateSim(sim);

            sim.EnqueueCommand(new PauseCommand(targetTick: 5));

            for (int t = 0; t < 30; t++) sim.Tick(SimConstants.TickDelta);

            Assert.IsTrue(sim.IsPaused);
            Assert.AreEqual(6, sim.CurrentTick, "Пауза на тике 5 → процессинг был на тиках 0–5");
        }

        [Test]
        public void PauseAndResume_WorkCorrectly()
        {
            var sim = BuildSim(Seed);
            PopulateSim(sim);

            sim.EnqueueCommand(new PauseCommand (targetTick: 2));
            sim.EnqueueCommand(new ResumeCommand(targetTick: 4));

            for (int t = 0; t < 10; t++) sim.Tick(SimConstants.TickDelta);

            Assert.IsFalse(sim.IsPaused);
            Assert.Greater(sim.CurrentTick, 4);
        }

        [Test]
        public void Battle_EventuallyEnds()
        {
            var sim = BuildSim(Seed);
            PopulateSim(sim);

            const int maxTicks = 3600;
            for (int t = 0; t < maxTicks && sim.Outcome == BattleOutcome.Ongoing; t++)
                sim.Tick(SimConstants.TickDelta);

            Assert.AreNotEqual(BattleOutcome.Ongoing, sim.Outcome,
                $"Бой не завершился за {maxTicks} тиков");
        }

        // Чек-сумма обязана видеть эффекты (аудит 2026-07-26, RC-8): раньше в неё входили только позиция,
        // HP и тайминги атаки, поэтому расхождение, начавшееся в эффектах, всплывало лишь когда доедет до
        // урона — с задержкой в секунды и уже без следа причины. Юниты здесь идентичны во всём, кроме
        // навешенного яда: если сумма их не различит, значит слепок снова слеп к эффектам.
        [Test]
        public void Checksum_SeesEffects_NotOnlyPositionAndHp()
        {
            var withEffect    = BuildSim(Seed);
            var withoutEffect = BuildSim(Seed);

            RuntimeUnit carrier = MakeMeleeUnit(team: 0, x: -5f);
            RuntimeUnit plain   = MakeMeleeUnit(team: 0, x: -5f);
            carrier.Id = plain.Id = 7;

            var poison = Guildmaster.Data.Definitions.EffectData.CreateRuntime(
                "test.poison",
                Guildmaster.Data.Definitions.EffectPolarity.Debuff,
                Guildmaster.Data.Definitions.EffectTag.None,
                baseDuration: 5f,
                unremovable: false);
            carrier.ActiveEffects.Add(new Guildmaster.Combat.Effects.RuntimeEffect
            {
                Def               = poison,
                ScaledPotency     = new float[0],
                PeriodicTicks     = new int[0],
            });
            carrier.ActiveEffects[0].SetDuration(60);

            withEffect.EnqueueUnitSpawn(carrier);
            withoutEffect.EnqueueUnitSpawn(plain);
            withEffect.FlushSpawns();
            withoutEffect.FlushSpawns();

            Assert.AreNotEqual(withoutEffect.ComputeChecksum(), withEffect.ComputeChecksum(),
                "Чек-сумма не различает юнита с эффектом и без — значит рассинхрон по эффектам она не поймает");
        }

        private static void PopulateSim(CombatSimulation sim)
        {
            sim.EnqueueUnitSpawn(MakeMeleeUnit(team: 0, x: -5f));
            sim.EnqueueUnitSpawn(MakeMeleeUnit(team: 0, x: -4f));
            sim.EnqueueUnitSpawn(MakeMeleeUnit(team: 1, x:  5f));
            sim.EnqueueUnitSpawn(MakeMeleeUnit(team: 1, x:  4f));
        }
    }
}
