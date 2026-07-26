using Guildmaster.UI.Tooltips;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// История закреплённого окна (Трек Т, план §II.10.5 слой 3): переходы по терминам — ОДНО окно
    /// с навигацией, а не стопка окон. Проверяем то, что ломается молча: обрезку ветки «вперёд»,
    /// границы и цикл терминов (Броня → Урон → Броня), на котором стек окон ушёл бы в бесконечность.
    /// </summary>
    public sealed class TooltipHistoryTests
    {
        private static TooltipRequest Kw(string id) => TooltipRequest.Keyword(id);

        [Test]
        public void Reset_StartsWithSingleEntry_AndNoMoves()
        {
            var history = new TooltipHistory();
            history.Reset(Kw("kw.armor"));

            Assert.AreEqual(1, history.Count);
            Assert.IsFalse(history.CanGoBack, "с одной записи назад идти некуда");
            Assert.IsFalse(history.CanGoForward);
            Assert.IsTrue(history.Current.SameAs(Kw("kw.armor")));
        }

        [Test]
        public void Push_MovesForward_AndEnablesBack()
        {
            var history = new TooltipHistory();
            history.Reset(Kw("kw.armor"));

            Assert.IsTrue(history.Push(Kw("kw.physical")));
            Assert.IsTrue(history.CanGoBack);
            Assert.IsTrue(history.Current.SameAs(Kw("kw.physical")));
        }

        [Test]
        public void Push_IgnoresSameContent()
        {
            var history = new TooltipHistory();
            history.Reset(Kw("kw.armor"));

            Assert.IsFalse(history.Push(Kw("kw.armor")), "повторный переход на то же — не переход");
            Assert.AreEqual(1, history.Count);
        }

        [Test]
        public void Back_ThenPush_TrimsForwardBranch()
        {
            var history = new TooltipHistory();
            history.Reset(Kw("kw.armor"));
            history.Push(Kw("kw.physical"));
            history.Push(Kw("kw.true"));

            history.GoBack();                       // вернулись на kw.physical
            Assert.IsTrue(history.CanGoForward);

            history.Push(Kw("kw.magical"));         // ушли в сторону
            Assert.IsFalse(history.CanGoForward, "ветка «вперёд» обрезается, как в браузере");
            Assert.AreEqual(3, history.Count);
            Assert.IsTrue(history.Current.SameAs(Kw("kw.magical")));
        }

        [Test]
        public void Cycle_BetweenTerms_WalksNaturally()
        {
            // Броня ссылается на Урон, Урон обратно на Броню: у стопки окон это бесконечность,
            // у истории — просто три записи, по которым можно ходить туда-сюда.
            var history = new TooltipHistory();
            history.Reset(Kw("kw.armor"));
            history.Push(Kw("kw.physical"));
            history.Push(Kw("kw.armor"));

            Assert.AreEqual(3, history.Count);
            Assert.IsTrue(history.GoBack());
            Assert.IsTrue(history.Current.SameAs(Kw("kw.physical")));
            Assert.IsTrue(history.GoForward());
            Assert.IsTrue(history.Current.SameAs(Kw("kw.armor")));
        }

        [Test]
        public void Moves_StopAtBoundaries()
        {
            var history = new TooltipHistory();
            history.Reset(Kw("kw.armor"));
            history.Push(Kw("kw.physical"));

            Assert.IsTrue(history.GoBack());
            Assert.IsFalse(history.GoBack(), "за начало истории не уходим");
            Assert.IsTrue(history.GoForward());
            Assert.IsFalse(history.GoForward(), "за конец истории не уходим");
        }

        [Test]
        public void Clear_EmptiesEverything()
        {
            var history = new TooltipHistory();
            history.Reset(Kw("kw.armor"));
            history.Push(Kw("kw.physical"));

            history.Clear();

            Assert.AreEqual(0, history.Count);
            Assert.IsTrue(history.Current.IsEmpty);
            Assert.IsFalse(history.CanGoBack);
            Assert.IsFalse(history.CanGoForward);
        }
    }
}
