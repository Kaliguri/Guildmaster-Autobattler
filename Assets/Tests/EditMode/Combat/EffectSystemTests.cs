using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Жизненный цикл эффектов в <see cref="EffectSystem"/>: наложение, маска тегов, истечение,
    /// периодика (детерминированный счётчик тиков), масштабирование длительности эфф-эффективностями,
    /// стакинг (вики «12» §7).
    /// </summary>
    public sealed class EffectSystemTests
    {
        private static RuntimeUnit[] One(RuntimeUnit u) => new[] { u };

        // --- Наложение / маска ---

        [Test]
        public void Apply_AddsEffect_AndSetsTagMaskBit()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();
            EffectData def = TestEffect.Make(baseDuration: 1f, tags: EffectTag.Debuff | EffectTag.DoT);

            sys.Apply(unit, def, unit, ctx);

            Assert.AreEqual(1, unit.ActiveEffects.Count);
            Assert.IsTrue((unit.EffectTagMask & EffectTag.DoT) != 0);
            Assert.IsTrue((unit.EffectTagMask & EffectTag.Debuff) != 0);
        }

        [Test]
        public void Expire_RemovesEffect_AndClearsTagMaskBit_AndCallsOnExpire()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();
            var comp = new CountingComponent();
            EffectData def = TestEffect.Make(baseDuration: 1f, tags: EffectTag.Buff, components: comp);

            sys.Apply(unit, def, unit, ctx);
            Assert.AreEqual(1, comp.Applied);

            // 1 сек = 30 тиков → истекает ровно на 30-м.
            for (int i = 0; i < SimConstants.TickRate; i++) sys.Tick(One(unit), ctx, SimConstants.TickDelta);

            Assert.AreEqual(0, unit.ActiveEffects.Count);
            Assert.AreEqual(EffectTag.None, unit.EffectTagMask);
            Assert.AreEqual(1, comp.Expired);
        }

        [Test]
        public void Instant_AppliesOnce_AndDoesNotPersist()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();
            var comp = new CountingComponent();
            EffectData def = TestEffect.Make(baseDuration: 0f, components: comp);

            sys.Apply(unit, def, unit, ctx);

            Assert.AreEqual(0, unit.ActiveEffects.Count, "Мгновенный эффект не должен персиститься");
            Assert.AreEqual(1, comp.Applied);
        }

        [Test]
        public void Permanent_NeverExpires()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();
            EffectData def = TestEffect.Make(baseDuration: -1f);

            sys.Apply(unit, def, unit, ctx);
            for (int i = 0; i < 300; i++) sys.Tick(One(unit), ctx, SimConstants.TickDelta);

            Assert.AreEqual(1, unit.ActiveEffects.Count);
            Assert.IsTrue(unit.ActiveEffects[0].IsPermanent);
        }

        // --- Периодика ---

        [Test]
        public void PeriodicDamage_TicksEveryInterval_NotEveryFrame()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();
            var comp = new CountingPeriodicComponent(interval: 1f, potencyPerSecond: 10f);
            EffectData def = TestEffect.Make(baseDuration: 3f, components: comp);

            sys.Apply(unit, def, unit, ctx);
            for (int i = 0; i < 90; i++) sys.Tick(One(unit), ctx, SimConstants.TickDelta);

            Assert.AreEqual(3, comp.Ticks, "Интервал 1с за 3с → ровно 3 срабатывания");
            Assert.AreEqual(30f, comp.TotalApplied, 0.0001f, "10/сек × 1с × 3 тика = 30");
        }

        [Test]
        public void DurationChange_ScalesTotalDot_RateStaysConstant()
        {
            // Та же per-second потенция, удвоенная длительность → вдвое больше тиков и total.
            float Run(float durationSeconds)
            {
                var sys = new EffectSystem();
                var ctx = new MockCombatContext();
                var unit = TestUnit.Make();
                var comp = new CountingPeriodicComponent(interval: 1f, potencyPerSecond: 10f);
                EffectData def = TestEffect.Make(baseDuration: durationSeconds, components: comp);

                sys.Apply(unit, def, unit, ctx);
                int ticks = (int)(durationSeconds * SimConstants.TickRate);
                for (int i = 0; i < ticks; i++) sys.Tick(One(unit), ctx, SimConstants.TickDelta);
                return comp.TotalApplied;
            }

            Assert.AreEqual(160f, Run(16f), 0.0001f);
            Assert.AreEqual(320f, Run(32f), 0.0001f);
        }

        // --- Длительность × эфф-эффективности ---

        [Test]
        public void ApplyDuration_ScaledBy_ApplyEff_TimesReceiveEff()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();

            var source = TestUnit.Make();
            source.Stats.AddModifiersFrom("s", new[] { new StatModifier(StatType.ApplyBuffEff, ModifierOp.Flat, 1f) });   // 1.0 → 2.0
            var target = TestUnit.Make();
            target.Stats.AddModifiersFrom("t", new[] { new StatModifier(StatType.ReceiveBuffEff, ModifierOp.Flat, 0.5f) }); // 1.0 → 1.5

            EffectData def = TestEffect.Make(baseDuration: 2f, polarity: EffectPolarity.Buff);
            sys.Apply(target, def, source, ctx);

            // 2с × 2.0 × 1.5 = 6с = 180 тиков
            Assert.AreEqual(180, target.ActiveEffects[0].RemainingTicks);
        }

        [Test]
        public void NeutralPolarity_DurationNotScaled()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();

            var source = TestUnit.Make();
            source.Stats.AddModifiersFrom("s", new[] { new StatModifier(StatType.ApplyBuffEff, ModifierOp.Flat, 1f) });
            var target = TestUnit.Make();

            EffectData def = TestEffect.Make(baseDuration: 2f, polarity: EffectPolarity.Neutral);
            sys.Apply(target, def, source, ctx);

            // Neutral не скейлится: 2с = 60 тиков, несмотря на ApplyBuffEff источника.
            Assert.AreEqual(60, target.ActiveEffects[0].RemainingTicks);
        }

        // --- Стакинг ---

        [Test]
        public void Stack_RespectsMaxStacks()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();
            EffectData def = TestEffect.Make(baseDuration: 5f, stacking: StackRule.Stack, maxStacks: 3);

            for (int i = 0; i < 5; i++) sys.Apply(unit, def, unit, ctx);

            Assert.AreEqual(1, unit.ActiveEffects.Count);
            Assert.AreEqual(3, unit.ActiveEffects[0].Stacks);
        }

        [Test]
        public void Refresh_ResetsDuration_KeepsStacks()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();
            EffectData def = TestEffect.Make(baseDuration: 2f, stacking: StackRule.Refresh);

            sys.Apply(unit, def, unit, ctx);                                   // 60 тиков
            for (int i = 0; i < 30; i++) sys.Tick(One(unit), ctx, SimConstants.TickDelta); // → 30
            sys.Apply(unit, def, unit, ctx);                                   // refresh → 60

            Assert.AreEqual(60, unit.ActiveEffects[0].RemainingTicks);
            Assert.AreEqual(1, unit.ActiveEffects[0].Stacks);
        }

        [Test]
        public void NoneStacking_IgnoresReapply()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();
            EffectData def = TestEffect.Make(baseDuration: 2f, stacking: StackRule.None);

            sys.Apply(unit, def, unit, ctx);
            for (int i = 0; i < 30; i++) sys.Tick(One(unit), ctx, SimConstants.TickDelta); // → 30
            sys.Apply(unit, def, unit, ctx); // None → игнор, длительность не трогаем

            Assert.AreEqual(1, unit.ActiveEffects.Count);
            Assert.AreEqual(30, unit.ActiveEffects[0].RemainingTicks);
        }
    }
}
