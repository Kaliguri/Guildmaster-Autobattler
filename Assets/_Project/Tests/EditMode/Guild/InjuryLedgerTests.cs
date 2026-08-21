using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Книга ран (<see cref="InjuryLedger"/>): выдача через каскад, взвешенный ролл от сида и истечение
    /// мелких по пройденным узлам (ГДД <c>injuries-mettle</c>).
    /// <para>Проверяется здесь то, что нельзя удержать комментарием: последовательность боёв (её видит
    /// игрок, а не отдельный вызов), детерминизм ролла (на нём стоит кооп) и то, что срок раны берётся
    /// из ассета, а не из сейва.</para>
    /// </summary>
    public sealed class InjuryLedgerTests
    {
        private FakeContent _content;

        [SetUp]
        public void SetUp()
        {
            _content = new FakeContent();
            // По две раны каждой ступени: с одной ролл был бы неотличим от «взять первую».
            _content.Add(Injury("consequence.sprained_leg", InjuryGrade.Bruise,  expiresAfterNodes: 3));
            _content.Add(Injury("consequence.bruised_arm",  InjuryGrade.Bruise,  expiresAfterNodes: 3));
            _content.Add(Injury("consequence.torn_tendon",  InjuryGrade.Wound));
            _content.Add(Injury("consequence.pierced_side", InjuryGrade.Wound));
            _content.Add(Injury("consequence.crushed_leg",  InjuryGrade.Maiming));
        }

        private static RosterSlot Slot() => new RosterSlot();

        [Test]
        public void FirstDeath_LeavesABruise()
        {
            RosterSlot slot = Slot();

            InjuryOutcome outcome = InjuryLedger.Inflict(slot, InjuryGrade.Bruise, _content, rollSeed: 1);

            Assert.That(outcome.Grade, Is.EqualTo(InjuryGrade.Bruise));
            Assert.That(slot.Injuries.Length, Is.EqualTo(1));
            Assert.That(GradeOf(slot.Injuries[0]), Is.EqualTo(InjuryGrade.Bruise));
        }

        /// <summary>
        /// Четыре смерти подряд: три ушиба забивают мелкие слоты, четвёртая приходит уже средней. Это
        /// та самая нелинейная цена, ради которой каскад и существует.
        /// </summary>
        [Test]
        public void FourDeaths_EndWithAWoundOnTheSlot()
        {
            RosterSlot slot = Slot();

            for (int i = 0; i < 4; i++)
                InjuryLedger.Inflict(slot, InjuryGrade.Bruise, _content, rollSeed: (ulong)(i + 1));

            Assert.That(slot.Injuries.Length, Is.EqualTo(4));
            Assert.That(slot.Injuries.Count(inj => GradeOf(inj) == InjuryGrade.Bruise), Is.EqualTo(3));
            Assert.That(slot.Injuries.Count(inj => GradeOf(inj) == InjuryGrade.Wound), Is.EqualTo(1));
        }

        [Test]
        public void SeventhDeath_RetiresTheVesselAndLeavesNothingBehind()
        {
            RosterSlot slot = Slot();
            for (int i = 0; i < 6; i++)
                InjuryLedger.Inflict(slot, InjuryGrade.Bruise, _content, rollSeed: (ulong)(i + 1));

            InjuryOutcome outcome = InjuryLedger.Inflict(slot, InjuryGrade.Bruise, _content, rollSeed: 99);

            Assert.That(outcome.Retired, Is.True);
            Assert.That(slot.Injuries.Length, Is.EqualTo(6), "Класть было некуда — седьмая не легла.");
        }

        /// <summary>
        /// Один сид — одна и та же рана, всегда. На этом стоит кооп: команда несёт сид, и хозяин с
        /// гостем обязаны прийти к одному состоянию, применив её по своей копии.
        /// </summary>
        [Test]
        public void SameSeed_PicksTheSameInjury()
        {
            RosterSlot a = Slot(), b = Slot();

            InjuryLedger.Inflict(a, InjuryGrade.Bruise, _content, rollSeed: 12345);
            InjuryLedger.Inflict(b, InjuryGrade.Bruise, _content, rollSeed: 12345);

            Assert.That(a.Injuries[0].Id, Is.EqualTo(b.Injuries[0].Id));
        }

        [Test]
        public void DifferentSeeds_ReachBothInjuriesOfTheGrade()
        {
            var seen = new HashSet<string>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                RosterSlot slot = Slot();
                InjuryLedger.Inflict(slot, InjuryGrade.Bruise, _content, seed);
                seen.Add(slot.Injuries[0].Id);
            }

            Assert.That(seen.Count, Is.EqualTo(2), "Ролл обязан доставать оба ушиба, а не один и тот же.");
        }

        /// <summary>Вес нуль или ниже — рана из пула выпадает: так гасят карточку, не удаляя ассет.</summary>
        [Test]
        public void ZeroWeight_KeepsTheInjuryOutOfThePool()
        {
            var content = new FakeContent();
            content.Add(Injury("consequence.sprained_leg", InjuryGrade.Bruise));
            content.Add(Injury("consequence.bruised_arm",  InjuryGrade.Bruise, weight: 0f));

            for (ulong seed = 1; seed <= 20; seed++)
            {
                RosterSlot slot = Slot();
                InjuryLedger.Inflict(slot, InjuryGrade.Bruise, content, seed);
                Assert.That(slot.Injuries[0].Id, Is.EqualTo("consequence.sprained_leg"));
            }
        }

        [Test]
        public void BruisesFadeAfterTheirNodesArePassed()
        {
            var run = new RunState { Guild = new[] { Slot() } };
            InjuryLedger.Inflict(run.Guild[0], InjuryGrade.Bruise, _content, rollSeed: 7);

            Assert.That(InjuryLedger.AdvanceNode(run, _content), Is.Zero, "Первый узел — рано.");
            Assert.That(InjuryLedger.AdvanceNode(run, _content), Is.Zero, "Второй узел — всё ещё рано.");
            Assert.That(InjuryLedger.AdvanceNode(run, _content), Is.EqualTo(1), "Третий узел — прошла сама.");
            Assert.That(run.Guild[0].Injuries, Is.Empty);
        }

        /// <summary>
        /// Средние и тяжёлые сами не проходят никогда: у них срок нулевой, и сколько бы узлов ни
        /// прошло, они остаются. Асимметрия намеренная — за них платят золотом.
        /// </summary>
        [Test]
        public void WoundsNeverFadeOnTheirOwn()
        {
            var run = new RunState { Guild = new[] { Slot() } };
            for (int i = 0; i < 4; i++)  // три ушиба забьют слоты, четвёртая ляжет средней
                InjuryLedger.Inflict(run.Guild[0], InjuryGrade.Bruise, _content, rollSeed: (ulong)(i + 1));

            for (int node = 0; node < 20; node++) InjuryLedger.AdvanceNode(run, _content);

            Assert.That(run.Guild[0].Injuries.Length, Is.EqualTo(1), "Три ушиба сошли, средняя осталась.");
            Assert.That(GradeOf(run.Guild[0].Injuries[0]), Is.EqualTo(InjuryGrade.Wound));
        }

        /// <summary>
        /// Каждая рана стареет со СВОЕГО момента: полученная позже уходит позже. Общий счётчик узлов на
        /// забег снял бы обе разом — и вторая прожила бы меньше обещанного карточкой.
        /// </summary>
        [Test]
        public void EachInjuryAgesFromItsOwnMoment()
        {
            var run = new RunState { Guild = new[] { Slot() } };
            InjuryLedger.Inflict(run.Guild[0], InjuryGrade.Bruise, _content, rollSeed: 1);

            InjuryLedger.AdvanceNode(run, _content);
            InjuryLedger.Inflict(run.Guild[0], InjuryGrade.Bruise, _content, rollSeed: 2);

            InjuryLedger.AdvanceNode(run, _content);
            Assert.That(InjuryLedger.AdvanceNode(run, _content), Is.EqualTo(1), "Первая ушла на своём третьем узле.");
            Assert.That(run.Guild[0].Injuries.Length, Is.EqualTo(1), "Вторая ещё жива.");

            Assert.That(InjuryLedger.AdvanceNode(run, _content), Is.EqualTo(1), "И вот ушла вторая.");
        }

        [Test]
        public void SlotsFreedByHealing_TakeBruisesAgain()
        {
            RosterSlot slot = Slot();
            for (int i = 0; i < 3; i++)
                InjuryLedger.Inflict(slot, InjuryGrade.Bruise, _content, rollSeed: (ulong)(i + 1));

            Assert.That(InjuryLedger.Remove(slot, slot.Injuries[0].Id), Is.True);
            InjuryOutcome outcome = InjuryLedger.Inflict(slot, InjuryGrade.Bruise, _content, rollSeed: 50);

            Assert.That(outcome.Grade, Is.EqualTo(InjuryGrade.Bruise),
                "Слот освободился — следующая мелкая снова ложится мелкой.");
        }

        [Test]
        public void RemovingWhatIsNotThere_ChangesNothing()
        {
            RosterSlot slot = Slot();
            InjuryLedger.Inflict(slot, InjuryGrade.Bruise, _content, rollSeed: 3);

            Assert.That(InjuryLedger.Remove(slot, "consequence.nonexistent"), Is.False);
            Assert.That(slot.Injuries.Length, Is.EqualTo(1));
        }

        /// <summary>
        /// Закалка слотов травм не занимает: она не рана, и каскад её не считает. Иначе награда за
        /// клатч приближала бы «Сосуд» к выбыванию из забега.
        /// </summary>
        [Test]
        public void MettleDoesNotOccupyInjurySlots()
        {
            _content.Add(Injury("consequence.hardened", InjuryGrade.Bruise, polarity: ConsequencePolarity.Mettle));
            RosterSlot slot = Slot();
            slot.Injuries = new[] { new Injury("consequence.hardened") };

            InjurySlots slots = InjuryLedger.SlotsOf(slot, _content);

            Assert.That(slots.Bruises, Is.Zero);
        }

        private InjuryGrade GradeOf(Injury injury) =>
            _content.TryGet(injury.Id, out ConsequenceData def) ? def.Grade : InjuryGrade.Bruise;

        private static ConsequenceData Injury(string id, InjuryGrade grade, int expiresAfterNodes = 0,
                                              float weight = 1f,
                                              ConsequencePolarity polarity = ConsequencePolarity.Injury)
        {
            var c = ScriptableObject.CreateInstance<ConsequenceData>();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ConsequenceData).GetField("_grade", F).SetValue(c, grade);
            typeof(ConsequenceData).GetField("_polarity", F).SetValue(c, polarity);
            typeof(ConsequenceData).GetField("_expiresAfterNodes", F).SetValue(c, expiresAfterNodes);
            typeof(ConsequenceData).GetField("_weight", F).SetValue(c, weight);
            typeof(ContentDefinition).GetField("_id", F).SetValue(c, id);
            return c;
        }

        private sealed class FakeContent : IContentDatabase
        {
            private readonly List<ContentDefinition> _all = new();

            public void Add(ContentDefinition d) => _all.Add(d);

            public bool TryGet<T>(string id, out T def) where T : ContentDefinition
            {
                foreach (ContentDefinition d in _all)
                    if (d.Id == id && d is T t) { def = t; return true; }
                def = null;
                return false;
            }

            public IReadOnlyList<T> All<T>() where T : ContentDefinition =>
                _all.OfType<T>().ToArray();
        }
    }
}
