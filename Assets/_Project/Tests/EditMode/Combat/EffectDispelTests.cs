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
        // Цена очистки в стаках (решение 2026-07-27/5). Эффект без цены снимается целиком, как раньше;
        // эффект с ценой отдаёт max(плоское, доля) и продолжает жить остатком. Заведено под «Угли»,
        // которые копятся без потолка: одна очистка стирала любую накопленную ставку.
        [Test]
        public void Dispel_TakesOnlyItsPriceInStacks_WhenEffectSetsOne()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            EffectData embers = TestEffect.Make(
                baseDuration: -1f, polarity: EffectPolarity.Debuff,
                stacking: StackRule.Stack, maxStacks: 999, cleanseTier: 1,
                cleanseStacksFlat: 10, cleanseStacksPct: 0.25f);

            for (int i = 0; i < 60; i++) sys.Apply(unit, embers, unit, ctx);
            Assert.AreEqual(60, unit.ActiveEffects[0].Stacks, "Шестьдесят угольков");

            // Снятие судит по состоянию НАЧАЛА тика: наложенное в этом же тике неприкосновенно.
            ctx.AdvanceTick();
            sys.Dispel(new DispelRequest(unit, DispelTargetPolarity.Debuff, EffectTag.None, dispelPower: 1, maxCount: 0), ctx);

            Assert.AreEqual(1, unit.ActiveEffects.Count, "Эффект остался — унесли только часть");
            Assert.AreEqual(45, unit.ActiveEffects[0].Stacks, "25% от 60 больше десяти → ушло 15");

            // На малом счёте выигрывает плоская часть и сметает остаток целиком. Стаки правим тем же
            // путём, что бой: у поля нет сеттера, чтобы граница тика оставалась у одного владельца.
            unit.ActiveEffects[0].RemoveStacks(unit.ActiveEffects[0].Stacks - 8);
            // Снятие судит по состоянию НАЧАЛА тика: наложенное в этом же тике неприкосновенно.
            ctx.AdvanceTick();
            sys.Dispel(new DispelRequest(unit, DispelTargetPolarity.Debuff, EffectTag.None, dispelPower: 1, maxCount: 0), ctx);
            Assert.AreEqual(0, unit.ActiveEffects.Count, "Восемь стаков меньше плоских десяти → эффект снят");
        }

        // Лестница цены (решение 2026-07-27/7): чем выше сила развеивания над тиром эффекта, тем больше
        // уносит одно очищение. Иначе сильный клинз ничем не отличался бы от базового.
        [Test]
        public void Dispel_TakesMore_WhenPowerExceedsEffectTier()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();

            EffectData Embers() => TestEffect.Make(
                baseDuration: -1f, polarity: EffectPolarity.Debuff,
                stacking: StackRule.Stack, maxStacks: 999, cleanseTier: 1,
                cleanseStacksFlat: 0, cleanseStacksPct: 0f);

            int StacksLeftAfter(int dispelPower)
            {
                var unit = TestUnit.Make();
                EffectData def = Embers();
                // Лестница тира 1: свой уровень (5;15%), на уровень выше (10;25%), на два (20;50%).
                var price = new[]
                {
                    new EffectData.CleansePrice { Flat = 5,  Pct = 0.15f },
                    new EffectData.CleansePrice { Flat = 10, Pct = 0.25f },
                    new EffectData.CleansePrice { Flat = 20, Pct = 0.50f },
                };
                Reflect.FindField(typeof(EffectData), "_cleansePrice").SetValue(def, price);

                for (int i = 0; i < 60; i++) sys.Apply(unit, def, unit, ctx);
                // Снятие судит по состоянию НАЧАЛА тика: наложенное в этом же тике неприкосновенно.
                ctx.AdvanceTick();
                sys.Dispel(new DispelRequest(unit, DispelTargetPolarity.Debuff, EffectTag.None, dispelPower, maxCount: 0), ctx);
                return unit.ActiveEffects.Count == 0 ? 0 : unit.ActiveEffects[0].Stacks;
            }

            Assert.AreEqual(51, StacksLeftAfter(1), "Свой тир: 15% от 60 = 9");
            Assert.AreEqual(45, StacksLeftAfter(2), "На уровень выше: 25% = 15");
            Assert.AreEqual(30, StacksLeftAfter(3), "На два уровня: половина");
        }

        [Test]
        public void Dispel_RemovesMatchingPolarity_KeepsOthers()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Debuff, tags: EffectTag.DoT), unit, ctx);
            sys.Apply(unit, TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Buff, tags: EffectTag.Buff), unit, ctx);
            Assert.AreEqual(2, unit.ActiveEffects.Count);

            // Снятие судит по состоянию НАЧАЛА тика: наложенное в этом же тике неприкосновенно.
            ctx.AdvanceTick();
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

            // Снятие судит по состоянию НАЧАЛА тика: наложенное в этом же тике неприкосновенно.
            ctx.AdvanceTick();
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

            // Снятие судит по состоянию НАЧАЛА тика: наложенное в этом же тике неприкосновенно.
            ctx.AdvanceTick();
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
            // Снятие судит по состоянию НАЧАЛА тика: наложенное в этом же тике неприкосновенно.
            ctx.AdvanceTick();
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
            // Дебафф должен «повисеть»: снятие судит по состоянию начала тика.
            ctx.AdvanceTick();

            // Мгновенный (BaseDuration 0): OnApply диспелит и сам не персистится.
            sys.Apply(unit, TestEffect.Make(baseDuration: 0f, components: dispel), unit, ctx);

            Assert.AreEqual(0, unit.ActiveEffects.Count, "Дебафф снят, диспел-эффект не персистнут");
        }
    }
}
