using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Четыре величины, которые вердикты Макса 2026-07-30 сделали данными, а не кодом: повтор
    /// self-нагрузки, доля лечения «Каменной десятины», число стаков за удар в тыл и прибавка урона
    /// за стак тега. Каждая из них — ровно тот случай, когда «поле не читается» выглядит как рабочая
    /// игра: эффект вешается, урон идёт, а число молча остаётся дефолтным.
    /// </summary>
    public sealed class DataDrivenPayloadRulesTests
    {
        // ===================== Повтор self-нагрузки (Хранитель углей) =====================

        [Test]
        public void SelfEffects_RepeatWithPayload_OnlyWhenFlagSet()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var caster = TestUnit.Make();

            EffectData ash = TestEffect.Make(baseDuration: -1f, stacking: StackRule.StackAndRefresh, maxStacks: 50);
            AbilityData ability = TestAbility.Make(mode: AbilityTargetMode.Self, selfEffects: new[] { ash })
                .With("_payloadRepeats", 5)
                .With("_repeatSelfEffects", true);

            caster.Abilities.Add(new AbilityRuntime(ability));
            new AbilitySystem().TryCast(caster, 0, new List<RuntimeUnit> { caster }, ctx);
            EffectSystem.CommitPending(caster);

            Assert.AreEqual(5, StacksOn(caster, ash), "При включённом флаге self-эффект кладётся столько же раз, сколько нагрузка");
        }

        [Test]
        public void SelfEffects_AppliedOnce_WhenFlagIsOff()
        {
            // Гвард щита «Стального вихря»: реактив вешается на каст РАЗ и растёт от урона всей нагрузки.
            // Повтор без флага тихо выдал бы Копейщику пять щитов вместо одного.
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);
            var caster = TestUnit.Make();

            EffectData shield = TestEffect.Make(baseDuration: 3f, stacking: StackRule.StackAndRefresh, maxStacks: 50);
            AbilityData ability = TestAbility.Make(mode: AbilityTargetMode.Self, selfEffects: new[] { shield })
                .With("_payloadRepeats", 5);

            caster.Abilities.Add(new AbilityRuntime(ability));
            new AbilitySystem().TryCast(caster, 0, new List<RuntimeUnit> { caster }, ctx);
            EffectSystem.CommitPending(caster);

            Assert.AreEqual(1, StacksOn(caster, shield), "Без флага self-эффект кладётся один раз за каст");
        }

        // ===================== «Каменная десятина»: запас и наполнение — разные числа =====================

        [Test]
        public void StoneTithe_HealsForFullTake_ButRaisesMaxHpOnlyByKeepShare()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            var carrier = TestUnit.Make(team: 0, maxHp: 1000f);
            carrier.Position = Vector2.zero;
            var ally = TestUnit.Make(team: 0, maxHp: 1000f);
            ally.Position = new Vector2(1f, 0f);
            ctx.UnitsInWorld.Add(carrier);
            ctx.UnitsInWorld.Add(ally);

            var tithe = new TitheComponent()
                .With("_startAtHpPct", 0.01f)
                .With("_tithePctCurrentHp", 0.2f)
                .With("_keepShare", 0.3f)
                .With("_healShareOfTaken", 1f)
                .With("_radius", 12f);
            EffectData def = TestEffect.Make(baseDuration: -1f, components: tithe);

            es.Apply(carrier, def, carrier, ctx);
            EffectSystem.CommitPending(carrier);
            es.Tick(new List<RuntimeUnit> { carrier }, ctx, 1f / 30f);
            EffectSystem.CommitPending(carrier);

            // Союзник отдал 20% текущего HP = 200.
            Assert.AreEqual(800f, ally.CurrentHP, 1e-3f, "Десятина берёт 20% текущего HP союзника");
            // Запас вырос на 30% забранного = 60; наполнение — на все 200, но клампится максимумом.
            Assert.AreEqual(1060f, carrier.Stats.Get(StatType.MaxHP), 1e-3f, "К максимуму уходит только KeepShare");
            Assert.AreEqual(210f, carrier.CurrentHP, 1e-3f, "Лечение считается от ВСЕГО забранного: 10 стартовых + 200");
        }

        // ===================== Удар в тыл: число стаков — свойство данных =====================

        [Test]
        public void RearStrike_AppliesConfiguredNumberOfStacks()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            // Тыл считается конвенцией сторон: атакующий из-за спины стоит дальше по X, чем жертва.
            var carrier = TestUnit.Make(team: 0);
            carrier.Position = new Vector2(5f, 0f);
            var victim = TestUnit.Make(team: 1);
            victim.Position = Vector2.zero;

            EffectData rot = TestEffect.Make(baseDuration: 6f, stacking: StackRule.StackAndRefresh, maxStacks: 99);
            var rear = new RearStrikeEffectComponent()
                .With("_bonusEffect", rot)
                .With("_autoAttacksOnly", true)
                .With("_rearConeCos", 0f)
                .With("_bonusStacks", 2);
            EffectData def = TestEffect.Make(baseDuration: -1f, components: rear);

            es.Apply(carrier, def, carrier, ctx);
            EffectSystem.CommitPending(carrier);
            es.Dispatch(carrier, new CombatEventData(
                CombatEvent.DamageDealt, carrier, victim, amount: 10f,
                tags: EffectTag.None, sourceKind: DamageSourceKind.AutoAttack), ctx);
            EffectSystem.CommitPending(victim);

            Assert.AreEqual(2, StacksOn(victim, rot), "Удар в тыл кладёт столько стаков, сколько задано данными");
        }

        // ===================== Бонус по тегам: за стак и с потолком =====================

        [Test]
        public void TaggedBonus_GrowsPerStack_AndStopsAtCap()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            var attacker = TestUnit.Make(team: 0);
            var target   = TestUnit.Make(team: 1);

            EffectData frost = TestEffect.Make(
                baseDuration: -1f, tags: EffectTag.Frozen, stacking: StackRule.StackAndRefresh, maxStacks: 99);

            var rule = new TaggedTargetDamageBonusComponent.Rule
            {
                RequiredTags  = EffectTag.Frozen,
                Bonus         = 0f,
                BonusPerStack = 0.05f,
                MaxBonus      = 1f,
            };
            var bonus = new TaggedTargetDamageBonusComponent()
                .With("_rules", new[] { rule })
                .With("_autoAttackOnly", false);
            EffectData carrierDef = TestEffect.Make(baseDuration: -1f, components: bonus);

            es.Apply(attacker, carrierDef, attacker, ctx);
            EffectSystem.CommitPending(attacker);

            // Четыре стака холода — прибавка ещё далеко от потолка.
            for (int i = 0; i < 4; i++) es.Apply(target, frost, attacker, ctx);
            EffectSystem.CommitPending(target);
            Assert.AreEqual(0.2f, BonusOf(es, attacker, target, ctx), 1e-4f, "+5% за каждый стак «Изморози»");

            // Тридцать стаков дали бы +150%, но правило упирается в свой потолок.
            for (int i = 0; i < 26; i++) es.Apply(target, frost, attacker, ctx);
            EffectSystem.CommitPending(target);
            Assert.AreEqual(1f, BonusOf(es, attacker, target, ctx), 1e-4f, "Потолок правила держит прибавку на +100%");
        }

        [Test]
        public void TaggedBonus_IsZero_WhenTargetHasNoRequiredTag()
        {
            var es  = new EffectSystem();
            var ctx = new MockCombatContext(effects: es);

            var attacker = TestUnit.Make(team: 0);
            var target   = TestUnit.Make(team: 1);

            var rule = new TaggedTargetDamageBonusComponent.Rule
            {
                RequiredTags  = EffectTag.Frozen,
                BonusPerStack = 0.05f,
                MaxBonus      = 1f,
            };
            var bonus = new TaggedTargetDamageBonusComponent()
                .With("_rules", new[] { rule })
                .With("_autoAttackOnly", false);
            EffectData carrierDef = TestEffect.Make(baseDuration: -1f, components: bonus);

            es.Apply(attacker, carrierDef, attacker, ctx);
            EffectSystem.CommitPending(attacker);

            Assert.AreEqual(0f, BonusOf(es, attacker, target, ctx), 1e-4f, "Без требуемого тега правило не применяется вовсе");
        }

        // ===================== Хелперы =====================

        private static int StacksOn(RuntimeUnit unit, EffectData def)
        {
            for (int i = 0; i < unit.ActiveEffects.Count; i++)
                if (ReferenceEquals(unit.ActiveEffects[i].Def, def)) return unit.ActiveEffects[i].VisibleStacks;
            return 0;
        }

        private static float BonusOf(EffectSystem es, RuntimeUnit attacker, RuntimeUnit target, ICombatContext ctx)
            => es.ResolveOutgoingDamageBonus(attacker, target, isAutoAttack: false, ctx);
    }
}
