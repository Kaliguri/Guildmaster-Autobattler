using System.Collections.Generic;
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
    /// Порядок опроса pre-damage реакций (решение Макса 2026-08-21/3): приоритет числом, при равных —
    /// своя реакция раньше наложенной союзником, и проход обрывается на первой, отменившей удар.
    /// <para>До этого решения порядок задавался тем, кто раньше повесил эффект, а остановку каждый
    /// компонент делал сам первой строкой — «Оплот» её не делал и жёг заряд на отменённый удар.</para>
    /// </summary>
    public sealed class ReactionOrderTests
    {
        // ===================== Порядок =====================

        [Test]
        public void Priority_HigherRunsFirst_RegardlessOfApplyOrder()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var target = TestUnit.Make();
            var log = new List<string>();

            // Вешаем в порядке, ОБРАТНОМ приоритету: если бы решал порядок наложения, лог был бы другим.
            ctx.ApplyEffect(target, ProbeEffect("modify", ReactionPriority.Modify, log), target);
            ctx.ApplyEffect(target, ProbeEffect("absorb", ReactionPriority.Absorb, log), target);
            ctx.ApplyEffect(target, ProbeEffect("evade", ReactionPriority.Evade, log), target);

            es.RunPreDamage(target, in Hit, ctx);

            CollectionAssert.AreEqual(new[] { "evade", "absorb", "modify" }, log,
                "Опрос идёт по приоритету вниз, а не по порядку наложения");
        }

        [Test]
        public void EqualPriority_SelfCastRunsBeforeAllyCast()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var target = TestUnit.Make();
            var ally = TestUnit.Make();
            var log = new List<string>();

            // Чужую вешаем первой: при равных числах её всё равно должны спросить второй.
            ctx.ApplyEffect(target, ProbeEffect("ally", ReactionPriority.Evade, log), ally);
            ctx.ApplyEffect(target, ProbeEffect("self", ReactionPriority.Evade, log), target);

            es.RunPreDamage(target, in Hit, ctx);

            CollectionAssert.AreEqual(new[] { "self", "ally" }, log,
                "Свой заряд восстанавливается сам и тратится первым; чужая поддержка — редкий ресурс");
        }

        // ===================== Ранняя остановка =====================

        [Test]
        public void Negated_StopsPollingTheRestOfTheChain()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var target = TestUnit.Make();
            var log = new List<string>();

            ctx.ApplyEffect(target, ProbeEffect("evade", ReactionPriority.Evade, log, negates: true), target);
            ctx.ApplyEffect(target, ProbeEffect("absorb", ReactionPriority.Absorb, log), target);
            ctx.ApplyEffect(target, ProbeEffect("modify", ReactionPriority.Modify, log), target);

            Assert.IsTrue(es.RunPreDamage(target, in Hit, ctx), "Верхняя реакция отменила удар");
            CollectionAssert.AreEqual(new[] { "evade" }, log,
                "За отменённый удар не платят: нижних реакций не спрашивают вовсе");
        }

        /// <summary>
        /// Живой регресс: у бойца одновременно «Отход» (уносит с места) и «Оплот» (встречает удар щитом).
        /// Отход отменяет автоатаку — значит Оплот не должен ни потратить заряд, ни повесить щит.
        /// Раньше вешал: его <c>OnPreDamage</c> не смотрел на <c>Negated</c>, а система опрос не обрывала.
        /// </summary>
        [Test]
        public void EvadeNegates_BulwarkKeepsItsChargeAndRaisesNoShield()
        {
            var es = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            // AI-профиля нет — «Оплот» падает на дефолтный триггер AnyHit, то есть встаёт на любой удар.
            var target = TestUnit.Make();

            EffectData shield = TestEffect.Make(baseDuration: 3f, polarity: EffectPolarity.Buff,
                components: new StatModifierComponent().With("_modifiers", new[]
                {
                    new StatModifier(StatType.PhysArmor, ModifierOp.Flat, 10f),
                }));

            ctx.ApplyEffect(target, DodgePassive(), target);
            ctx.ApplyEffect(target, BulwarkPassive(shield), target);
            int effectsBefore = target.ActiveEffects.Count;

            Assert.IsTrue(es.RunPreDamage(target, in Hit, ctx), "Отход отменил автоатаку");
            Assert.AreEqual(effectsBefore, target.ActiveEffects.Count,
                "Щит «Оплота» не встал: удара, против которого его поднимать, уже нет");

            // Заряд цел — иначе следующий удар, от которого отход уже не спасает, прошёл бы без щита.
            EffectSystem.CommitPending(target);
            ctx.AdvanceTick(target);
            Assert.IsTrue(es.RunPreDamage(target, in Hit, ctx), "Второй заряд отхода на месте");
        }

        // ===================== Хелперы =====================

        private static readonly DamageRequest Hit = new DamageRequest(
            null, null, 30f, DamageType.Pure, CombatTestValues.ArmorK, sourceKind: DamageSourceKind.AutoAttack);

        private static EffectData ProbeEffect(string name, int priority, List<string> log, bool negates = false)
            => TestEffect.Make(baseDuration: -1f, components: new ProbeReaction(name, priority, log, negates));

        private static EffectData DodgePassive()
        {
            var dodge = new DodgeComponent()
                .With("_maxCharges", 2)
                .With("_rechargeSeconds", 5f)
                .With("_rollDistance", 0f)
                .With("_rollSpeedPerSecond", 12f);
            return TestEffect.Make(baseDuration: -1f, components: dodge);
        }

        private static EffectData BulwarkPassive(EffectData shield)
        {
            var block = new BlockComponent()
                .With("_maxCharges", 1)
                .With("_internalCooldownSeconds", 4f)
                .With("_shieldEffect", shield);
            return TestEffect.Make(baseDuration: -1f, components: block);
        }

        /// <summary>Пустышка-реакция: пишет своё имя в лог опроса и по заказу отменяет удар.</summary>
        private sealed class ProbeReaction : IPreDamageComponent
        {
            private readonly string _name;
            private readonly List<string> _log;
            private readonly bool _negates;

            public ProbeReaction(string name, int priority, List<string> log, bool negates)
            {
                _name = name;
                Priority = priority;
                _log = log;
                _negates = negates;
            }

            public int Priority { get; }

            public void OnApply(in EffectContext ctx) { }
            public void OnExpire(in EffectContext ctx) { }

            public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
            {
                _log.Add(_name);
                if (_negates) result.Negated = true;
            }
        }
    }
}
