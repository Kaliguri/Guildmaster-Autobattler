using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Срез кита мага ветра ([[the-zephyr]], решение `2026-08-21/4`): «Встречный ветер» —
    /// щит-половинщик против дальнего боя, «Порыв» — реакция, выдёргивающая союзника ДО удара,
    /// «Уклонение» — зеркало Слепоты со стороны цели.
    /// </summary>
    public sealed class ZephyrSliceTests
    {
        // ===================== «Встречный ветер» =====================

        [Test]
        public void Ward_HalvesRangedHit_AndBillsTheCutHalfToItsPool()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var carrier = TestUnit.Make();
            var archer = RangedAttacker();

            ctx.ApplyEffect(carrier, Ward(pool: 200f), TestUnit.Make());

            Assert.IsFalse(es.RunPreDamage(carrier, Ranged(archer, 60f), ctx), "Поток режет, а не отменяет");
            Assert.AreEqual(0.5f, es.PreDamageMultiplier, 1e-4f, "Дальний удар прошёл вполовину");
            Assert.AreEqual(30f, carrier.AbsorbedByWard, 1e-4f, "В запас списана ровно срезанная половина");
        }

        [Test]
        public void Ward_IgnoresMeleeEntirely()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var carrier = TestUnit.Make();
            var brawler = MeleeAttacker();

            ctx.ApplyEffect(carrier, Ward(pool: 200f), TestUnit.Make());

            Assert.IsFalse(es.RunPreDamage(carrier, Ranged(brawler, 60f), ctx));
            Assert.AreEqual(1f, es.PreDamageMultiplier, 1e-4f, "Ближний бой поток не трогает вовсе");
            Assert.AreEqual(0f, carrier.AbsorbedByWard, 1e-4f, "И запас на него не тратится");
        }

        /// <summary>
        /// Слово «сломан» из заметки Макса: запас исчерпан — эффект уходит сразу, не досиживая срок.
        /// Последний удар поток гасит частично, ровно на остаток.
        /// </summary>
        [Test]
        public void Ward_WhenPoolRunsOut_LeavesEarlyAndCutsOnlyWhatIsLeft()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var carrier = TestUnit.Make();
            var archer = RangedAttacker();

            ctx.ApplyEffect(carrier, Ward(pool: 20f), TestUnit.Make());
            Assert.AreEqual(1, carrier.ActiveEffects.Count, "Поток лёг");

            // Половина от 100 — это 50, но в запасе только 20: срезано будет ровно 20.
            Assert.IsFalse(es.RunPreDamage(carrier, Ranged(archer, 100f), ctx));
            Assert.AreEqual(0.8f, es.PreDamageMultiplier, 1e-4f, "Срезан остаток запаса, а не половина удара");
            Assert.AreEqual(0, carrier.ActiveEffects.Count, "Запас кончился — поток сломан и снят");
        }

        // ===================== «Уклонение» =====================

        [Test]
        public void Evasion_OneStack_SendsEveryFourthAttackWide()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var carrier = TestUnit.Make();
            var archer = RangedAttacker();

            ctx.ApplyEffect(carrier, Evasion(), carrier);

            for (int hit = 1; hit <= 8; hit++)
            {
                carrier.HitsTaken++;   // счётчик двигает симуляция до опроса реакций
                bool evaded = es.RunPreDamage(carrier, Ranged(archer, 30f), ctx);
                Assert.AreEqual(hit % 4 == 0, evaded, $"Удар {hit}: мимо уходит каждый четвёртый");
            }
        }

        [Test]
        public void Evasion_FourStacks_SendsEveryAttackWide()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var carrier = TestUnit.Make();
            var archer = RangedAttacker();

            EffectData evasion = Evasion(maxStacks: 4);
            for (int i = 0; i < 4; i++) ctx.ApplyEffect(carrier, evasion, carrier);
            EffectSystem.CommitPending(carrier);

            for (int hit = 1; hit <= 4; hit++)
            {
                carrier.HitsTaken++;
                Assert.IsTrue(es.RunPreDamage(carrier, Ranged(archer, 30f), ctx),
                    $"Удар {hit}: на четырёх стаках мимо уходит каждый");
            }
        }

        // ===================== «Порыв» =====================

        [Test]
        public void Gust_PullsAllyOutOfTheHit_AndLeavesEvasionBehind()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var mage = TestUnit.Make();
            var ally = TestUnit.Make();
            mage.Position = new Vector2(0f, 0f);
            ally.Position = new Vector2(2f, 0f);

            var brawler = MeleeAttacker();
            brawler.Position = new Vector2(4f, 0f);

            ctx.ApplyEffect(ally, Gust(after: Evasion()), mage);

            Assert.IsTrue(es.RunPreDamage(ally, Ranged(brawler, 80f), ctx), "Удар отменён — союзника выдернули");
            Assert.AreEqual(1, ctx.Displaces.Count, "Рывок состоялся ровно один");
            Assert.AreEqual(new Vector2(-1f, 0f), ctx.Displaces[0].Direction, "От ближнего бьющего уносит прочь от него");
            Assert.IsTrue(ally.ActiveEffects.Exists(e => e.Def.Id == "test"), "Следом легло Уклонение");
        }

        [Test]
        public void Gust_FromRangedAttacker_PushesSideways()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var mage = TestUnit.Make();
            var ally = TestUnit.Make();
            mage.Position = Vector2.zero;
            ally.Position = new Vector2(2f, 0f);

            var archer = RangedAttacker();
            archer.Position = new Vector2(6f, 0f);

            ctx.ApplyEffect(ally, Gust(after: null), mage);

            Assert.IsTrue(es.RunPreDamage(ally, Ranged(archer, 80f), ctx));
            Vector2 dir = ctx.Displaces[0].Direction;
            Assert.AreEqual(0f, Vector2.Dot(dir, new Vector2(-1f, 0f)), 1e-4f,
                "От снаряда уносит ПОД ПРЯМЫМ УГЛОМ к линии полёта, а не вдоль неё");
        }

        [Test]
        public void Gust_OutOfReach_DoesNothing()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var mage = TestUnit.Make();
            var ally = TestUnit.Make();
            mage.Position = Vector2.zero;
            ally.Position = new Vector2(9f, 0f);   // радиус «Порыва» — 4

            ctx.ApplyEffect(ally, Gust(after: null), mage);

            Assert.IsFalse(es.RunPreDamage(ally, Ranged(MeleeAttacker(), 80f), ctx),
                "До союзника не дотянуться — удар проходит");
            Assert.AreEqual(0, ctx.Displaces.Count, "И рывка не было");
        }

        /// <summary>
        /// Дееспособность спрашивается у АВТОРА, а не у носителя: руками машет маг. Оглушённый маг не
        /// дёргает; оглушённого союзника выдернуть, наоборот, можно — его согласия не спрашивают.
        /// </summary>
        [Test]
        public void Gust_StunnedMageDoesNotPull_ButStunnedAllyCanBePulled()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var mage = TestUnit.Make();
            var ally = TestUnit.Make();
            mage.Position = Vector2.zero;
            ally.Position = new Vector2(2f, 0f);
            var brawler = MeleeAttacker();
            brawler.Position = new Vector2(4f, 0f);

            ctx.ApplyEffect(ally, Gust(after: null), mage);

            mage.CanActAtTickStart = false;
            Assert.IsFalse(es.RunPreDamage(ally, Ranged(brawler, 80f), ctx), "Оглушённый маг не дёргает");

            mage.CanActAtTickStart = true;
            ally.CanActAtTickStart = false;
            Assert.IsTrue(es.RunPreDamage(ally, Ranged(brawler, 80f), ctx), "Оглушённого союзника выдёргивают");
        }

        // ===================== Аура-раздатчик =====================

        [Test]
        public void Aura_GivesTheEffectToAlliesInRange_AndSkipsThoseWhoHaveIt()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var mage = TestUnit.Make();
            var near = TestUnit.Make();
            var far = TestUnit.Make();
            mage.Position = Vector2.zero;
            near.Position = new Vector2(2f, 0f);
            far.Position = new Vector2(20f, 0f);
            ctx.UnitsInWorld.AddRange(new[] { mage, near, far });

            EffectData gust = Gust(after: null);
            ctx.ApplyEffect(mage, Aura(gust), mage);

            TickAura(es, ctx, mage);
            Assert.AreEqual(1, near.ActiveEffects.Count, "Ближнему союзнику «Порыв» выдан");
            Assert.AreEqual(0, far.ActiveEffects.Count, "Дальнему — нет, он вне радиуса");
            Assert.AreEqual(1, mage.ActiveEffects.Count, "На маге только сама аура: себе он не раздаёт");
        }

        /// <summary>
        /// Главное свойство ауры: она не трогает уже висящую копию. Наложение поверх взвело бы заряды
        /// заново, и «Порыв» с перезарядкой в восемь секунд срабатывал бы на каждый удар.
        /// </summary>
        [Test]
        public void Aura_DoesNotRearmTheChargeOfAnEffectAlreadyInPlace()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var mage = TestUnit.Make();
            var ally = TestUnit.Make();
            mage.Position = Vector2.zero;
            ally.Position = new Vector2(2f, 0f);
            ctx.UnitsInWorld.AddRange(new[] { mage, ally });

            var brawler = MeleeAttacker();
            brawler.Position = new Vector2(4f, 0f);

            ctx.ApplyEffect(mage, Aura(Gust(after: null)), mage);
            TickAura(es, ctx, mage);

            Assert.IsTrue(es.RunPreDamage(ally, Ranged(brawler, 80f), ctx), "Первый удар перехвачен");
            Assert.IsFalse(es.RunPreDamage(ally, Ranged(brawler, 80f), ctx), "Второй проходит — заряд потрачен");

            // Аура тикает снова: если бы она перевешивала эффект, заряд взвёлся бы заново.
            TickAura(es, ctx, mage);
            Assert.IsFalse(es.RunPreDamage(ally, Ranged(brawler, 80f), ctx),
                "Аура не перевзвела заряд: перезарядка идёт своим ходом");
        }

        // ===================== Хелперы =====================

        private static DamageRequest Ranged(RuntimeUnit source, float raw) => new DamageRequest(
            source, null, raw, DamageType.Pierce, CombatTestValues.ArmorK, sourceKind: DamageSourceKind.AutoAttack);

        private static RuntimeUnit RangedAttacker() => Attacker(AttackType.Ranged);
        private static RuntimeUnit MeleeAttacker() => Attacker(AttackType.Melee);

        private static RuntimeUnit Attacker(AttackType type)
        {
            var unit = TestUnit.Make(team: 1);
            // UnitData абстрактен — берём конкретного наследника; для дальности вида не важно.
            unit.Unit = ScriptableObject.CreateInstance<EnemyData>().With("_attackType", type);
            return unit;
        }

        private static EffectData Ward(float pool)
        {
            var ward = new RangedWardComponent()
                .With("_amount", new ScalableValue(pool))
                .With("_cutShare", 0.5f);
            return TestEffect.Make(baseDuration: 4f, polarity: EffectPolarity.Buff, components: ward);
        }

        private static EffectData Evasion(int maxStacks = 1)
        {
            var evasion = new EvasionComponent().With("_periodAtOneStack", 4);
            return TestEffect.Make(baseDuration: 4f, polarity: EffectPolarity.Buff,
                stacking: maxStacks > 1 ? StackRule.StackAndRefresh : StackRule.Refresh,
                maxStacks: maxStacks, components: evasion);
        }

        /// <summary>
        /// Прогнать ауру ровно на один её период. Периодика считает ТИКИ симуляции, а не переданный dt:
        /// интервал в секунду — это тридцать вызовов <c>Tick</c>, сколько бы ни стояло в аргументе.
        /// </summary>
        private static void TickAura(EffectSystem es, MockCombatContext ctx, RuntimeUnit carrier)
        {
            var units = new[] { carrier };
            for (int i = 0; i < Guildmaster.Core.Simulation.SimConstants.TickRate; i++)
                es.Tick(units, ctx, 1f / Guildmaster.Core.Simulation.SimConstants.TickRate);
        }

        private static EffectData Aura(EffectData carried)
        {
            var aura = new AllyAuraComponent()
                .With("_effect", carried)
                .With("_radius", 4f)
                .With("_includeSelf", false)
                .With("_interval", 1f);
            return TestEffect.Make(baseDuration: -1f, components: aura);
        }

        private static EffectData Gust(EffectData after)
        {
            var gust = new AllyGustComponent()
                .With("_radius", 4f)
                .With("_distance", 1.6f)
                .With("_speedPerSecond", 14f)
                .With("_cooldownSeconds", 8f)
                .With("_afterEffect", after);
            return TestEffect.Make(baseDuration: -1f, components: gust);
        }
    }
}
