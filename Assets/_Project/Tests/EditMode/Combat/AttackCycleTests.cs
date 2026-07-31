using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Цикл ударов земляного голема (<see cref="AttackCycleComponent"/>): удары идут по кругу, у каждой
    /// фазы свой заряд. Здесь же охраняется ловушка, на которой цикл сломался в первый раз: заряд БЕЗ
    /// тега снятия не должен обчищать носителя.
    /// </summary>
    public sealed class AttackCycleTests
    {
        [Test]
        public void Cycle_RepeatsItsPhases_InOrder()
        {
            // Фазы: обычный, обычный, тяжёлый (x2). Значит каждый третий удар вдвое больнее.
            List<float> hits = HitsOf(attacks: 7, cycle: Cycle(HeavyPhaseCharge()));

            Assert.AreEqual(new[] { 100f, 100f, 200f, 100f, 100f, 200f, 100f }, hits.ToArray(),
                "Цикл 1-2-3 повторяется, тяжёлый — каждый третий");
        }

        [Test]
        public void ChargeWithoutConsumeTag_DoesNotStripTheCarrier()
        {
            // Ловушка: Dispel по EffectTag.None означает «по любому тегу». Заряд, которому нечего снимать,
            // стирал с носителя ВСЁ — включая сам цикл, отчего голем навсегда застревал на первой фазе.
            List<float> hits = HitsOf(attacks: 7, cycle: Cycle(HeavyPhaseCharge()));

            Assert.AreEqual(200f, hits[2], 1e-3f, "Третий удар тяжёлый — цикл на носителе выжил");
            Assert.AreEqual(200f, hits[5], 1e-3f, "И на втором проходе тоже: заряд не снёс цикл");
        }

        // ===================== Обвязка =====================

        /// <summary>Урон каждого из первых <paramref name="attacks"/> ударов носителя цикла.</summary>
        private static List<float> HitsOf(int attacks, EffectData cycle)
        {
            var sim = BuildSim();
            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero);
            var victim   = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f), maxHp: 1_000_000f, damage: 0f);
            sim.EnqueueUnitSpawn(attacker);
            sim.EnqueueUnitSpawn(victim);
            sim.Tick(SimConstants.TickDelta);
            attacker.CurrentTarget = victim;

            var hits = new List<float>();
            sim.OnDamageDealt += (src, tgt, res) => { if (src == attacker) hits.Add(res.HpDamage); };

            sim.ApplyEffect(attacker, cycle, attacker);
            EffectSystem.CommitPending(attacker);

            for (int t = 0; t < attacks * 60 && hits.Count < attacks; t++)
                sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(attacks, hits.Count, "Предусловие: носитель нанёс все удары");
            return hits;
        }

        /// <summary>Цикл из трёх фаз: две обычные, третья — заданный заряд.</summary>
        private static EffectData Cycle(EffectData heavy)
        {
            var comp = new AttackCycleComponent().With("_phases", new[] { null, null, heavy });
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: comp);
        }

        /// <summary>Тяжёлая фаза: вдвое больнее. Тег снятия НЕ задан — в этом и суть охраняемой ловушки.</summary>
        private static EffectData HeavyPhaseCharge()
        {
            var comp = new EmpowerNextAttackComponent()
                .With("_damageMult", 2f)
                .With("_consumeTag", EffectTag.None);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral,
                stacking: StackRule.Refresh, components: comp);
        }

        private static CombatSimulation BuildSim() =>
            new CombatSimulation(
                new Guildmaster.Core.Random.XorShiftRng(17UL), CombatTestValues.ArmorK,
                new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new Guildmaster.Combat.AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp = 500f,
                                            float damage = 100f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, damage),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 3f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 0f),
            });
            return new RuntimeUnit
            {
                Id                   = id,
                Team                 = team,
                Stats                = stats,
                CurrentHP            = maxHp,
                Position             = pos,
                PreviousPosition     = pos,
                AutoAttackDamageType = DamageType.Pure,
            };
        }
    }
}
