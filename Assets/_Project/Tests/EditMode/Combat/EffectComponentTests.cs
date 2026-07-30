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
    /// Библиотека компонентов эффектов: стат-моды, периодический урон/хил, щит, контроль
    /// (вики «12» §6, §7).
    /// </summary>
    public sealed class EffectComponentTests
    {
        private static RuntimeUnit[] One(RuntimeUnit u) => new[] { u };

        private static void TickN(EffectSystem sys, RuntimeUnit u, MockCombatContext ctx, int n)
        {
            for (int i = 0; i < n; i++) sys.Tick(One(u), ctx, SimConstants.TickDelta);
        }

        // --- StatModifierComponent ---

        [Test]
        public void StatModifier_AppliesOnApply_RemovesOnExpire()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            var comp = new StatModifierComponent()
                .With("_modifiers", new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 5f) });
            EffectData def = TestEffect.Make(baseDuration: 1f, components: comp);

            sys.Apply(unit, def, unit, ctx);
            Assert.AreEqual(0f, unit.Stats.Get(StatType.MoveSpeed), 1e-4f,
                "Закон видимости: наложенный этим тиком мод ещё не действует");

            EffectSystem.CommitPending(unit);   // конец тика
            Assert.AreEqual(5f, unit.Stats.Get(StatType.MoveSpeed), 1e-4f);

            TickN(sys, unit, ctx, SimConstants.TickRate); // истечение
            EffectSystem.CommitPending(unit);   // снятие проявляется там же, где наложение
            Assert.AreEqual(0f, unit.Stats.Get(StatType.MoveSpeed), 1e-4f);
        }

        [Test]
        public void StatModifier_ScalesByStacks()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            var comp = new StatModifierComponent()
                .With("_modifiers", new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 5f) });
            EffectData def = TestEffect.Make(baseDuration: 5f, stacking: StackRule.Stack, maxStacks: 3, components: comp);

            sys.Apply(unit, def, unit, ctx);
            sys.Apply(unit, def, unit, ctx);
            sys.Apply(unit, def, unit, ctx);
            EffectSystem.CommitPending(unit);   // конец тика: три наложения проявляются разом

            Assert.AreEqual(3, unit.ActiveEffects[0].Stacks);
            Assert.AreEqual(15f, unit.Stats.Get(StatType.MoveSpeed), 1e-4f, "3 стака × 5 = 15");
        }

        // --- PeriodicDamageComponent ---

        [Test]
        public void PeriodicDamage_DealsPerSecondRate_OverDuration()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            var comp = new PeriodicDamageComponent()
                .With("_interval", 1f)
                .With("_damagePerSecond", new ScalableValue(10f))
                .With("_damageSchool", DamageType.Pure);
            EffectData def = TestEffect.Make(baseDuration: 3f, tags: EffectTag.DoT, components: comp);

            sys.Apply(unit, def, unit, ctx);
            TickN(sys, unit, ctx, 90);

            Assert.AreEqual(3, ctx.DamageCalls.Count, "3с / 1с = 3 тика урона");
            Assert.AreEqual(30f, ctx.TotalRawDamage, 1e-4f, "10/сек × 1с × 3 = 30");
        }

        // --- PeriodicHealComponent ---

        [Test]
        public void PeriodicHeal_HealsPerSecondRate_OverDuration()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            var comp = new PeriodicHealComponent()
                .With("_interval", 1f)
                .With("_healPerSecond", new ScalableValue(10f));
            EffectData def = TestEffect.Make(baseDuration: 3f, tags: EffectTag.HoT, components: comp);

            sys.Apply(unit, def, unit, ctx);
            TickN(sys, unit, ctx, 90);

            Assert.AreEqual(30f, ctx.TotalHealed, 1e-4f);
        }

        // --- ShieldComponent ---

        [Test]
        public void Shield_AddsOnApply_RemovesRemainderOnExpire()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            var comp = new ShieldComponent().With("_amount", new ScalableValue(50f));
            EffectData def = TestEffect.Make(baseDuration: 1f, tags: EffectTag.Shield, components: comp);

            sys.Apply(unit, def, unit, ctx);
            Assert.AreEqual(50f, unit.CurrentShield, 1e-4f);

            TickN(sys, unit, ctx, SimConstants.TickRate);
            Assert.AreEqual(0f, unit.CurrentShield, 1e-4f);
        }

        // --- ShieldBurstComponent (M17) ---

        [Test]
        public void ShieldBurst_DamagesAroundCarrier_ByFractionOfTheShieldSize()
        {
            // M17: своего числа урона у взрыва нет — он функция размера щита. Полного, а не остатка:
            // платой служит щит, который враг был вынужден пробить.
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var carrier = TestUnit.Make();
            var enemy   = TestUnit.Make(team: 1);
            ctx.UnitsInWorld.Add(enemy);

            var shield = new ShieldComponent().With("_amount", new ScalableValue(50f));
            var burst  = new ShieldBurstComponent()
                .With("_fractionOfShield", 1f)
                .With("_radius", 2f);
            EffectData def = TestEffect.Make(
                baseDuration: 5f, tags: EffectTag.Shield, components: new IEffectComponent[] { shield, burst });

            sys.Apply(carrier, def, carrier, ctx);
            Assert.AreEqual(50f, carrier.CurrentShield, 1e-4f);

            // Щит пробит: взрыв на весь его размер.
            carrier.CurrentShield = 0f;
            sys.Dispatch(carrier, new CombatEventData(CombatEvent.DamageTaken, carrier, carrier, 10f), ctx);

            Assert.AreEqual(1, ctx.DamageCalls.Count, "Взрыв ударил по врагу в радиусе");
            Assert.AreEqual(50f, ctx.DamageCalls[0].RawDamage, 1e-4f, "Урон = 100% размера щита");
        }

        [Test]
        public void ShieldBurst_DoesNotFireWhileTheShieldHolds_AndOnlyOnce()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var carrier = TestUnit.Make();
            var enemy   = TestUnit.Make(team: 1);
            ctx.UnitsInWorld.Add(enemy);

            var shield = new ShieldComponent().With("_amount", new ScalableValue(40f));
            var burst  = new ShieldBurstComponent().With("_fractionOfShield", 0.5f).With("_radius", 2f);
            EffectData def = TestEffect.Make(
                baseDuration: 5f, tags: EffectTag.Shield, components: new IEffectComponent[] { shield, burst });

            sys.Apply(carrier, def, carrier, ctx);

            // Щит ещё держит — взрыва быть не должно, иначе кит бьёт зоной с каждого удара по себе.
            carrier.CurrentShield = 10f;
            sys.Dispatch(carrier, new CombatEventData(CombatEvent.DamageTaken, carrier, carrier, 10f), ctx);
            Assert.AreEqual(0, ctx.DamageCalls.Count, "Пока щит цел, взрыва нет");

            // Пробит — взрыв. И только один: следующий удар в том же тике второго не даёт.
            carrier.CurrentShield = 0f;
            sys.Dispatch(carrier, new CombatEventData(CombatEvent.DamageTaken, carrier, carrier, 10f), ctx);
            sys.Dispatch(carrier, new CombatEventData(CombatEvent.DamageTaken, carrier, carrier, 10f), ctx);

            Assert.AreEqual(1, ctx.DamageCalls.Count, "Один щит — один взрыв");
            Assert.AreEqual(20f, ctx.DamageCalls[0].RawDamage, 1e-4f, "Доля 0.5 от щита 40");
        }

        // --- ControlComponent ---

        [Test]
        public void Control_SetsFlags_WhileActive_AndClearsAfterExpiry()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            var comp = new ControlComponent().With("_preventAct", true).With("_preventMove", true);
            EffectData def = TestEffect.Make(baseDuration: 1f, tags: EffectTag.Control, components: comp);

            sys.Apply(unit, def, unit, ctx);
            sys.Tick(One(unit), ctx, SimConstants.TickDelta); // первый пересчёт флагов

            Assert.IsFalse(unit.CanAct);
            Assert.IsFalse(unit.CanMove);
            Assert.IsTrue(unit.CanCast, "preventCast не задан");

            TickN(sys, unit, ctx, SimConstants.TickRate); // до истечения
            Assert.IsTrue(unit.CanAct);
            Assert.IsTrue(unit.CanMove);
        }

        [Test]
        public void Control_StunnedUnit_DoesNotAutoAttack()
        {
            var ctx = new MockCombatContext();
            var attacker = TestUnit.Make(team: 0);
            attacker.Stats.AddModifiersFrom("atk", new[]
            {
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 50f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 5f),
            });
            var target = TestUnit.Make(team: 1);
            attacker.CurrentTarget = target;
            attacker.Position = Vector2.zero;
            target.Position = new Vector2(1f, 0f);
            attacker.CanAct = false; // оглушён

            new AutoAttackSystem().Tick(new System.Collections.Generic.List<RuntimeUnit> { attacker, target }, ctx, SimConstants.TickDelta);

            Assert.AreEqual(0, ctx.DamageCalls.Count, "Оглушённый юнит не должен атаковать");
        }
    }
}
