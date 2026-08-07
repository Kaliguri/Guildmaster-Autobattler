using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// «Изморозь» (<see cref="FrostComponent"/>): ступени решаются по числу стаков НА НАЧАЛО ТИКА.
    /// Проверяем обе ступени, которые смотрят на порог, — уязвимость ко льду и обращение в статую.
    /// </summary>
    /// <remarks>
    /// Закон видимости завели после того, как «Угли» развели зеркало на тике 300: компонент, читающий
    /// живое число стаков, отдаёт разный исход в зависимости от того, чей ход разрешён раньше в этом же
    /// тике. У «Изморози» та же дыра оставалась открытой до 2026-08-07 — оба порога читали
    /// <c>ctx.Effect.Stacks</c>, а не <c>ctx.Stacks</c>. Инвариант живёт между <c>FrostComponent</c>,
    /// <c>EffectContext</c> и <c>EffectSystem</c>, поэтому держит его тест, а не комментарий.
    /// Механический запрет на возврат дыры — <see cref="EffectStacksVisibilityGateTests"/>.
    /// </remarks>
    public sealed class FrostTests
    {
        private const int RootThreshold   = 3;
        private const int StatueThreshold = 20;
        private const float IceVulnMid    = 0.1f;

        /// <summary>
        /// Стак, легший в этом же тике, не даёт прибавки ко льду этого же тика: иначе удар одного юнита
        /// усиливается наложением другого, и величина зависит от круга разрешения.
        /// </summary>
        [Test]
        public void IceVulnerability_JudgesStacksAtTickStart()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);
            var attacker = TestUnit.Make(team: 0);
            var victim   = TestUnit.Make(team: 1);

            EffectData frost = MakeFrost();

            // На начало тика висит на один стак МЕНЬШЕ порога.
            for (int i = 0; i < RootThreshold - 1; i++) sys.Apply(victim, frost, attacker, ctx);
            ctx.AdvanceTick(victim);

            Assert.AreEqual(1f, IceMultiplier(sys, ctx, attacker, victim), 1e-4f,
                "Предусловие: ниже порога прибавки нет");

            // Порог добирается ЭТИМ тиком — живое число уже на пороге, снимок ещё нет.
            sys.Apply(victim, frost, attacker, ctx);

            Assert.AreEqual(1f, IceMultiplier(sys, ctx, attacker, victim), 1e-4f,
                "Стак этого тика в исход этого тика не входит");

            // Граница тика пройдена — снимок догнал живое число.
            ctx.AdvanceTick(victim);

            Assert.AreEqual(1f + IceVulnMid, IceMultiplier(sys, ctx, attacker, victim), 1e-4f,
                "Со следующего тика порог взят и прибавка идёт");
        }

        /// <summary>
        /// Обращение в статую — то же правило: цель не может застыть от стака, положенного в том же
        /// тике, потому что порог судит снимок.
        /// </summary>
        [Test]
        public void StatueThreshold_JudgesStacksAtTickStart()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext(effects: sys);
            var attacker = TestUnit.Make(team: 0);
            var victim   = TestUnit.Make(team: 1);
            var units    = new System.Collections.Generic.List<RuntimeUnit> { victim };

            EffectData statue = TestEffect.Make(baseDuration: 5f, polarity: EffectPolarity.Debuff,
                                                tags: EffectTag.Frozen);
            EffectData frost  = MakeFrost(statue);

            for (int i = 0; i < StatueThreshold - 1; i++) sys.Apply(victim, frost, attacker, ctx);
            ctx.AdvanceTick(victim);

            sys.Tick(units, ctx, SimDelta);
            Assert.IsFalse(HasEffect(victim, statue), "Предусловие: ниже порога статуи нет");

            // Последний стак кладётся в этом тике.
            sys.Apply(victim, frost, attacker, ctx);
            sys.Tick(units, ctx, SimDelta);

            Assert.IsFalse(HasEffect(victim, statue),
                "Стак этого тика цель в лёд не обращает — порог судит начало тика");

            ctx.AdvanceTick(victim);
            sys.Tick(units, ctx, SimDelta);

            Assert.IsTrue(HasEffect(victim, statue), "Со следующего тика порог взят и статуя встаёт");
        }

        // --- Помощники ---

        private const float SimDelta = 1f / Guildmaster.Core.Simulation.SimConstants.TickRate;

        private static EffectData MakeFrost(EffectData statueEffect = null)
        {
            var comp = new FrostComponent()
                .With("_rootThreshold",   RootThreshold)
                .With("_statueThreshold", StatueThreshold)
                .With("_iceVulnMid",      IceVulnMid)
                .With("_statueEffect",    statueEffect);

            return TestEffect.Make(baseDuration: 60f, polarity: EffectPolarity.Debuff,
                                   tags: EffectTag.Frostbite, stacking: StackRule.Stack,
                                   maxStacks: StatueThreshold, components: comp);
        }

        /// <summary>Множитель, который компоненты цели накинули на ледяной удар в pre-damage проходе.</summary>
        private static float IceMultiplier(EffectSystem sys, MockCombatContext ctx,
                                           RuntimeUnit attacker, RuntimeUnit victim)
        {
            var req = new DamageRequest(attacker, victim, 100f, DamageType.Ice, ctx.ArmorK);
            sys.RunPreDamage(victim, in req, ctx);
            return sys.PreDamageMultiplier;
        }

        private static bool HasEffect(RuntimeUnit unit, EffectData def)
        {
            for (int i = 0; i < unit.ActiveEffects.Count; i++)
                if (ReferenceEquals(unit.ActiveEffects[i].Def, def)) return true;
            return false;
        }
    }
}
