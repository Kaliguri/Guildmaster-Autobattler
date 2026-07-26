using System;
using Guildmaster.Core.Input;
using Guildmaster.Core.Settings;
using Guildmaster.Data.Definitions;
using Guildmaster.UI.Components;
using MessagePipe;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// Единственный показыватель тултипов (план §II.10.5 п.1): слой <c>layer-tooltip</c>, задержка
    /// наведения, grace между соседними целями, кламп и флип у краёв, живой рефреш и глушение при драге.
    /// </summary>
    /// <remarks>
    /// Система одна на панель, а не «окно на каждый элемент»: два одновременно видимых тултипа —
    /// это всегда баг, и дешевле сделать его невозможным, чем ловить. Содержимое собирает
    /// <see cref="ITooltipContentFactory"/>; система про контент не знает ничего, кроме размера.
    /// </remarks>
    public sealed class TooltipSystem : IDisposable
    {
        /// <summary>Задержка перед показом, мс. Меньше — окна мельтешат при проводке курсора через грид.</summary>
        public const long DelayMs = 400;

        /// <summary>
        /// Окно уже было открыто и закрылось меньше этого времени назад — следующее показываем сразу.
        /// Без grace переход между соседними карточками грида выглядит как «подсказка отключилась».
        /// </summary>
        public const float GraceSeconds = 0.35f;

        /// <summary>Период живого рефреша (§II.10.5 п.5).</summary>
        public const long RefreshMs = 500;

        private readonly ITooltipContentFactory _factory;
        private readonly ISubscriber<RelicDragEvent> _relicDrag;
        private readonly IInputService _input;
        private readonly ISettingsService _settings;
        private readonly UiSoundSystem _sound;   // шелест подсказки; null в EditMode-тестах
        private IDisposable _dragSubscription;

        private VisualElement _root;
        private VisualElement _layer;
        private VisualElement _window;

        private IVisualElementScheduledItem _delay;
        private IVisualElementScheduledItem _refresh;

        private TooltipRequest _current;      // что показано (или показывается по таймеру)
        private VisualElement _anchor;        // относительно чего стоит окно
        private bool _visible;
        private bool _pinned;

        // История закреплённого окна (слой 3): переходы по терминам — это ОДНО окно с навигацией,
        // а не стопка окон. Логика вынесена в TooltipHistory — её можно проверить без панели.
        private readonly TooltipHistory _history = new();
        private VisualElement _chrome;        // шапка закреплённого окна: назад / вперёд / закрыть
        private float _hiddenAt = float.NegativeInfinity;
        private bool _detailed;

        public TooltipSystem(ITooltipContentFactory factory, ISubscriber<RelicDragEvent> relicDrag,
            IInputService input, ISettingsService settings, UiSoundSystem sound = null)
        {
            _factory   = factory;
            _relicDrag = relicDrag;
            _input     = input;
            _settings  = settings;
            _sound     = sound;

            if (_input != null) _input.DetailsHeldChanged += OnDetailsHeldChanged;
            if (_settings != null) _settings.Changed += SyncDetailed;
            SyncDetailed();
        }

        /// <summary>Тултип открыт (в т.ч. закреплён) — ESC гасит его раньше, чем откроет меню (§II.4).</summary>
        public bool IsVisible => _visible;

        /// <summary>
        /// Подробный режим: то же окно, больше разбора (§II.10.4). Считается из удержания Shift и галки
        /// «всегда подробно», открытое окно пересобирается на месте — иначе игрок жмёт Shift и не понимает,
        /// почему ничего не изменилось.
        /// </summary>
        public bool Detailed => _detailed;

        private void OnDetailsHeldChanged(bool _) => SyncDetailed();

        // Shift — ПЕРЕКЛЮЧАТЕЛЬ, а не включатель (§II.10.4): у кого стоит галка «всегда подробно»,
        // тому удержание временно возвращает краткий вид, иначе владелец галки теряет быстрый режим.
        private void SyncDetailed()
        {
            bool always = _settings != null && _settings.Gameplay.AlwaysDetailedTooltips;
            bool held   = _input != null && _input.DetailsHeld;
            bool value  = always ^ held;
            if (_detailed == value) return;
            _detailed = value;
            if (_visible) { Rebuild(); _sound?.PlayUi("tooltip_detail"); }
        }

        /// <summary>
        /// Тултипы заглушены (активный drag, §II.12.7). Текущее окно снимается: подсказка над рукой,
        /// которая тащит юнита, только мешает.
        /// </summary>
        public bool Suppressed { get; private set; }

        /// <summary>Привязать систему к панели: корень ловит всплывающие запросы, слой держит окно.</summary>
        public void Attach(VisualElement root, VisualElement layer)
        {
            Detach();
            _root  = root;
            _layer = layer;
            if (_root == null || _layer == null) return;

            _window = new VisualElement { name = "tooltip-window", pickingMode = PickingMode.Ignore };
            _window.AddToClassList("gm-tooltip");
            _window.style.position = Position.Absolute;
            _window.style.display = DisplayStyle.None;
            _layer.Add(_window);

            _root.RegisterCallback<TooltipShowEvent>(OnShowRequested);
            _root.RegisterCallback<TooltipHideEvent>(OnHideRequested);
            _root.RegisterCallback<TooltipPinEvent>(OnPinRequested);

            // Драг реликвии — единственный жест, который сейчас перекрывает hover (карточку тащат из грида
            // в мир). Драг юнита живёт в мире и над панелью не проходит, но глушитель общий: событий
            // станет больше, а правило «пока тащим — подсказок нет» останется одно.
            _dragSubscription = _relicDrag?.Subscribe(e => SetSuppressed(e.Phase != RelicDragPhase.Drop));
        }

        /// <summary>Отвязать от панели (смена UIDocument, выключение системы).</summary>
        public void Detach()
        {
            _dragSubscription?.Dispose();
            _dragSubscription = null;
            _delay?.Pause();
            _refresh?.Pause();
            _delay = null;
            _refresh = null;

            if (_root != null)
            {
                _root.UnregisterCallback<TooltipShowEvent>(OnShowRequested);
                _root.UnregisterCallback<TooltipHideEvent>(OnHideRequested);
                _root.UnregisterCallback<TooltipPinEvent>(OnPinRequested);
            }

            _history.Clear();

            _window?.RemoveFromHierarchy();
            _window = null;
            _root = null;
            _layer = null;
            _visible = false;
            _pinned = false;
            _anchor = null;
            _current = default;
        }

        /// <summary>Окно закреплено: живёт после ухода курсора, ссылки внутри него работают.</summary>
        public bool IsPinned => _pinned;

        /// <summary>
        /// Закрепить открытое окно на текущем содержимом (Alt-клик по якорю). Закреплённое окно
        /// переживает уход курсора, становится кликабельным и снимается только явно — крестиком или ESC.
        /// </summary>
        public void Pin()
        {
            if (!_visible || _pinned) return;
            _pinned = true;
            _history.Reset(_current);
            ApplyPinnedChrome();
            // Своего звука у закрепления пока нет: в FMOD заведены только tooltip_show/tooltip_detail,
            // и заводить пустую запись в каталоге ради ключа — обман (аудио-сторож её и ловит).
            // Переиспользуем «показ»; отдельные tooltip_pin/tooltip_nav — задача аудио-контура.
            _sound?.PlayUi("tooltip_show");
        }

        /// <summary>Снять закрепление; окно остаётся видимым до ухода курсора или явного скрытия.</summary>
        public void Unpin()
        {
            if (!_pinned) return;
            _pinned = false;
            _history.Clear();
            ApplyPinnedChrome();
        }

        /// <summary>
        /// Перейти к другому содержимому ВНУТРИ закреплённого окна (клик по термину в тексте).
        /// Переход пишется в историю, а не открывает второе окно (план §II.10.5, слой 3).
        /// </summary>
        public void Navigate(TooltipRequest request)
        {
            if (!_pinned || !_history.Push(request)) return;
            _current = _history.Current;
            Rebuild();
            _sound?.PlayUi("tooltip_show"); // см. Pin(): своего звука у перехода пока нет
        }

        /// <summary>Шаг назад по истории закреплённого окна.</summary>
        public bool GoBack()
        {
            if (!_pinned || !_history.GoBack()) return false;
            _current = _history.Current;
            Rebuild();
            return true;
        }

        /// <summary>Шаг вперёд по истории закреплённого окна.</summary>
        public bool GoForward()
        {
            if (!_pinned || !_history.GoForward()) return false;
            _current = _history.Current;
            Rebuild();
            return true;
        }

        /// <summary>Убрать окно чем бы оно ни держалось. <c>true</c> — было что убирать (для приоритета ESC).</summary>
        public bool HideAll()
        {
            if (!_visible) return false;
            _pinned = false;
            HideNow();
            return true;
        }

        public void Dispose()
        {
            if (_input != null) _input.DetailsHeldChanged -= OnDetailsHeldChanged;
            if (_settings != null) _settings.Changed -= SyncDetailed;
            Detach();
        }

        // --- Запросы от элементов ---

        private void OnShowRequested(TooltipShowEvent e)
        {
            // Пока окно закреплено, наведение на другие цели его НЕ подменяет: закрепление — это
            // «я читаю вот это», и увести содержимое случайным движением мыши было бы предательством.
            if (_pinned) return;
            Request(e.Request, e.Anchor);
        }

        private void OnHideRequested(TooltipHideEvent e)
        {
            if (_pinned) return;                                   // закреплённое окно уход курсора не гасит
            if (e.Anchor != null && e.Anchor != _anchor && _visible) return; // ушли не с той цели — не наше дело
            CancelPending();
            if (_visible) HideNow();
        }

        // Alt-клик по якорю закрепляет подсказку; клик по термину ВНУТРИ закреплённого окна — переход.
        // Различаем по источнику: пришло изнутри окна — навигация, снаружи — новое закрепление.
        private void OnPinRequested(TooltipPinEvent e)
        {
            if (_window == null || e.Request.IsEmpty) return;

            bool fromInsideWindow = e.Anchor != null && IsInsideWindow(e.Anchor);
            if (_pinned && fromInsideWindow)
            {
                Navigate(e.Request);
                return;
            }

            if (_pinned) return; // клик по постороннему якорю закреплённое окно не трогает

            CancelPending();
            _current = e.Request;
            _anchor  = e.Anchor;
            if (!_visible) ShowNow();
            else Rebuild();
            Pin();
        }

        private bool IsInsideWindow(VisualElement element)
        {
            for (VisualElement cur = element; cur != null; cur = cur.parent)
                if (cur == _window) return true;
            return false;
        }

        private void Request(TooltipRequest request, VisualElement anchor)
        {
            if (_window == null || request.IsEmpty || Suppressed) return;
            if (_visible && _anchor == anchor && _current.SameAs(request)) return; // уже показано это же

            _current = request;
            _anchor  = anchor;

            // Grace: подряд идущие наведения (проводка по гриду) не заставляют ждать задержку каждый раз.
            bool instant = _visible || Time.unscaledTime - _hiddenAt <= GraceSeconds;
            CancelPending();
            if (instant) ShowNow();
            else _delay = _layer.schedule.Execute(ShowNow).StartingIn(DelayMs);
        }

        private void CancelPending()
        {
            _delay?.Pause();
            _delay = null;
        }

        // --- Показ ---

        private void ShowNow()
        {
            CancelPending();
            if (_window == null || Suppressed) return;
            if (!Rebuild()) return;

            _visible = true;
            _sound?.PlayUi("tooltip_show");
            _window.style.display = DisplayStyle.Flex;
            // Размер окна известен только после раскладки, а ставить его «примерно» нельзя — именно у
            // краёв экрана ошибка и вылезает. Поэтому первый кадр окно прозрачно: считаем позицию по
            // фактическому размеру и лишь потом показываем (мигания на месте старой позиции нет).
            _window.style.opacity = 0f;
            _window.RegisterCallback<GeometryChangedEvent>(OnWindowLaidOut);

            if (_factory != null && _factory.IsLive(_current))
                _refresh = _layer.schedule.Execute(() => Rebuild()).Every(RefreshMs);
        }

        private void OnWindowLaidOut(GeometryChangedEvent _)
        {
            _window.UnregisterCallback<GeometryChangedEvent>(OnWindowLaidOut);
            Place();
            _window.style.opacity = 1f;
        }

        // Пересобрать содержимое под текущий запрос и режим детализации. false = показывать нечего.
        private bool Rebuild()
        {
            VisualElement content = _factory?.Build(_current, _detailed);
            if (content == null)
            {
                if (_visible) HideNow();
                return false;
            }

            _window.Clear();
            _chrome = null;
            if (_pinned) _window.Add(BuildChrome());
            _window.Add(content);

            // В закреплённом окне термины в тексте становятся живыми: по ним и ходит навигация.
            // В обычном окне этого не нужно — оно вообще не принимает курсор (pickingMode Ignore).
            if (_pinned && content is TooltipCard card)
            {
                card.pickingMode = PickingMode.Position;
                card.Description.pickingMode = PickingMode.Position;
                card.Description.WithKeywordTooltips();
            }

            // Ширину просит содержимое (стат-строки в узкой колонке нечитаемы), решает окно.
            _window.EnableInClassList("gm-tooltip--wide", content.ClassListContains(TooltipCard.WideHintClass));
            if (_visible) Place(); // живой рефреш мог сменить высоту — окно не должно свисать за край
            return true;
        }

        /// <summary>
        /// Шапка закреплённого окна: назад / вперёд / закрыть. Строит система, а не фабрика содержимого:
        /// это орган окна, и он обязан выглядеть одинаково, что бы внутри ни показывали.
        /// </summary>
        private VisualElement BuildChrome()
        {
            _chrome = new VisualElement { name = "tooltip-chrome" };
            _chrome.AddToClassList("gm-tooltip__chrome");

            var back = new Button(() => GoBack()) { text = "‹" };
            back.AddToClassList("gm-tooltip__nav-btn");
            back.SetEnabled(_history.CanGoBack);

            var forward = new Button(() => GoForward()) { text = "›" };
            forward.AddToClassList("gm-tooltip__nav-btn");
            forward.SetEnabled(_history.CanGoForward);

            var close = new Button(() => HideAll()) { text = "×" };
            close.AddToClassList("gm-tooltip__nav-btn");
            close.AddToClassList("gm-tooltip__nav-btn--close");

            _chrome.Add(back);
            _chrome.Add(forward);
            _chrome.Add(close);
            return _chrome;
        }

        // Закреплённое окно ЛОВИТ курсор (иначе по ссылкам внутри не кликнуть) и помечено классом,
        // чтобы отличаться от подсказки, которая уйдёт сама.
        private void ApplyPinnedChrome()
        {
            if (_window == null) return;
            _window.pickingMode = _pinned ? PickingMode.Position : PickingMode.Ignore;
            _window.EnableInClassList("gm-tooltip--pinned", _pinned);
            Rebuild();
        }

        private void Place()
        {
            if (_anchor == null || _window == null || _layer == null) return;

            Rect anchor = _anchor.worldBound;
            Rect panel  = _root.worldBound;
            var size = new Vector2(_window.resolvedStyle.width, _window.resolvedStyle.height);
            Vector2 pos = TooltipPlacement.Place(anchor, size, panel);

            // Считаем в координатах панели (там же живут worldBound якоря и границы экрана), а ставим
            // относительно слоя: слой сейчас лежит в нуле корня, но закладываться на это не нужно.
            Rect layerBound = _layer.worldBound;
            _window.style.left = pos.x - layerBound.x;
            _window.style.top  = pos.y - layerBound.y;
        }

        private void HideNow()
        {
            _refresh?.Pause();
            _refresh = null;
            _visible = false;
            _pinned = false;
            _history.Clear();
            _chrome = null;
            _hiddenAt = Time.unscaledTime;
            _anchor = null;
            _current = default;
            if (_window == null) return;
            _window.pickingMode = PickingMode.Ignore; // снятое окно снова прозрачно для курсора
            _window.RemoveFromClassList("gm-tooltip--pinned");
            _window.style.display = DisplayStyle.None;
            _window.Clear();
        }

        private void SetSuppressed(bool value)
        {
            Suppressed = value;
            if (!value) return;
            CancelPending();
            _pinned = false;
            if (_visible) HideNow();
        }
    }
}
