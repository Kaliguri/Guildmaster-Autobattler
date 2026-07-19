using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Guildmaster.Core.Flow;
using Guildmaster.Core.Input;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Реализация <see cref="IMenuRouter"/>: строит и проводит экраны из UXML-шаблонов, а стек, видимость и
    /// глушение геймплейного ввода делегирует <see cref="UiNavigator"/> (UI-реворк Ф2). Каждый экран получает
    /// честный <see cref="ScreenKind"/> — по нему навигатор ВЫЧИСЛЯЕТ видимость нижних и suppress, вместо
    /// прежней ручной синхронизации (<c>_menuModeActive</c>/<c>_prevContext</c>/CSS-классов-флагов).
    /// Настройки применяются живьём; Cancel/Save — на кнопках, ESC = навигация назад.
    /// </summary>
    public sealed class MenuRouter : IMenuRouter
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
        private VisualTreeAsset _mapUxml;
        private VisualTreeAsset _continueUxml;
        private VisualTreeAsset _shopUxml;
        private VisualTreeAsset _chestUxml;
        private VisualTreeAsset _outcomeUxml;
        private VisualTreeAsset _mainMenuUxml;
        private VisualTreeAsset _loadoutHubUxml;
        private VisualTreeAsset _loadoutInventoryUxml;
        private VisualTreeAsset _arcanaCardUxml;

        // Идентификатор системного меню (pause) в стеке навигатора — ToggleSystemMenu отличает «мы в меню»
        // (даже если сверху настройки) от «поверх игры». Заменил маркер-скан gm-pause-root по CSS-классу.
        private const string PauseId = "pause";

        public MenuRouter(IInputService input, UiNavigator nav, SettingsViewModel settingsVm, LoadoutViewModel loadoutVm,
                          LoadoutHubViewModel hubVm, ILocalizationService loc, IRunControl runControl)
        {
            _input = input;
            _nav = nav;
            _settingsVm = settingsVm;
            _loadoutVm = loadoutVm;
            _hubVm = hubVm;
            _loc = loc;
            _runControl = runControl;
        }

        public bool IsOpen => _nav.IsOpen;

        /// <summary>Бутстрап отдаёт корень панели и UXML-шаблоны экранов (ссылки из сцены, не DI).</summary>
        public void Initialize(VisualElement root, VisualTreeAsset pauseUxml, VisualTreeAsset settingsUxml,
            VisualTreeAsset loadoutUxml = null, VisualTreeAsset rewardUxml = null, VisualTreeAsset eventUxml = null,
            VisualTreeAsset mapUxml = null, VisualTreeAsset continueUxml = null, VisualTreeAsset shopUxml = null,
            VisualTreeAsset chestUxml = null, VisualTreeAsset outcomeUxml = null, VisualTreeAsset mainMenuUxml = null,
            VisualTreeAsset loadoutHubUxml = null, VisualTreeAsset loadoutInventoryUxml = null,
            VisualTreeAsset arcanaCardUxml = null)
        {
            _root = root;
            _pauseUxml = pauseUxml;
            _settingsUxml = settingsUxml;
            _loadoutUxml = loadoutUxml;
            _rewardUxml = rewardUxml;
            _eventUxml = eventUxml;
            _mapUxml = mapUxml;
            _continueUxml = continueUxml;
            _shopUxml = shopUxml;
            _chestUxml = chestUxml;
            _outcomeUxml = outcomeUxml;
            _mainMenuUxml = mainMenuUxml;
            _loadoutHubUxml = loadoutHubUxml;
            _loadoutInventoryUxml = loadoutInventoryUxml;
            _arcanaCardUxml = arcanaCardUxml;

            // Навигатор Ф2: в Ф2 слой экранов = сам корень панели (слои-контейнеры появятся в Ф4). Экраны
            // добавляются в корень, топбар держится поверх через BringToFront бутстрапа (как раньше).
            _nav.Initialize(root, new UiScreenContext(root, key => _loc?.GetString(key)));
        }

        // Тонкая обёртка существующего вью-билдера в экран навигатора (Ф2): несёт Kind (видимость/suppress
        // считает навигатор), тег режима для подсветки таба и опц. идентификатор (pause). Билдер вызывается
        // лениво при Push — resolved-guard/Detach-страховки внутри билдеров пока живут (уберутся в Ф3).
        private sealed class RouterScreen : UiScreen
        {
            private readonly Func<VisualElement> _build;
            public override ScreenKind Kind { get; }
            public override string ModeTag { get; }
            public string ScreenId { get; }

            public RouterScreen(ScreenKind kind, Func<VisualElement> build, string modeTag = null, string screenId = null)
            {
                Kind = kind;
                _build = build;
                ModeTag = modeTag;
                ScreenId = screenId;
            }

            public override void Build(UiScreenContext ctx) => Root = _build();
        }

        private void PushScreen(Func<VisualElement> build, ScreenKind kind, string modeTag = null, string screenId = null)
            => _nav.Push(new RouterScreen(kind, build, modeTag, screenId));

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

        // Настройки поверх текущего экрана (топбар «Опции»): Push, а не ToggleSystemMenu — иначе при одном
        // экране на стеке (карта забега) сработал бы PopAll и оборвал бы забег. Save/Cancel в настройках — Pop.
        public void OpenSettings()
        {
            if (_root == null || _settingsUxml == null) return;
            PushScreen(BuildSettingsScreen, ScreenKind.Modal);
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
        public void OpenInventory(int gold, Action onClose,
            Action<RelicData, RelicDragPhase> onRelicDrag = null)
        {
            if (_root == null || _loadoutInventoryUxml == null || _arcanaCardUxml == null) return;

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

            // Инвентарь = ТОЛЬКО тело; навигация (режимы) и меню — в глобальном топбаре (RunModeBar).
            // Sheet: навигатор НЕ глушит геймплей — под инвентарём живут юниты/камера (клики разводит
            // IInputService.PointerOverUI над панелями vs дыркой). Класс gm-screen--transparent остаётся
            // только как СТИЛЬ (прозрачный фон), из логики suppress ушёл в ScreenKind.Sheet.
            screen.AddToClassList(TransparentScreenClass);
            // Закрытие по любому пути (Pop/Esc/PopAll) → onClose (бутстрап снимет подсветку режима).
            screen.RegisterCallback<DetachFromPanelEvent>(_ => onClose?.Invoke());
            PushScreen(() => screen, ScreenKind.Sheet, modeTag: "inventory"); // QA #21: подсветка таба «Инвентарь»
        }

        /// <summary>Закрыть все оверлеи (режим «Бой»/выход в игру из глобального топбара). No-op если ничего не открыто.</summary>
        public void CloseOverlays() { if (_nav.IsOpen) _nav.PopAll(); }

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

        /// <summary>Закрыть все экраны и снять глушение ввода (навигатор пересчитает suppress из фазы).</summary>
        public void CloseAll() => _nav.PopAll();

        /// <summary>Режим-таб верхнего оверлея (QA #21): "inventory"/"map"/null. Единый источник подсветки топбара.</summary>
        public string ActiveScreenMode => _nav.ActiveModeTag;

        // Верхний экран, ВРЕМЕННО скрытый под тест-зоной (QA #2): карту петли акта не сносим (иначе resolve
        // узла = null → Aborted забег), а прячем — на выходе из тест-зоны возвращаем. Мост до Ф5 (там тест-зона
        // станет Sheet поверх карты и эти методы уйдут); suppress снимаем/пересчитываем через навигатор.
        private UiScreen _hiddenForTest;

        /// <summary>Скрыть верхний оверлей на время тест-зоны (не detach) и отдать ввод миру (QA #2).</summary>
        public void HideTopForTest()
        {
            _hiddenForTest = _nav.Top;
            if (_hiddenForTest?.Root != null) _hiddenForTest.Root.style.display = DisplayStyle.None;
            _input.GameplaySuppressed = false;
        }

        /// <summary>Вернуть скрытый оверлей после тест-зоны и пересчитать suppress из стека/фазы (QA #2).</summary>
        public void ShowHiddenForTest()
        {
            if (_hiddenForTest?.Root != null) _hiddenForTest.Root.style.display = DisplayStyle.Flex;
            _hiddenForTest = null;
            _nav.SyncInput();
        }

        /// <summary>Скрыт ли сейчас оверлей под тест-зоной (карта петли акта).</summary>
        public bool HasHiddenForTest => _hiddenForTest != null;

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
            screen.Q<Button>("btn-return").clicked += CloseAll;
            screen.Q<Button>("btn-settings").clicked += () => PushScreen(BuildSettingsScreen, ScreenKind.Modal);

            // QA #18: «В главное меню» прерывает забег (сейв остаётся — можно продолжить); «Выход» закрывает игру.
            var toMenu = screen.Q<Button>("btn-main-menu");
            if (toMenu != null)
            {
                toMenu.text = Loc("ui.menu.to_main_menu", "В главное меню");
                toMenu.clicked += () => { CloseAll(); _runControl?.RequestReturnToMainMenu(); };
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

            RewardChoiceResult result = await _nav.ShowAsync(screen); // экран снят ДО колбэка (навигатор, II.5)
            req.OnResolved?.Invoke(result);
        }

        // Экран текстового ивента (StS-style) — на UXML (EventScreen.uxml) через общий EventScreenView.
        // Выбор фиксирует последствие (колбэк → флоу применяет эффекты), затем показывается текст-результат.
        // Закрытие без выбора (ESC/PopAll) = -1, чтобы флоу не завис.
        public void OpenTextEvent(OpenTextEventRequest req)
        {
            if (_root == null || _eventUxml == null || req.Event == null) { req.OnChosen?.Invoke(-1); return; }
            PushScreen(() => BuildTextEventScreen(req), ScreenKind.Page);
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

        // Экран карты акта (A3) — на UXML (MapScreen.uxml) через общий MapScreenView. Клик по доступному узлу
        // возвращает его id и закрывает экран; закрытие без выбора (ESC/PopAll) шлёт null, чтобы петля не завис.
        public void OpenMap(OpenMapRequest req)
        {
            if (_root == null || _mapUxml == null) { req.OnChosen?.Invoke(null); return; }
            ShowMapAsync(req).Forget();
        }

        private async UniTaskVoid ShowMapAsync(OpenMapRequest req)
        {
            var screen = new RouterResultScreen<string>(ScreenKind.Page, null,
                resolve => MapScreenView.Build(
                    _mapUxml,
                    req.Map,
                    new HashSet<string>(req.AvailableNodeIds),
                    key => _loc?.GetString(key),
                    resolve),
                modeTag: "map"); // QA #21: подсветка таба «Карта»

            string nodeId = await _nav.ShowAsync(screen);
            req.OnChosen?.Invoke(nodeId);
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

            await _nav.ShowAsync(screen); // явный «Продолжить» и закрытие без нажатия → OnContinue (петля не виснет)
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

            await _nav.ShowAsync(screen); // «Уйти» и закрытие → OnLeave (петля продолжается)
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

            await _nav.ShowAsync(screen); // клик по крышке и закрытие → OnOpen (флоу катит награду)
            req.OnOpen?.Invoke();
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

            MainMenuChoice choice = await _nav.ShowAsync(screen); // снятие без выбора = Quit (верхний цикл не виснет)
            req.OnChoice?.Invoke(choice);
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
