using Guildmaster.Combat;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Интеграция периодического урона в полный тик-цикл (шаги 9–10): DoT добивает юнита через
    /// <c>EffectSystem.Tick → DealDamage → DeathSystem</c>, а тики DoT переиспользуют Ф1-хук
    /// <see cref="CombatSimulation.OnDamageDealt"/> для презентации (цифры урона, вики «12» §3.5, §8 шаг 10).
    /// </summary>
    public sealed class DotBattleIntegrationTests
    {
        private const float CellSize = 3f;
        private const float ArmorK   = 100f;

        private static CombatSimulation BuildSim()
        {
            return new CombatSimulation(
                new XorShiftRng(7UL),
                ArmorK,
                new SpatialHash(CellSize),
                new TargetingSystem(),
                new AbilitySystem(),
                new MovementSystem(),
                new AutoAttackSystem(),
                new ProjectileSystem(),
                new DeathSystem(),
                new EffectSystem());
        }

        private static RuntimeUnit MakeInertUnit(int team, float maxHp, Vector2 pos)
        {
            // Без урона/скорости — на юнита влияет только DoT, ход боя чистый.
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, maxHp) });
            return new RuntimeUnit
            {
                Team             = team,
                Stats            = stats,
                CurrentHP        = maxHp,
                Position         = pos,
                PreviousPosition = pos,
            };
        }

        private static EffectData MakeDot(float dps, float interval, float duration)
        {
            var periodic = new PeriodicDamageComponent()
                .With("_interval", interval)
                .With("_damagePerSecond", new ScalableValue(dps))
                .With("_damageType", DamageType.Magic);
            return TestEffect.Make(baseDuration: duration, tags: EffectTag.DoT, components: periodic);
        }

        [Test]
        public void Dot_KillsUnit_AndEndsBattle()
        {
            var sim = BuildSim();
            var victim = MakeInertUnit(team: 0, maxHp: 100f, pos: new Vector2(-3f, 0f));
            var caster = MakeInertUnit(team: 1, maxHp: 100f, pos: new Vector2( 3f, 0f));

            sim.EnqueueUnitSpawn(victim);
            sim.EnqueueUnitSpawn(caster);
            sim.Tick(SimConstants.TickDelta); // flush spawns

            // 50 урона/сек × 4 сек = 200 > 100 HP → DoT добивает.
            sim.ApplyEffect(victim, MakeDot(dps: 50f, interval: 0.5f, duration: 4f), caster);

            const int maxTicks = 600;
            for (int t = 0; t < maxTicks && !victim.IsDead; t++)
                sim.Tick(SimConstants.TickDelta);

            Assert.IsTrue(victim.IsDead, "DoT должен добить юнита");
            Assert.AreNotEqual(BattleOutcome.Ongoing, sim.Outcome, "После смерти от DoT бой завершается");
        }

        [Test]
        public void DotTicks_ReuseOnDamageDealtHook_ForPresentation()
        {
            var sim = BuildSim();
            var victim = MakeInertUnit(team: 0, maxHp: 1000f, pos: new Vector2(-3f, 0f));
            var caster = MakeInertUnit(team: 1, maxHp: 100f, pos: new Vector2( 3f, 0f));

            sim.EnqueueUnitSpawn(victim);
            sim.EnqueueUnitSpawn(caster);
            sim.Tick(SimConstants.TickDelta);

            int dotHits = 0;
            float dotDamage = 0f;
            sim.OnDamageDealt += (src, tgt, result) =>
            {
                if (tgt == victim) { dotHits++; dotDamage += result.TotalDamage; }
            };

            // 20 урона/сек × интервал 1с × 3с длительности → ровно 3 тика DoT.
            sim.ApplyEffect(victim, MakeDot(dps: 20f, interval: 1f, duration: 3f), caster);

            for (int t = 0; t < SimConstants.TickRate * 3; t++) sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(3, dotHits, "Три тика DoT → три события OnDamageDealt (хук Ф1 переиспользован)");
            Assert.Greater(dotDamage, 0f, "Презентация получает ненулевой урон DoT");
        }
    }
}
