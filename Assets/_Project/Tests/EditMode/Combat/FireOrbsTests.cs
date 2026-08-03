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
    /// Огненные сферы гоблина-мага (<see cref="FireOrbsComponent"/>): запас усиленных ударов, каждый
    /// расходует одну сферу, потраченные возвращаются по одной.
    /// </summary>
    /// <remarks>
    /// Сферы — это заряды эффекта, и инвариант здесь ровно один: <b>сфер столько, сколько задано, и они
    /// кончаются</b>. Без теста «бесконечные сферы» выглядят как чуть более сильный маг, а не как дефект:
    /// урон каждого удара по отдельности правильный, неверен только их СЧЁТ.
    /// </remarks>
    public sealed class FireOrbsTests
    {
        [Test]
        public void Orbs_EmpowerExactlyTheirCount_ThenRunOut()
        {
            List<float> hits = SwingsOf(count: 4, orbs: 3, rechargeSeconds: 60f);

            Assert.AreEqual(new[] { 200f, 200f, 200f, 100f }, hits.ToArray(),
                "Три сферы — три усиленных удара, четвёртый обычный");
        }

        [Test]
        public void Orbs_ComeBackOneByOne()
        {
            // Откат (3 сек) заведомо длиннее интервала атаки (1 сек): иначе сфера успевает вернуться ровно
            // к следующему удару, и тест не различает «вернулась» и «не тратилась вовсе».
            List<float> hits = SwingsOf(count: 4, orbs: 1, rechargeSeconds: 3f);

            Assert.AreEqual(200f, hits[0], 1e-3f, "Первый удар — на стартовой сфере");
            Assert.AreEqual(100f, hits[1], 1e-3f, "Сфера потрачена, откат ещё идёт");
            Assert.AreEqual(100f, hits[2], 1e-3f, "И второй удар без сферы");
            Assert.AreEqual(200f, hits[3], 1e-3f, "Через три секунды сфера вернулась");
        }

        // ===================== Обвязка =====================

        /// <summary>Урон первых <paramref name="count"/> ударов носителя сфер.</summary>
        private static List<float> SwingsOf(int count, int orbs, float rechargeSeconds)
        {
            var sim = BuildSim();
            var mage   = MakeUnit(0, team: 0, pos: Vector2.zero);
            var victim = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f), maxHp: 1_000_000f, damage: 0f);
            sim.EnqueueUnitSpawn(mage);
            sim.EnqueueUnitSpawn(victim);
            sim.Tick(SimConstants.TickDelta);
            mage.CurrentTarget = victim;

            var hits = new List<float>();
            sim.OnDamageDealt += (src, tgt, res) => { if (src == mage) hits.Add(res.HpDamage); };

            sim.ApplyEffect(mage, Orbs(orbs, rechargeSeconds), mage);
            EffectSystem.CommitPending(mage);

            for (int t = 0; t < count * 90 && hits.Count < count; t++)
                sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(count, hits.Count, "Предусловие: носитель нанёс все удары");
            return hits;
        }

        /// <summary>Сферы с зарядом ×2 и без ускорения: тест меряет СЧЁТ сфер, а не темп.</summary>
        private static EffectData Orbs(int orbs, float rechargeSeconds)
        {
            var strike = new EmpowerNextAttackComponent()
                .With("_damageMult", 2f)
                .With("_consumeTag", EffectTag.None);
            EffectData orbStrike = TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral,
                stacking: StackRule.Refresh, components: strike);

            var comp = new FireOrbsComponent()
                .With("_orbs", orbs)
                .With("_rechargeSeconds", rechargeSeconds)
                .With("_hasteBuff", null)
                .With("_orbStrike", orbStrike);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: comp);
        }

        private static CombatSimulation BuildSim() =>
            new CombatSimulation(
                new Guildmaster.Core.Random.XorShiftRng(23UL), CombatTestValues.ArmorK,
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
