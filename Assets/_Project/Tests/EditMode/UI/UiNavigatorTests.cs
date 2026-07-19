using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Core.Input;
using Guildmaster.Data.Definitions;
using Guildmaster.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Навигатор экранов (план UI-реворка Ф1): порядок push/pop, видимость по типу (Page прячет низ,
    /// Modal/Sheet — нет), глушение/контекст ввода = f(верх стека, фаза боя) БЕЗ снапшота, ShowAsync с
    /// ровно одним резолвом (явный / дефолт при PopAll / повтор игнорируется) и регресс реэнтрантности #22.
    /// Всё — чистая логика без сцены и панели.
    /// </summary>
    public sealed class UiNavigatorTests
    {
        // --- Фейки зависимостей ---

        private sealed class FakeInput : IInputService
        {
            public InputContext Context { get; private set; } = InputContext.None;
            public void SetContext(InputContext context) => Context = context;
            public bool GameplaySuppressed { get; set; }

            public Vector2 CameraPan => default;
            public float CameraZoomDelta => 0f;
            public Vector2 CameraPanDrag => default;
            public bool PointerOverUI => false;
            public Vector2 PointerScreenPosition => default;
            public bool PointerHeld => false;

#pragma warning disable 67 // события интерфейса не поднимаются в тесте — реализуем как заглушки
            public event Action CycleViewRequested;
            public event Action PointerPressed;
            public event Action PointerReleased;
            public event Action PauseToggleRequested;
            public event Action GameSpeedCycleRequested;
            public event Action MenuToggleRequested;
#pragma warning restore 67
        }

        private sealed class FakeClock : IBattleClock
        {
            public BattlePhase Phase { get; set; } = BattlePhase.None;
            public float ElapsedSeconds => 0f;
            public void RequestStart() { }
        }

        private sealed class TestScreen : UiScreen
        {
            public override ScreenKind Kind { get; }
            public TestScreen(ScreenKind kind) => Kind = kind;
            public override void Build(UiScreenContext ctx) => Root = new VisualElement();
        }

        private sealed class TestResultScreen : UiScreen<int>
        {
            public override ScreenKind Kind => ScreenKind.Page;
            public override int DefaultResult => -1;
            public override void Build(UiScreenContext ctx) => Root = new VisualElement();
            public void Choose(int value) => Resolve(value);
        }

        private static UiNavigator NewNav(out FakeInput input, out FakeClock clock)
        {
            input = new FakeInput();
            clock = new FakeClock();
            var nav = new UiNavigator(input, clock);
            var layer = new VisualElement();
            nav.Initialize(layer, new UiScreenContext(layer));
            return nav;
        }

        private static bool IsVisible(UiScreen s) => s.Root.style.display.value == DisplayStyle.Flex;

        // --- Порядок стека ---

        [Test]
        public void Push_SetsTop_Pop_RestoresPrevious()
        {
            var nav = NewNav(out _, out _);
            var a = new TestScreen(ScreenKind.Page);
            var b = new TestScreen(ScreenKind.Modal);

            nav.Push(a);
            Assert.AreSame(a, nav.Top);
            Assert.IsTrue(nav.IsOpen);

            nav.Push(b);
            Assert.AreSame(b, nav.Top);

            nav.Pop();
            Assert.AreSame(a, nav.Top);

            nav.Pop();
            Assert.IsNull(nav.Top);
            Assert.IsFalse(nav.IsOpen);
        }

        // --- Видимость по типу ---

        [Test]
        public void Page_HidesScreenBelow()
        {
            var nav = NewNav(out _, out _);
            var below = new TestScreen(ScreenKind.Page);
            var page = new TestScreen(ScreenKind.Page);

            nav.Push(below);
            nav.Push(page);

            Assert.IsFalse(IsVisible(below), "Page должен прятать экран под собой");
            Assert.IsTrue(IsVisible(page));
        }

        [Test]
        public void Modal_And_Sheet_KeepScreenBelowVisible()
        {
            var nav = NewNav(out _, out _);
            var below = new TestScreen(ScreenKind.Page);
            var modal = new TestScreen(ScreenKind.Modal);

            nav.Push(below);
            nav.Push(modal);
            Assert.IsTrue(IsVisible(below), "Modal НЕ прячет нижний");

            var sheet = new TestScreen(ScreenKind.Sheet);
            nav.Push(sheet);
            Assert.IsTrue(IsVisible(below), "Sheet НЕ прячет нижний");
            Assert.IsTrue(IsVisible(modal));
        }

        // --- Ввод = f(верх, фаза) ---

        [Test]
        public void Sheet_OverDeployment_LeavesInputToWorld()
        {
            var nav = NewNav(out FakeInput input, out FakeClock clock);
            clock.Phase = BattlePhase.Deployment;

            nav.Push(new TestScreen(ScreenKind.Sheet)); // инвентарь/тест-зона поверх расстановки

            Assert.IsFalse(input.GameplaySuppressed, "Sheet не глушит геймплей");
            Assert.AreEqual(InputContext.Deployment, input.Context);
        }

        [Test]
        public void Modal_OverSheet_SuppressesToMenu()
        {
            var nav = NewNav(out FakeInput input, out FakeClock clock);
            clock.Phase = BattlePhase.Deployment;

            nav.Push(new TestScreen(ScreenKind.Sheet));
            nav.Push(new TestScreen(ScreenKind.Modal)); // пауза поверх инвентаря

            Assert.IsTrue(input.GameplaySuppressed, "Modal глушит геймплей");
            Assert.AreEqual(InputContext.Menu, input.Context);
        }

        [Test]
        public void PopModal_WhilePhaseChangedToFighting_RecomputesFromPhase_NotSnapshot()
        {
            var nav = NewNav(out FakeInput input, out FakeClock clock);
            clock.Phase = BattlePhase.Deployment;

            nav.Push(new TestScreen(ScreenKind.Sheet));
            nav.Push(new TestScreen(ScreenKind.Modal));

            // Фаза сменилась, ПОКА модалка была открыта — снапшот "Deployment" протух бы.
            clock.Phase = BattlePhase.Fighting;
            nav.Pop(); // модалка снята → верх Sheet → контекст пересчитывается из ТЕКУЩЕЙ фазы

            Assert.IsFalse(input.GameplaySuppressed);
            Assert.AreEqual(InputContext.Combat, input.Context, "Контекст = пересчёт из фазы, а не снапшот");
        }

        [Test]
        public void EmptyStack_InputFollowsPhase()
        {
            var nav = NewNav(out FakeInput input, out FakeClock clock);
            clock.Phase = BattlePhase.Fighting;
            nav.SyncInput(); // внешний вызов (бутстрап на смену фазы)

            Assert.IsFalse(input.GameplaySuppressed);
            Assert.AreEqual(InputContext.Combat, input.Context);
        }

        // --- ShowAsync: ровно один резолв ---

        [Test]
        public void ShowAsync_ExplicitResolve_ReturnsValue_AndRemovesScreen()
        {
            var nav = NewNav(out _, out _);
            var screen = new TestResultScreen();

            UniTask<int> task = nav.ShowAsync(screen);
            Assert.AreEqual(UniTaskStatus.Pending, task.Status);

            screen.Choose(42);

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
            Assert.IsFalse(nav.IsOpen, "экран снят на резолве");
        }

        [Test]
        public void ShowAsync_PopAll_ResolvesDefault()
        {
            var nav = NewNav(out _, out _);
            var screen = new TestResultScreen();

            UniTask<int> task = nav.ShowAsync(screen);
            nav.PopAll();

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status);
            Assert.AreEqual(-1, task.GetAwaiter().GetResult(), "снятие без выбора = DefaultResult");
        }

        [Test]
        public void ShowAsync_PopResolvesDefault()
        {
            var nav = NewNav(out _, out _);
            var screen = new TestResultScreen();

            UniTask<int> task = nav.ShowAsync(screen);
            nav.Pop();

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status);
            Assert.AreEqual(-1, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Resolve_SecondCall_Ignored()
        {
            var nav = NewNav(out _, out _);
            var screen = new TestResultScreen();

            UniTask<int> task = nav.ShowAsync(screen);
            screen.Choose(7);
            screen.Choose(99); // повтор — игнорируется

            Assert.AreEqual(7, task.GetAwaiter().GetResult());
        }

        // --- Регресс #22: реэнтрантный Push из резолва при PopAll не ломает обход ---

        [Test]
        public void PopAll_ReentrantPushFromResolve_Survives()
        {
            var nav = NewNav(out _, out _);
            var flow = new TestResultScreen();
            var next = new TestScreen(ScreenKind.Page);

            // Продолжение флоу открывает новый экран прямо в резолве — как петля акта после выбора узла.
            nav.ShowAsync(flow).ContinueWith(_ => nav.Push(next)).Forget();
            nav.Push(new TestScreen(ScreenKind.Page)); // ещё один экран поверх — снапшот > 1

            nav.PopAll(); // резолвит flow дефолтом → продолжение пушит next в уже пустой стек

            Assert.AreSame(next, nav.Top, "экран, открытый из резолва, пережил PopAll");
            Assert.IsTrue(nav.IsOpen);
        }
    }
}
