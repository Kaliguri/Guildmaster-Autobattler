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
    /// «Целебный свет» Светлого пастыря (<see cref="AllyMendComponent"/>): автоатака светом (True)
    /// по врагу лечит самого раненого союзника (HP%) в радиусе вокруг носителя на долю нанесённого.
    /// </summary>
    public sealed class AllyMendComponentTests
    {
        [Test]
        public void HealsMostWoundedAlly_OnAutoAttackDamage()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();

            RuntimeUnit shepherd = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 100f, hp: 100f);
            RuntimeUnit wounded  = MakeUnit(1, team: 0, pos: new Vector2(2f, 0f), maxHp: 100f, hp: 30f); // 30%
            RuntimeUnit healthy  = MakeUnit(2, team: 0, pos: new Vector2(3f, 0f), maxHp: 100f, hp: 90f); // 90%
            RuntimeUnit victim   = MakeUnit(3, team: 1, pos: new Vector2(5f, 0f), maxHp: 100f, hp: 100f);
            ctx.UnitsInWorld.AddRange(new[] { shepherd, wounded, healthy });

            var comp = new AllyMendComponent().With("_fraction", 1f).With("_radius", 5f).With("_autoAttackOnly", true);
            sys.Apply(shepherd, TestEffect.Make(baseDuration: -1f, components: comp), shepherd, ctx);

            var ev = new CombatEventData(CombatEvent.DamageDealt, shepherd, victim, 40f, EffectTag.None,
                sourceKind: DamageSourceKind.AutoAttack);
            sys.Dispatch(shepherd, in ev, ctx);

            Assert.AreEqual(1, ctx.Heals.Count, "Один хил на автоатаку");
            Assert.AreSame(wounded, ctx.Heals[0].Target, "Лечит самого раненого союзника (30% < 90%)");
            // Союзнику достаётся больше, чем досталось бы себе: 40 × (1 + 0.5) — решение 2026-07-27/3.
            Assert.AreEqual(60f, ctx.Heals[0].Amount, 1e-4f, "150% от нанесённого (40 → 60)");
        }

        // Приоритет союзника не зависит от того, кому хуже: даже самый раненый носитель отдаёт свет
        // другому, пока рядом есть кого лечить. Себе достаётся только когда рядом никого.
        [Test]
        public void PrefersAlly_EvenWhenBearerIsTheMostWounded()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();

            RuntimeUnit shepherd = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 100f, hp: 10f);          // 10% — самый раненый
            RuntimeUnit ally     = MakeUnit(1, team: 0, pos: new Vector2(2f, 0f), maxHp: 100f, hp: 30f);   // 30%
            RuntimeUnit victim   = MakeUnit(3, team: 1, pos: new Vector2(5f, 0f), maxHp: 100f, hp: 100f);
            ctx.UnitsInWorld.AddRange(new[] { shepherd, ally });

            var comp = new AllyMendComponent().With("_fraction", 1f).With("_radius", 5f).With("_autoAttackOnly", true);
            sys.Apply(shepherd, TestEffect.Make(baseDuration: -1f, components: comp), shepherd, ctx);

            var ev = new CombatEventData(CombatEvent.DamageDealt, shepherd, victim, 40f, EffectTag.None,
                sourceKind: DamageSourceKind.AutoAttack);
            sys.Dispatch(shepherd, in ev, ctx);

            Assert.AreEqual(1, ctx.Heals.Count, "Хил ушёл союзнику, а не пропал");
            Assert.AreSame(ally, ctx.Heals[0].Target, "Свет уходит другому, даже если носителю хуже");
        }

        // Решение 2026-07-27/3 заместило прежний полный запрет само-лечения: свет всегда что-то даёт
        // носителю, просто отдавать выгоднее. Неубиваемым Пастыря делала ульта с процентом от
        // НЕДОСТАЮЩЕГО HP, а не этот хил, — процент срезан, запрет больше не нужен.
        [Test]
        public void HealsSelf_ByBaseFraction_WhenBearerIsAlone()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();

            RuntimeUnit shepherd = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 100f, hp: 20f);
            RuntimeUnit victim   = MakeUnit(3, team: 1, pos: new Vector2(5f, 0f), maxHp: 100f, hp: 100f);
            ctx.UnitsInWorld.Add(shepherd);

            var comp = new AllyMendComponent().With("_fraction", 1f).With("_allyBonus", 0.5f)
                                              .With("_radius", 5f).With("_autoAttackOnly", true);
            sys.Apply(shepherd, TestEffect.Make(baseDuration: -1f, components: comp), shepherd, ctx);

            var ev = new CombatEventData(CombatEvent.DamageDealt, shepherd, victim, 40f, EffectTag.None,
                sourceKind: DamageSourceKind.AutoAttack);
            sys.Dispatch(shepherd, in ev, ctx);

            Assert.AreEqual(1, ctx.Heals.Count, "Рядом никого — свет достаётся носителю");
            Assert.AreSame(shepherd, ctx.Heals[0].Target);
            Assert.AreEqual(40f, ctx.Heals[0].Amount, 1e-4f, "Себе — по базовой доле (100%), без бонуса союзника");
        }

        [Test]
        public void DoesNotHeal_OnNonAutoAttackDamage_WhenAutoAttackOnly()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();

            RuntimeUnit shepherd = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 100f, hp: 100f);
            RuntimeUnit wounded  = MakeUnit(1, team: 0, pos: new Vector2(2f, 0f), maxHp: 100f, hp: 30f);
            RuntimeUnit victim   = MakeUnit(3, team: 1, pos: new Vector2(5f, 0f), maxHp: 100f, hp: 100f);
            ctx.UnitsInWorld.AddRange(new[] { shepherd, wounded });

            var comp = new AllyMendComponent().With("_fraction", 1f).With("_radius", 5f).With("_autoAttackOnly", true);
            sys.Apply(shepherd, TestEffect.Make(baseDuration: -1f, components: comp), shepherd, ctx);

            // Урон способности (не автоатака) — не лечит.
            var ev = new CombatEventData(CombatEvent.DamageDealt, shepherd, victim, 40f);
            sys.Dispatch(shepherd, in ev, ctx);

            Assert.AreEqual(0, ctx.Heals.Count, "autoAttackOnly: урон способности не лечит");
        }

        [Test]
        public void TieBreak_HealsLowerId_OnEqualHpPercent()
        {
            var sys = new EffectSystem();
            var ctx = new MockCombatContext();

            RuntimeUnit shepherd = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 100f, hp: 100f);
            RuntimeUnit allyA    = MakeUnit(2, team: 0, pos: new Vector2(1f, 0f), maxHp: 100f, hp: 50f); // 50%
            RuntimeUnit allyB    = MakeUnit(1, team: 0, pos: new Vector2(1f, 0f), maxHp: 100f, hp: 50f); // 50%, меньший Id
            RuntimeUnit victim   = MakeUnit(3, team: 1, pos: new Vector2(5f, 0f), maxHp: 100f, hp: 100f);
            ctx.UnitsInWorld.AddRange(new[] { allyA, allyB });

            var comp = new AllyMendComponent().With("_fraction", 1f).With("_radius", 5f);
            sys.Apply(shepherd, TestEffect.Make(baseDuration: -1f, components: comp), shepherd, ctx);

            var ev = new CombatEventData(CombatEvent.DamageDealt, shepherd, victim, 20f, EffectTag.None,
                sourceKind: DamageSourceKind.AutoAttack);
            sys.Dispatch(shepherd, in ev, ctx);

            Assert.AreEqual(1, ctx.Heals.Count);
            Assert.AreSame(allyB, ctx.Heals[0].Target, "При равном HP% выбирается меньший Id (детерминизм)");
        }

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp, float hp)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, maxHp) });
            return new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentHP        = hp,
                Position         = pos,
                PreviousPosition = pos,
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
        }
    }
}
