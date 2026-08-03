using Guildmaster.UI.Tooltips;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Цепочка открытых подсказок (Трек Т, sticky-режим): переходов может быть сколько угодно,
    /// но на экране живут последние <see cref="TooltipChain{T}.Limit"/> окон (решение Макса).
    /// </summary>
    /// <remarks>
    /// Проверяем то, что ломается молча: вытеснение самого старого, порядок и полное снятие.
    /// «Окна не закрылись» и «на экране их пять» иначе находятся только руками.
    /// </remarks>
    public sealed class TooltipChainTests
    {
        [Test]
        public void Add_KeepsOrder_UntilLimit()
        {
            var chain = new TooltipChain<string>();

            chain.Add("a", out bool e1);
            chain.Add("b", out bool e2);
            chain.Add("c", out bool e3);

            Assert.AreEqual(3, chain.Count);
            Assert.IsFalse(e1 || e2 || e3, "до предела никого вытеснять не должно");
            Assert.AreEqual("a", chain.Oldest);
            Assert.AreEqual("c", chain.Top);
        }

        [Test]
        public void Add_BeyondLimit_EvictsOldest()
        {
            var chain = new TooltipChain<string>();
            chain.Add("a", out _);
            chain.Add("b", out _);
            chain.Add("c", out _);

            string evicted = chain.Add("d", out bool wasEvicted);

            Assert.IsTrue(wasEvicted);
            Assert.AreEqual("a", evicted, "уходит самое старое: интерес движется вперёд по цепочке");
            Assert.AreEqual(TooltipChain<string>.Limit, chain.Count);
            Assert.AreEqual("b", chain.Oldest);
            Assert.AreEqual("d", chain.Top);
            Assert.IsFalse(chain.Contains("a"));
        }

        [Test]
        public void Remove_TakesOutMiddleEntry()
        {
            var chain = new TooltipChain<string>();
            chain.Add("a", out _);
            chain.Add("b", out _);
            chain.Add("c", out _);

            Assert.IsTrue(chain.Remove("b"));

            Assert.AreEqual(2, chain.Count);
            Assert.IsFalse(chain.Contains("b"));
            Assert.AreEqual("c", chain.Top);
        }

        [Test]
        public void DrainAll_EmptiesAndReturnsEverything()
        {
            var chain = new TooltipChain<string>();
            chain.Add("a", out _);
            chain.Add("b", out _);

            var drained = chain.DrainAll();

            CollectionAssert.AreEqual(new[] { "a", "b" }, drained, "вызывающий снимает окна в порядке открытия");
            Assert.AreEqual(0, chain.Count);
            Assert.IsNull(chain.Top);
        }

        [Test]
        public void EmptyChain_HasNoTopOrOldest()
        {
            var chain = new TooltipChain<string>();

            Assert.AreEqual(0, chain.Count);
            Assert.IsNull(chain.Top);
            Assert.IsNull(chain.Oldest);
        }
    }
}
