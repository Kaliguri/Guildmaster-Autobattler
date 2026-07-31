using System.Collections.Generic;
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
    /// Парирование как общий примитив (дизайн Макса 2026-07-30, триггер уточнён 2026-07-31): отбить
    /// прилетевший ближний удар, открыть окно, зафиксировать целившихся спереди микро-станом и ответить
    /// уникальным ударом вне очереди.
    /// </summary>
    /// <remarks>
    /// Тесты держат ЗАМЫСЕЛ, а не устройство: что парирование отбивает, кого ловит стан, чем отвечает и
    /// чего не делает. Устройство (два компонента, окно отдельным эффектом) может поменяться — падать
    /// эти тесты должны только от смены правил боя.
    /// </remarks>
    public sealed class ParryTests
    {
        [Test]
        public void Parry_NegatesTheMeleeHitThatWokeIt()
        {
            var (es, ctx, hero) = Scene();
            RuntimeUnit attacker = Attacker(ctx, pos: new Vector2(1f, 0f), range: 2f, aimsAt: hero);

            Assert.IsTrue(es.RunPreDamage(hero, MeleeHit(attacker, hero), ctx),
                "Удар, разбудивший парирование, им же и отбит — как щит Блока встаёт под свой удар");
        }

        [Test]
        public void Parry_IgnoresRangedAttackers()
        {
            // «Атакуют в ближнем бою» — условие самого дизайна: парирующий не отбивает стрелы.
            var (es, ctx, hero) = Scene();
            RuntimeUnit archer = Attacker(ctx, pos: new Vector2(6f, 0f), range: 8f, aimsAt: hero,
                attackType: AttackType.Ranged);

            Assert.IsFalse(es.RunPreDamage(hero, MeleeHit(archer, hero), ctx),
                "Выстрел проходит: парирование его не встречает");
        }

        [Test]
        public void Parry_WindowEatsFurtherHits_WithoutSpendingASecondCharge()
        {
            // Вердикт Макса 2026-07-31: окно — это щит на 0.3 с, а не одиночный отбив. Второй Удар гасит
            // окно, и именно поэтому он не должен стоить второго заряда: парирование уже состоялось.
            var (es, ctx, hero) = Scene(maxCharges: 2);
            RuntimeUnit attacker = Attacker(ctx, pos: new Vector2(1f, 0f), range: 2f, aimsAt: hero);

            Assert.IsTrue(es.RunPreDamage(hero, MeleeHit(attacker, hero), ctx), "Первый Удар парирован");
            es.Tick(ctx.UnitsInWorld, ctx, SimConstants.TickDelta);   // окно вступило в силу (закон видимости)

            Assert.IsTrue(es.RunPreDamage(hero, MeleeHit(attacker, hero), ctx), "Второй Удар съело окно");
            Assert.IsTrue(es.RunPreDamage(hero, MeleeHit(attacker, hero), ctx), "И третий тоже");

            // Окно кончилось — теперь работает второй заряд, а не бесконечная защита.
            for (int t = 0; t < SimConstants.TickRate; t++) es.Tick(ctx.UnitsInWorld, ctx, SimConstants.TickDelta);
            Assert.IsTrue(es.RunPreDamage(hero, MeleeHit(attacker, hero), ctx), "Второе парирование — вторым зарядом");
        }

        [Test]
        public void Parry_StunsThoseWhoAimedAtIt_AndNobodyElse()
        {
            var (es, ctx, hero) = Scene();
            RuntimeUnit attacker = Attacker(ctx, pos: new Vector2(1f, 0f),  range: 2f, aimsAt: hero);
            RuntimeUnit bystander = Attacker(ctx, pos: new Vector2(2f, 0f), range: 2f, aimsAt: null);

            es.RunPreDamage(hero, MeleeHit(attacker, hero), ctx);

            Assert.IsTrue(IsStunned(attacker),    "Замахнувшийся на парирующего сбит микро-станом");
            Assert.IsFalse(IsStunned(bystander),  "Прохожий не при делах: парирование сбивает целившихся, а не всех вокруг");
        }

        [Test]
        public void Parry_DoesNotStunFromBehind()
        {
            // «Перед ним, на фланги обычно не работает» — иначе парирование становится круговым контролем.
            var (es, ctx, hero) = Scene();
            RuntimeUnit front = Attacker(ctx, pos: new Vector2(1f, 0f),  range: 2f, aimsAt: hero);
            RuntimeUnit back  = Attacker(ctx, pos: new Vector2(-1f, 0f), range: 2f, aimsAt: hero);
            hero.CurrentTarget = front;   // смотрит на переднего

            es.RunPreDamage(hero, MeleeHit(front, hero), ctx);

            Assert.IsTrue(IsStunned(front), "Тот, на кого смотрит, сбит");
            Assert.IsFalse(IsStunned(back), "Зашедший со спины стана не получает");
        }

        [Test]
        public void Parry_AnswersOutOfTurn_WithTheChargedStrike()
        {
            var (es, ctx, hero) = Scene();
            RuntimeUnit attacker = Attacker(ctx, pos: new Vector2(1f, 0f), range: 2f, aimsAt: hero);
            hero.AttackCooldownTicks = 25;

            es.RunPreDamage(hero, MeleeHit(attacker, hero), ctx);
            es.Tick(ctx.UnitsInWorld, ctx, SimConstants.TickDelta);   // заряд ответа вступил в силу

            Assert.AreEqual(0, hero.AttackCooldownTicks, "Ответ идёт вне очереди — ожидание интервала снято");
            Assert.AreEqual(2f, hero.EmpowerDamageMult, 1e-4f, "И этот удар уникальный: множитель взведён");
        }

        [Test]
        public void Parry_GetsTheHaste_OnlyWhenOutranged()
        {
            var (es, ctx, hero) = Scene();
            RuntimeUnit spearman = Attacker(ctx, pos: new Vector2(3f, 0f), range: 5f, aimsAt: hero);

            es.RunPreDamage(hero, MeleeHit(spearman, hero), ctx);
            es.Tick(ctx.UnitsInWorld, ctx, SimConstants.TickDelta);
            hero.Stats.Commit();   // статы эффектов вступают в силу в конце тика, как в бою
            float hastened = hero.Stats.Get(StatType.MoveSpeed);

            var (es2, ctx2, hero2) = Scene();
            RuntimeUnit equal = Attacker(ctx2, pos: new Vector2(1f, 0f), range: 2f, aimsAt: hero2);
            es2.RunPreDamage(hero2, MeleeHit(equal, hero2), ctx2);
            es2.Tick(ctx2.UnitsInWorld, ctx2, SimConstants.TickDelta);
            hero2.Stats.Commit();

            Assert.Greater(hastened, hero2.Stats.Get(StatType.MoveSpeed),
                "Парировал длиннорукого — получил разгон, чтобы дойти до него");
            Assert.AreEqual(3f, hero2.Stats.Get(StatType.MoveSpeed), 1e-4f,
                "Против равного по дальности разгона нет: это ответ на «меня достают, а я нет»");
        }

        [Test]
        public void Parry_DoesNotWorkWhileIncapacitated()
        {
            // Парирование — ДЕЙСТВИЕ, как «Изворотливость» (решение Макса 2026-07-29 про дееспособность).
            var (es, ctx, hero) = Scene();
            RuntimeUnit attacker = Attacker(ctx, pos: new Vector2(1f, 0f), range: 2f, aimsAt: hero);

            hero.CanAct = hero.CanActAtTickStart = false;
            Assert.IsFalse(es.RunPreDamage(hero, MeleeHit(attacker, hero), ctx), "Оглушённый не парирует");

            hero.CanAct = hero.CanActAtTickStart = true;
            Assert.IsTrue(es.RunPreDamage(hero, MeleeHit(attacker, hero), ctx), "Заряд цел: контроль отнял возможность, а не запас");
        }

        // ===================== Сцена и фабрики =====================

        private static (EffectSystem, MockCombatContext, RuntimeUnit) Scene(int maxCharges = 1)
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            RuntimeUnit hero = MakeUnit(0, team: 0, Vector2.zero, range: 2f);
            ctx.UnitsInWorld.Add(hero);
            ctx.ApplyEffect(hero, ParryPassive(maxCharges), hero);

            return (es, ctx, hero);
        }

        private static RuntimeUnit Attacker(MockCombatContext ctx, Vector2 pos, float range, RuntimeUnit aimsAt,
            AttackType attackType = AttackType.Melee)
        {
            RuntimeUnit unit = MakeUnit(ctx.UnitsInWorld.Count + 1, team: 1, pos, range, attackType);
            unit.CurrentTarget = aimsAt;
            ctx.UnitsInWorld.Add(unit);
            return unit;
        }

        private static DamageRequest MeleeHit(RuntimeUnit from, RuntimeUnit to) =>
            new DamageRequest(from, to, 30f, DamageType.Slash, CombatTestValues.ArmorK,
                sourceKind: DamageSourceKind.AutoAttack);

        private static bool IsStunned(RuntimeUnit unit)
        {
            for (int i = 0; i < unit.ActiveEffects.Count; i++)
                if ((unit.ActiveEffects[i].Def.Tags & EffectTag.Control) != 0) return true;

            return false;
        }

        /// <summary>Числа как в общем ассете: 1 заряд / 6 с, окно 0.3 с, стан 0.4 с, ответ ×2, разгон +30% на 1 с.</summary>
        private static EffectData ParryPassive(int maxCharges)
        {
            var parry = new ParryComponent()
                .With("_maxCharges", maxCharges)
                .With("_cooldownSeconds", 6f)
                .With("_parryWindow", ParryWindow())
                .With("_microStun", MicroStun())
                .With("_stunRangeFactor", 2f)
                .With("_frontalDegrees", 180f)
                .With("_riposteCharge", RiposteCharge())
                .With("_outrangedHaste", OutrangedHaste());

            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: parry);
        }

        private static EffectData ParryWindow() =>
            TestEffect.Make(baseDuration: 0.3f, polarity: EffectPolarity.Buff, tags: EffectTag.Buff,
                stacking: StackRule.Refresh, components: new ParryWindowComponent());

        private static EffectData MicroStun() =>
            TestEffect.Make(baseDuration: 0.4f, polarity: EffectPolarity.Debuff,
                tags: EffectTag.Debuff | EffectTag.Control, stacking: StackRule.Refresh,
                components: new ControlComponent(preventAct: true, preventMove: true, preventCast: true));

        private static EffectData RiposteCharge()
        {
            var empower = new EmpowerNextAttackComponent()
                .With("_damageMult", 2f)
                .With("_recastImmediately", true)
                .With("_consumeTag", EffectTag.Empowered);

            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral,
                tags: EffectTag.Empowered, stacking: StackRule.Refresh, components: empower);
        }

        private static EffectData OutrangedHaste()
        {
            var mod = new StatModifierComponent().With("_modifiers", new[]
            {
                new StatModifier(StatType.MoveSpeed, ModifierOp.PercentMult, 0.3f),
            });

            return TestEffect.Make(baseDuration: 1f, polarity: EffectPolarity.Buff, tags: EffectTag.Buff,
                stacking: StackRule.Refresh, components: mod);
        }

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float range,
            AttackType attackType = AttackType.Melee)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 200f),
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
                CurrentHP            = 200f,
                Position             = pos,
                PreviousPosition     = pos,
                Unit                 = TestRelic.Make(attackType: attackType),
                AttackType           = attackType,
                AutoAttackDamageType = DamageType.Slash,
            };
        }
    }
}
