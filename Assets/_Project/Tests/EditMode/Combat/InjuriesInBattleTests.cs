using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Травмы и закалки в бою: доезжают ли последствия забега до собранного юнита и что именно они с
    /// ним делают (ГДД <c>injuries-mettle</c>, <c>injury-catalogue</c>).
    /// <para>Инварианты держатся тестом, потому что нарушаются они в другом файле и молча: слой
    /// последствий стоит последним в каскаде, и любая перестановка или потерянный аргумент по дороге
    /// «гильдия → бой» дают бойца, вошедшего в бой здоровым после того, как игрок заплатил за травму
    /// слотом. Ошибки при этом не будет — будет тихо другая игра.</para>
    /// </summary>
    public sealed class InjuriesInBattleTests
    {
        private static RuntimeUnitFactory MakeFactory()
        {
            var effects = new EffectSystem();
            return new RuntimeUnitFactory(null, null, effects, new MockCombatContext(effects: effects));
        }

        /// <summary>Предмет с одними статами — своего билдера у <c>ItemData</c> в поддержке нет.</summary>
        private static ItemData Item(params StatModifier[] mods)
            => ScriptableObject.CreateInstance<ItemData>().With("_mods", mods);

        private static RelicData Kit(float maxHp = 100f, float moveSpeed = 3f) => TestRelic.Make(stats: new[]
        {
            new StatModifier(StatType.MaxHP,     ModifierOp.Override, maxHp),
            new StatModifier(StatType.MoveSpeed, ModifierOp.Override, moveSpeed),
        });

        [Test]
        public void InjuryMods_ReachTheUnitThroughTheFactory()
        {
            var factory = MakeFactory();
            ConsequenceData sprainedLeg = TestConsequence.Make(mods: new[]
            {
                new StatModifier(StatType.MoveSpeed, ModifierOp.PercentMult, -0.3f),
            });

            RuntimeUnit unit = factory.Create(Kit(), null, team: 0, Vector2.zero,
                                              items: null, consequences: new[] { sprainedLeg });

            Assert.That(unit.Stats.Get(StatType.MoveSpeed), Is.EqualTo(2.1f).Within(1e-4f),
                "«Подвёрнутая нога» — −30% скорости от 3.0.");
        }

        [Test]
        public void HealthyVessel_IsUntouched()
        {
            var factory = MakeFactory();

            RuntimeUnit unit = factory.Create(Kit(), null, team: 0, Vector2.zero);

            Assert.That(unit.Stats.Get(StatType.MoveSpeed), Is.EqualTo(3f).Within(1e-4f));
            Assert.That(unit.CurrentHP, Is.EqualTo(100f).Within(1e-4f), "Целый выходит с полным запасом.");
        }

        /// <summary>
        /// Три ушиба на один стат перемножаются, а не складываются. Складывались бы — «−30%» трижды
        /// дало бы ровно ноль скорости и бойца, стоящего на месте; в игре так быть не должно ни при
        /// каком наборе травм.
        /// </summary>
        [Test]
        public void ThreeInjuriesOnOneStat_Multiply()
        {
            var factory = MakeFactory();
            ConsequenceData Slow() => TestConsequence.Make(mods: new[]
            {
                new StatModifier(StatType.MoveSpeed, ModifierOp.PercentMult, -0.3f),
            });

            RuntimeUnit unit = factory.Create(Kit(), null, team: 0, Vector2.zero,
                                              items: null, consequences: new[] { Slow(), Slow(), Slow() });

            Assert.That(unit.Stats.Get(StatType.MoveSpeed), Is.EqualTo(3f * 0.7f * 0.7f * 0.7f).Within(1e-4f));
            Assert.That(unit.Stats.Get(StatType.MoveSpeed), Is.GreaterThan(0f),
                "Перемножение оставляет бойца подвижным — сложение обнулило бы его.");
        }

        /// <summary>
        /// Травма по стартовому запасу срезает HP на входе в бой, но НЕ трогает потолок: разница
        /// механическая — такую травму отыгрывает лечение внутри боя, травму по <c>MaxHP</c> не
        /// отыгрывает ничто.
        /// </summary>
        [Test]
        public void StartHpInjury_CutsTheStartingPoolButNotTheCeiling()
        {
            var factory = MakeFactory();
            ConsequenceData crackedRib = TestConsequence.Make(mods: new[]
            {
                new StatModifier(StatType.StartHpPct, ModifierOp.PercentMult, -0.3f),
            });

            RuntimeUnit unit = factory.Create(Kit(maxHp: 100f), null, team: 0, Vector2.zero,
                                              items: null, consequences: new[] { crackedRib });

            Assert.That(unit.CurrentHP, Is.EqualTo(70f).Within(1e-4f), "Вошёл в бой с 70 из 100.");
            Assert.That(unit.Stats.Get(StatType.MaxHP), Is.EqualTo(100f).Within(1e-4f),
                "Потолок не тронут — лечение вернёт бойца к сотне.");
        }

        [Test]
        public void MaxHpInjury_LowersTheCeilingAndTheStartTogether()
        {
            var factory = MakeFactory();
            ConsequenceData piercedSide = TestConsequence.Make(
                grade: InjuryGrade.Wound,
                mods: new[] { new StatModifier(StatType.MaxHP, ModifierOp.PercentMult, -0.4f) });

            RuntimeUnit unit = factory.Create(Kit(maxHp: 100f), null, team: 0, Vector2.zero,
                                              items: null, consequences: new[] { piercedSide });

            Assert.That(unit.Stats.Get(StatType.MaxHP), Is.EqualTo(60f).Within(1e-4f));
            Assert.That(unit.CurrentHP, Is.EqualTo(60f).Within(1e-4f),
                "Стартует полным от нового потолка — доля стартового запаса не тронута.");
        }

        /// <summary>
        /// Стартовая доля выше единицы не переливает через край: закалка «+10% стартового HP» на
        /// здоровом бойце просто ничего не делает — она отыгрывает травму, а не даёт лишнего.
        /// </summary>
        [Test]
        public void StartFractionAboveOne_DoesNotOverfill()
        {
            var factory = MakeFactory();
            ConsequenceData mettle = TestConsequence.Make(
                polarity: ConsequencePolarity.Mettle,
                mods: new[] { new StatModifier(StatType.StartHpPct, ModifierOp.PercentMult, 0.5f) });

            RuntimeUnit unit = factory.Create(Kit(maxHp: 100f), null, team: 0, Vector2.zero,
                                              items: null, consequences: new[] { mettle });

            Assert.That(unit.CurrentHP, Is.EqualTo(100f).Within(1e-4f));
        }

        /// <summary>
        /// Последствия ложатся ПОСЛЕ предметов. Порядок виден на паре «предмет даёт +100% скорости,
        /// травма отнимает 30%»: при обратном порядке травма считалась бы от базы, и сапоги скорости
        /// отыгрывали бы её сильнее, чем обещает карточка.
        /// </summary>
        [Test]
        public void ConsequencesApplyAfterItems()
        {
            var factory = MakeFactory();
            ItemData boots = Item(new StatModifier(StatType.MoveSpeed, ModifierOp.PercentMult, 1.0f));
            ConsequenceData sprainedLeg = TestConsequence.Make(mods: new[]
            {
                new StatModifier(StatType.MoveSpeed, ModifierOp.PercentMult, -0.3f),
            });

            RuntimeUnit unit = factory.Create(Kit(moveSpeed: 3f), null, team: 0, Vector2.zero,
                                              items: new[] { boots },
                                              consequences: new[] { sprainedLeg });

            // Оба множителя мультипликативны, поэтому результат один и тот же при любом порядке —
            // проверяем ВЕЛИЧИНУ, а порядок слоёв держит соседний тест на Override.
            Assert.That(unit.Stats.Get(StatType.MoveSpeed), Is.EqualTo(3f * 2f * 0.7f).Within(1e-4f));
        }

        /// <summary>
        /// А вот <see cref="ModifierOp.Override"/> порядок различает: последний побеждает. Последствие
        /// с Override обязано перебивать предмет, а не наоборот — иначе снаряжение «вылечивало» бы
        /// травму молча.
        /// </summary>
        [Test]
        public void ConsequenceOverride_BeatsItemOverride()
        {
            var factory = MakeFactory();
            ItemData charm = Item(new StatModifier(StatType.MoveSpeed, ModifierOp.Override, 9f));
            ConsequenceData shatteredLeg = TestConsequence.Make(
                grade: InjuryGrade.Maiming,
                mods: new[] { new StatModifier(StatType.MoveSpeed, ModifierOp.Override, 1f) });

            RuntimeUnit unit = factory.Create(Kit(), null, team: 0, Vector2.zero,
                                              items: new[] { charm },
                                              consequences: new[] { shatteredLeg });

            Assert.That(unit.Stats.Get(StatType.MoveSpeed), Is.EqualTo(1f).Within(1e-4f));
        }
    }
}
