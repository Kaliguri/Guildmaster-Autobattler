using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Срез трёх новых китов (E2): Друид (яд + «Взрыв спор»), Огненный мечник («Пылающие клинки»),
    /// Древень («Шипастое древо»). Проверяем механику, а не числа баланса.
    /// </summary>
    public sealed class PoisonBurnThornsSliceTests
    {
        // --- Древень: ответка по площади от БРОНИ, только на авто-атаку ---

        [Test]
        public void ArmorThorns_RetaliatesAllEnemiesAround_ForArmorValue()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);

            var treant = TestUnit.Make(team: 0);
            treant.Stats.AddModifiersFrom("armor", new[] { new StatModifier(StatType.PhysArmor, ModifierOp.Flat, 20f) });

            var attacker = TestUnit.Make(team: 1);
            var bystander = TestUnit.Make(team: 1);
            bystander.Position = new Vector2(2f, 0f); // в радиусе шипов
            ctx.UnitsInWorld.Add(attacker);
            ctx.UnitsInWorld.Add(bystander);

            var comp = new ArmorThornsComponent().With("_armorRatio", 1f).With("_radius", 3f);
            sys.Apply(treant, TestEffect.Make(baseDuration: -1f, components: comp), treant, ctx);

            var hit = new CombatEventData(CombatEvent.DamageTaken, attacker, treant, 100f, EffectTag.None, isAutoAttack: true);
            sys.Dispatch(treant, in hit, ctx);

            Assert.AreEqual(2, ctx.DamageCalls.Count, "Шипы бьют ВСЕХ врагов вокруг, не только атакующего");
            Assert.AreEqual(20f, ctx.DamageCalls[0].RawDamage, 1e-4f, "Урон шипов = 100% брони носителя");
        }

        [Test]
        public void ArmorThorns_DoesNotRetaliate_OnNonAutoAttackDamage()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);

            var treant = TestUnit.Make(team: 0);
            treant.Stats.AddModifiersFrom("armor", new[] { new StatModifier(StatType.PhysArmor, ModifierOp.Flat, 20f) });
            var attacker = TestUnit.Make(team: 1);
            ctx.UnitsInWorld.Add(attacker);

            sys.Apply(treant, TestEffect.Make(baseDuration: -1f, components: new ArmorThornsComponent()), treant, ctx);

            // Урон способности/DoT/чужих шипов — не авто-атака: ответка не срабатывает (нет пинг-понга шипов).
            var hit = new CombatEventData(CombatEvent.DamageTaken, attacker, treant, 100f);
            sys.Dispatch(treant, in hit, ctx);

            Assert.AreEqual(0, ctx.DamageCalls.Count);
        }

        // --- Огненный мечник: разгон скорости атаки + само-урон за удар ---

        [Test]
        public void BlazingBlades_RampsAttackSpeed_AndCostsOwnHp()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);

            var pyre = TestUnit.Make(team: 0);
            pyre.CurrentHP = 1000f;
            pyre.Stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.AttackSpeed, ModifierOp.Flat, 1f) });
            var victim = TestUnit.Make(team: 1);

            EffectData ramp = TestEffect.Make(
                baseDuration: -1f,
                polarity: EffectPolarity.Buff,
                tags: EffectTag.Buff,
                stacking: StackRule.Stack,
                maxStacks: 20,
                components: new StatModifierComponent().With("_modifiers",
                    new[] { new StatModifier(StatType.AttackSpeed, ModifierOp.PercentAdd, 0.05f) }));

            var comp = new BlazingBladesComponent()
                .With("_selfDamagePctCurrentHp", 0.01f)
                .With("_rampEffect", ramp);
            sys.Apply(pyre, TestEffect.Make(baseDuration: -1f, components: comp), pyre, ctx);

            float baseAttackSpeed = pyre.Stats.Get(StatType.AttackSpeed);

            var hit = new CombatEventData(CombatEvent.DamageDealt, pyre, victim, 100f, EffectTag.None, isAutoAttack: true);
            sys.Dispatch(pyre, in hit, ctx);

            Assert.Greater(pyre.Stats.Get(StatType.AttackSpeed), baseAttackSpeed, "Удар клинком разгоняет скорость атаки");

            Assert.AreEqual(1, ctx.DamageCalls.Count, "Каждый удар стоит носителю части своего HP");
            Assert.AreSame(pyre, ctx.DamageCalls[0].Target, "Само-урон бьёт по себе");
            Assert.AreEqual(10f, ctx.DamageCalls[0].RawDamage, 1e-4f, "1% от текущего HP (1000)");
            Assert.AreEqual(DamageSchool.True, ctx.DamageCalls[0].School, "Само-урон идёт мимо брони");
        }

        [Test]
        public void BlazingBlades_Ignores_NonAutoAttackDamage()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);
            var pyre = TestUnit.Make(team: 0);
            var victim = TestUnit.Make(team: 1);

            sys.Apply(pyre, TestEffect.Make(baseDuration: -1f, components: new BlazingBladesComponent()), pyre, ctx);

            var abilityHit = new CombatEventData(CombatEvent.DamageDealt, pyre, victim, 100f);
            sys.Dispatch(pyre, in abilityHit, ctx);

            Assert.AreEqual(0, ctx.DamageCalls.Count, "Само-урон не капает с урона способностей (иначе рекурсия)");
        }

        // --- Друид: детонация всех отравленных («Взрыв спор») ---

        [Test]
        public void SporeBurst_DamagesEveryPoisonedEnemy_WithPoisonAffinity_AndConsumesTag()
        {
            var effects = new EffectSystem();

            var druid = TestUnit.Make(team: 0);
            druid.Stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 100f) });

            var poisoned1 = TestUnit.Make(team: 1);
            var poisoned2 = TestUnit.Make(team: 1);
            var clean = TestUnit.Make(team: 1);

            EffectData spores = TestEffect.Make(
                baseDuration: 4f, polarity: EffectPolarity.Debuff,
                tags: EffectTag.Debuff | EffectTag.DoT | EffectTag.Poison,
                stacking: StackRule.Refresh, maxStacks: 1);

            var units = new List<RuntimeUnit> { druid, poisoned1, poisoned2, clean };
            var ctx = new MockCombatContext(effects: effects);
            effects.Apply(poisoned1, spores, druid, ctx);
            effects.Apply(poisoned2, spores, druid, ctx);

            AbilityData burst = TestAbility.Make(
                mode: AbilityTargetMode.AllEnemiesWithTag,
                damageMultiplier: 2.5f,
                castCondition: CastCondition.EnemiesWithTagCount,
                castConditionCount: 2,
                triggerTag: EffectTag.Poison,
                consumesTriggerTag: true,
                schoolOverride: DamageSchoolOverride.True,
                affinityOverride: DamageAffinityOverride.Poison);

            druid.Abilities.Add(new AbilityRuntime(burst));

            var abilities = new AbilitySystem();
            Assert.IsTrue(abilities.TryCast(druid, 0, units, ctx), "Двое отравленных — условие каста выполнено");

            Assert.AreEqual(2, ctx.DamageCalls.Count, "Детонируют только отравленные");
            Assert.AreEqual(250f, ctx.DamageCalls[0].RawDamage, 1e-4f, "2.5 × AutoAttackDamage");
            Assert.AreEqual(DamageAffinity.Poison, ctx.DamageCalls[0].Affinity);
            Assert.AreEqual(DamageSchool.True, ctx.DamageCalls[0].School);

            Assert.AreEqual(EffectTag.None, poisoned1.EffectTagMask & EffectTag.Poison, "Тег «Яд» израсходован взрывом");
        }
    }
}
