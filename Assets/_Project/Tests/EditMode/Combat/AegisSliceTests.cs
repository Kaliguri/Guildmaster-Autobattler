using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Вертикальный срез «Эгида» (Антимаг, карточка [[the-aegis]]): порядок причин у «Отражающего налёта».
    /// Держит ровно то правило из карточки, которое нельзя выразить внутри одного файла — <b>щит за
    /// вражеский каст встаёт ДО попадания нагрузки этого же каста</b>.
    /// </summary>
    /// <remarks>
    /// Инвариант кросс-файловый и потому живёт тестом: заявление о касте ставит <c>AbilitySystem</c>,
    /// доставляет <c>CombatSimulation</c> (очередь заявлений), щит поднимает <c>WardOnEnemyCastComponent</c>,
    /// а гасит удар <c>SchoolShieldComponent</c> — ни одна из четырёх сторон в одиночку правило не видит.
    /// <para>
    /// Было сломано до 2026-07-31: заявление лежало в общей очереди последствий, которая дренится ПОСЛЕ
    /// коммита реестра урона, поэтому щит приходил на удар, который уже прилетел.
    /// </para>
    /// </remarks>
    public sealed class AegisSliceTests
    {
        private const float WardAmount = 60f;
        private const float SpellHit   = 50f;

        [Test]
        public void WardFromEnemyCast_AbsorbsPayloadOfThatSameCast()
        {
            var effects = new EffectSystem();
            CombatSimulation sim = BuildSim(1UL, effects);

            RuntimeUnit aegis = MakeUnit(0, team: 0, pos: Vector2.zero,   maxHp: 3000f);
            RuntimeUnit mage  = MakeUnit(1, team: 1, pos: new Vector2(20f, 0f), maxHp: 100f);
            Spawn(sim, aegis, mage);

            // Налёт висит, но пул ПУСТ: щита ещё нет, он появится только за каст врага.
            effects.Apply(aegis, ReflectivePlating(ArcaneWard()), aegis, sim);

            // Маг объявляет каст и в том же тике применяет его нагрузку — ровно как это делает
            // AbilitySystem: сначала ReportAbilityCast, затем ApplyPayload.
            sim.ReportAbilityCast(mage);
            sim.DealDamage(new DamageRequest(mage, aegis, SpellHit, DamageType.Arcane, sim.ArmorK,
                                             sourceKind: DamageSourceKind.Ability));

            sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(3000f, aegis.CurrentHP, 1e-3f,
                "Щит за вражеский каст обязан встать ДО попадания и съесть нагрузку этого же каста " +
                "(карточка the-aegis, шаг 5 п.1). HP просело — значит удар применился раньше щита.");
            Assert.AreEqual(SpellHit, aegis.AbsorbedByWard, 1e-3f,
                "Поглощённое должно попасть в накопитель: «Перегрузка» считает именно по нему.");
        }

        /// <summary>
        /// Обратная сторона того же правила: щит фильтрован по школе, поэтому ФИЗИЧЕСКИЙ удар он не ест —
        /// иначе «против чистой физики обычный танк» из карточки перестало бы быть правдой.
        /// </summary>
        [Test]
        public void WardFromEnemyCast_DoesNotAbsorbPhysicalHit()
        {
            var effects = new EffectSystem();
            CombatSimulation sim = BuildSim(1UL, effects);

            RuntimeUnit aegis = MakeUnit(0, team: 0, pos: Vector2.zero,   maxHp: 3000f);
            RuntimeUnit mage  = MakeUnit(1, team: 1, pos: new Vector2(20f, 0f), maxHp: 100f);
            Spawn(sim, aegis, mage);

            effects.Apply(aegis, ReflectivePlating(ArcaneWard()), aegis, sim);

            sim.ReportAbilityCast(mage);
            sim.DealDamage(new DamageRequest(mage, aegis, SpellHit, DamageType.Slash, sim.ArmorK,
                                             sourceKind: DamageSourceKind.Ability));

            sim.Tick(SimConstants.TickDelta);

            Assert.Less(aegis.CurrentHP, 3000f, "Физический удар щит от магии гасить не должен");
            Assert.AreEqual(0f, aegis.AbsorbedByWard, 1e-3f,
                "Физика в накопитель не идёт — иначе «Перегрузка» питалась бы не магией");
        }

        /// <summary>
        /// Сам инвариант, из-за которого зеркало расходилось: <b>заявка урона НЕ трогает защиты цели</b>.
        /// Пока удар лежит в реестре, запас щита цел и накопитель пуст; всё это меняется только на коммите.
        /// </summary>
        /// <remarks>
        /// Проверять именно так, а не по HP: раньше pre-damage срабатывал в момент заявки, то есть посреди
        /// фазы решений, и HP при этом оставались прежними — расхождение прятало́сь в запасе щита и
        /// накопителе. Тот, кто ходил в обходе позже, обналичивал уже изменённый мир.
        /// </remarks>
        [Test]
        public void DamageClaim_DoesNotTouchTargetDefencesBeforeCommit()
        {
            var effects = new EffectSystem();
            CombatSimulation sim = BuildSim(1UL, effects);

            RuntimeUnit aegis = MakeUnit(0, team: 0, pos: Vector2.zero,   maxHp: 3000f);
            RuntimeUnit mage  = MakeUnit(1, team: 1, pos: new Vector2(20f, 0f), maxHp: 100f);
            Spawn(sim, aegis, mage);

            // Щит уже поднят и пул полон — заявке будет что тратить, если она полезет не вовремя.
            effects.Apply(aegis, ArcaneWard(), aegis, sim);
            RuntimeEffect ward = aegis.ActiveEffects[0];
            Assert.AreEqual(WardAmount, ward.HeldShield, 1e-3f, "стенд: щит должен стоять с полным запасом");

            sim.DealDamage(new DamageRequest(mage, aegis, SpellHit, DamageType.Arcane, sim.ArmorK,
                                             sourceKind: DamageSourceKind.Ability));

            Assert.AreEqual(WardAmount, ward.HeldShield, 1e-3f,
                "Заявка не должна тратить запас щита: счёт живёт на коммите реестра");
            Assert.AreEqual(0f, aegis.AbsorbedByWard, 1e-3f,
                "Заявка не должна копить поглощённое — иначе решения этого же тика читают изменённый мир");

            sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(WardAmount - SpellHit, ward.HeldShield, 1e-3f, "На коммите запас обязан списаться");
            Assert.AreEqual(SpellHit, aegis.AbsorbedByWard, 1e-3f, "На коммите поглощённое обязано попасть в накопитель");
        }

        // --- Стенд ---

        /// <summary>Щит с фильтром по школе — те же поля, что в ассете ArcaneWard (60 арканы, вся школа).</summary>
        private static EffectData ArcaneWard() =>
            TestEffect.Make(
                baseDuration: 6f, polarity: EffectPolarity.Buff, tags: EffectTag.Buff | EffectTag.Shield,
                stacking: StackRule.Stack, maxStacks: 10,
                components: new SchoolShieldComponent()
                    .With("_amount", new ScalableValue(WardAmount))
                    .With("_damageType", DamageType.Arcane)
                    .With("_wholeSchool", true));

        /// <summary>Пассивка «Отражающий налёт»: выдаёт носителю щит за каждый каст врага.</summary>
        private static EffectData ReflectivePlating(EffectData ward) =>
            TestEffect.Make(
                baseDuration: -1f, polarity: EffectPolarity.Buff, unremovable: true,
                components: new WardOnEnemyCastComponent().With("_ward", ward));

        private static void Spawn(CombatSimulation sim, params RuntimeUnit[] units)
        {
            foreach (RuntimeUnit u in units) sim.EnqueueUnitSpawn(u);
            sim.FlushSpawns();
        }

        /// <summary>
        /// Симуляция с ВНЕШНИМ EffectSystem: налёт кладётся тем же экземпляром, которым бой потом будет
        /// его дренить. Свой второй экземпляр в тесте дал бы эффект, о котором бой не знает.
        /// </summary>
        private static CombatSimulation BuildSim(ulong seed, EffectSystem effects) =>
            new CombatSimulation(
                new XorShiftRng(seed), CombatTestValues.ArmorK, new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                effects, new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                // Автоатаки в срезе нет намеренно: дистанция 20 м при радиусе 1 м, иначе тычки замусорят
                // накопитель и HP, а проверяется порядок «каст → щит → удар».
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 0f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 1f),
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
                AutoAttackDamageType = DamageType.Slash,
            };
        }
    }
}
