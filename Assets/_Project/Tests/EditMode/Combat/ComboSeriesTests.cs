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
    /// Комбо — серия Атак (ГДД: глоссарий, 2026-07-30/6 и /11). Здесь охраняется вся его механика: что
    /// именно считается Атакой, что серию рвёт, а что нет, и что разрыв делает с уже взведённым зарядом.
    /// <para>Почему тестами, а не комментариями: инвариант проходит между четырьмя сторонами —
    /// <c>AutoAttackSystem</c> (считает Атаки и рвёт серию), <c>AttackCycleComponent</c> и
    /// <c>EveryNthAttackComponent</c> (читают счётчик и гасят своё), <c>SimTuning</c> (порог разрыва). Ни
    /// одна не видит остальных, а цена расхождения — кит, который после стана продолжает цикл с той же
    /// фазы, то есть бьёт тяжёлым ударом сразу после того, как его выбили из боя.</para>
    /// </summary>
    public sealed class ComboSeriesTests
    {
        [Test]
        public void ComboBreak_StartsTheCycleFromTheFirstPhase()
        {
            Fixture f = Fixture.WithCycle();

            // Две Атаки: вторая закончилась, значит заряд третьей уже взведён.
            f.RunUntilHits(2);
            Assert.Greater(f.Attacker.EmpowerDamageMult, 0f, "Предусловие: тяжёлый удар взведён");

            f.BreakCombo();

            // Круг начался заново, поэтому тяжёлым будет только третий удар ПОСЛЕ разрыва.
            List<float> after = f.HitsAfter(3);
            Assert.AreEqual(new[] { 100f, 100f, 200f }, after.ToArray(),
                "После разрыва цикл идёт с начала: обычный, обычный, тяжёлый");
        }

        [Test]
        public void ComboBreak_DisarmsTheChargeItArmed()
        {
            Fixture f = Fixture.WithCycle();
            f.RunUntilHits(2);
            Assert.Greater(f.Attacker.EmpowerDamageMult, 0f, "Предусловие: тяжёлый удар взведён");

            f.BreakCombo();

            Assert.AreEqual(0f, f.Attacker.EmpowerDamageMult, 1e-4f,
                "Разрыв серии гасит взведённый удар: его выбили из боя, награда не переносится");
            Assert.AreEqual(0, f.Attacker.ComboAttacks, "И счётчик серии обнулён");
        }

        [Test]
        public void CombatIdle_DoesNotBreakTheCombo()
        {
            // Интервал атаки (2 с) НАМЕРЕННО длиннее порога разрыва (1.5 с): между ударами боец дольше
            // полутора секунд стоит и ничего не делает. Это боевое ожидание, а не выпадение из боя, и
            // серия обязана его пережить — иначе медленный кит не доберётся до своей третьей фазы никогда.
            Fixture f = Fixture.WithCycle(attackSpeed: 0.5f);

            List<float> hits = f.HitsAfter(3);

            Assert.AreEqual(new[] { 100f, 100f, 200f }, hits.ToArray(),
                "Ожидание своего интервала серию не рвёт: третий удар всё равно тяжёлый");
        }

        [Test]
        public void MissedAttack_StillCountsForTheSeries()
        {
            // Слепота отправляет удар мимо, но взмах состоялся. Считается путь Атаки, а не попадание
            // (вердикт Макса 2026-08-01), поэтому тяжёлой остаётся третья Атака — просто её могло и
            // не докатиться до цели.
            Fixture f = Fixture.WithCycle();

            f.RunTicks(1);
            int before = f.Attacker.ComboAttacks;
            f.Blind();                       // каждая Атака мимо
            f.RunUntilAttacks(before + 2);

            Assert.AreEqual(before + 2, f.Attacker.ComboAttacks,
                "Промахнувшиеся Атаки серию двигают — иначе уклонениями можно было бы «съесть» особый удар");
        }

        [Test]
        public void ComboBreak_LeavesForeignChargesAlone()
        {
            // Заряд от активки живёт по своему правилу: сняли носителя — сгорел, отошёл — цел. Карточка
            // Скрытности прямо разрешает не атаковать и отбегать, сохраняя удар из тени (вердикт Макса
            // 2026-08-01), поэтому разрыв серии до чужих зарядов не дотягивается.
            Fixture f = Fixture.WithCycle();
            f.RunUntilHits(1);

            EffectData foreign = ForeignCharge();
            f.Sim.ApplyEffect(f.Attacker, foreign, f.Attacker);
            EffectSystem.CommitPending(f.Attacker);
            Assert.Greater(f.Attacker.EmpowerDamageMult, 0f, "Предусловие: чужой заряд взведён");

            f.BreakCombo();

            Assert.AreEqual(3f, f.Attacker.EmpowerDamageMult, 1e-4f,
                "Чужой заряд пережил разрыв серии — у него своё правило снятия");
            Assert.IsTrue(HasEffect(f.Attacker, foreign), "И сам эффект остался висеть");
        }

        // ===================== Обвязка =====================

        /// <summary>Заряд НЕ от серии: такой ставит активка или триггер. Множитель ×3 — чтобы отличался.</summary>
        private static EffectData ForeignCharge()
        {
            var comp = new EmpowerNextAttackComponent()
                .With("_damageMult", 3f)
                .With("_consumeTag", EffectTag.Empowered);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Buff,
                tags: EffectTag.Empowered, stacking: StackRule.Refresh, components: comp);
        }

        private static bool HasEffect(RuntimeUnit unit, EffectData def)
        {
            for (int i = 0; i < unit.ActiveEffects.Count; i++)
                if (ReferenceEquals(unit.ActiveEffects[i].Def, def)) return true;
            return false;
        }

        /// <summary>
        /// Бой из двух юнитов: носитель цикла бьёт неубиваемую жертву. Держит обе стороны и умеет то, что
        /// нужно всем тестам разом — выбить носителя из боя и вернуть обратно.
        /// </summary>
        private sealed class Fixture
        {
            public CombatSimulation Sim;
            public RuntimeUnit Attacker;
            public RuntimeUnit Victim;

            private readonly List<float> _hits = new List<float>();
            private Vector2 _victimHome;

            public static Fixture WithCycle(float attackSpeed = 1f)
            {
                var f = new Fixture();
                f.Sim = BuildSim();
                f.Attacker = MakeUnit(0, team: 0, pos: Vector2.zero, attackSpeed: attackSpeed);
                f.Victim   = MakeUnit(1, team: 1, pos: new Vector2(1f, 0f), maxHp: 1_000_000f, damage: 0f);
                f._victimHome = f.Victim.Position;

                f.Sim.EnqueueUnitSpawn(f.Attacker);
                f.Sim.EnqueueUnitSpawn(f.Victim);
                f.Sim.Tick(SimConstants.TickDelta);
                f.Attacker.CurrentTarget = f.Victim;

                f.Sim.OnDamageDealt += (src, tgt, res) => { if (src == f.Attacker) f._hits.Add(res.HpDamage); };

                f.Sim.ApplyEffect(f.Attacker, Cycle(), f.Attacker);
                EffectSystem.CommitPending(f.Attacker);
                return f;
            }

            public void RunTicks(int ticks)
            {
                for (int t = 0; t < ticks; t++) Sim.Tick(SimConstants.TickDelta);
            }

            /// <summary>Крутить бой, пока носитель не нанесёт столько ударов.</summary>
            public void RunUntilHits(int hits)
            {
                for (int t = 0; t < hits * 200 && _hits.Count < hits; t++) Sim.Tick(SimConstants.TickDelta);
                Assert.AreEqual(hits, _hits.Count, "Предусловие: носитель нанёс столько ударов");
            }

            /// <summary>Крутить бой, пока счётчик серии не дойдёт до значения (нужен там, где удары мимо).</summary>
            public void RunUntilAttacks(int attacks)
            {
                for (int t = 0; t < attacks * 200 && Attacker.ComboAttacks < attacks; t++)
                    Sim.Tick(SimConstants.TickDelta);
                Assert.AreEqual(attacks, Attacker.ComboAttacks, "Предусловие: носитель завершил столько Атак");
            }

            /// <summary>Урон следующих <paramref name="count"/> ударов носителя.</summary>
            public List<float> HitsAfter(int count)
            {
                int from = _hits.Count;
                RunUntilHits(from + count);
                return _hits.GetRange(from, count);
            }

            /// <summary>
            /// Выбить носителя из боя дольше порога и вернуть обратно: цель уезжает за карту, боец
            /// остаётся без досягаемости (двигаться он не умеет — скорость нулевая), то есть вне
            /// атакующего лупа. Так же выглядит стан и потеря цели, только без лишних эффектов в сцене.
            /// </summary>
            public void BreakCombo()
            {
                Victim.Position = Victim.PreviousPosition = new Vector2(500f, 0f);
                RunTicks(SimTuning.Default.ComboBreakTicks + 2);
                Assert.AreEqual(0, Attacker.ComboAttacks, "Предусловие: серия действительно порвалась");

                Victim.Position = Victim.PreviousPosition = _victimHome;
            }

            /// <summary>Полная слепота: мимо уходит каждый удар носителя.</summary>
            public void Blind()
            {
                var comp = new BlindComponent().With("_periodAtOneStack", 1);
                Sim.ApplyEffect(Attacker, TestEffect.Make(baseDuration: -1f, components: comp), Attacker);
                EffectSystem.CommitPending(Attacker);
            }

            /// <summary>Цикл голема: обычный, обычный, тяжёлый (×2).</summary>
            private static EffectData Cycle()
            {
                var heavy = new EmpowerNextAttackComponent()
                    .With("_damageMult", 2f)
                    .With("_consumeTag", EffectTag.Empowered);
                EffectData heavyCharge = TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral,
                    tags: EffectTag.Empowered, stacking: StackRule.Refresh, components: heavy);

                var cycle = new AttackCycleComponent().With("_phases", new[] { null, null, heavyCharge });
                return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: cycle);
            }
        }

        private static CombatSimulation BuildSim() =>
            new CombatSimulation(
                new Guildmaster.Core.Random.XorShiftRng(17UL), CombatTestValues.ArmorK,
                new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new Guildmaster.Combat.AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp = 500f,
                                            float damage = 100f, float attackSpeed = 1f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, damage),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, attackSpeed),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 3f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 0f),
            });
            return new RuntimeUnit
            {
                Id                   = id,
                Team                 = team,
                Stats                = stats,
                CurrentHP            = maxHp,
                Position             = pos,
                PreviousPosition     = pos,
                AutoAttackDamageType = DamageType.Pure,
            };
        }
    }
}
