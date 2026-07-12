using Guildmaster.ContentHub.Editor;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.ContentHub
{
    public sealed class NavHistoryTests
    {
        [Test]
        public void Push_MovesCursorToNew()
        {
            var h = new NavHistory<string>();
            h.Push("a");
            Assert.AreEqual("a", h.Current);
            Assert.IsFalse(h.CanBack);
            Assert.IsFalse(h.CanForward);
            h.Push("b");
            Assert.AreEqual("b", h.Current);
            Assert.IsTrue(h.CanBack);
        }

        [Test]
        public void BackForward_Traverses()
        {
            var h = new NavHistory<string>();
            h.Push("a"); h.Push("b"); h.Push("c");
            Assert.AreEqual("b", h.Back());
            Assert.AreEqual("a", h.Back());
            Assert.IsFalse(h.CanBack);
            Assert.AreEqual("b", h.Forward());
            Assert.AreEqual("c", h.Forward());
            Assert.IsFalse(h.CanForward);
        }

        [Test]
        public void PushAfterBack_TruncatesForward()
        {
            var h = new NavHistory<string>();
            h.Push("a"); h.Push("b"); h.Push("c");
            h.Back(); // b
            h.Push("d");
            Assert.AreEqual("d", h.Current);
            Assert.IsFalse(h.CanForward);
            Assert.AreEqual("b", h.Back());
        }
    }
}
