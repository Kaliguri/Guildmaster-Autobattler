using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Guildmaster.UI;

namespace Guildmaster.Tests.EditMode
{
    /// <summary>
    /// Политика затемнения стека экранов. Тема возвращалась дважды: сперва двойной скрим от двух
    /// модалок, потом — затемнение под настройками, открытыми из главного меню (там панель ЗАМЕНЯЕТ
    /// панель, темнить нечего). Второй раз он вернулся потому, что класс скрима ставили из двух мест:
    /// роутер вешал руками, а <c>SyncVisibility</c> тут же перезаписывал по своей политике.
    /// Тест держит обе части договора.
    /// </summary>
    [TestFixture]
    public class ScrimPolicyTests
    {
        private const string ScreenClass = "gm-screen";
        private const string ScrimlessClass = "gm-screen--scrimless";

        /// <summary>Экран-пустышка: строит корень с носителем <c>.gm-screen</c>, как настоящие UXML-экраны.</summary>
        private sealed class FakeScreen : UiScreen
        {
            public override ScreenKind Kind { get; }
            public override bool SuppressScrim { get; }

            public FakeScreen(ScreenKind kind, bool suppressScrim = false)
            {
                Kind = kind;
                SuppressScrim = suppressScrim;
            }

            public override void Build(UiScreenContext ctx)
            {
                // Namely как CloneTree: контейнер снаружи, .gm-screen внутри — навигатор обязан найти
                // носителя класса, а не красить контейнер (наход. раунда 3).
                var container = new VisualElement();
                var screen = new VisualElement();
                screen.AddToClassList(ScreenClass);
                container.Add(screen);
                Root = container;
            }
        }

        private static VisualElement ScrimOf(UiScreen s) => s.Root.Q(className: ScreenClass);

        /// <summary>Навигатор без ввода и часов боя: политика скрима от них не зависит.</summary>
        private static UiNavigator MakeNavigator()
        {
            var screensLayer = new VisualElement();
            var modalLayer = new VisualElement();
            var nav = new UiNavigator(input: null, clock: null);
            nav.Initialize(screensLayer, modalLayer, new UiScreenContext(screensLayer));
            return nav;
        }

        [Test]
        public void Одна_модалка_рисует_своё_затемнение()
        {
            UiNavigator nav = MakeNavigator();
            var modal = new FakeScreen(ScreenKind.Modal);
            nav.Push(modal);

            Assert.IsFalse(ScrimOf(modal).ClassListContains(ScrimlessClass),
                "единственная модалка обязана затемнять фон");
        }

        [Test]
        public void Две_модалки_дают_ОДНО_затемнение()
        {
            UiNavigator nav = MakeNavigator();
            var lower = new FakeScreen(ScreenKind.Modal);
            var upper = new FakeScreen(ScreenKind.Modal);
            nav.Push(lower);
            nav.Push(upper);

            Assert.IsFalse(ScrimOf(lower).ClassListContains(ScrimlessClass), "нижняя модалка держит скрим");
            Assert.IsTrue(ScrimOf(upper).ClassListContains(ScrimlessClass),
                "верхняя модалка не должна класть второе затемнение поверх первого");
        }

        [Test]
        public void Экран_с_SuppressScrim_не_затемняет_фон()
        {
            UiNavigator nav = MakeNavigator();
            var settings = new FakeScreen(ScreenKind.Modal, suppressScrim: true);
            nav.Push(settings);

            Assert.IsTrue(ScrimOf(settings).ClassListContains(ScrimlessClass),
                "настройки из главного меню подменяют панель — затемнять нечего");
        }

        [Test]
        public void Экран_без_затемнения_не_отменяет_затемнение_верхних()
        {
            // Иначе «прозрачная» модалка внизу молча лишала бы скрима всё, что легло выше неё.
            UiNavigator nav = MakeNavigator();
            var quiet = new FakeScreen(ScreenKind.Modal, suppressScrim: true);
            var normal = new FakeScreen(ScreenKind.Modal);
            nav.Push(quiet);
            nav.Push(normal);

            Assert.IsTrue(ScrimOf(quiet).ClassListContains(ScrimlessClass), "нижняя осталась без затемнения");
            Assert.IsFalse(ScrimOf(normal).ClassListContains(ScrimlessClass),
                "верхняя модалка обязана затемнить сама — под ней затемнения нет");
        }
    }
}
