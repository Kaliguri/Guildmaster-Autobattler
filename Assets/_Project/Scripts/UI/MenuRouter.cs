using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Core.Flow;
using Guildmaster.Core.Input;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Diagnostics;
using Guildmaster.Guild;
using MessagePipe;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Строит и проводит экраны забега из UXML-шаблонов, а стек, видимость и
    /// глушение геймплейного ввода делегирует <see cref="UiNavigator"/> (UI-реворк Ф2). Каждый экран получает
    /// честный <see cref="ScreenKind"/> — по нему навигатор ВЫЧИСЛЯЕТ видимость нижних и suppress, вместо
    /// прежней ручной синхронизации (<c>_menuModeActive</c>/<c>_prevContext</c>/CSS-классов-флагов).
    /// Настройки применяются живьём; Cancel/Save — на кнопках, ESC = навигация назад.
    /// </summary>
    public sealed class MenuRouter
    {
        private readonly IInputService _input;
        private readonly UiNavigator _nav;
        private readonly SettingsViewModel _settingsVm;
        private readonly LoadoutViewModel _loadoutVm;
        private readonly LoadoutHubViewModel _hubVm;
        private readonly ILocalizationService _loc;
        private readonly IRunControl _runControl; // QA #18: «В главное меню»/«Выход» из системного меню

        private VisualElement _root;
        private VisualTreeAsset _pauseUxml;
        private VisualTreeAsset _settingsUxml;
        private VisualTreeAsset _loadoutUxml;
        private VisualTreeAsset _rewardUxml;
        private VisualTreeAsset _eventUxml;
        private VisualTreeAsset _continueUxml;
        private VisualTreeAsset _shopUxml;
        private VisualTreeAsset _chestUxml;
        private VisualTreeAsset _campUxml;
        private VisualTreeAsset _outcomeUxml;
        private VisualTreeAsset _mainMenuUxml;
        private VisualTreeAsset _loadoutHubUxml;
        private VisualTreeAsset _loadoutInventoryUxml;
        private VisualTreeAsset _arcanaCardUxml;

        // Идентификатор системного меню (pause) в стеке навигатора — ToggleSystemMenu отличает «мы в меню»
        // (даже если сверху настройки) от «поверх игры». Заменил маркер-скан gm-pause-root по CSS-классу.
        private const string PauseId = "pause";

        public MenuRouter(IInputService input, UiNavigator nav, SettingsViewModel settingsVm, LoadoutViewModel loadoutVm,
                          LoadoutHubViewModel hubVm, ILocalizationService loc, IRunControl runControl,
                          IPublisher<MainMenuVisibilityChangedEvent> mainMenuVisPub)
        {
            _input = input;
            _nav = nav;
            _settingsVm = settingsVm;
            _loadoutVm = loadoutVm;
            _hubVm = hubVm;
            _loc = loc;
            _runControl = runControl;
            _mainMenuVisPub = mainMenuVisPub;
        }

        private readonly IPublisher<MainMenuVisibilityChangedEvent> _mainMenuVisPub;

        public bool IsOpen => _nav.IsOpen;

        /// <summary>
        /// Меняется на каждый Push/Pop/резолв навигатора (Ф4): бутстрап подписывается вместо поллинга структуры
        /// стека в <c>Update</c> — по нему пересчитываются подсветка таба и backdrop (снос части K3/K4).
        /// </summary>
        public event Action Changed
        {
            add => _nav.Changed += value;
            remove => _nav.Changed -= value;
        }

        /// <summary>Бутстрап отдаёт слои-контейнеры (Ф4) и UXML-шаблоны экранов (ссылки из сцены, не DI).</summary>
        public void Initialize(VisualElement screensLayer, VisualElement modalLayer, VisualTreeAsset pauseUxml, VisualTreeAsset settingsUxml,
            VisualTreeAsset loadoutUxml = null, VisualTreeAsset rewardUxml = null, VisualTreeAsset eventUxml = null,
            VisualTreeAsset continueUxml = null, VisualTreeAsset shopUxml = null,
            VisualTreeAsset chestUxml = null, VisualTreeAsset outcomeUxml = null, VisualTreeAsset mainMenuUxml = null,
            VisualTreeAsset loadoutHubUxml = null, VisualTreeAsset loadoutInventoryUxml = null,
            VisualTreeAsset arcanaCardUxml = null, VisualTreeAsset campUxml = null)
        {
            _root = screensLayer; // корень оверлеев = слой экранов (null-guard в Open*); FillRoot растягивает по нему
            _pauseUxml = pauseUxml;
            _settingsUxml = settingsUxml;
            _loadoutUxml = loadoutUxml;
            _rewardUxml = rewardUxml;
            _eventUxml = eventUxml;
            _continueUxml = continueUxml;
            _shopUxml = shopUxml;
            _chestUxml = chestUxml;
            _outcomeUxml = outcomeUxml;
            _mainMenuUxml = mainMenuUxml;
            _loadoutHubUxml = loadoutHubUxml;
            _loadoutInventoryUxml = loadoutInventoryUxml;
            _arcanaCardUxml = arcanaCardUxml;
            _campUxml = campUxml;

            // Навигатор Ф4: два слоя-контейнера. Page/Sheet → screensLayer (под топбаром); Modal (pause/
            // settings) → modalLayer (над топбаром, fullscreen-scrim накрывает его — QA #36). Контекст сборки
            // несёт слой экранов (куда FillRoot-оверлеи растягиваются) и локализатор.
            _nav.Initialize(screensLayer, modalLayer, new UiScreenContext(screensLayer, key => _loc?.GetString(key)));
        }

        // Тонкая обёртка существующего вью-билдера в экран навигатора (Ф2): несёт Kind (видимость/suppress
        // считает навигатор), тег режима для подсветки таба и опц. идентификатор (pause). Билдер вызывается
        // лениво при Push — resolved-guard/Detach-страховки внутри билдеров пока живут (уберутся в Ф3).
        private sealed class RouterScreen : UiScreen
        {
            private readonly Func<VisualElement> _build;
            private readonly Action _onExit;
            public override ScreenKind Kind { get; }
            public override string ModeTag { get; }
            public string ScreenId { get; }

            public RouterScreen(ScreenKind kind, Func<VisualElement> build, string modeTag = null,
                                string screenId = null, Action onExit = null)
            {
                Kind = kind;
                _build = build;
                ModeTag = modeTag;
                ScreenId = screenId;
                _onExit = onExit;
            }

            public override void Build(UiScreenContext ctx) => Root = _build();

            // Владелец persistent-оверлея (тест-зона/инвентарь, Ф5/Ф6) обнуляет свою ссылку при ЛЮБОМ снятии
            // (Pop/PopAll/Remove) — иначе PopAll завершения забега оставил бы висячую ссылку → рассинхрон.
            public override void OnExit() => _onExit?.Invoke();
        }

        private void PushScreen(Func<VisualElement> build, ScreenKind kind, string modeTag = null, string screenId = null,
                                CancellationToken ct = default)
            => _nav.Push(new RouterScreen(kind, build, modeTag, screenId), ct);

        // Обёртка flow-экрана с результатом (Ф3): вью-билдер получает делегат Resolve и связывает с ним свои
        // колбэки (выбор/пропуск). Навигатор гарантирует РОВНО ОДИН резолв — явный или DefaultResult при снятии
        // без выбора. Снимает нужду в resolved-guard'ах и DetachFromPanelEvent-страховках внутри билдеров.
        private sealed class RouterResultScreen<TResult> : UiScreen<TResult>
        {
            private readonly Func<Action<TResult>, VisualElement> _build;
            private readonly TResult _default;
            public override ScreenKind Kind { get; }
            public override string ModeTag { get; }
            public override TResult DefaultResult => _default;

            public RouterResultScreen(ScreenKind kind, TResult defaultResult,
                Func<Action<TResult>, VisualElement> build, string modeTag = null)
            {
                Kind = kind;
                _default = defaultResult;
                _build = build;
                ModeTag = modeTag;
            }

            public override void Build(UiScreenContext ctx) => Root = _build(Resolve);
        }

        private void Pop() => _nav.Pop();

        /// <summary>
        /// Открыть loadout-экран для юнита (по дабл-клику в фазе расстановки; публикуется как
        /// <see cref="OpenLoadoutRequest"/>, бутстрап зовёт сюда). Пушится как полноэкранный оверлей.
        /// </summary>
        public void OpenLoadout(OpenLoadoutRequest req)
        {
            if (_root == null || _loadoutUxml == null) return;
            _loadoutVm.Open(req);
            PushScreen(BuildLoadoutScreen, ScreenKind.Page);
        }

        /// <summary>
        /// Лоадаут-хаб (кольцо реликвий, Фаза 2): обзор гильдии + навешивание собранных реликвий на сосуды.
        /// Открывается кнопкой «Хаб» в топбаре, пушится оверлеем поверх карты. Реликвии переносятся драгом
        /// (тащишь из запаса на сосуд → надеть; с сосуда в запас → снять). Правки durable (RunState) — контент
        /// ребилдится на каждое действие (хаб дёшев).
        /// </summary>
        public void OpenHub()
        {
            if (_root == null || _loadoutHubUxml == null) return;

            var container = new VisualElement { name = "hub-container" };
            container.style.position = Position.Absolute;
            container.style.left = 0; container.style.top = 0; container.style.right = 0; container.style.bottom = 0;

            void Rebuild()
            {
                container.Clear();
                VisualElement hub = LoadoutHubView.Build(
                    _loadoutHubUxml,
                    _hubVm.Roster(), _hubVm.Banners(), _hubVm.Stash(), _hubVm.Gold,
                    nameOf: id => _hubVm.NameOf(id),
                    localize: key => _loc?.GetString(key),
                    onClose: Pop,
                    onEquip: (vessel, stash) => { _hubVm.Equip(vessel, stash); Rebuild(); },
                    onUnequip: vessel => { _hubVm.Unequip(vessel); Rebuild(); });
                container.Add(hub);
            }

            Rebuild();
            PushScreen(() => container, ScreenKind.Page);
        }

        /// <summary>
        /// Новый полноэкранный лоадаут/инвентарь (редизайн, Ф3a): грид таро-карточек реликвий + детали.
        /// Открывается кнопкой «Хаб» в топбаре (заменил старый хаб-оверлей). <paramref name="onClose"/>
        /// зовётся на ЛЮБОМ закрытии (Pop/Esc/PopAll) через DetachFromPanelEvent — бутстрап по нему
        /// возвращает ран-топбар. Реликвии — весь контент (фильтр по владению — Фаза 5); gold из RunState.
        /// </summary>
        // Инвентарь (Ф6): формальный Sheet-экран навигатора. ТУМБЛЕР — открыт → снять (Remove из любого места
        // стека, даже под паузой), закрыт → построить и Push. Владеет экраном роутер (ссылка _inventoryScreen,
        // обнуляется onExit при ЛЮБОМ снятии — смерть _inventoryOpen/onClose-Detach в бутстрапе, K6). Закрытие
        // НЕ через PopAll → карта петли акта под инвентарём НЕ сносится (нет Aborted, класс #31).
        private UiScreen _inventoryScreen;

        /// <summary>Открыт ли инвентарь (для backdrop-логики бутстрапа — единый источник вместо флага).</summary>
        public bool IsInventoryOpen => _inventoryScreen != null;

        /// <summary>Есть ли карта петли акта в стеке (result-экран выбора узла) — скрытая под геймплеем или видимая.
        /// Отличает «вернуться на карту петли» (выйти из боя) от read-only просмотра (нет карты петли).</summary>
        public bool HasMapInStack => _nav.AnyScreen(s => s.ModeTag == "map");

        /// <summary>Показать инвентарь (радио-режим): Sheet-тело поверх геймплея. Идемпотентно (уже открыт → no-op).</summary>
        public void ShowInventory(int gold, Action<RelicData, RelicDragPhase> onRelicDrag = null)
        {
            if (_inventoryScreen != null) { UiTrace.Log("router.ShowInventory: уже открыт → no-op"); return; }
            if (_root == null || _loadoutInventoryUxml == null || _arcanaCardUxml == null)
            {
                UiTrace.Log("router.ShowInventory: ассеты не готовы → no-op");
                return;
            }
            UiTrace.Log("router.ShowInventory: Push inventory Sheet");
            _inventoryScreen = new RouterScreen(ScreenKind.Sheet, () => BuildInventory(gold, onRelicDrag),
                                                modeTag: "inventory", onExit: () => _inventoryScreen = null);
            _nav.Push(_inventoryScreen); // QA #21: ModeTag "inventory" подсвечивает таб
        }

        /// <summary>Снять инвентарь (радио-режим). Идемпотентно (не открыт → no-op). Remove из любого места стека.</summary>
        public void HideInventory()
        {
            UiTrace.Log($"router.HideInventory: {(_inventoryScreen != null ? "Remove inventory" : "нет экрана → no-op")}");
            if (_inventoryScreen != null) _nav.Remove(_inventoryScreen); // OnExit обнулит ссылку
        }

        private VisualElement BuildInventory(int gold, Action<RelicData, RelicDragPhase> onRelicDrag)
        {
            VisualElement screen = LoadoutInventoryView.Build(
                _loadoutInventoryUxml, _arcanaCardUxml,
                _loadoutVm.Relics, gold,
                titleOf: r => ArcanaTitle(r != null ? r.Id : null),
                narrativeOf: r => _loadoutVm.Desc(r),
                localize: key => _loc?.GetString(key),
                lockedSlots: 0,
                cardAnimations: _settingsVm.CardAnimations,
                cardAttackAnimation: _settingsVm.CardAttackAnimation,
                onRelicDrag: onRelicDrag); // QA #5: drag карточки реликвии на юнита в мире

            // Инвентарь = ТОЛЬКО тело; навигация (режимы) и меню — в глобальном топбаре (RunModeBar). Sheet:
            // навигатор НЕ глушит геймплей — под инвентарём живут юниты/камера (клики разводит PointerOverUI над
            // панелями vs дыркой). Класс gm-screen--transparent — только СТИЛЬ (прозрачный фон), suppress = Kind.
            screen.AddToClassList(TransparentScreenClass);
            return screen;
        }

        // Тест-зона (Ф5): «геймплей»-пространство = Sheet-экран навигатора (ModeTag "battle", прозрачный корень,
        // pickingMode Ignore — мир под ним живёт и кликается). Карта петли акта (Page) под ним прячется правилом
        // видимости (Sheet скрывает Page) — БЕЗ ручного HideTopForTest (снос K5). Показ/скрытие — по СОСТОЯНИЮ
        // TestZoneChangedEvent (бутстрап), владелец состояния — DeploymentController. Идемпотентно.
        private UiScreen _testZoneScreen;

        /// <summary>Войти в UI тест-зоны: Sheet-пространство «Бой» поверх стека (карта под ним прячется).</summary>
        public void ShowTestZone()
        {
            if (_testZoneScreen != null) { UiTrace.Log("router.ShowTestZone: уже показан → no-op"); return; }
            UiTrace.Log("router.ShowTestZone: Push test-zone Sheet");
            _testZoneScreen = new RouterScreen(ScreenKind.Sheet, BuildTestZoneSpace, modeTag: "battle",
                                               onExit: () => _testZoneScreen = null);
            _nav.Push(_testZoneScreen);
        }

        /// <summary>Выйти из UI тест-зоны: снять Sheet-пространство из любого места стека (даже под инвентарём).</summary>
        public void HideTestZone()
        {
            UiTrace.Log($"router.HideTestZone: {(_testZoneScreen != null ? "Remove test-zone" : "нет экрана → no-op")}");
            if (_testZoneScreen != null) _nav.Remove(_testZoneScreen); // OnExit обнулит _testZoneScreen
        }

        // World-карта (фаза D): сама карта живёт в мире, а UI держит лишь прозрачное Sheet-пространство —
        // ради тега режима (подсветка таба «Карта») и контекста ввода (навигатор ставит InputContext.Map,
        // где world-камера жива). Показ/скрытие — по СОСТОЯНИЮ WorldMapSpaceChangedEvent (бутстрап),
        // владелец состояния — WorldMapNodeChooser. Идемпотентно, как тест-зона.
        private UiScreen _mapSpaceScreen;

        /// <summary>Показана ли world-карта: её пространство есть в стеке. Фон забега при этом гасится — карта
        /// теперь рисуется В МИРЕ, и непрозрачный backdrop просто закрыл бы её собой.</summary>
        public bool IsMapSpaceOpen => _mapSpaceScreen != null;

        /// <summary>Войти в пространство world-карты: прозрачный Sheet с тегом режима «карта».</summary>
        public void ShowMapSpace()
        {
            if (_mapSpaceScreen != null) { UiTrace.Log("router.ShowMapSpace: уже показан → no-op"); return; }
            UiTrace.Log("router.ShowMapSpace: Push map Sheet");
            _mapSpaceScreen = new RouterScreen(ScreenKind.Sheet, BuildMapSpace, modeTag: UiScreen.MapModeTag,
                                               onExit: () => _mapSpaceScreen = null);
            _nav.Push(_mapSpaceScreen);
        }

        /// <summary>Выйти из пространства world-карты: снять Sheet из любого места стека.</summary>
        public void HideMapSpace()
        {
            UiTrace.Log($"router.HideMapSpace: {(_mapSpaceScreen != null ? "Remove map space" : "нет экрана → no-op")}");
            if (_mapSpaceScreen != null) _nav.Remove(_mapSpaceScreen); // OnExit обнулит _mapSpaceScreen
        }

        // Прозрачное «окно в мир» карты — та же роль, что у пространства тест-зоны: контента не рисует,
        // ввод не ловит (клики уходят в мир через PointerOverUI), несёт лишь режим.
        private static VisualElement BuildMapSpace()
        {
            var space = new VisualElement { name = "map-space", pickingMode = PickingMode.Ignore };
            space.style.position = Position.Absolute;
            space.style.left = 0; space.style.top = 0; space.style.right = 0; space.style.bottom = 0;
            return space;
        }

        // Прозрачное полноэкранное «окно в мир» тест-зоны: не рисует контента (мир виден сквозь), не ловит ввод
        // (Ignore — клики идут в мир через PointerOverUI). Несёт лишь роль «мы в геймплей-пространстве тест-зоны».
        private static VisualElement BuildTestZoneSpace()
        {
            var space = new VisualElement { name = "test-zone-space", pickingMode = PickingMode.Ignore };
            space.style.position = Position.Absolute;
            space.style.left = 0; space.style.top = 0; space.style.right = 0; space.style.bottom = 0;
            return space;
        }

        // Титул таро-карты в стиле ГДД (аркан «The X»): «relic.flame_swordsman» → «The Flame Swordsman».
        private static string ArcanaTitle(string id)
        {
            if (string.IsNullOrEmpty(id)) return "—";
            int dot = id.LastIndexOf('.');
            string s = (dot >= 0 ? id.Substring(dot + 1) : id).Replace('_', ' ');
            var parts = s.Split(' ');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            return "The " + string.Join(" ", parts);
        }

        // QA #12: ☰/ESC ОТКРЫВАЮТ системное меню ПОВЕРХ текущего экрана (инвентарь/карта/бой), не закрывая
        // его. Если мы уже в меню (pause в стеке — даже под настройками) — шаг назад (settings→pause→закрыть).
        public void ToggleSystemMenu()
        {
            if (_root == null) return;
            bool inMenu = _nav.AnyScreen(s => s is RouterScreen r && r.ScreenId == PauseId);
            if (inMenu) _nav.Pop();                                          // уже в системном меню → назад/закрыть
            else PushScreen(BuildPauseScreen, ScreenKind.Modal, screenId: PauseId); // QA #19: меню ПОВЕРХ (Modal со scrim)
        }

        // Закрыть все экраны и снять глушение (навигатор пересчитает suppress из фазы). Внутренний close-callback
        // текстового ивента (выбор без результата = снять стек). НЕ для завершения забега (то — единая отмена, K11).
        private void CloseAll() => _nav.PopAll();

        /// <summary>Режим-таб верхнего оверлея (QA #21): "inventory"/"map"/null. Единый источник подсветки топбара.</summary>
        public string ActiveScreenMode => _nav.ActiveModeTag;

        // Прозрачные оверлеи (инвентарь) НЕ глушат геймплей — под ними живёт мир (юниты/камера, развязка
        // через IInputService.PointerOverUI). Класс остаётся ТОЛЬКО как стиль (прозрачный фон); поведение
        // suppress теперь определяет ScreenKind.Sheet в навигаторе, а не наличие этого класса.
        private const string TransparentScreenClass = "gm-screen--transparent";

        // Маркер экрана системного меню (pause) для стиля/якорей UXML. Логика «мы в меню» — по RouterScreen.ScreenId.
        private const string PauseScreenClass = "gm-pause-root";

        // --- Экраны ---

        private VisualElement BuildPauseScreen()
        {
            var screen = FillRoot(_pauseUxml.CloneTree());
            screen.AddToClassList(PauseScreenClass); // стилевой маркер «системное меню»
            // «Продолжить» = снять ТОЛЬКО системное меню (Pop), а не весь стек (CloseAll снёс бы карту под паузой
            // → resolve узла null → Aborted, тот же баг класса #37). Экраны под меню (карта/инвентарь) остаются.
            screen.Q<Button>("btn-return").clicked += Pop;
            screen.Q<Button>("btn-settings").clicked += () => PushScreen(BuildSettingsScreen, ScreenKind.Modal);

            // QA #18/#37: «В главное меню» прерывает забег ЕДИНОЙ отменой (токен) — снять меню (Pop) + отменить
            // забег; отмена сама закрывает открытый экран забега (карта/награда/…) через навигатор и всплывает
            // OperationCanceledException в GameFlow → главное меню. Никакого CloseAll-веника (снос K11). Сейв цел.
            var toMenu = screen.Q<Button>("btn-main-menu");
            if (toMenu != null)
            {
                toMenu.text = Loc("ui.menu.to_main_menu", "В главное меню");
                toMenu.clicked += () => { Pop(); _runControl?.RequestReturnToMainMenu(); };
            }
            var quit = screen.Q<Button>("btn-quit");
            if (quit != null)
            {
                quit.text = Loc("ui.menu.quit", "Выйти из игры");
                quit.clicked += () => _runControl?.RequestQuit();
            }
            return screen;
        }

        // Локализованная строка с RU-фолбэком (весь новый UI на code-fallback до записи в String Table).
        private string Loc(string key, string ru)
        {
            string v = _loc?.GetString(key);
            return string.IsNullOrEmpty(v) ? ru : v;
        }

        private VisualElement BuildSettingsScreen()
        {
            var screen = FillRoot(_settingsUxml.CloneTree());

            var master = screen.Q<Guildmaster.UI.Components.SliderRow>("row-master");
            var music  = screen.Q<Guildmaster.UI.Components.SliderRow>("row-music");
            var sfx    = screen.Q<Guildmaster.UI.Components.SliderRow>("row-sfx");
            master.LabelText = "Общий";
            music.LabelText  = "Музыка";
            sfx.LabelText    = "Звук";

            // Таб «Игра»: тумблеры презентации (анимация карточек / анимация атаки). Подписи через loc
            // с RU-фолбэком (как остальной новый UI); значения проводятся из VM.
            string L(string key, string ru) { string v = _loc?.GetString(key); return string.IsNullOrEmpty(v) ? ru : v; }
            var cardAnim   = screen.Q<Guildmaster.UI.Components.ToggleRow>("toggle-card-anim");
            var cardAttack = screen.Q<Guildmaster.UI.Components.ToggleRow>("toggle-card-attack");
            if (cardAnim   != null) cardAnim.LabelText   = L("ui.settings.card_anim", "Анимация карточек");
            if (cardAttack != null) cardAttack.LabelText = L("ui.settings.card_attack", "Анимация атаки карточек");

            _settingsVm.BeginEdit();

            // SliderRow/ToggleRow сами обновляют свой вид (в т.ч. в SetValueWithoutNotify).
            void Sync()
            {
                master.SetValueWithoutNotify(_settingsVm.Master);
                music.SetValueWithoutNotify(_settingsVm.Music);
                sfx.SetValueWithoutNotify(_settingsVm.Sfx);
                cardAnim?.SetValueWithoutNotify(_settingsVm.CardAnimations);
                cardAttack?.SetValueWithoutNotify(_settingsVm.CardAttackAnimation);
                // «Атака» осмысленна только при включённой анимации карточек.
                cardAttack?.SetEnabled(_settingsVm.CardAnimations);
            }

            Sync();

            master.Slider.RegisterValueChangedCallback(e => _settingsVm.SetMaster(e.newValue));
            music.Slider.RegisterValueChangedCallback(e => _settingsVm.SetMusic(e.newValue));
            sfx.Slider.RegisterValueChangedCallback(e => _settingsVm.SetSfx(e.newValue));
            cardAnim?.Toggle.RegisterValueChangedCallback(e => _settingsVm.SetCardAnimations(e.newValue));
            cardAttack?.Toggle.RegisterValueChangedCallback(e => _settingsVm.SetCardAttackAnimation(e.newValue));

            // VM → слайдеры (Defaults/Cancel меняют значения «снаружи»). Отписка при снятии с панели.
            Action onChanged = Sync;
            _settingsVm.Changed += onChanged;
            screen.RegisterCallback<DetachFromPanelEvent>(_ => _settingsVm.Changed -= onChanged);

            screen.Q<Button>("btn-save").clicked += () => { _settingsVm.Save(); Pop(); };
            screen.Q<Button>("btn-cancel").clicked += () => { _settingsVm.Cancel(); Pop(); };
            screen.Q<Button>("btn-defaults").clicked += () => _settingsVm.ResetToDefaults();

            WireSettingsTabs(screen);
            return screen;
        }

        // Табы настроек (Игра/Графика/Звук) — визуал-каркас: клик показывает свою страницу и прячет прочие.
        // Игра/Графика пока плейсхолдеры; Звук несёт живые слайдеры. Раскладка/стиль — из UXML/USS.
        private static void WireSettingsTabs(VisualElement screen)
        {
            var tabGame  = screen.Q<Button>("tab-game");
            var tabVideo = screen.Q<Button>("tab-video");
            var tabAudio = screen.Q<Button>("tab-audio");
            var pageGame  = screen.Q<VisualElement>("page-game");
            var pageVideo = screen.Q<VisualElement>("page-video");
            var pageAudio = screen.Q<VisualElement>("page-audio");
            if (tabGame == null || tabVideo == null || tabAudio == null) return;

            void Show(Button tab, VisualElement page)
            {
                tabGame.EnableInClassList("gm-tab--active", tab == tabGame);
                tabVideo.EnableInClassList("gm-tab--active", tab == tabVideo);
                tabAudio.EnableInClassList("gm-tab--active", tab == tabAudio);
                pageGame?.EnableInClassList("gm-tab-page--hidden", page != pageGame);
                pageVideo?.EnableInClassList("gm-tab-page--hidden", page != pageVideo);
                pageAudio?.EnableInClassList("gm-tab-page--hidden", page != pageAudio);
            }

            tabGame.clicked  += () => Show(tabGame, pageGame);
            tabVideo.clicked += () => Show(tabVideo, pageVideo);
            tabAudio.clicked += () => Show(tabAudio, pageAudio);
        }

        private VisualElement BuildLoadoutScreen()
        {
            var screen = FillRoot(_loadoutUxml.CloneTree());

            var grid       = screen.Q<ScrollView>("relic-grid");
            var detailName = screen.Q<Label>("detail-name");
            var detailDesc = screen.Q<Label>("detail-desc");
            var detailTags = screen.Q<Label>("detail-tags");
            var detailStats = screen.Q<Label>("detail-stats");

            grid.contentContainer.AddToClassList("gm-grid");
            var cards = new List<(RelicData relic, VisualElement card)>();

            void ShowDetail(RelicData r)
            {
                detailName.text  = _loadoutVm.Name(r);
                detailDesc.text  = _loadoutVm.Desc(r);
                detailTags.text  = _loadoutVm.Tags(r);
                detailStats.text = _loadoutVm.StatsSummary(r);
            }

            void RefreshCards()
            {
                foreach (var (relic, card) in cards)
                {
                    card.EnableInClassList("gm-card--selected", _loadoutVm.IsSelected(relic));
                    card.EnableInClassList("gm-card--current", _loadoutVm.IsCurrent(relic));
                }
            }

            IReadOnlyList<RelicData> relics = _loadoutVm.Relics;
            for (int i = 0; i < relics.Count; i++)
            {
                RelicData relic = relics[i];
                var card = new VisualElement();
                card.AddToClassList("gm-card");

                var sprite = new VisualElement();
                sprite.AddToClassList("gm-card__sprite");
                if (relic.Icon != null) sprite.style.backgroundImage = new StyleBackground(relic.Icon);
                card.Add(sprite);

                var name = new Label(_loadoutVm.Name(relic));
                name.AddToClassList("gm-card__name");
                card.Add(name);

                // Наведение → детали; клик → выбор (+звук) + предпросмотр деталей.
                card.RegisterCallback<PointerEnterEvent>(_ => ShowDetail(relic));
                card.RegisterCallback<ClickEvent>(_ => { _loadoutVm.Select(relic); RefreshCards(); ShowDetail(relic); });

                grid.Add(card);
                cards.Add((relic, card));
            }

            RefreshCards();
            ShowDetail(_loadoutVm.Selected ?? (relics.Count > 0 ? relics[0] : null));

            // Табы-заглушки (кроме Релик) — недоступны (структура на будущее: Предметы/Улучшения/AI).
            Disable(screen.Q<Button>("tab-items"));
            Disable(screen.Q<Button>("tab-upgrades"));
            Disable(screen.Q<Button>("tab-ai"));

            // Принять = применить + закрыть; Сохранить = применить, не закрывая; Закрыть = отмена.
            screen.Q<Button>("btn-accept").clicked += () => { _loadoutVm.Apply(); Pop(); };
            screen.Q<Button>("btn-save").clicked   += () => { _loadoutVm.Apply(); RefreshCards(); };
            screen.Q<Button>("btn-close").clicked  += Pop;
            return screen;
        }

        // Экран награды (A3) — на UXML (RewardScreen.uxml) через общий RewardScreenView. Навигатор гарантирует
        // ровно один OnResolved, включая закрытие без выбора (= пропуск), чтобы флоу забега не завис (Ф3).
        public void OpenReward(OpenRewardRequest req)
        {
            if (_root == null || _rewardUxml == null) { req.OnResolved?.Invoke(RewardChoiceResult.Skip); return; }
            ShowRewardAsync(req).Forget();
        }

        private async UniTaskVoid ShowRewardAsync(OpenRewardRequest req)
        {
            var screen = new RouterResultScreen<RewardChoiceResult>(ScreenKind.Page, RewardChoiceResult.Skip,
                resolve => RewardScreenView.Build(
                    _rewardUxml,
                    req.Choices,
                    req.InventoryFull,
                    req.CurrentInventory,
                    relic => _loadoutVm.Name(relic),
                    key => _loc?.GetString(key),
                    (chosen, dropId) => resolve(dropId != null
                        ? RewardChoiceResult.Swap(chosen, dropId)
                        : RewardChoiceResult.Take(chosen)),
                    () => resolve(RewardChoiceResult.Skip)));

            RewardChoiceResult result = await _nav.ShowAsync(screen, req.Cancellation); // экран снят ДО колбэка (II.5); ct → закрыть при отмене (QA #37)
            req.OnResolved?.Invoke(result);
        }

        // Экран текстового ивента (StS-style) — на UXML (EventScreen.uxml) через общий EventScreenView.
        // Выбор фиксирует последствие (колбэк → флоу применяет эффекты), затем показывается текст-результат.
        // Закрытие без выбора (ESC/PopAll) = -1, чтобы флоу не завис.
        public void OpenTextEvent(OpenTextEventRequest req)
        {
            if (_root == null || _eventUxml == null || req.Event == null) { req.OnChosen?.Invoke(-1); return; }
            PushScreen(() => BuildTextEventScreen(req), ScreenKind.Page, ct: req.Cancellation); // QA #37: отмена закрывает ивент
        }

        private VisualElement BuildTextEventScreen(OpenTextEventRequest req)
        {
            bool resolved = false;

            void Resolve(int index)
            {
                if (resolved) return;
                resolved = true;
                req.OnChosen?.Invoke(index);
            }

            VisualElement screen = EventScreenView.Build(
                _eventUxml,
                req.Event,
                key => _loc?.GetString(key),
                Resolve,
                CloseAll);

            // Страховка: закрытие без выбора (ESC/PopAll) = пропуск (-1), чтобы флоу не завис.
            screen.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (!resolved) { resolved = true; req.OnChosen?.Invoke(-1); }
            });

            return screen;
        }

        // Единая кнопка «Продолжить» (A4) — оверлей с кнопкой в правом нижнем углу. Нажатие резолвит и закрывает;
        // закрытие без нажатия (ESC/PopAll) тоже резолвит, чтобы петля акта не зависла.
        public void ShowContinue(OpenContinueRequest req)
        {
            if (_root == null || _continueUxml == null) { req.OnContinue?.Invoke(); return; }
            ShowContinueAsync(req).Forget();
        }

        private async UniTaskVoid ShowContinueAsync(OpenContinueRequest req)
        {
            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false, resolve =>
            {
                var body = FillRoot(_continueUxml.CloneTree());
                var btn = body.Q<Button>("btn-continue");
                if (btn != null)
                {
                    if (!string.IsNullOrEmpty(req.LabelKey))
                    {
                        string label = _loc?.GetString(req.LabelKey);
                        if (!string.IsNullOrEmpty(label)) btn.text = label;
                    }
                    btn.clicked += () => resolve(true);
                }
                return body;
            });

            await _nav.ShowAsync(screen, req.Cancellation); // «Продолжить»/закрытие → OnContinue; ct → закрыть при отмене (QA #37)
            req.OnContinue?.Invoke();
        }

        // Экран магазина (B2) — на UXML (ShopScreen.uxml) через общий ShopScreenView, биндится к IShopController.
        // «Уйти»/закрытие резолвит OnLeave (петля продолжается). Ровно один вызов.
        public void OpenShop(OpenShopRequest req)
        {
            if (_root == null || _shopUxml == null || req.Shop == null) { req.OnLeave?.Invoke(); return; }
            ShowShopAsync(req).Forget();
        }

        private async UniTaskVoid ShowShopAsync(OpenShopRequest req)
        {
            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false,
                resolve => ShopScreenView.Build(
                    _shopUxml,
                    req.Shop,
                    relic => _loadoutVm.Name(relic),
                    key => _loc?.GetString(key),
                    () => resolve(true)));

            await _nav.ShowAsync(screen, req.Cancellation); // «Уйти»/закрытие → OnLeave; ct → закрыть при отмене (QA #37)
            req.OnLeave?.Invoke();
        }

        // Экран сундука (B3) — на UXML (ChestScreen.uxml). Клик по крышке резолвит OnOpen (флоу катит награду),
        // затем сундук закрывается. Закрытие без клика тоже резолвит, чтобы флоу не завис.
        public void OpenChest(OpenChestRequest req)
        {
            if (_root == null || _chestUxml == null) { req.OnOpen?.Invoke(); return; }
            ShowChestAsync(req).Forget();
        }

        private async UniTaskVoid ShowChestAsync(OpenChestRequest req)
        {
            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false,
                resolve => ChestScreenView.Build(_chestUxml, key => _loc?.GetString(key), () => resolve(true)));

            await _nav.ShowAsync(screen, req.Cancellation); // клик/закрытие → OnOpen; ct → закрыть при отмене (QA #37)
            req.OnOpen?.Invoke();
        }

        // Экран привала — на UXML (CampScreen.uxml). Живёт, пока отряд тратит бюджет действий; закрывается
        // по «Пройти мимо» (или ESC/PopAll), и только тогда резолвит OnLeave. Тем и отличается от ивента:
        // выбор здесь повторяемый, а выход — отдельное решение.
        public void OpenCamp(OpenCampRequest req)
        {
            if (_root == null || _campUxml == null || req.Session == null) { req.OnLeave?.Invoke(); return; }
            ShowCampAsync(req).Forget();
        }

        private async UniTaskVoid ShowCampAsync(OpenCampRequest req)
        {
            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false,
                resolve => CampScreenView.Build(_campUxml, req.Session, key => _loc?.GetString(key), () => resolve(true)));

            await _nav.ShowAsync(screen, req.Cancellation); // уход/закрытие → OnLeave; ct → закрыть при отмене (QA #37)
            req.OnLeave?.Invoke();
        }

        // Экран исхода забега (C2) — на UXML (OutcomeScreen.uxml). «В меню» резолвит OnToMenu; закрытие тоже.
        public void ShowOutcome(OpenOutcomeRequest req)
        {
            if (_root == null || _outcomeUxml == null) { req.OnToMenu?.Invoke(); return; }
            ShowOutcomeAsync(req).Forget();
        }

        private async UniTaskVoid ShowOutcomeAsync(OpenOutcomeRequest req)
        {
            var screen = new RouterResultScreen<bool>(ScreenKind.Page, false,
                resolve => OutcomeScreenView.Build(_outcomeUxml, req.Victory, key => _loc?.GetString(key), () => resolve(true)));

            await _nav.ShowAsync(screen); // «В меню» и закрытие → OnToMenu
            req.OnToMenu?.Invoke();
        }

        // Главное меню (D1) — на UXML (MainMenuScreen.uxml). Начать/Продолжить/Выход резолвят OnChoice и закрывают
        // меню; «Настройки» открываются поверх (Push) и меню не закрывают.
        public void OpenMainMenu(OpenMainMenuRequest req)
        {
            if (_root == null || _mainMenuUxml == null) { req.OnChoice?.Invoke(MainMenuChoice.Quit); return; }
            ShowMainMenuAsync(req).Forget();
        }

        private async UniTaskVoid ShowMainMenuAsync(OpenMainMenuRequest req)
        {
            var screen = new RouterResultScreen<MainMenuChoice>(ScreenKind.Page, MainMenuChoice.Quit,
                resolve => MainMenuScreenView.Build(
                    _mainMenuUxml,
                    req.HasSave,
                    key => _loc?.GetString(key),
                    onStart:    () => resolve(MainMenuChoice.StartRun),
                    onContinue: () => resolve(MainMenuChoice.Continue),
                    onSettings: () => PushScreen(BuildSettingsScreen, ScreenKind.Modal), // поверх меню, НЕ резолв
                    onQuit:     () => resolve(MainMenuChoice.Quit)));

            // Пока меню на экране, презентационный слой подкладывает под него стол (иначе за меню пустота).
            _mainMenuVisPub?.Publish(new MainMenuVisibilityChangedEvent(true));
            try
            {
                MainMenuChoice choice = await _nav.ShowAsync(screen); // снятие без выбора = Quit (верхний цикл не виснет)
                req.OnChoice?.Invoke(choice);
            }
            finally
            {
                // Через finally, а не после await: меню снимают и отменой, и выходом из игры — фон обязан
                // погаснуть в любом случае, иначе он останется висеть поверх мира.
                _mainMenuVisPub?.Publish(new MainMenuVisibilityChangedEvent(false));
            }
        }

        private static void Disable(Button b) { if (b != null) b.SetEnabled(false); }

        // Клон UXML → растянуть на весь корень панели (оверлей).
        private static VisualElement FillRoot(VisualElement tree)
        {
            tree.style.position = Position.Absolute;
            tree.style.left = 0;
            tree.style.top = 0;
            tree.style.right = 0;
            tree.style.bottom = 0;
            return tree;
        }

        private static string Percent(float v01) => Mathf.RoundToInt(Mathf.Clamp01(v01) * 100f) + "%";
    }
}
