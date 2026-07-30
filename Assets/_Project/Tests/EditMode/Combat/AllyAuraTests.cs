using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Режим цели «союзники в радиусе» (<see cref="AbilityTargetMode.AlliesInRadius"/>) — опора поддержки:
    /// «Командный клич» гоблина-командира, будущие бафы Шамана и Барда. Проверяет: баф ложится на всех
    /// союзников в радиусе И на самого кастующего; дальние союзники и враги в радиусе не задеты; аура
    /// не наносит урона даже с ненулевым множителем; каст валиден в одиночку (кастующий сам себе союзник).
    /// </summary>
    public sealed class AllyAuraTests
    {
        [Test]
        public void AllyAura_BuffsAlliesInRadius_AndCaster_IgnoresEnemiesAndFarAllies()
        {
            var ctx = new MockCombatContext(effects: new EffectSystem());

            var caster   = MakeUnit(0, team: 0, pos: Vector2.zero);
            var allyNear = MakeUnit(1, team: 0, pos: new Vector2(3f, 0f));
            var allyFar  = MakeUnit(2, team: 0, pos: new Vector2(30f, 0f));
            var enemy    = MakeUnit(3, team: 1, pos: new Vector2(2f, 0f));

            var units = new List<RuntimeUnit> { caster, allyNear, allyFar, enemy };
            ctx.UnitsInWorld.AddRange(units);

            caster.Abilities.Add(new AbilityRuntime(MakeWarCry(radius: 5f)));
            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);
            // Закон видимости: баф проявляется в конце тика — доигрываем его за всю четвёрку.
            foreach (RuntimeUnit u in units) EffectSystem.CommitPending(u);

            Assert.IsTrue(cast, "Клич кастуется");
            Assert.AreNotEqual(EffectTag.None, allyNear.EffectTagMask & EffectTag.Buff, "Союзник в радиусе получил баф");
            Assert.AreNotEqual(EffectTag.None, caster.EffectTagMask   & EffectTag.Buff, "Кастующий бафает и себя");
            Assert.AreEqual(EffectTag.None, allyFar.EffectTagMask & EffectTag.Buff, "Союзник вне радиуса не задет");
            Assert.AreEqual(EffectTag.None, enemy.EffectTagMask   & EffectTag.Buff, "Враг в радиусе не задет — аура только по своим");

            Assert.AreEqual(60f, allyNear.Stats.Get(StatType.AutoAttackDamage), 1e-4f,
                "Баф реально поднял урон союзника (+20% к 50)");
        }

        [Test]
        public void AllyAura_DealsNoDamage_EvenWithDamageMultiplier()
        {
            var ctx = new MockCombatContext(effects: new EffectSystem());

            var caster = MakeUnit(0, team: 0, pos: Vector2.zero);
            var enemy  = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            var units  = new List<RuntimeUnit> { caster, enemy };
            ctx.UnitsInWorld.AddRange(units);

            // Множитель урона задан, но режим — аура: удара по врагам нет, это поддержка, а не AOE.
            caster.Abilities.Add(new AbilityRuntime(MakeWarCry(radius: 5f, damageMultiplier: 3f)));
            new AbilitySystem().TryCast(caster, 0, units, ctx);

            Assert.AreEqual(0, ctx.DamageCalls.Count, "Аура не наносит урона");
        }

        [Test]
        public void AllyAura_CastsAlone_CasterIsHisOwnAlly()
        {
            var ctx = new MockCombatContext(effects: new EffectSystem());

            var caster = MakeUnit(0, team: 0, pos: Vector2.zero);
            var enemy  = MakeUnit(1, team: 1, pos: new Vector2(2f, 0f));
            var units  = new List<RuntimeUnit> { caster, enemy };
            ctx.UnitsInWorld.AddRange(units);

            caster.Abilities.Add(new AbilityRuntime(MakeWarCry(radius: 5f)));
            bool cast = new AbilitySystem().TryCast(caster, 0, units, ctx);
            EffectSystem.CommitPending(caster);   // закон видимости: баф проявляется в конце тика

            Assert.IsTrue(cast, "Без союзников рядом клич всё равно кастуется — кастующий сам себе союзник");
            Assert.AreNotEqual(EffectTag.None, caster.EffectTagMask & EffectTag.Buff, "И бафает себя");
        }

        // ===================== Фабрики =====================

        /// <summary>«Командный клич»: баф +20% урона автоатаки всем союзникам в радиусе на 6с.</summary>
        private static AbilityData MakeWarCry(float radius, float damageMultiplier = 0f)
        {
            var buff = new StatModifierComponent().With("_modifiers",
                new[] { new StatModifier(StatType.AutoAttackDamage, ModifierOp.PercentMult, 0.2f) });

            EffectData rally = TestEffect.Make(
                baseDuration: 6f,
                polarity: EffectPolarity.Buff,
                tags: EffectTag.Buff,
                components: buff);

            return TestAbility.Make(
                effects: new[] { rally },
                mode: AbilityTargetMode.AlliesInRadius,
                areaRadius: radius,
                damageMultiplier: damageMultiplier);
        }

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp = 500f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 50f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 5f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 3f),
            });
            return new RuntimeUnit
            {
                Id        = id,
                Team      = team,
                Stats     = stats,
                CurrentHP = maxHp,
                Position  = pos,
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
        }
    }
}
