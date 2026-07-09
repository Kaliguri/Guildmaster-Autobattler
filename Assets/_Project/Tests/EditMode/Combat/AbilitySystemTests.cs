using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Способности: кулдаун (× CooldownEff), трата ресурса, наложение эффектов на цель,
    /// плейсхолдер-автокаст (вики «12» §6, §8).
    /// </summary>
    public sealed class AbilitySystemTests
    {
        private static RuntimeUnit WithAbility(RuntimeUnit u, AbilityData data)
        {
            u.Abilities.Add(new AbilityRuntime(data));
            return u;
        }

        [Test]
        public void Cast_ConsumesResource_AndSetsCooldown()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = TestUnit.Make();
            caster.CurrentResource = 50f;
            WithAbility(caster, TestAbility.Make(cooldown: 4f, cost: 30f, mode: AbilityTargetMode.Self));

            bool cast = sys.TryCast(caster, 0, new List<RuntimeUnit> { caster }, ctx);

            Assert.IsTrue(cast);
            Assert.AreEqual(20f, caster.CurrentResource, 1e-4f);
            Assert.AreEqual(4f, caster.Abilities[0].CooldownRemaining, 1e-4f);
        }

        [Test]
        public void Cast_FailsWhenInsufficientResource()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = TestUnit.Make();
            caster.CurrentResource = 20f;
            WithAbility(caster, TestAbility.Make(cooldown: 4f, cost: 30f, mode: AbilityTargetMode.Self));

            bool cast = sys.TryCast(caster, 0, new List<RuntimeUnit> { caster }, ctx);

            Assert.IsFalse(cast);
            Assert.AreEqual(20f, caster.CurrentResource, 1e-4f);
            Assert.AreEqual(0f, caster.Abilities[0].CooldownRemaining, 1e-4f, "Кулдаун не ставится при провале");
        }

        [Test]
        public void Cooldown_ScaledBy_CooldownEff()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = TestUnit.Make();
            caster.Stats.AddModifiersFrom("cdr", new[] { new StatModifier(StatType.CooldownEff, ModifierOp.Flat, -0.5f) }); // 1.0 → 0.5
            WithAbility(caster, TestAbility.Make(cooldown: 4f, cost: 0f, mode: AbilityTargetMode.Self));

            sys.TryCast(caster, 0, new List<RuntimeUnit> { caster }, ctx);

            Assert.AreEqual(2f, caster.Abilities[0].CooldownRemaining, 1e-4f, "4с × 0.5 CooldownEff = 2с");
        }

        [Test]
        public void Cast_AppliesEffectsToTarget()
        {
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects); // ApplyEffect делегирует в EffectSystem
            var sys = new AbilitySystem();

            var caster = TestUnit.Make();
            var statMod = new StatModifierComponent()
                .With("_modifiers", new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 5f) });
            EffectData buff = TestEffect.Make(baseDuration: -1f, components: statMod);
            WithAbility(caster, TestAbility.Make(effects: new[] { buff }, mode: AbilityTargetMode.Self));

            sys.TryCast(caster, 0, new List<RuntimeUnit> { caster }, ctx);

            Assert.AreEqual(5f, caster.Stats.Get(StatType.MoveSpeed), 1e-4f, "Эффект способности наложен на цель");
        }

        [Test]
        public void AutoCast_FiresReadyAbility_OnTick()
        {
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);
            var sys = new AbilitySystem();

            var caster = TestUnit.Make();
            var statMod = new StatModifierComponent()
                .With("_modifiers", new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 5f) });
            EffectData buff = TestEffect.Make(baseDuration: -1f, components: statMod);
            WithAbility(caster, TestAbility.Make(effects: new[] { buff }, cooldown: 5f, mode: AbilityTargetMode.Self));

            sys.Tick(new List<RuntimeUnit> { caster }, ctx, SimConstants.TickDelta);

            Assert.AreEqual(5f, caster.Stats.Get(StatType.MoveSpeed), 1e-4f);
            Assert.Greater(caster.Abilities[0].CooldownRemaining, 0f, "После автокаста способность на кулдауне");
        }

        [Test]
        public void AutoCast_Blocked_WhenSilenced()
        {
            var effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);
            var sys = new AbilitySystem();

            var caster = TestUnit.Make();
            caster.CanCast = false; // немота
            var statMod = new StatModifierComponent()
                .With("_modifiers", new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.Flat, 5f) });
            EffectData buff = TestEffect.Make(baseDuration: -1f, components: statMod);
            WithAbility(caster, TestAbility.Make(effects: new[] { buff }, mode: AbilityTargetMode.Self));

            sys.Tick(new List<RuntimeUnit> { caster }, ctx, SimConstants.TickDelta);

            Assert.AreEqual(0f, caster.Stats.Get(StatType.MoveSpeed), 1e-4f, "Под немотой не кастуем");
        }

        [Test]
        public void Cooldown_TicksDown_OverTime()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            var caster = TestUnit.Make();
            caster.CurrentResource = 0f;
            // Стоимость выше ресурса → автокаст не сработает, изолируем убывание кулдауна.
            WithAbility(caster, TestAbility.Make(cost: 100f, mode: AbilityTargetMode.Self));
            caster.Abilities[0].CooldownRemaining = 1f;

            for (int i = 0; i < SimConstants.TickRate; i++)
                sys.Tick(new List<RuntimeUnit> { caster }, ctx, SimConstants.TickDelta);

            Assert.LessOrEqual(caster.Abilities[0].CooldownRemaining, 0f);
        }
    }
}
