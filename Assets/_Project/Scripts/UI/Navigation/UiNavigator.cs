using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Core.Input;
using Guildmaster.Data.Definitions;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Единственный владелец видимости экранов и геймплейного ввода (см. план UI-реворка II.2–II.5).
    /// Стек типизированных <see cref="UiScreen"/> в слое-контейнере; видимость нижних и глушение ввода
    /// НЕ мутируются вручную, а ВЫЧИСЛЯЮТСЯ из (верх стека, фаза боя). Заменяет ручную синхронизацию
    /// <c>_menuModeActive</c>/<c>_prevContext</c>/CSS-классов-флагов в старом роутере.
    /// <para>POCO: зависимости через конструктор, тестируется в EditMode без сцены. Слой экранов и контекст
    /// сборки отдаются в <see cref="Initialize"/> (бутстрапом/роутером).</para>
    /// </summary>
    public sealed class UiNavigator
    {
        private readonly IInputService _input;
        private readonly IBattleClock _clock;
        private readonly List<UiScreen> _stack = new(); // [0] низ, [last] верх

        private VisualElement _screensLayer;
        private UiScreenContext _context;

        public UiNavigator(IInputService input, IBattleClock clock)
        {
            _input = input;
            _clock = clock;
        }

        /// <summary>Отдать навигатору слой экранов (куда добавляются корни) и общий контекст сборки.</summary>
        public void Initialize(VisualElement screensLayer, UiScreenContext context)
        {
            _screensLayer = screensLayer;
            _context = context;
        }

        /// <summary>Верхний экран стека (null, если пусто).</summary>
        public UiScreen Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        /// <summary>Открыт ли хоть один экран.</summary>
        public bool IsOpen => _stack.Count > 0;

        /// <summary>Тег режима верхнего экрана — единый источник подсветки таба топбара.</summary>
        public string ActiveModeTag => Top?.ModeTag;

        /// <summary>Есть ли в стеке экран, удовлетворяющий предикату (напр. «системное меню где-то открыто»).</summary>
        public bool AnyScreen(Func<UiScreen, bool> predicate)
        {
            for (int i = 0; i < _stack.Count; i++)
                if (predicate(_stack[i])) return true;
            return false;
        }

        /// <summary>Меняется на каждый Push/Pop/резолв — топбар/backdrop подписываются вместо поллинга структуры.</summary>
        public event Action Changed;

        /// <summary>
        /// Добавить экран поверх стека. <paramref name="ct"/> (напр. токен забега, QA #37) снимает экран через
        /// навигатор при отмене — единый механизм для ПРОСТЫХ экранов (ивент/loadout). Result-экраны идут через
        /// <see cref="ShowAsync{TResult}"/> (там свой ct-путь: отмена резолвит <c>DefaultResult</c>).
        /// </summary>
        public void Push(UiScreen screen, CancellationToken ct = default)
        {
            if (screen == null) return;
            if (screen.Root == null) screen.Build(_context);

            UiScreen prevTop = Top;
            _stack.Add(screen);
            _screensLayer?.Add(screen.Root);

            prevTop?.OnBlur();
            screen.OnEnter();

            SyncVisibility();
            SyncInput();
            FocusTop();
            Changed?.Invoke();

            if (ct.CanBeCanceled)
                ct.Register(() => RemoveScreen(screen)); // RemoveScreen идемпотентен (уже снят → no-op)
        }

        /// <summary>Снять верхний экран. Result-экран без выбора резолвится своим <c>DefaultResult</c>.</summary>
        public void Pop()
        {
            if (_stack.Count == 0) return;
            UiScreen top = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            _screensLayer?.Remove(top.Root);
            top.OnExit();

            SyncVisibility();
            SyncInput();
            Top?.OnFocus();
            FocusTop();
            Changed?.Invoke();

            // Резолв ПОСЛЕ снятия из стека и layer — гарантия «закрыть ДО колбэка» (II.5): продолжение
            // потребителя (напр. открыть следующий экран) выполняется, когда этот экран уже снят.
            top.ResolveDefaultIfPending();
        }

        /// <summary>Снять все экраны (бывший CloseAll). Result-экраны резолвятся дефолтом.</summary>
        public void PopAll()
        {
            if (_stack.Count == 0) { SyncInput(); return; }

            // Снапшот + очистка ДО резолвов (регресс #22): резолв result-экрана может синхронно открыть
            // новый экран (продолжение флоу) — он попадёт в уже пустой стек и переживёт обход, а не будет
            // снесён тем же циклом.
            UiScreen[] snapshot = _stack.ToArray();
            _stack.Clear();
            foreach (UiScreen s in snapshot)
            {
                _screensLayer?.Remove(s.Root);
                s.OnExit();
            }

            SyncVisibility();
            SyncInput();
            Changed?.Invoke();

            foreach (UiScreen s in snapshot) s.ResolveDefaultIfPending();
        }

        /// <summary>
        /// Показать result-экран и дождаться его резолва. Ровно один резолв гарантирован: явный
        /// (<c>Resolve</c>), снятие без выбора (Pop/PopAll → <see cref="UiScreen{TResult}.DefaultResult"/>)
        /// или отмена по <paramref name="ct"/>. Экран снимается ДО отдачи результата (II.5).
        /// </summary>
        public UniTask<TResult> ShowAsync<TResult>(UiScreen<TResult> screen, CancellationToken ct = default)
        {
            var tcs = new UniTaskCompletionSource<TResult>();

            screen.BindResolver(result =>
            {
                RemoveScreen(screen);   // снять из стека+layer ДО отдачи результата
                tcs.TrySetResult(result);
            });

            Push(screen);

            if (ct.CanBeCanceled)
                ct.Register(() => screen.ResolveDefaultIfPending());

            return tcs.Task;
        }

        /// <summary>
        /// Пересчитать глушение ввода и контекст из (верх стека, фаза боя) — II.3. Вызывается навигатором на
        /// каждое изменение стека И извне (бутстрапом) при смене <see cref="BattlePhase"/>: восстановление =
        /// пересчёт из фазы, а не снапшот (снапшот гнил бы, если фаза сменилась при открытом меню).
        /// </summary>
        public void SyncInput()
        {
            if (_input == null) return;
            UiScreen top = Top;
            bool modal = top != null && top.Kind != ScreenKind.Sheet;
            _input.GameplaySuppressed = modal;
            _input.SetContext(modal ? InputContext.Menu : WorldContextOf(_clock != null ? _clock.Phase : BattlePhase.None));
        }

        private static InputContext WorldContextOf(BattlePhase phase) => phase switch
        {
            BattlePhase.Deployment => InputContext.Deployment,
            BattlePhase.Fighting => InputContext.Combat,
            _ => InputContext.None,
        };

        // Снять конкретный экран (идемпотентно: если уже снят — no-op). Через него резолв ShowAsync-экрана
        // снимает его вне зависимости от того, был он верхним или дорезолвлен из Pop/PopAll.
        private void RemoveScreen(UiScreen screen)
        {
            int idx = _stack.IndexOf(screen);
            if (idx < 0) return; // уже снят (напр. Pop уже удалил, а мы пришли из ResolveDefaultIfPending)
            _stack.RemoveAt(idx);
            _screensLayer?.Remove(screen.Root);
            screen.OnExit();

            SyncVisibility();
            SyncInput();
            FocusTop();
            Changed?.Invoke();
        }

        // Видимость: экран виден, пока над ним нет непрозрачного Page. Идём сверху вниз — как только прошли
        // Page, всё под ним скрыто. Modal/Sheet прозрачны/со scrim → нижние остаются отрисованными.
        private void SyncVisibility()
        {
            bool hiddenBelow = false;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                UiScreen s = _stack[i];
                if (s.Root != null)
                    s.Root.style.display = hiddenBelow ? DisplayStyle.None : DisplayStyle.Flex;
                if (s.Kind == ScreenKind.Page) hiddenBelow = true;
            }
        }

        private void FocusTop()
        {
            Top?.GetInitialFocus()?.Focus();
        }
    }
}
