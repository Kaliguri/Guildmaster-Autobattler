using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Вертикальный срез «Криомант» (вики «13» §10.2): on-hit «Заморозка» снаряда (§9.1) и
    /// глобальный масс-стан «Ледяные оковы» по тегу с конверсией (§9.10). Проверяет: снаряд вешает
    /// «Заморозку» при попадании; ульта станит всех замороженных без ограничения дальности, игнорит
    /// незамороженных, конвертирует «Заморозку» в стан (снимает тег); каст по порогу числа
    /// замороженных (блок D); в пустоту не кастует; таргетинг PreferUntagged избегает замороженного.
    /// <para>Наложение/снятие эффектов гоняем через реальные <c>EffectSystem</c>/<c>ProjectileSystem</c>
    /// за <see cref="MockCombatContext"/> — как настоящий бой, но headless.</para>
    /// </summary>
    public sealed class CryomancerSliceTests
    {
        // ===================== §9.1 On-hit «Заморозка» =====================

        [Test]
        public void Cryo_Projectile_AppliesFrozen_OnImpact()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            var source = MakeUnit(0, team: 0, pos: Vector2.zero);
            var enemy  = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f), moveSpeed: 5f);
            var units  = new List<RuntimeUnit> { source, enemy };

            EffectData frozen = MakeFrozen();
            var proj = new Projectile
            {
                Source           = source,
                Position         = source.Position,
                PreviousPosition = source.Position,
                Velocity         = new Vector2(20f, 0f),
                CollisionRadius  = 0.25f,
                TargetUnit       = enemy,
                RawDamage        = 15f,
                DamageType       = DamageType.Physical,
                OnHitEffects     = new[] { frozen },
                IsAlive          = true,
            };

            var system      = new ProjectileSystem();
            var projectiles = new List<Projectile> { proj };
            for (int t = 0; t < 32 && (enemy.EffectTagMask & EffectTag.Frozen) == 0; t++)
                system.Tick(projectiles, units, ctx, SimConstants.TickDelta, ArenaBounds.Unbounded);

            Assert.AreNotEqual(EffectTag.None, enemy.EffectTagMask & EffectTag.Frozen, "Снаряд при попадании вешает «Заморозку»");
            Assert.AreEqual(1, ctx.DamageCalls.Count, "Урон снаряда тоже применён (on-hit не заменяет урон)");
            Assert.AreEqual(2f, enemy.Stats.Get(StatType.MoveSpeed), 1e-4f, "«Заморозка» режет MoveSpeed на 60% (5 → 2)");
        }

        [Test]
        public void MeleeAutoAttack_AppliesOnHitEffect_ToTarget()
        {
            // Гвард симметричного шва §9.1: мили-путь тоже накладывает on-hit эффекты.
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            RelicData relic = TestRelic.Make(attackType: AttackType.Melee, autoAttackEffects: new[] { MakeFrozen() });
            var attacker = MakeUnit(0, team: 0, pos: Vector2.zero, relic: relic, range: 5f);
            var enemy    = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f), moveSpeed: 5f);
            attacker.CurrentTarget = enemy;
            var units = new List<RuntimeUnit> { attacker, enemy };

            var system = new AutoAttackSystem();
            for (int t = 0; t < 64 && (enemy.EffectTagMask & EffectTag.Frozen) == 0; t++)
                system.Tick(units, ctx, SimConstants.TickDelta);

            Assert.AreNotEqual(EffectTag.None, enemy.EffectTagMask & EffectTag.Frozen, "Мили-АА с AutoAttackEffects вешает эффект на цель");
        }

        // ===================== §9.10 «Ледяные оковы» (масс-стан по тегу) =====================

        [Test]
        public void Cryo_Ult_StunsAllFrozen_IgnoresUnfrozen_NoRange()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            var caster    = MakeUnit(0, team: 0, pos: Vector2.zero);
            var frozenNear = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            var frozenFar  = MakeUnit(2, team: 1, pos: new Vector2(1000f, 0f)); // абсурдно далеко — доказывает «без дальности»
            var unfrozen   = MakeUnit(3, team: 1, pos: new Vector2(1f, 0f));
            var units = new List<RuntimeUnit> { caster, frozenNear, frozenFar, unfrozen };

            EffectData frozen = MakeFrozen();
            ctx.ApplyEffect(frozenNear, frozen, caster);
            ctx.ApplyEffect(frozenFar,  frozen, caster);

            caster.Abilities.Add(new AbilityRuntime(MakeIceChains()));
            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);

            Assert.IsTrue(cast);
            Assert.AreNotEqual(EffectTag.None, frozenNear.EffectTagMask & EffectTag.Control, "Ближний замороженный оглушён");
            Assert.AreNotEqual(EffectTag.None, frozenFar.EffectTagMask  & EffectTag.Control, "Дальний замороженный тоже оглушён — без ограничения дальности");
            Assert.IsFalse(frozenNear.CanAct, "Стан реально снимает дееспособность");
            Assert.AreEqual(EffectTag.None, unfrozen.EffectTagMask & EffectTag.Control, "Незамороженного ульта не трогает");
        }

        [Test]
        public void Cryo_Ult_ConsumesFrozen_ReplacedByStun()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            var caster = MakeUnit(0, team: 0, pos: Vector2.zero);
            var enemy  = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            var units = new List<RuntimeUnit> { caster, enemy };

            ctx.ApplyEffect(enemy, MakeFrozen(), caster);
            Assert.AreNotEqual(EffectTag.None, enemy.EffectTagMask & EffectTag.Frozen, "Предусловие: цель заморожена");

            caster.Abilities.Add(new AbilityRuntime(MakeIceChains()));
            new AbilitySystem().TryCast(caster, 0, units, ctx);

            Assert.AreEqual(EffectTag.None, enemy.EffectTagMask & EffectTag.Frozen, "«Заморозка» снята (конверсия)");
            Assert.AreNotEqual(EffectTag.None, enemy.EffectTagMask & EffectTag.Control, "На месте «Заморозки» — стан");
        }

        [Test]
        public void Cryo_Ult_CastsAt_FrozenCountThreshold()
        {
            AbilityData chains = MakeIceChains(
                castCondition: CastCondition.EnemiesWithTagCount, castConditionCount: 2);

            // 1 замороженный < 2 → каста нет.
            {
                var caster = MakeUnit(0, team: 0, pos: Vector2.zero);
                var e1 = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
                e1.EffectTagMask = EffectTag.Frozen;
                var units = new List<RuntimeUnit> { caster, e1 };
                caster.Abilities.Add(new AbilityRuntime(chains));

                Assert.IsFalse(new AbilitySystem().TryCast(caster, 0, units, new MockCombatContext(effects: new EffectSystem())),
                    "Замороженных меньше порога — не кастуем");
                Assert.AreEqual(0f, caster.Abilities[0].CooldownRemaining, 1e-4f, "КД не ставится без каста");
            }

            // 2 замороженных ≥ 2 → каст.
            {
                var caster = MakeUnit(0, team: 0, pos: Vector2.zero);
                var e1 = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
                var e2 = MakeUnit(2, team: 1, pos: new Vector2(3f, 0f));
                e1.EffectTagMask = EffectTag.Frozen;
                e2.EffectTagMask = EffectTag.Frozen;
                var units = new List<RuntimeUnit> { caster, e1, e2 };
                caster.Abilities.Add(new AbilityRuntime(chains));

                Assert.IsTrue(new AbilitySystem().TryCast(caster, 0, units, new MockCombatContext(effects: new EffectSystem())),
                    "Замороженных достигло порога — кастуем");
            }
        }

        [Test]
        public void Cryo_Ult_DoesNotCast_WhenNoFrozen()
        {
            // Даже с условием Immediately масс-стан в пустоту не жжёт КД/ману (нет валидной цели).
            var caster = MakeUnit(0, team: 0, pos: Vector2.zero);
            var enemy  = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f)); // не заморожен
            var units = new List<RuntimeUnit> { caster, enemy };
            caster.Abilities.Add(new AbilityRuntime(MakeIceChains(cost: 30f)));
            caster.CurrentResource = 30f;

            bool cast = new AbilitySystem().TryCast(caster, 0, units, new MockCombatContext(effects: new EffectSystem()));

            Assert.IsFalse(cast, "Замороженных нет — каста нет");
            Assert.AreEqual(30f, caster.CurrentResource, 1e-4f, "Мана не потрачена");
            Assert.AreEqual(0f, caster.Abilities[0].CooldownRemaining, 1e-4f, "КД не поставлен");
        }

        // ===================== Таргетинг PreferUntagged (блок A) =====================

        [Test]
        public void PreferUntagged_AvoidsFrozenEnemy_PicksPlain()
        {
            var self       = MakeUnit(0, team: 0, pos: Vector2.zero);
            var nearFrozen = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f)); // ближе, но заморожен → избегаем
            var farPlain   = MakeUnit(2, team: 1, pos: new Vector2(7f, 0f)); // дальше, но чистый → выбираем
            nearFrozen.EffectTagMask = EffectTag.Frozen;

            var view  = new FakeBattleView(self, nearFrozen, farPlain);
            var brain = new ProfileBrain(new AIProfile(TargetingMode.PreferUntagged, targetTag: EffectTag.Frozen));

            brain.Decide(self, view);

            Assert.AreSame(farPlain, self.CurrentTarget, "PreferUntagged(Frozen) избегает замороженного даже ближнего");
        }

        // ===================== Фабрики контента среза =====================

        /// <summary>«Заморозка»: Debuff-эффект 4с, тег Frozen, −60% MoveSpeed, обновляется повторной АА.</summary>
        private static EffectData MakeFrozen()
        {
            var slow = new StatModifierComponent().With("_modifiers",
                new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.PercentMult, -0.6f) });
            return TestEffect.Make(
                baseDuration: 4f,
                polarity: EffectPolarity.Debuff,
                tags: EffectTag.Frozen | EffectTag.Debuff,
                stacking: StackRule.Refresh,
                components: slow);
        }

        /// <summary>«Ледяные оковы»: масс-стан 2с по всем врагам с Frozen, конвертирует Frozen в стан.</summary>
        private static AbilityData MakeIceChains(
            float cost = 0f,
            CastCondition castCondition = CastCondition.Immediately,
            int castConditionCount = 1)
        {
            var stun = new ControlComponent().With("_preventAct", true);
            EffectData stunEffect = TestEffect.Make(
                baseDuration: 2f,
                polarity: EffectPolarity.Debuff,
                tags: EffectTag.Control | EffectTag.Debuff,
                components: stun);

            return TestAbility.Make(
                effects: new[] { stunEffect },
                cost: cost,
                mode: AbilityTargetMode.AllEnemiesWithTag,
                triggerTag: EffectTag.Frozen,
                consumesTriggerTag: true,
                castCondition: castCondition,
                castConditionCount: castConditionCount);
        }

        private static RuntimeUnit MakeUnit(
            int id, int team, Vector2 pos, RelicData relic = null,
            float maxHp = 500f, float range = 5f, float moveSpeed = 3f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 10f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, range),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, moveSpeed),
            });
            return new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentHP        = maxHp,
                Position         = pos,
                PreviousPosition = pos,
                Unit             = relic,
            };
        }
    }
}
