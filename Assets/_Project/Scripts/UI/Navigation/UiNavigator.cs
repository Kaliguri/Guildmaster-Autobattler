using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Core.Input;
using Guildmaster.Data.Definitions;
using Guildmaster.Diagnostics;
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
    public sealed class UiNavigator : IDisposable
    {
        private readonly IInputService _input;
        private readonly IBattleClock _clock;
        // Звук открытия/закрытия экранов: одно место на всю игру. Может быть null (EditMode-тесты
        // конструируют навигатор без звука) — все вызовы через ?.
        private readonly UiSoundSystem _sound;
        private readonly List<UiScreen> _stack = new(); // [0] низ, [last] верх

        // Подписки на отмену живут ровно столько, сколько экран. Токен здесь — обычно токен ЗАБЕГА, то есть
        // он переживает десятки экранов: пока регистрацию не снять, отменённый callback держит и её, и сам
        // экран до конца акта (аудит 2026-07-26, R1-19/R1-61).
        private readonly Dictionary<UiScreen, CancellationTokenRegistration> _cancelHooks = new();

        private VisualElement _screensLayer;
        private VisualElement _modalLayer;
        private UiScreenContext _context;

        public UiNavigator(IInputService input, IBattleClock clock, UiSoundSystem sound = null)
        {
            _input = input;
            _clock = clock;
            _sound = sound;
            // K8 (план II.3): смена фазы боя → пересчёт глушения/контекста. Навигатор — ЕДИНСТВЕННЫЙ писатель
            // контекста; боевой слой больше не зовёт SetContext руками, только SetPhase → поднимает это событие.
            if (_clock != null) _clock.PhaseChanged += SyncInput;
        }

        /// <summary>Отписка от смены фазы (Singleton → зовёт VContainer при разрушении скоупа).</summary>
        public void Dispose()
        {
            if (_clock != null) _clock.PhaseChanged -= SyncInput;

            foreach (CancellationTokenRegistration registration in _cancelHooks.Values)
                registration.Dispose();
            _cancelHooks.Clear();
        }

        /// <summary>
        /// Отдать навигатору два слоя-контейнера (Ф4) и общий контекст сборки. Экран кладётся в слой ПО СВОЕМУ
        /// <see cref="ScreenKind"/>: <c>Modal</c> (pause/settings) → <paramref name="modalLayer"/> (над топбаром,
        /// fullscreen-scrim накрывает его — QA #36); <c>Page</c>/<c>Sheet</c> → <paramref name="screensLayer"/>
        /// (под топбаром). Снятие — через <c>RemoveFromHierarchy</c>, поэтому слой хранить у экрана не нужно.
        /// </summary>
        public void Initialize(VisualElement screensLayer, VisualElement modalLayer, UiScreenContext context)
        {
            _screensLayer = screensLayer;
            _modalLayer = modalLayer;
            _context = context;
        }

        // Слой для экрана по его типу: Modal — над топбаром, остальные — под ним (Ф4, план II.4).
        private VisualElement LayerFor(UiScreen screen)
            => screen.Kind == ScreenKind.Modal ? _modalLayer : _screensLayer;

        /// <summary>Верхний экран стека (null, если пусто).</summary>
        public UiScreen Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        /// <summary>
        /// Сколько подписок на отмену сейчас держит навигатор. Диагностика: число обязано ходить за стеком,
        /// а не расти на каждый показанный за забег экран. Смотрит тест и трейс.
        /// </summary>
        public int ActiveCancelHooks => _cancelHooks.Count;

        /// <summary>Открыт ли хоть один экран.</summary>
        public bool IsOpen => _stack.Count > 0;

        /// <summary>
        /// Тег режима для подсветки таба топбара — из верхнего НЕ-Modal экрана (QA #35, K4). Modal (pause/
        /// settings) НЕ меняет подсветку: открытое поверх карты системное меню оставляет активным «Карта»,
        /// а не сбрасывает в null (иначе подсветка «прыгала» на бой/карту при открытии ESC-меню).
        /// </summary>
        public string ActiveModeTag
        {
            get
            {
                for (int i = _stack.Count - 1; i >= 0; i--)
                    if (_stack[i].Kind != ScreenKind.Modal) return _stack[i].ModeTag;
                return null;
            }
        }

        /// <summary>
        /// Лежит ли на экране НЕПРОЗРАЧНАЯ страница (ивент, магазин, сундук, награда, исход, главное меню) —
        /// та, что закрывает собой мир и потому требует под собой задник (QA #50). Sheet (карта/инвентарь/
        /// тест-зона) и Modal (пауза/настройки) не считаются: сквозь них игрок смотрит на живой мир.
        /// Скрытые страницы (под другой страницей) не в счёт — важно, что видно СЕЙЧАС.
        /// </summary>
        public bool HasVisiblePage
        {
            get
            {
                for (int i = 0; i < _stack.Count; i++)
                {
                    UiScreen s = _stack[i];
                    if (s.Kind != ScreenKind.Page) continue;
                    if (s.Root == null || s.Root.style.display.value == DisplayStyle.Flex) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Просит ли ВИДИМЫЙ экран задник явно (<see cref="UiScreen.RequiresBackdrop"/>) — вторая, независимая
        /// причина показать стол помимо <see cref="HasVisiblePage"/>. Разведены намеренно: страница закрывает
        /// мир по своему типу, а этот запрос идёт от самого экрана и сильнее живого боя за спиной.
        /// </summary>
        public bool HasVisibleBackdropRequest
        {
            get
            {
                for (int i = 0; i < _stack.Count; i++)
                {
                    UiScreen s = _stack[i];
                    if (!s.RequiresBackdrop) continue;
                    if (s.Root == null || s.Root.style.display.value == DisplayStyle.Flex) return true;
                }
                return false;
            }
        }

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

            // Резолв мог случиться прямо в Build — главное меню, получившее готовый заказ (принятое
            // приглашение в кооп, dev-команда Ристалища), решает всё, ещё собираясь. Снятие в тот момент
            // прошло вхолостую: экрана в стеке не было. Положить его сюда сейчас значит оставить панель
            // лежать поверх мира до следующего PopAll — снять её будет уже нечем.
            if (screen.IsResolved) return;

            UiScreen prevTop = Top;
            _stack.Add(screen);
            LayerFor(screen)?.Add(screen.Root);

            prevTop?.OnBlur();
            screen.OnEnter();
            _sound?.PlayUi(screen.Kind == ScreenKind.Modal ? "modal_open" : "screen_open");

            SyncVisibility();
            SyncInput();
            FocusTop();
            Changed?.Invoke();
            UiTrace.Log($"nav.Push {Desc(screen)} → [{StackDesc()}] suppress={_input?.GameplaySuppressed}");

            // Токен, отменённый ДО показа, разбираем сами. Register в этом случае выполняет колбэк
            // синхронно — экран снялся бы раньше, чем Hook положит регистрацию в словарь, и запись
            // осталась бы там навсегда, держа ссылку на мёртвый экран.
            if (ct.IsCancellationRequested) { RemoveScreen(screen); return; }

            if (ct.CanBeCanceled)
                Hook(screen, ct.Register(() => RemoveScreen(screen))); // RemoveScreen идемпотентен (уже снят → no-op)
        }

        /// <summary>Снять верхний экран. Result-экран без выбора резолвится своим <c>DefaultResult</c>.</summary>
        public void Pop()
        {
            if (_stack.Count == 0) return;
            UiScreen top = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            Unhook(top);
            top.Root?.RemoveFromHierarchy();
            top.OnExit();
            _sound?.PlayUi("screen_close");

            SyncVisibility();
            SyncInput();
            Top?.OnFocus();
            FocusTop();
            Changed?.Invoke();

            UiTrace.Log($"nav.Pop {Desc(top)} → [{StackDesc()}] suppress={_input?.GameplaySuppressed}");

            // Резолв ПОСЛЕ снятия из стека и layer — гарантия «закрыть ДО колбэка» (II.5): продолжение
            // потребителя (напр. открыть следующий экран) выполняется, когда этот экран уже снят.
            top.ResolveDefaultIfPending();
        }

        /// <summary>
        /// Снять КОНКРЕТНЫЙ экран из любого места стека (не обязательно верхний) — для persistent-оверлеев вроде
        /// тест-зоны (Ф5): состояние гасит владелец (сессия), даже если поверх открыт инвентарь. Идемпотентно.
        /// </summary>
        public void Remove(UiScreen screen) => RemoveScreen(screen);

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
                Unhook(s);
                s.Root?.RemoveFromHierarchy();
                s.OnExit();
            }

            SyncVisibility();
            SyncInput();
            Changed?.Invoke();
            UiTrace.Log($"nav.PopAll ({snapshot.Length} снято) → [{StackDesc()}] suppress={_input?.GameplaySuppressed}");

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

            // См. Push: отменённый токен выполнил бы колбэк синхронно, до Hook, и регистрация осела бы
            // в словаре навсегда — счётчик ActiveCancelHooks рос бы за забег, вместо того чтобы ходить
            // за стеком.
            if (ct.IsCancellationRequested) { screen.ResolveDefaultIfPending(); return tcs.Task; }

            if (ct.CanBeCanceled)
                Hook(screen, ct.Register(() => screen.ResolveDefaultIfPending()));

            return tcs.Task;
        }

        /// <summary>Запомнить подписку на отмену за экраном, сняв прежнюю, если она была.</summary>
        private void Hook(UiScreen screen, CancellationTokenRegistration registration)
        {
            if (_cancelHooks.TryGetValue(screen, out CancellationTokenRegistration previous))
                previous.Dispose();

            _cancelHooks[screen] = registration;
        }

        /// <summary>Снять подписку экрана: он больше не существует, и токен не должен его удерживать.</summary>
        private void Unhook(UiScreen screen)
        {
            if (screen == null) return;
            if (!_cancelHooks.TryGetValue(screen, out CancellationTokenRegistration registration)) return;

            registration.Dispose();
            _cancelHooks.Remove(screen);
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
            // Только СВОЁ глушение: чужие источники (dev-консоль) держат его сами, и снимать его за
            // них навигатор не вправе — иначе набор команд протекал бы в геймплей.
            _input.SetSuppressed(Core.Input.InputSuppressSource.Ui, modal);

            if (modal) { _input.SetContext(InputContext.Menu); return; }

            // Карта акта — тоже «мир», но свой: её world-камера должна жить (пан/зум как в бою), боевых
            // действий нет. Фаза боя тут ничего не скажет (карта вне боя → BattlePhase.None → камера мертва),
            // поэтому контекст берём из верха стека по тегу режима. Это и есть заявленное «ввод = f(верх, фаза)».
            if (top != null && top.ModeTag == UiScreen.MapModeTag) { _input.SetContext(InputContext.Map); return; }

            _input.SetContext(WorldContextOf(_clock != null ? _clock.Phase : BattlePhase.None));
        }

        private static InputContext WorldContextOf(BattlePhase phase) => phase switch
        {
            BattlePhase.Deployment => InputContext.Deployment,
            // Бой и передышка между узлами — один контекст: мир на экране, камера должна жить (осмотреть поле,
            // досмотреть добивание, походить по арене). Боевые команды в Interlude исполнять некому — sim стоит.
            BattlePhase.Fighting or BattlePhase.Interlude => InputContext.Combat,
            _ => InputContext.None,
        };

        // Снять конкретный экран (идемпотентно: если уже снят — no-op). Через него резолв ShowAsync-экрана
        // снимает его вне зависимости от того, был он верхним или дорезолвлен из Pop/PopAll.
        private void RemoveScreen(UiScreen screen)
        {
            Unhook(screen);

            int idx = _stack.IndexOf(screen);
            if (idx < 0) { UiTrace.Log($"nav.Remove {Desc(screen)} — УЖЕ СНЯТ (no-op)"); return; }
            _stack.RemoveAt(idx);
            screen.Root?.RemoveFromHierarchy();
            screen.OnExit();

            SyncVisibility();
            SyncInput();
            FocusTop();
            Changed?.Invoke();
            UiTrace.Log($"nav.Remove {Desc(screen)} (был idx {idx}) → [{StackDesc()}] suppress={_input?.GameplaySuppressed}");
        }

        // Компактное описание экрана и стека для трейса (UiTrace). vis/hid = фактический display корня.
        private static string Desc(UiScreen s)
            => s == null ? "null" : $"{s.Kind}:{s.ModeTag ?? "-"}";

        private string StackDesc()
        {
            if (_stack.Count == 0) return "пусто";
            var sb = new StringBuilder();
            for (int i = 0; i < _stack.Count; i++)
            {
                UiScreen s = _stack[i];
                bool vis = s.Root != null && s.Root.style.display.value == DisplayStyle.Flex;
                sb.Append(Desc(s)).Append(vis ? ",vis" : ",hid");
                if (i < _stack.Count - 1) sb.Append(" | ");
            }
            return sb.ToString();
        }

        // Видимость по модели «карта ↔ геймплей — взаимоисключающие пространства» (решение Макса, Ф5).
        // Идём сверху вниз:
        //  - Page (карта/магазин/ивент — непрозрачный) скрывает ВСЁ под собой.
        //  - Sheet (инвентарь/тест-зона — «геймплей») прозрачен и виден сам, НО скрывает Page под собой:
        //    геймплей целиком закрывает карту (мир виден сквозь Sheet, карта не должна просвечивать). Соседние
        //    Sheet друг друга не прячут (оба — прозрачные окна в один мир).
        //  - Modal (пауза/настройки, scrim) не прячет ничего структурно — нижнее видно за затемнением.
        private void SyncVisibility()
        {
            bool pageAbove = false;  // выше есть непрозрачный Page → всё под ним скрыто
            bool sheetAbove = false; // выше есть Sheet (геймплей) → Page под ним скрыт (карта уходит)
            bool scrimBelow = false; // ниже уже есть видимый Modal → его затемнения достаточно
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                UiScreen s = _stack[i];
                bool hidden = pageAbove || (sheetAbove && s.Kind == ScreenKind.Page);
                if (s.Root != null)
                    s.Root.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;

                if (s.Kind == ScreenKind.Page) pageAbove = true;
                else if (s.Kind == ScreenKind.Sheet) sheetAbove = true;
            }

            // Затемнение — РОВНО ОДНО на стек. Каждый Modal несёт свой scrim в .gm-screen, и настройки
            // поверх паузы складывали два полупрозрачных слоя: фон темнел вдвое (наход. Макса, раунд 2).
            // Скрим оставляем самому НИЖНЕМУ видимому Modal — он лежит ближе к геймплею, который и надо
            // приглушить; всё, что выше, идёт без своего затемнения.
            for (int i = 0; i < _stack.Count; i++)
            {
                UiScreen s = _stack[i];
                if (s.Kind != ScreenKind.Modal || s.Root == null) continue;
                VisualElement scrim = ScrimOf(s.Root);
                if (scrim == null) continue;
                bool visible = s.Root.style.display.value == DisplayStyle.Flex;
                // SuppressScrim — намерение самого экрана («я подменяю панель, темнить нечего»).
                // Учитываем его здесь, потому что класс scrimless принадлежит этому методу: раньше
                // роутер вешал класс сам, и следующий же SyncVisibility его снимал.
                bool scrimless = scrimBelow || s.SuppressScrim;
                scrim.EnableInClassList(ScrimlessClass, scrimless);
                // Экран без своего затемнения не может служить «затемнением снизу» для тех, кто выше.
                if (visible && !scrimless) scrimBelow = true;
            }
        }

        /// <summary>
        /// Элемент, который РИСУЕТ затемнение экрана. Это не всегда сам Root: билдеры роутера отдают
        /// <c>TemplateContainer</c> от <c>CloneTree()</c>, а класс <c>.gm-screen</c> (и с ним скрим)
        /// висит на элементе ВНУТРИ. Вешать модификатор на контейнер бесполезно — затемнение остаётся.
        /// </summary>
        private static VisualElement ScrimOf(VisualElement root)
            => root.ClassListContains(ScreenClass) ? root : root.Q(className: ScreenClass);

        private const string ScreenClass = "gm-screen";

        /// <summary>Модалка без собственного затемнения: скрим уже даёт модалка под ней.</summary>
        private const string ScrimlessClass = "gm-screen--scrimless";

        private void FocusTop()
        {
            Top?.GetInitialFocus()?.Focus();
        }
    }
}
