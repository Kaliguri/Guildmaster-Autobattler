using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Каскад травм (<see cref="InjuryCascade"/>): правило «3 мелких / 2 средних / 1 тяжёлая, лишнее
    /// поднимается на ступень» (решение Макса 2026-08-21).
    /// <para>Инвариант держится тестом, а не комментарием, потому что нарушить его легко снаружи и
    /// молча: каскад — единственное, что превращает «ещё одна смерть» из линейного налога в растущую
    /// угрозу, и любая правка чисел слотов или направления подъёма меняет всю кривую сложности забега,
    /// ничего не ломая при компиляции.</para>
    /// </summary>
    public sealed class InjuryCascadeTests
    {
        private static InjurySlots Empty => new InjurySlots(0, 0, 0);

        [Test]
        public void FreshVessel_TakesBruiseAsBruise()
        {
            InjuryOutcome outcome = InjuryCascade.Resolve(Empty, InjuryGrade.Bruise);

            Assert.That(outcome.Grade, Is.EqualTo(InjuryGrade.Bruise));
            Assert.That(outcome.Retired, Is.False);
            Assert.That(outcome.Escalated, Is.False, "Свободный слот — подниматься некуда и незачем.");
        }

        [Test]
        public void ThreeBruises_TurnTheFourthIntoWound()
        {
            var occupied = new InjurySlots(InjuryCascade.BruiseSlots, 0, 0);

            InjuryOutcome outcome = InjuryCascade.Resolve(occupied, InjuryGrade.Bruise);

            Assert.That(outcome.Grade, Is.EqualTo(InjuryGrade.Wound));
            Assert.That(outcome.Escalated, Is.True);
            Assert.That(outcome.Requested, Is.EqualTo(InjuryGrade.Bruise),
                "Исход помнит, что просили мелкую: на этом стоит подача игроку.");
        }

        [Test]
        public void BruisesAndWoundsFull_TurnTheNextBruiseIntoMaiming()
        {
            var occupied = new InjurySlots(InjuryCascade.BruiseSlots, InjuryCascade.WoundSlots, 0);

            InjuryOutcome outcome = InjuryCascade.Resolve(occupied, InjuryGrade.Bruise);

            Assert.That(outcome.Grade, Is.EqualTo(InjuryGrade.Maiming));
            Assert.That(outcome.Retired, Is.False, "Тяжёлый слот ещё свободен — выбывать рано.");
        }

        [Test]
        public void EverySlotFull_RetiresTheVesselFromTheRun()
        {
            var occupied = new InjurySlots(
                InjuryCascade.BruiseSlots, InjuryCascade.WoundSlots, InjuryCascade.MaimingSlots);

            InjuryOutcome outcome = InjuryCascade.Resolve(occupied, InjuryGrade.Bruise);

            Assert.That(outcome.Retired, Is.True);
            Assert.That(outcome.Escalated, Is.False, "Выбывание — не подъём ступени, а отдельный исход.");
        }

        [Test]
        public void MaimingOnFullMaimingSlot_RetiresImmediately()
        {
            var occupied = new InjurySlots(0, 0, InjuryCascade.MaimingSlots);

            InjuryOutcome outcome = InjuryCascade.Resolve(occupied, InjuryGrade.Maiming);

            Assert.That(outcome.Retired, Is.True,
                "Тяжёлому подниматься некуда: свободные мелкие слоты его не спасают.");
        }

        /// <summary>
        /// Каскад идёт только ВВЕРХ. Средняя при занятых средних становится увечьем, даже когда
        /// мелкие слоты пусты — иначе игрок лечил бы тяжёлые последствия, набивая лёгкие.
        /// </summary>
        [Test]
        public void CascadeNeverFallsBackToLighterSlots()
        {
            var occupied = new InjurySlots(0, InjuryCascade.WoundSlots, 0);

            InjuryOutcome outcome = InjuryCascade.Resolve(occupied, InjuryGrade.Wound);

            Assert.That(outcome.Grade, Is.EqualTo(InjuryGrade.Maiming));
        }

        [Test]
        public void SlotCounts_MatchTheDesign()
        {
            Assert.That(InjuryCascade.Capacity(InjuryGrade.Bruise),  Is.EqualTo(3));
            Assert.That(InjuryCascade.Capacity(InjuryGrade.Wound),   Is.EqualTo(2));
            Assert.That(InjuryCascade.Capacity(InjuryGrade.Maiming), Is.EqualTo(1));
        }

        [Test]
        public void SlotsAreCountedFromTheGradeList()
        {
            var grades = new[]
            {
                InjuryGrade.Bruise, InjuryGrade.Maiming, InjuryGrade.Bruise, InjuryGrade.Wound,
            };

            InjurySlots slots = InjurySlots.Of(grades);

            Assert.That(slots.Bruises,  Is.EqualTo(2));
            Assert.That(slots.Wounds,   Is.EqualTo(1));
            Assert.That(slots.Maimings, Is.EqualTo(1));
            Assert.That(slots.Free(InjuryGrade.Bruise), Is.EqualTo(1));
            Assert.That(slots.Free(InjuryGrade.Maiming), Is.Zero);
        }

        /// <summary>
        /// Три смерти подряд забивают мелкие слоты, четвёртая уже приходит средней. Проверка не
        /// отдельного шага, а последовательности: именно её видит игрок за забег.
        /// </summary>
        [Test]
        public void FourDeathsInARow_EndWithAWound()
        {
            InjurySlots slots = Empty;
            InjuryOutcome last = default;

            for (int death = 0; death < 4; death++)
            {
                last = InjuryCascade.Resolve(slots, InjuryGrade.Bruise);
                slots = slots.With(last.Grade);
            }

            Assert.That(last.Grade, Is.EqualTo(InjuryGrade.Wound));
            Assert.That(slots.Bruises, Is.EqualTo(3));
            Assert.That(slots.Wounds, Is.EqualTo(1));
        }

        [Test]
        public void ResolveDoesNotMutateSlots()
        {
            var occupied = new InjurySlots(1, 0, 0);

            InjuryCascade.Resolve(occupied, InjuryGrade.Bruise);

            Assert.That(occupied.Bruises, Is.EqualTo(1),
                "Resolve только считает: класть травму — дело вызывающего.");
        }
    }
}
