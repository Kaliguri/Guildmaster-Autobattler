using System.Collections;
using Guildmaster.Combat;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Guildmaster.Tests.PlayMode.Battle
{
    /// <summary>
    /// Интеграционный тест: бой стартует, тикает, одна из сторон побеждает и симуляция останавливается
    /// (вики «10» §10). Запускается в PlayMode без сцены — симуляция headless.
    /// </summary>
    public sealed class BattleIntegrationTest
    {
        private const float   CellSize = 3f;
        private const float   ArmorK   = 100f;
        private const ulong   Seed     = 77UL;
        private const int     MaxTicks = 3600;

        private static CombatSimulation BuildAndPopulate()
        {
            var sim = new CombatSimulation(
                new XorShiftRng(Seed),
                ArmorK,
                new SpatialHash(CellSize),
                new TargetingSystem(),
                new AbilitySystem(),
                new MovementSystem(),
                new AutoAttackSystem(),
                new ProjectileSystem(),
                new DeathSystem(),
                new EffectSystem());

            sim.EnqueueUnitSpawn(MakeUnit(0, new Vector2(-5f, 0f)));
            sim.EnqueueUnitSpawn(MakeUnit(0, new Vector2(-4f, 1f)));
            sim.EnqueueUnitSpawn(MakeUnit(1, new Vector2( 5f, 0f)));
            sim.EnqueueUnitSpawn(MakeUnit(1, new Vector2( 4f, 1f)));
            return sim;
        }

        private static RuntimeUnit MakeUnit(int team, Vector2 pos)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 300f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat,  60f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat,   1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat,   1.5f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat,   4f),
            });
            return new RuntimeUnit
            {
                Team             = team,
                Stats            = stats,
                CurrentHP        = 300f,
                Position         = pos,
                PreviousPosition = pos,
            };
        }

        [UnityTest]
        public IEnumerator Battle_StartsAndEndsWithWinner()
        {
            var sim = BuildAndPopulate();

            BattleOutcome? endOutcome = null;
            sim.OnBattleEnded += outcome => endOutcome = outcome;

            int ticks = 0;
            while (sim.Outcome == BattleOutcome.Ongoing && ticks < MaxTicks)
            {
                sim.Tick(SimConstants.TickDelta);
                ticks++;

                if (ticks % 30 == 0) yield return null;
            }

            Assert.AreNotEqual(BattleOutcome.Ongoing, sim.Outcome,
                $"Бой не завершился за {MaxTicks} тиков");
            Assert.IsNotNull(endOutcome, "OnBattleEnded не сработал");
            Assert.AreEqual(sim.Outcome, endOutcome.Value);
        }

        [UnityTest]
        public IEnumerator Battle_AllUnitsOnWinnerSideAlive()
        {
            var sim = BuildAndPopulate();

            while (sim.Outcome == BattleOutcome.Ongoing)
            {
                sim.Tick(SimConstants.TickDelta);
            }

            yield return null;

            int winTeam = sim.Outcome == BattleOutcome.TeamAWins ? 0 : 1;
            bool anyLoserAlive = false;

            for (int i = 0; i < sim.Units.Count; i++)
            {
                var unit = sim.Units[i];
                if (unit.Team != winTeam && !unit.IsDead)
                    anyLoserAlive = true;
            }

            Assert.IsFalse(anyLoserAlive, "После победы у проигравшей стороны не должно быть живых юнитов");
        }
    }
}
