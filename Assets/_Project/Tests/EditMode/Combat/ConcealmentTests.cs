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
    /// Маскировка (дизайн Макса 2026-07-30, правила и числа 2026-07-31): четыре ступени невидимости,
    /// обнаружение по радиусу, командное и не залипающее; скрытого не выбирают целью, удар по нему
    /// гаснет, а своя атака или каст выдают его сами.
    /// </summary>
    public sealed class ConcealmentTests
    {
        [Test]
        public void Concealed_IsNotPickedAsATarget()
        {
            var sys = new ConcealmentSystem();
            var (hider, seeker, units) = Scene(ConcealmentTier.Strong, seekerAt: new Vector2(10f, 0f));

            sys.Tick(units, SimTuning.Default);
            Assert.IsTrue(hider.IsHidden, "Предусловие: враг далеко, юнит скрыт");

            new ProfileBrain(null).Decide(seeker, new FakeBattleView(units));
            Assert.IsNull(seeker.CurrentTarget, "Скрытого не выбирают целью — к нему даже не идут");
        }

        [Test]
        public void SteppingIntoTheRadius_RevealsHim_SteppingOutHidesHimAgain()
        {
            // Обнаружение не залипает: иначе маскировка была бы бесплатным первым ходом и больше ничем,
            // а Убийце некуда было бы возвращаться из боя.
            var sys = new ConcealmentSystem();
            var (hider, seeker, units) = Scene(ConcealmentTier.Medium, seekerAt: new Vector2(10f, 0f));

            sys.Tick(units, SimTuning.Default);
            Assert.IsTrue(hider.IsHidden, "Издалека не видно");

            seeker.Position = new Vector2(3f, 0f);   // ближе четырёх — радиус средней ступени
            sys.Tick(units, SimTuning.Default);
            Assert.IsFalse(hider.IsHidden, "Подошёл ближе радиуса — заметил");

            seeker.Position = new Vector2(10f, 0f);
            sys.Tick(units, SimTuning.Default);
            Assert.IsTrue(hider.IsHidden, "Отошёл — юнит снова пропал");
        }

        [Test]
        public void TheTierDecidesHowCloseYouMustGet()
        {
            // Ступень — свойство кита, радиус — общая ручка. Проверяем именно связку: на одной и той же
            // дистанции сильная маскировка держится, а слабая уже нет.
            var sys = new ConcealmentSystem();
            var (weak, seekerA, unitsA) = Scene(ConcealmentTier.Weak,   seekerAt: new Vector2(5f, 0f));
            var (strong, seekerB, unitsB) = Scene(ConcealmentTier.Strong, seekerAt: new Vector2(5f, 0f));

            sys.Tick(unitsA, SimTuning.Default);
            sys.Tick(unitsB, SimTuning.Default);

            Assert.IsFalse(weak.IsHidden,   "Слабую видно с пяти единиц (радиус 6)");
            Assert.IsTrue(strong.IsHidden,  "Сильную — нет (радиус 2)");
        }

        [Test]
        public void Invisible_IsNeverRevealedByDistance()
        {
            var sys = new ConcealmentSystem();
            var (hider, seeker, units) = Scene(ConcealmentTier.Invisible, seekerAt: new Vector2(0.1f, 0f));

            sys.Tick(units, SimTuning.Default);

            Assert.IsTrue(hider.IsHidden, "Инвиз не снимается подходом вплотную — только своим действием");
        }

        [Test]
        public void DetectionIsShared_OneSeesHim_TheWholeTeamDoes()
        {
            // Личное обнаружение дало бы картину, которую нельзя прочитать глазами: игрок не понимает,
            // кто именно его видит.
            var sys = new ConcealmentSystem();
            var (hider, near, units) = Scene(ConcealmentTier.Medium, seekerAt: new Vector2(2f, 0f));
            RuntimeUnit far = MakeUnit(3, team: 1, new Vector2(20f, 0f));
            units.Add(far);

            sys.Tick(units, SimTuning.Default);
            Assert.IsFalse(hider.IsHidden, "Один подошёл — маскировка снята для всей его команды");

            new ProfileBrain(null).Decide(far, new FakeBattleView(units));
            Assert.AreSame(hider, far.CurrentTarget, "Дальний союзник тоже видит цель");
        }

        [Test]
        public void AHitOnTheHidden_IsNegated_LikeADodge()
        {
            // «Если его пытаются ударить и он уходит в инвиз — это уклонение и должно работать как
            // уклонение» (Макс). Случай краевой: враг занёс удар, пока цель была видна.
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            RuntimeUnit hider = MakeUnit(0, team: 0, Vector2.zero);
            ctx.ApplyEffect(hider, Concealment(ConcealmentTier.Invisible), hider);
            new ConcealmentSystem().Tick(new List<RuntimeUnit> { hider }, SimTuning.Default);

            var hit = new DamageRequest(null, hider, 30f, DamageType.Slash, CombatTestValues.ArmorK,
                sourceKind: DamageSourceKind.AutoAttack);
            Assert.IsTrue(es.RunPreDamage(hider, in hit, ctx), "Удар по скрытому уходит в пустоту");

            var dot = new DamageRequest(null, hider, 5f, DamageType.PoisonPhysical, CombatTestValues.ArmorK,
                sourceKind: DamageSourceKind.Periodic);
            Assert.IsFalse(es.RunPreDamage(hider, in dot, ctx),
                "А тик яда проходит: прятаться от него нечего, иначе маскировка станет иммунитетом к DoT");
        }

        [Test]
        public void StrongestSourceWins_AndOutlivesTheWeakerOne()
        {
            // Два источника на одном юните — случай, ради которого ступень считает система, а не поле:
            // снятие одной маскировки не должно обнулять вторую.
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var sys = new ConcealmentSystem();
            RuntimeUnit hider = MakeUnit(0, team: 0, Vector2.zero);
            var units = new List<RuntimeUnit> { hider, MakeUnit(1, team: 1, new Vector2(5f, 0f)) };

            EffectData weak   = Concealment(ConcealmentTier.Weak);
            EffectData strong = Concealment(ConcealmentTier.Strong);
            ctx.ApplyEffect(hider, weak, hider);
            ctx.ApplyEffect(hider, strong, hider);

            sys.Tick(units, SimTuning.Default);
            Assert.AreEqual(ConcealmentTier.Strong, hider.ConcealTier, "Побеждает сильнейшая ступень");
            Assert.IsTrue(hider.IsHidden, "И с пяти единиц его не видно");

            ctx.Dispel(new DispelRequest(hider, DispelTargetPolarity.Any, EffectTag.Stealth, 1, 0));
            sys.Tick(units, SimTuning.Default);
            Assert.AreNotEqual(ConcealmentTier.None, hider.ConcealTier, "Снятие одной не обнуляет вторую");
        }

        [Test]
        public void HisOwnAttack_GivesHimAway()
        {
            var sim = BuildSim(1UL);
            RuntimeUnit hider = MakeUnit(0, team: 0, Vector2.zero, range: 5f);
            RuntimeUnit enemy = MakeUnit(1, team: 1, new Vector2(3f, 0f), maxHp: 5000f);
            sim.EnqueueUnitSpawn(hider);
            sim.EnqueueUnitSpawn(enemy);
            sim.Tick(SimConstants.TickDelta);

            sim.ApplyEffect(hider, Concealment(ConcealmentTier.Invisible), hider);
            hider.CurrentTarget = enemy;

            for (int t = 0; t < 64 && hider.ConcealTier != ConcealmentTier.None; t++)
                sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(ConcealmentTier.None, hider.ConcealTier,
                "Ударил — выдал себя: «притаился перед нанесением удара» и есть смысл маскировки");
        }

        // ===================== Сцена и фабрики =====================

        private static (RuntimeUnit, RuntimeUnit, List<RuntimeUnit>) Scene(ConcealmentTier tier, Vector2 seekerAt)
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            RuntimeUnit hider  = MakeUnit(0, team: 0, Vector2.zero);
            RuntimeUnit seeker = MakeUnit(1, team: 1, seekerAt);
            ctx.ApplyEffect(hider, Concealment(tier), hider);

            return (hider, seeker, new List<RuntimeUnit> { hider, seeker });
        }

        /// <summary>Эффект маскировки заданной ступени. Тег Stealth — по нему её снимает своё действие.</summary>
        private static EffectData Concealment(ConcealmentTier tier)
        {
            var conceal = new ConcealmentComponent().With("_tier", tier);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Buff,
                tags: EffectTag.Buff | EffectTag.Stealth, stacking: StackRule.Refresh, components: conceal);
        }

        private static CombatSimulation BuildSim(ulong seed) =>
            new CombatSimulation(
                new Guildmaster.Core.Random.XorShiftRng(seed), CombatTestValues.ArmorK,
                new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp = 200f, float range = 2f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 10f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, range),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 3f),
            });

            return new RuntimeUnit
            {
                Id                   = id,
                Team                 = team,
                Stats                = stats,
                CurrentHP            = maxHp,
                Position             = pos,
                PreviousPosition     = pos,
                Unit                 = TestRelic.Make(attackType: AttackType.Melee),
                AttackType           = AttackType.Melee,
                AutoAttackDamageType = DamageType.Slash,
            };
        }
    }
}
