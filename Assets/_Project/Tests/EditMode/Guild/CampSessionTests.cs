using Guildmaster.Guild;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Бюджет привала (реш. Макса 2026-07-20): отряд привозит 8 действий, каждая трата стоит 2 — значит
    /// ровно четыре траты и ни одной сверх. Закрепляем это здесь, потому что счётчик держит домен, а экран
    /// только читает: разъедься эти двое — увидели бы уже в play-QA.
    /// </summary>
    public sealed class CampSessionTests
    {
        [Test]
        public void NewSession_HasFullBudget()
        {
            var camp = new CampSession();
            Assert.AreEqual(8, camp.Budget, "Бюджет привала — 8 действий.");
            Assert.AreEqual(2, camp.ActionCost, "Одна трата стоит 2 действия.");
            Assert.AreEqual(8, camp.Remaining);
            Assert.IsTrue(camp.CanAfford);
            Assert.IsFalse(camp.IsClosed);
        }

        [Test]
        public void Perform_SpendsCost_AndStopsWhenBudgetRunsOut()
        {
            var camp = new CampSession();

            for (int i = 1; i <= 4; i++)
            {
                Assert.IsTrue(camp.TryPerform(CampAction.Empower), $"Трата {i} из четырёх должна пройти.");
                Assert.AreEqual(8 - i * 2, camp.Remaining);
            }

            Assert.IsFalse(camp.CanAfford, "После четырёх трат бюджет исчерпан.");
            Assert.IsFalse(camp.TryPerform(CampAction.Empower), "Пятая трата не проходит.");
            Assert.AreEqual(0, camp.Remaining, "Отказ не списывает бюджет.");
        }

        [Test]
        public void MoveOn_IsFreeAndAlwaysAvailable_EvenWithEmptyBudget()
        {
            var camp = new CampSession(budget: 0);

            Assert.IsFalse(camp.CanAfford);
            Assert.IsTrue(camp.TryPerform(CampAction.MoveOn), "Уйти можно с любым остатком.");
            Assert.IsTrue(camp.IsClosed);
            Assert.AreEqual(0, camp.Spent, "Уход ничего не стоит.");
        }

        /// <summary>
        /// Отказ исполнителя не стоит игроку действия. Иначе промах по цели («снять нечего») списывал
        /// бы четверть бюджета привала и выглядел бы как поломка кнопки.
        /// </summary>
        [Test]
        public void RefusedEffect_CostsNothing()
        {
            var camp = new CampSession(effect: (action, slot, id) => false);

            Assert.IsFalse(camp.TryPerform(CampAction.Cleanse, slotIndex: 0, consequenceId: "consequence.x"));
            Assert.AreEqual(0, camp.Spent, "Не вышло — не заплатили.");
            Assert.AreEqual(8, camp.Remaining);
        }

        [Test]
        public void SuccessfulEffect_GetsTheTargetItWasGiven()
        {
            int seenSlot = -99;
            string seenId = null;
            var camp = new CampSession(effect: (action, slot, id) =>
            {
                seenSlot = slot;
                seenId   = id;
                return true;
            });

            Assert.IsTrue(camp.TryPerform(CampAction.Cleanse, slotIndex: 2, consequenceId: "consequence.rib"));
            Assert.AreEqual(2, seenSlot);
            Assert.AreEqual("consequence.rib", seenId);
            Assert.AreEqual(2, camp.Spent);
        }

        /// <summary>
        /// Действия без своей механики по-прежнему тратят бюджет: их эффекты придут позже, а кнопка
        /// уже есть, и отказывать в ней значило бы соврать про то, чего привал не умеет.
        /// </summary>
        [Test]
        public void ActionsWithoutEffect_StillSpendBudget()
        {
            var camp = new CampSession();

            Assert.IsTrue(camp.TryPerform(CampAction.Empower));
            Assert.AreEqual(2, camp.Spent);
        }

        [Test]
        public void ClosedSession_RefusesEverything()
        {
            var camp = new CampSession();
            camp.TryPerform(CampAction.MoveOn);

            Assert.IsFalse(camp.TryPerform(CampAction.Empower), "После ухода привал больше ничего не принимает.");
            Assert.AreEqual(0, camp.Spent);
        }

        [Test]
        public void Changed_FiresOnSpendAndOnLeave()
        {
            var camp = new CampSession();
            int changes = 0;
            camp.Changed += () => changes++;

            camp.TryPerform(CampAction.Cleanse);
            Assert.AreEqual(1, changes, "Трата перерисовывает экран.");

            camp.TryPerform(CampAction.MoveOn);
            Assert.AreEqual(2, changes, "Уход тоже.");
        }
    }
}
