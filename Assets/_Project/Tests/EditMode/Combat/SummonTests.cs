using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Abilities;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Призывы (M10): рождение тел из способности, лимит как гейт каста, срок жизни, связь с хозяином и
    /// статы силы призывов. Решения Макса 2026-07-29: условия жизни у каждого призыва свои, призыв
    /// считается живым наравне со всеми, статы — множители на базу ассета, лимит у способности и лишний
    /// каст просто не идёт.
    /// </summary>
    public sealed class SummonTests
    {
        // Кит призыва: своя база, из которой и считаются множители силы призывов.
        private static UnitData SkeletonKit(float maxHp = 100f, float damage = 10f) =>
            TestRelic.Make(stats: new[]
            {
                new StatModifier(StatType.MaxHP, ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, damage),
            });

        private static RuntimeUnit Summoner(float healthEff = 1f, float damageEff = 1f)
        {
            RuntimeUnit unit = TestUnit.Make();
            unit.CurrentResource = 100f;
            if (!Mathf.Approximately(healthEff, 1f) || !Mathf.Approximately(damageEff, 1f))
                unit.Stats.AddModifiersFrom("items", new[]
                {
                    new StatModifier(StatType.SummonHealthEff, ModifierOp.Flat, healthEff - 1f),
                    new StatModifier(StatType.SummonDamageEff, ModifierOp.Flat, damageEff - 1f),
                });
            return unit;
        }

        // Мок собирает призыв сам: срезу призывов не нужна ни фабрика, ни SO-каскад.
        private static void WithSummonFactory(MockCombatContext ctx, UnitData kit)
        {
            int nextId = 100;
            ctx.SummonFactory = (data, team, pos, summoner) =>
            {
                var stats = new Stats(null);
                if (data?.Stats != null) stats.AddModifiersFrom(data, data.Stats);
                return new RuntimeUnit
                {
                    Id = nextId++, Team = team, Stats = stats,
                    CurrentHP = stats.Get(StatType.MaxHP),
                    AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
                };
            };
        }

        [Test]
        public void Summon_PutsBodiesOnTheField_AndMarksTheirOrigin()
        {
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            UnitData kit = SkeletonKit();
            WithSummonFactory(ctx, kit);

            RuntimeUnit necro = Summoner();
            necro.Abilities.Add(new AbilityRuntime(TestAbility.Make(
                cost: 40f, mode: AbilityTargetMode.Self, summonUnit: kit, summonCount: 2,
                id: "necro.raise")));

            sys.Tick(new List<RuntimeUnit> { necro }, ctx, SimConstants.TickDelta);

            Assert.AreEqual(2, ctx.Summons.Count, "Оба тела появились за один каст");
            Assert.AreSame(necro, ctx.Summons[0].Summoner, "У призыва записан хозяин");
            Assert.AreEqual("necro.raise", ctx.Summons[0].SummonAbilityId, "И способность, по которой считается лимит");
            Assert.IsTrue(ctx.Summons[0].IsSummon);
            Assert.AreNotEqual(ctx.Summons[0].Position, ctx.Summons[1].Position,
                "Тела не рождаются друг в друге");
        }

        [Test]
        public void Summon_Layout_IsDeterministic_ForMirroredSides()
        {
            // Зеркальный бой: раскладка призывов обязана быть чистой функцией от индекса, иначе одинаковые
            // отряды разойдутся с первого тика — та же болезнь, что ловил MirrorMatchTests на сепарации.
            var left  = new MockCombatContext();
            var right = new MockCombatContext();
            UnitData kit = SkeletonKit();
            WithSummonFactory(left, kit);
            WithSummonFactory(right, kit);

            AbilityData ability = TestAbility.Make(
                cost: 0f, mode: AbilityTargetMode.Self, summonUnit: kit, summonCount: 3, id: "necro.raise");

            RuntimeUnit a = Summoner();
            RuntimeUnit b = Summoner();
            a.Abilities.Add(new AbilityRuntime(ability));
            b.Abilities.Add(new AbilityRuntime(ability));

            new AbilitySystem().Tick(new List<RuntimeUnit> { a }, left,  SimConstants.TickDelta);
            new AbilitySystem().Tick(new List<RuntimeUnit> { b }, right, SimConstants.TickDelta);

            Assert.AreEqual(left.Summons.Count, right.Summons.Count);
            for (int i = 0; i < left.Summons.Count; i++)
                Assert.AreEqual(left.Summons[i].Position, right.Summons[i].Position,
                    "Одинаковый призыв встаёт одинаково у обеих сторон");
        }

        [Test]
        public void SummonStats_MultiplyTheKitBase_HealthAndDamageSeparately()
        {
            // Заявка Макса: живучесть и урон призывов усиливаются НЕЗАВИСИМО, чтобы предмет мог дать одно
            // без другого. Множители применяются к базе ассета, а не к статам хозяина.
            var factory = new RuntimeUnitFactory(
                null, null, new EffectSystem(), new MockCombatContext());
            UnitData kit = SkeletonKit(maxHp: 100f, damage: 10f);

            RuntimeUnit plain = factory.CreateSummon(kit, team: 0, Vector2.zero, Summoner());
            RuntimeUnit buffed = factory.CreateSummon(
                kit, team: 0, Vector2.zero, Summoner(healthEff: 1.5f, damageEff: 1.3f));

            Assert.AreEqual(100f, plain.Stats.Get(StatType.MaxHP), 1e-3f, "Без предметов — ровно база кита");
            Assert.AreEqual(150f, buffed.Stats.Get(StatType.MaxHP), 1e-3f, "+50% живучести");
            Assert.AreEqual(13f, buffed.Stats.Get(StatType.AutoAttackDamage), 1e-3f, "+30% урона");
            Assert.AreEqual(buffed.Stats.Get(StatType.MaxHP), buffed.CurrentHP, 1e-3f,
                "Усиленный призыв выходит целым, а не раненым");
        }

        [Test]
        public void SummonLimit_BlocksTheCastEntirely_KeepingManaAndCooldown()
        {
            // Решение Макса: предел — гейт каста. Мана и КД целы, игрок видит предел глазами.
            var sys = new AbilitySystem();
            var ctx = new MockCombatContext();
            UnitData kit = SkeletonKit();
            WithSummonFactory(ctx, kit);

            RuntimeUnit necro = Summoner();
            necro.Abilities.Add(new AbilityRuntime(TestAbility.Make(
                cooldown: 0f, cost: 40f, mode: AbilityTargetMode.Self,
                summonUnit: kit, summonCount: 1, summonLimit: 2, id: "necro.raise")));

            var units = new List<RuntimeUnit> { necro };
            for (int i = 0; i < 3; i++)
            {
                sys.Tick(units, ctx, SimConstants.TickDelta);
                units = new List<RuntimeUnit> { necro };
                units.AddRange(ctx.Summons);   // призывы попадают в список боя следующим тиком
            }

            Assert.AreEqual(2, ctx.Summons.Count, "Лимит держит армию на двух телах");
            Assert.AreEqual(20f, necro.CurrentResource, 1e-3f,
                "Заблокированный каст не сжёг ману: 100 − 40 × 2");
        }

        [Test]
        public void SummonLifetime_Expires_AndTheBodyDiesLikeAnyOther()
        {
            var sys = new SummonSystem();
            RuntimeUnit necro = Summoner();
            RuntimeUnit skeleton = TestUnit.Make(maxHp: 100f);
            skeleton.Summoner = necro;
            skeleton.SummonLifetimeRemaining = 3;

            var units = new List<RuntimeUnit> { necro, skeleton };
            sys.Tick(units);
            sys.Tick(units);
            Assert.Greater(skeleton.CurrentHP, 0f, "Срок ещё не вышел");

            sys.Tick(units);

            Assert.AreEqual(0f, skeleton.CurrentHP, 1e-4f,
                "Срок вышел — призыв уходит обычной смертью, а не исчезает мимо DeathSystem");
        }

        [Test]
        public void Summon_OutlivesTheSummoner_UnlessItIsBoundToHim()
        {
            // Условия у каждого призыва свои (решение Макса): обычный переживает хозяина, связанный — нет.
            var sys = new SummonSystem();
            RuntimeUnit necro = Summoner();

            RuntimeUnit free = TestUnit.Make(maxHp: 100f);
            free.Summoner = necro;

            RuntimeUnit bound = TestUnit.Make(maxHp: 100f);
            bound.Summoner = necro;
            bound.DiesWithSummoner = true;

            necro.IsDead = true;
            sys.Tick(new List<RuntimeUnit> { free, bound });

            Assert.Greater(free.CurrentHP, 0f, "Независимый призыв стоит и без хозяина");
            Assert.AreEqual(0f, bound.CurrentHP, 1e-4f, "Связанный уходит вместе с ним");
        }

        [Test]
        public void CountLiveSummons_IgnoresDeadOnesAndOtherAbilities()
        {
            RuntimeUnit necro = Summoner();
            RuntimeUnit other = Summoner();

            RuntimeUnit alive = TestUnit.Make(); alive.Summoner = necro; alive.SummonAbilityId = "raise";
            RuntimeUnit dead  = TestUnit.Make(); dead.Summoner  = necro; dead.SummonAbilityId  = "raise";
            dead.IsDead = true;
            RuntimeUnit fromOtherAbility = TestUnit.Make();
            fromOtherAbility.Summoner = necro; fromOtherAbility.SummonAbilityId = "guardians";
            RuntimeUnit foreign = TestUnit.Make(); foreign.Summoner = other; foreign.SummonAbilityId = "raise";

            var units = new List<RuntimeUnit> { alive, dead, fromOtherAbility, foreign };

            Assert.AreEqual(1, SummonSystem.CountLiveSummons(necro, "raise", units),
                "Лимит считает только живых, только свои и только от этой способности");
        }
    }
}
