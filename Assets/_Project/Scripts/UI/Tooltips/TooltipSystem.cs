using System;
using System.Collections.Generic;
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
    /// Показыватель подсказок (план §II.10.5): слой <c>layer-tooltip</c>, задержка наведения, grace
    /// между соседними целями, кламп и флип у краёв, глушение при драге — и sticky-режим, в котором
    /// подсказка становится читаемой: по ней можно водить курсором и переходить по терминам.
    /// </summary>
    /// <remarks>
    /// <b>Sticky (решение Макса 2026-07-26, образец — Total War: Warhammer 3).</b> Подержал курсор —
    /// внизу окна заполняется полоса; заполнилась — окно «залипло»: принимает курсор, живёт после
    /// ухода с якоря, а наведение на термин внутри открывает СЛЕДУЮЩЕЕ окно. Одновременно живут
    /// не больше <see cref="TooltipChain{T}.Limit"/> окон.
    /// <para>Это не украшение: WCAG 1.4.13 требует от hover-контента быть <i>hoverable</i>
    /// (курсор может войти, содержимое не исчезает) и <i>dismissible</i> (ESC убирает без движения
    /// мыши). Окно, которое нельзя тронуть курсором, этому не удовлетворяет.</para>
    /// <para>Задержки НЕ складываются: полоса заполняется с момента появления окна, а не после него —
    /// иначе до интерактива проходила бы почти секунда, и жест ощущался бы как «не отзывается».</para>
    /// </remarks>
    public sealed class TooltipSystem : IDisposable
    {
        /// <summary>Сколько держать курсор, чтобы окно залипло (Baymard: рабочий диапазон 300–500 мс).</summary>
        public const float DwellSeconds = 0.5f;

        /// <summary>
        /// Сколько цепочка живёт после ухода курсора. Без этого окна гибли бы на перелёте между
        /// соседними — расстояние между ними курсор проходит не мгновенно.
        /// </summary>
        public const float ChainGraceSeconds = 0.4f;

        /// <summary>Период живого рефреша (§II.10.5 п.5).</summary>
        public const long RefreshMs = 500;

        private const long DwellTickMs = 16; // шаг заполнения полосы: кадр при 60 Гц

        private readonly ITooltipContentFactory _factory;
        private readonly ISubscriber<RelicDragEvent> _relicDrag;
        private readonly IInputService _input;
        private readonly ISettingsService _settings;
        private readonly UiSoundSystem _sound;   // шелест подсказки; null в EditMode-тестах
        private IDisposable _dragSubscription;

        private VisualElement _root;
        private VisualElement _layer;

        private IVisualElementScheduledItem _refresh;
        private IVisualElementScheduledItem _dwell;
        private IVisualElementScheduledItem _chainGrace;

        // Окна на экране. Первое — то, что открылось по наведению; остальные — переходы по терминам.
        private readonly TooltipChain<Window> _chain = new();

        private float _dwellStartedAt;
        private int _pointersInside;          // сколько окон цепочки сейчас под курсором
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

        /// <summary>Есть хоть одно открытое окно — ESC гасит его раньше, чем откроет меню (§II.4).</summary>
        public bool IsVisible => _chain.Count > 0;

        /// <summary>Первое окно залипло: цепочка в режиме чтения, курсор может ходить по окнам.</summary>
        public bool IsPinned => _chain.Count > 0 && _chain.Items[0].Sticky;

        /// <summary>Сколько окон открыто сейчас (для тестов и диагностики).</summary>
        public int OpenWindows => _chain.Count;

        /// <summary>
        /// Подробный режим: то же окно, больше разбора (§II.10.4). Считается из удержания Shift и галки
        /// «всегда подробно», открытые окна пересобираются на месте — иначе игрок жмёт Shift и не
        /// понимает, почему ничего не изменилось.
        /// </summary>
        public bool Detailed => _detailed;

        /// <summary>
        /// Подсказки заглушены (активный drag, §II.12.7). Цепочка снимается: окно над рукой,
        /// которая тащит юнита, только мешает.
        /// </summary>
        public bool Suppressed { get; private set; }

        /// <summary>Привязать систему к панели: корень ловит всплывающие запросы, слой держит окна.</summary>
        public void Attach(VisualElement root, VisualElement layer)
        {
            Detach();
            _root  = root;
            _layer = layer;
            if (_root == null || _layer == null) return;

            _root.RegisterCallback<TooltipShowEvent>(OnShowRequested);
            _root.RegisterCallback<TooltipHideEvent>(OnHideRequested);

            // Драг реликвии — жест, который перекрывает чтение: карточку тащат из грида в мир.
            _dragSubscription = _relicDrag?.Subscribe(e => SetSuppressed(e.Phase != RelicDragPhase.Drop));
        }

        /// <summary>Отвязать от панели (смена UIDocument, выключение системы).</summary>
        public void Detach()
        {
            _dragSubscription?.Dispose();
            _dragSubscription = null;
            StopTimers();

            if (_root != null)
            {
                _root.UnregisterCallback<TooltipShowEvent>(OnShowRequested);
                _root.UnregisterCallback<TooltipHideEvent>(OnHideRequested);
            }

            foreach (Window w in _chain.DrainAll()) w.Root.RemoveFromHierarchy();
            _root = null;
            _layer = null;
            _pointersInside = 0;
        }

        /// <summary>
        /// Убрать всё, чем бы оно ни держалось. <c>true</c> — было что убирать (приоритет ESC, §II.4;
        /// заодно это <i>dismissible</i> из WCAG 1.4.13: закрытие без движения мыши).
        /// </summary>
        public bool HideAll()
        {
            if (_chain.Count == 0) return false;
            CloseChain();
            return true;
        }

        public void Dispose()
        {
            if (_input != null) _input.DetailsHeldChanged -= OnDetailsHeldChanged;
            if (_settings != null) _settings.Changed -= SyncDetailed;
            Detach();
        }

        // --- Режим детализации ---

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
            if (_chain.Count == 0) return;

            // Перебираем СНИМОК: FillWindow, получив пустой контент, убирает окно из цепочки — то есть
            // правит коллекцию прямо во время обхода. Сегодняшняя фабрика контента от «подробности»
            // не зависит и потому null не отдаёт, но интерфейс это допускает, и тогда Shift над
            // открытым окном ронял бы исключение прямо в обработчике ввода.
            foreach (Window w in new List<Window>(_chain.Items)) FillWindow(w);
            _sound?.PlayUi("tooltip_detail");
        }

        // --- Запросы от элементов ---

        private void OnShowRequested(TooltipShowEvent e)
        {
            if (_layer == null || e.Request.IsEmpty || Suppressed) return;

            Window source = WindowOf(e.Anchor);
            if (source != null)
            {
                // Наведение ВНУТРИ залипшего окна — это переход по термину, а не новая подсказка.
                if (source.Sticky) OpenNext(e.Request, source);
                return;
            }

            // Пока цепочка в режиме чтения, наведение снаружи её не подменяет: увести то, что человек
            // читает, случайным движением мыши — худшее, что может сделать подсказка.
            if (IsPinned) return;

            Request(e.Request, e.Anchor);
        }

        private void OnHideRequested(TooltipHideEvent e)
        {
            if (WindowOf(e.Anchor) != null) return; // уход внутри окна — не повод закрываться

            if (IsPinned)
            {
                // Курсор ушёл с якоря, но цепочка залипла — она держится, пока курсор не покинет ВСЁ.
                if (_pointersInside == 0) StartChainGrace();
                return;
            }

            if (_chain.Count > 0) CloseChain();
        }

        // Показ БЕЗ задержки (решение Макса 2026-07-26): навёлся — окно уже здесь. Классическая
        // hover-задержка защищает от мельтешения при проводке курсора через грид, но у нас окно и так
        // сменяется целиком на новое наведение, а ожидание перед каждым чтением дороже, чем мигание.
        private void Request(TooltipRequest request, VisualElement anchor)
        {
            if (_layer == null || Suppressed || request.IsEmpty) return;

            Window first = _chain.Count > 0 ? _chain.Items[0] : null;
            if (first != null && first.Anchor == anchor && first.Request.SameAs(request)) return;

            if (_chain.Count > 0) CloseChain(); // одна подсказка за раз, пока цепочка не залипла

            var window = new Window { Request = request, Anchor = anchor };
            if (!CreateWindow(window)) return;

            _chain.Add(window, out _);
            PlaceByAnchor(window, anchor);
            StartDwell(window);
            _sound?.PlayUi("tooltip_show");

            if (_factory != null && _factory.IsLive(window.Request))
                _refresh = _layer.schedule.Execute(() => FillWindow(window)).Every(RefreshMs);
        }

        /// <summary>
        /// Открыть следующее окно цепочки — переход по термину внутри залипшего окна.
        /// Позиционируется относительно окна-источника, а не курсора: цепочка должна читаться лесенкой.
        /// </summary>
        private void OpenNext(TooltipRequest request, Window source)
        {
            foreach (Window w in _chain.Items)
                if (w.Request.SameAs(request)) return; // это окно уже открыто — не плодим дубль

            var window = new Window { Request = request, Anchor = source.Root, Sticky = true };
            if (!CreateWindow(window)) return;

            Window evicted = _chain.Add(window, out bool wasEvicted);
            if (wasEvicted && evicted != null) DestroyWindow(evicted);

            // ApplySticky здесь НЕ зовём: окно создано с Sticky = true, и CreateWindow уже применил
            // залипание. Повтор не бесплатен — внутри WithKeywordTooltips, а он безусловно вешает пару
            // колбэков на ссылки термина, и каждое наведение слало бы по два запроса подсказки.
            PlaceByAnchor(window, source.Root);
            _sound?.PlayUi("tooltip_show");
        }

        // --- Жизнь окна ---

        private bool CreateWindow(Window window)
        {
            var root = new VisualElement { name = "tooltip-window", pickingMode = PickingMode.Ignore };
            root.AddToClassList("gm-tooltip");
            root.style.position = Position.Absolute;
            root.style.opacity = 0f; // до первой раскладки позиция неизвестна — не показываем «прыжок»

            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList("gm-tooltip__body");
            root.Add(body);

            window.Root = root;
            window.Body = body;

            if (!FillWindow(window)) return false;

            _layer.Add(root);
            root.RegisterCallback<GeometryChangedEvent>(window.OnLaidOut = _ =>
            {
                root.UnregisterCallback<GeometryChangedEvent>(window.OnLaidOut);
                PlaceByAnchor(window, window.Anchor);
                root.style.opacity = 1f;
            });
            root.RegisterCallback<PointerEnterEvent>(_ => OnWindowEnter());
            root.RegisterCallback<PointerLeaveEvent>(_ => OnWindowLeave());
            return true;
        }

        // Наполнить окно содержимым под текущий режим. false = показывать нечего.
        private bool FillWindow(Window window)
        {
            VisualElement content = _factory?.Build(window.Request, _detailed);
            if (content == null)
            {
                if (_chain.Contains(window)) { _chain.Remove(window); DestroyWindow(window); }
                return false;
            }

            window.Body.Clear();
            window.Body.Add(content);
            window.Content = content;
            window.Root.EnableInClassList("gm-tooltip--wide", content.ClassListContains(TooltipCard.WideHintClass));

            // Полоса есть у ЛЮБОГО окна (реш. Макса: консистентность). Залипание нужно не только ради
            // переходов: длинный текст тоже хочется перечитать, не боясь, что окно исчезнет.
            EnsureDwellBar(window, !window.Sticky);
            if (window.Sticky) ApplySticky(window);
            return true;
        }

        private void DestroyWindow(Window window)
        {
            window.Root?.RemoveFromHierarchy();
        }

        private void CloseChain()
        {
            StopTimers();
            foreach (Window w in _chain.DrainAll()) DestroyWindow(w);
            _pointersInside = 0;
        }

        // --- Sticky: полоса ожидания и залипание ---

        private void StartDwell(Window window)
        {
            _dwell?.Pause();
            if (window.DwellFill == null) return;

            _dwellStartedAt = Time.unscaledTime;
            _dwell = _layer.schedule.Execute(() =>
            {
                float t = (Time.unscaledTime - _dwellStartedAt) / DwellSeconds;
                if (t >= 1f)
                {
                    _dwell?.Pause();
                    _dwell = null;
                    MakeSticky(window);
                    return;
                }
                window.DwellFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(t) * 100f));
            }).Every(DwellTickMs);
        }

        private void MakeSticky(Window window)
        {
            if (window.Sticky) return;
            window.Sticky = true;
            EnsureDwellBar(window, false); // полоса своё отработала, место возвращаем содержимому
            ApplySticky(window);
            _sound?.PlayUi("tooltip_show");
        }

        // Залипшее окно ЛОВИТ курсор (без этого по ссылкам внутри не навестись — и не выполняется
        // требование «hoverable» из WCAG 1.4.13) и помечено классом, чтобы отличаться от подсказки,
        // которая уйдёт сама.
        private void ApplySticky(Window window)
        {
            window.Root.pickingMode = PickingMode.Position;
            window.Root.AddToClassList("gm-tooltip--sticky");
            if (window.Content is TooltipCard card)
            {
                card.pickingMode = PickingMode.Position;
                card.Description.pickingMode = PickingMode.Position;
                card.Description.WithKeywordTooltips();
            }
        }

        // Полоса ожидания живёт ВНИЗУ окна и занимает своё место в раскладке: появись она поверх
        // текста, последняя строка читалась бы сквозь неё.
        private void EnsureDwellBar(Window window, bool needed)
        {
            if (!needed)
            {
                window.DwellBar?.RemoveFromHierarchy();
                window.DwellBar = null;
                window.DwellFill = null;
                return;
            }
            if (window.DwellBar != null)
            {
                window.DwellBar.BringToFront();
                return;
            }

            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("gm-tooltip__dwell");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("gm-tooltip__dwell-fill");
            fill.style.width = new StyleLength(Length.Percent(0f));
            bar.Add(fill);
            window.Root.Add(bar);

            window.DwellBar = bar;
            window.DwellFill = fill;
        }

        // --- Курсор в цепочке ---

        private void OnWindowEnter()
        {
            _pointersInside++;
            _chainGrace?.Pause();
            _chainGrace = null;
        }

        private void OnWindowLeave()
        {
            if (_pointersInside > 0) _pointersInside--;
            if (_pointersInside == 0 && IsPinned) StartChainGrace();
        }

        private void StartChainGrace()
        {
            _chainGrace?.Pause();
            _chainGrace = _layer.schedule.Execute(() =>
            {
                _chainGrace?.Pause();
                _chainGrace = null;
                if (_pointersInside == 0) CloseChain();
            }).StartingIn((long)(ChainGraceSeconds * 1000f));
        }

        // --- Позиция ---

        private void PlaceByAnchor(Window window, VisualElement anchor)
        {
            if (anchor == null || _layer == null || window.Root == null) return;

            Rect anchorBound = anchor.worldBound;
            Rect panel = _root.worldBound;
            var size = new Vector2(window.Root.resolvedStyle.width, window.Root.resolvedStyle.height);
            Vector2 pos = TooltipPlacement.Place(anchorBound, size, panel);

            // Считаем в координатах панели (там же живут worldBound якоря и границы экрана), а ставим
            // относительно слоя: слой сейчас лежит в нуле корня, но закладываться на это не нужно.
            Rect layerBound = _layer.worldBound;
            window.Root.style.left = pos.x - layerBound.x;
            window.Root.style.top  = pos.y - layerBound.y;
        }

        // --- Служебное ---

        private Window WindowOf(VisualElement element)
        {
            if (element == null) return null;
            foreach (Window w in _chain.Items)
            {
                for (VisualElement cur = element; cur != null; cur = cur.parent)
                    if (cur == w.Root) return w;
            }
            return null;
        }

        private void StopTimers()
        {
            _refresh?.Pause();
            _dwell?.Pause();
            _chainGrace?.Pause();
            _refresh = null;
            _dwell = null;
            _chainGrace = null;
        }

        private void SetSuppressed(bool value)
        {
            Suppressed = value;
            if (!value) return;
            if (_chain.Count > 0) CloseChain();
        }

        /// <summary>Одно окно цепочки: корень, тело, полоса ожидания и состояние залипания.</summary>
        private sealed class Window
        {
            public VisualElement Root;
            public VisualElement Body;
            public VisualElement Content;
            public VisualElement DwellBar;
            public VisualElement DwellFill;
            public TooltipRequest Request;
            public VisualElement Anchor;
            public bool Sticky;
            public EventCallback<GeometryChangedEvent> OnLaidOut;
        }
    }
}
