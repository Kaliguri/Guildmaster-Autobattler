using Guildmaster.UI.Tooltips;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Размещение окна тултипа (Трек Т, план §II.10.5 п.1): предпочтение справа, зеркало влево у правого
    /// края, кламп по вертикали. Ошибка тут видна только глазами на краю экрана — поэтому проверяем числом.
    /// </summary>
    public sealed class TooltipPlacementTests
    {
        private static readonly Rect Panel = new Rect(0f, 0f, 1920f, 1080f);
        private static readonly Vector2 Size = new Vector2(220f, 120f);

        [Test]
        public void Places_ToTheRight_ByDefault()
        {
            var anchor = new Rect(400f, 300f, 100f, 140f);
            Vector2 pos = TooltipPlacement.Place(anchor, Size, Panel);

            Assert.AreEqual(anchor.xMax + TooltipPlacement.Gap, pos.x, 0.01f, "окно должно вставать справа от якоря");
            Assert.AreEqual(anchor.yMin, pos.y, 0.01f, "и выравниваться по его верхнему краю");
        }

        [Test]
        public void Mirrors_ToTheLeft_WhenNoRoomOnTheRight()
        {
            var anchor = new Rect(1700f, 300f, 180f, 140f); // справа остаётся 40 px — окно не влезает
            Vector2 pos = TooltipPlacement.Place(anchor, Size, Panel);

            Assert.AreEqual(anchor.xMin - TooltipPlacement.Gap - Size.x, pos.x, 0.01f);
            Assert.Less(pos.x + Size.x, anchor.xMin, "зеркальное окно не должно наезжать на якорь");
        }

        [Test]
        public void Clamps_ToPanel_WhenNeitherSideFits()
        {
            // Якорь во всю ширину: ни справа, ни слева места нет — окно прижимается к краю панели,
            // но НЕ вылезает за него (иначе подсказка обрезана и бесполезна).
            var anchor = new Rect(0f, 300f, 1900f, 140f);
            Vector2 pos = TooltipPlacement.Place(anchor, Size, Panel);

            Assert.GreaterOrEqual(pos.x, Panel.xMin);
            Assert.LessOrEqual(pos.x + Size.x, Panel.xMax + 0.01f);
        }

        [Test]
        public void Lifts_Window_WhenItWouldHangBelowPanel()
        {
            var anchor = new Rect(400f, 1020f, 100f, 40f);
            Vector2 pos = TooltipPlacement.Place(anchor, Size, Panel);

            Assert.AreEqual(Panel.yMax - Size.y, pos.y, 0.01f, "окно поднимается, а не свисает за низ панели");
        }

        [Test]
        public void Keeps_Window_InsidePanel_WhenItIsTallerThanPanel()
        {
            // Патологический случай (окно выше экрана): верх важнее низа — заголовок должен остаться виден.
            var tall = new Vector2(220f, 1500f);
            var anchor = new Rect(400f, 900f, 100f, 40f);
            Vector2 pos = TooltipPlacement.Place(anchor, tall, Panel);

            Assert.AreEqual(Panel.yMin, pos.y, 0.01f);
        }
    }
}
