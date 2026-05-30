using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Реактивные компоненты и внутренняя event-queue: вампиризм/шипы, отсутствие бесконечной
    /// рекурсии и детерминизм (спайк S2, вики «12» §5, §7).
    /// </summary>
    public sealed class ReactiveEffectTests
    {
        // --- Диспатч (изолированно, мок-контекст) ---

        [Test]
        public void Lifesteal_HealsCarrier_OnDamageDealt()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var dealer = TestUnit.Make();
            var victim = TestUnit.Make(team: 1);

            var comp = new LifestealComponent().With("_fraction", 0.5f);
            EffectData def = TestEffect.Make(baseDuration: -1f, components: comp);
            sys.Apply(dealer, def, dealer, ctx);

            var ev = new CombatEventData(CombatEvent.DamageDealt, dealer, victim, 100f);
            sys.Dispatch(dealer, in ev, ctx);

            Assert.AreEqual(50f, ctx.TotalHealed, 1e-4f, "50% от 100 урона");
        }

        [Test]
        public void Thorns_ReflectsToAttacker_OnDamageTaken()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var defender = TestUnit.Make(team: 0);
            var attacker = TestUnit.Make(team: 1);

            var comp = new ThornsComponent().With("_reflectFraction", 0.2f).With("_damageType", DamageType.True);
            EffectData def = TestEffect.Make(baseDuration: -1f, components: comp);
            sys.Apply(defender, def, defender, ctx);

            var ev = new CombatEventData(CombatEvent.DamageTaken, attacker, defender, 100f);
            sys.Dispatch(defender, in ev, ctx);

            Assert.AreEqual(1, ctx.DamageCalls.Count);
            Assert.AreEqual(20f, ctx.DamageCalls[0].RawDamage, 1e-4f);
            Assert.AreSame(attacker, ctx.DamageCalls[0].Target, "Отражение бьёт атакующего");
        }

        [Test]
        public void Lifesteal_OnTrueEvent_DoesNotFire_OnWrongEventType()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();
            var unit = TestUnit.Make();

            var comp = new LifestealComponent().With("_fraction", 0.5f);
            EffectData def = TestEffect.Make(baseDuration: -1f, components: comp);
            sys.Apply(unit, def, unit, ctx);

            // Вампиризм слушает DamageDealt; событие DamageTaken его не триггерит.
            var ev = new CombatEventData(CombatEvent.DamageTaken, unit, unit, 100f);
            sys.Dispatch(unit, in ev, ctx);

            Assert.AreEqual(0f, ctx.TotalHealed, 1e-4f);
        }

        // --- Реальный сим: взаимные шипы завершаются и детерминированы (S2) ---

        [Test]
        public void Thorns_MutualReflection_TerminatesDeterministically()
        {
            ulong RunChecksum()
            {
                CombatSimulation sim = BuildSim(7UL);

                RuntimeUnit a = MakeMeleeUnit(team: 0, x: -1f);
                RuntimeUnit b = MakeMeleeUnit(team: 1, x: 1f);
                sim.ApplyEffect(a, Thorns(), a);
                sim.ApplyEffect(b, Thorns(), b);
                sim.EnqueueUnitSpawn(a);
                sim.EnqueueUnitSpawn(b);

                // Если бы реактивность рекурсировала бесконечно — здесь был бы hang/overflow.
                for (int t = 0; t < 120 && sim.Outcome == BattleOutcome.Ongoing; t++)
                    sim.Tick(SimConstants.TickDelta);

                return sim.ComputeChecksum();
            }

            Assert.AreEqual(RunChecksum(), RunChecksum(),
                "Взаимные шипы должны давать детерминированный результат при одном сиде");
        }

        // --- Хелперы ---

        private static EffectData Thorns()
        {
            var comp = new ThornsComponent().With("_reflectFraction", 0.25f).With("_damageType", DamageType.True);
            return TestEffect.Make(baseDuration: -1f, components: comp);
        }

        private static CombatSimulation BuildSim(ulong seed) => new CombatSimulation(
            new XorShiftRng(seed),
            100f,
            new SpatialHash(3f),
            new TargetingSystem(),
            new MovementSystem(),
            new AutoAttackSystem(),
            new ProjectileSystem(),
            new DeathSystem(),
            new EffectSystem());

        private static RuntimeUnit MakeMeleeUnit(int team, float x)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 500f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat,  50f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat,   1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat,   2f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat,   3f),
            });
            return new RuntimeUnit
            {
                Team             = team,
                Stats            = stats,
                CurrentHP        = 500f,
                Position         = new Vector2(x, 0f),
                PreviousPosition = new Vector2(x, 0f),
            };
        }
    }
}
