using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Многоуровневый диспел: полярность ∧ теги ∧ CleanseTier ≤ DispelPower ∧ !Unremovable,
    /// лимит MaxCount, и компонент <see cref="DispelComponent"/> (вики «6» §5.4, «12» §7).
    /// </summary>
    public sealed class EffectDispelTests
    {
        [Test]
        public void Dispel_RemovesMatchingPolarity_KeepsOthers()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Debuff, tags: EffectTag.DoT), unit, ctx);
            sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Buff, tags: EffectTag.Buff), unit, ctx);
            Assert.AreEqual(2, unit.ActiveEffects.Count);

            sys.Dispel(new DispelRequest(unit, DispelTargetPolarity.Debuff, EffectTag.None, dispelPower: 1, maxCount: 0), ctx);

            Assert.AreEqual(1, unit.ActiveEffects.Count);
            Assert.AreEqual(EffectPolarity.Buff, unit.ActiveEffects[0].Def.Polarity, "Бафф должен остаться");
        }

        [Test]
        public void Dispel_SkipsUnremovable_AndHigherCleanseTier()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Debuff, cleanseTier: 0), unit, ctx);
            sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Debuff, cleanseTier: 5), unit, ctx);
            sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Debuff, unremovable: true), unit, ctx);

            sys.Dispel(new DispelRequest(unit, DispelTargetPolarity.Any, EffectTag.None, dispelPower: 1, maxCount: 0), ctx);

            // Снят только tier0; tier5 (выше DispelPower) и unremovable остались.
            Assert.AreEqual(2, unit.ActiveEffects.Count);
        }

        [Test]
        public void Dispel_RespectsMaxCount()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            for (int i = 0; i < 3; i++)
                sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Debuff, tags: EffectTag.DoT), unit, ctx);

            // Все три — разные EffectData (StackRule.None матчится по Def), поэтому 3 экземпляра.
            Assert.AreEqual(3, unit.ActiveEffects.Count);

            sys.Dispel(new DispelRequest(unit, DispelTargetPolarity.Debuff, EffectTag.None, dispelPower: 1, maxCount: 2), ctx);

            Assert.AreEqual(1, unit.ActiveEffects.Count);
        }

        [Test]
        public void Dispel_FilterByTag_RemovesOnlyMatchingCategory()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Debuff, tags: EffectTag.DoT), unit, ctx);
            sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Debuff, tags: EffectTag.Control), unit, ctx);

            // Снять только DoT, оглушение оставить.
            sys.Dispel(new DispelRequest(unit, DispelTargetPolarity.Debuff, EffectTag.DoT, dispelPower: 1, maxCount: 0), ctx);

            Assert.AreEqual(1, unit.ActiveEffects.Count);
            Assert.IsTrue((unit.ActiveEffects[0].Def.Tags & EffectTag.Control) != 0);
        }

        [Test]
        public void DispelComponent_OnApply_CleansesDebuffs()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys); // мок делегирует Dispel в sys
            var unit = TestUnit.Make();

            sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Debuff, tags: EffectTag.DoT), unit, ctx);
            Assert.AreEqual(1, unit.ActiveEffects.Count);

            var dispel = new DispelComponent()
                .With("_targetPolarity", DispelTargetPolarity.Debuff)
                .With("_targetTags", EffectTag.None)
                .With("_dispelPower", 1)
                .With("_maxCount", 0);
            // Мгновенный (BaseDuration 0): OnApply диспелит и сам не персистится.
            sys.Apply(unit, TestEffect.Make(baseDuration: 0f, components: dispel), unit, ctx);

            Assert.AreEqual(0, unit.ActiveEffects.Count, "Дебафф снят, диспел-эффект не персистнут");
        }
    }
}
