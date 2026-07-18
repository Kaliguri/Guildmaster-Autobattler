using System;
using System.Collections.Generic;
using Guildmaster.Core.Input;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Реализация <see cref="IMenuRouter"/>: стек оверлейных экранов над корнем UITK-панели.
    /// Строит и проводит экраны (Pause + Settings) из UXML-шаблонов, отданных
    /// <see cref="Initialize"/> бутстрапом. На открытии первого экрана глушит локальный геймплейный
    /// ввод (<see cref="IInputService.GameplaySuppressed"/> + контекст Menu) и восстанавливает на закрытии.
    /// Ручной биндинг слайдер⇄VM (надёжнее рантайм-биндинга для трёх контролов). Настройки применяются
    /// живьём; Cancel/Save — на кнопках, ESC = навигация назад (без неявного отката).
    /// </summary>
    public sealed class MenuRouter : IMenuRouter
    {
        private readonly IInputService _input;
        private readonly SettingsViewModel _settingsVm;
        private readonly LoadoutViewModel _loadoutVm;
        private readonly LoadoutHubViewModel _hubVm;
        private readonly ILocalizationService _loc;

        private readonly Stack<VisualElement> _stack = new();
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
        private InputContext _prevContext;

        public MenuRouter(IInputService input, SettingsViewModel settingsVm, LoadoutViewModel loadoutVm,
                          LoadoutHubViewModel hubVm, ILocalizationService loc)
        {
            _input = input;
            _settingsVm = settingsVm;
            _loadoutVm = loadoutVm;
            _hubVm = hubVm;
            _loc = loc;
        }

        public bool IsOpen => _stack.Count > 0;

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
        }

        /// <summary>
        /// Открыть loadout-экран для юнита (по дабл-клику в фазе расстановки; публикуется как
        /// <see cref="OpenLoadoutRequest"/>, бутстрап зовёт сюда). Пушится как обычный оверлей поверх боя.
        /// </summary>
        public void OpenLoadout(OpenLoadoutRequest req)
        {
            if (_root == null || _loadoutUxml == null) return;
            _loadoutVm.Open(req);
            Push(BuildLoadoutScreen());
        }

        // Настройки поверх текущего экрана (топбар «Опции»): Push, а не ToggleSystemMenu — иначе при одном
        // экране на стеке (карта забега) сработал бы CloseAll и оборвал бы забег. Save/Cancel в настройках — Pop.
        public void OpenSettings()
        {
            if (_root == null || _settingsUxml == null) return;
            Push(BuildSettingsScreen());
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
            Push(container);
        }

        /// <summary>
        /// Новый полноэкранный лоадаут/инвентарь (редизайн, Ф3a): грид таро-карточек реликвий + детали.
        /// Открывается кнопкой «Хаб» в топбаре (заменил старый хаб-оверлей). <paramref name="onClose"/>
        /// зовётся на ЛЮБОМ закрытии (Pop/Esc/CloseAll) через DetachFromPanelEvent — бутстрап по нему
        /// возвращает ран-топбар. Реликвии — весь контент (фильтр по владению — Фаза 5); gold из RunState.
        /// </summary>
        public void OpenInventory(int gold, Action onClose)
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
                cardAttackAnimation: _settingsVm.CardAttackAnimation);

            // Инвентарь = ТОЛЬКО тело; навигация (режимы) и меню — в глобальном топбаре (RunModeBar).
            // Закрытие по любому пути (Pop/Esc/CloseAll) → onClose (бутстрап снимет подсветку режима).
            screen.RegisterCallback<DetachFromPanelEvent>(_ => onClose?.Invoke());
            Push(screen);
        }

        /// <summary>Закрыть все оверлеи (режим «Бой»/выход в игру из глобального топбара). No-op если ничего не открыто
        /// (иначе ExitMenuMode на пустом стеке восстанавливал бы контекст ввода вслепую — источник вылета).</summary>
        public void CloseOverlays() { if (IsOpen) CloseAll(); }

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

        public void ToggleSystemMenu()
        {
            if (_root == null) return;
            if (_stack.Count == 0) Push(BuildPauseScreen());
            else if (_stack.Count == 1) CloseAll();
            else Pop();
        }

        public void CloseAll()
        {
            while (_stack.Count > 0) _root.Remove(_stack.Pop());
            ExitMenuMode();
        }

        private void Push(VisualElement screen)
        {
            if (_stack.Count == 0) EnterMenuMode();
            else _stack.Peek().style.display = DisplayStyle.None;
            _stack.Push(screen);
            _root.Add(screen);
        }

        private void Pop()
        {
            if (_stack.Count == 0) return;
            _root.Remove(_stack.Pop());
            if (_stack.Count > 0) _stack.Peek().style.display = DisplayStyle.Flex;
            else ExitMenuMode();
        }

        private void EnterMenuMode()
        {
            _prevContext = _input.Context;
            _input.GameplaySuppressed = true;
            _input.SetContext(InputContext.Menu);
        }

        private void ExitMenuMode()
        {
            _input.GameplaySuppressed = false;
            _input.SetContext(_prevContext);
        }

        // --- Экраны ---

        private VisualElement BuildPauseScreen()
        {
            var screen = FillRoot(_pauseUxml.CloneTree());
            screen.Q<Button>("btn-return").clicked += CloseAll;
            screen.Q<Button>("btn-settings").clicked += () => Push(BuildSettingsScreen());
            return screen;
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

        // Экран награды (A3) — на UXML (RewardScreen.uxml) через общий RewardScreenView. Гарантирует ровно
        // один OnResolved, включая закрытие без выбора (= пропуск), чтобы флоу забега не завис.
        public void OpenReward(OpenRewardRequest req)
        {
            if (_root == null || _rewardUxml == null) { req.OnResolved?.Invoke(RewardChoiceResult.Skip); return; }
            Push(BuildRewardScreen(req));
        }

        private VisualElement BuildRewardScreen(OpenRewardRequest req)
        {
            bool resolved = false;

            void Resolve(RewardChoiceResult result)
            {
                if (resolved) return;
                resolved = true;
                CloseAll();                      // закрыть ДО колбэка: его inline-продолжение может открыть следующий экран
                req.OnResolved?.Invoke(result);
            }

            VisualElement screen = RewardScreenView.Build(
                _rewardUxml,
                req.Choices,
                req.InventoryFull,
                req.CurrentInventory,
                relic => _loadoutVm.Name(relic),
                key => _loc?.GetString(key),
                (chosen, dropId) => Resolve(dropId != null
                    ? RewardChoiceResult.Swap(chosen, dropId)
                    : RewardChoiceResult.Take(chosen)),
                () => Resolve(RewardChoiceResult.Skip));

            // Страховка: любое снятие экрана без явного выбора (ESC/CloseAll) = пропуск, чтобы флоу не завис.
            screen.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (!resolved) { resolved = true; req.OnResolved?.Invoke(RewardChoiceResult.Skip); }
            });

            return screen;
        }

        // Экран текстового ивента (StS-style) — на UXML (EventScreen.uxml) через общий EventScreenView.
        // Выбор фиксирует последствие (колбэк → флоу применяет эффекты), затем показывается текст-результат.
        // Закрытие без выбора (ESC/CloseAll) = -1, чтобы флоу не завис.
        public void OpenTextEvent(OpenTextEventRequest req)
        {
            if (_root == null || _eventUxml == null || req.Event == null) { req.OnChosen?.Invoke(-1); return; }
            Push(BuildTextEventScreen(req));
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

            // Страховка: закрытие без выбора (ESC/CloseAll) = пропуск (-1), чтобы флоу не завис.
            screen.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (!resolved) { resolved = true; req.OnChosen?.Invoke(-1); }
            });

            return screen;
        }

        // Экран карты акта (A3) — на UXML (MapScreen.uxml) через общий MapScreenView. Клик по доступному узлу
        // возвращает его id и закрывает экран; закрытие без выбора (ESC/CloseAll) шлёт null, чтобы петля не завис.
        public void OpenMap(OpenMapRequest req)
        {
            if (_root == null || _mapUxml == null) { req.OnChosen?.Invoke(null); return; }
            Push(BuildMapScreen(req));
        }

        private VisualElement BuildMapScreen(OpenMapRequest req)
        {
            bool resolved = false;

            void Resolve(string nodeId)
            {
                if (resolved) return;
                resolved = true;
                CloseAll();                      // закрыть карту ДО колбэка (его продолжение откроет узел)
                req.OnChosen?.Invoke(nodeId);
            }

            VisualElement screen = MapScreenView.Build(
                _mapUxml,
                req.Map,
                new HashSet<string>(req.AvailableNodeIds),
                key => _loc?.GetString(key),
                Resolve);

            // Страховка: снятие экрана без выбора (ESC/CloseAll) = null, чтобы петля акта не зависла.
            screen.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (!resolved) { resolved = true; req.OnChosen?.Invoke(null); }
            });

            return screen;
        }

        // Единая кнопка «Продолжить» (A4) — оверлей с кнопкой в правом нижнем углу. Нажатие резолвит и закрывает;
        // закрытие без нажатия (ESC/CloseAll) тоже резолвит, чтобы петля акта не зависла.
        public void ShowContinue(OpenContinueRequest req)
        {
            if (_root == null || _continueUxml == null) { req.OnContinue?.Invoke(); return; }
            Push(BuildContinueScreen(req));
        }

        private VisualElement BuildContinueScreen(OpenContinueRequest req)
        {
            bool resolved = false;

            void Resolve()
            {
                if (resolved) return;
                resolved = true;
                CloseAll();                      // закрыть ДО колбэка (его продолжение вернёт на карту)
                req.OnContinue?.Invoke();
            }

            var screen = FillRoot(_continueUxml.CloneTree());
            var btn = screen.Q<Button>("btn-continue");
            if (btn != null)
            {
                if (!string.IsNullOrEmpty(req.LabelKey))
                {
                    string label = _loc?.GetString(req.LabelKey);
                    if (!string.IsNullOrEmpty(label)) btn.text = label;
                }
                btn.clicked += Resolve;
            }

            // Страховка: снятие без нажатия (ESC/CloseAll) = резолв, чтобы петля не зависла.
            screen.RegisterCallback<DetachFromPanelEvent>(_ => { if (!resolved) { resolved = true; req.OnContinue?.Invoke(); } });
            return screen;
        }

        // Экран магазина (B2) — на UXML (ShopScreen.uxml) через общий ShopScreenView, биндится к IShopController.
        // «Уйти»/закрытие резолвит OnLeave (петля продолжается). Ровно один вызов.
        public void OpenShop(OpenShopRequest req)
        {
            if (_root == null || _shopUxml == null || req.Shop == null) { req.OnLeave?.Invoke(); return; }
            Push(BuildShopScreen(req));
        }

        private VisualElement BuildShopScreen(OpenShopRequest req)
        {
            bool resolved = false;

            void Resolve()
            {
                if (resolved) return;
                resolved = true;
                CloseAll();                      // закрыть магазин ДО колбэка (его продолжение вернёт на карту)
                req.OnLeave?.Invoke();
            }

            VisualElement screen = ShopScreenView.Build(
                _shopUxml,
                req.Shop,
                relic => _loadoutVm.Name(relic),
                key => _loc?.GetString(key),
                Resolve);

            // Растянуть оверлей на весь корень + страховка: закрытие без «Уйти» = резолв, чтобы петля не зависла.
            screen.RegisterCallback<DetachFromPanelEvent>(_ => { if (!resolved) { resolved = true; req.OnLeave?.Invoke(); } });
            return screen;
        }

        // Экран сундука (B3) — на UXML (ChestScreen.uxml). Клик по крышке резолвит OnOpen (флоу катит награду),
        // затем сундук закрывается. Закрытие без клика тоже резолвит, чтобы флоу не завис.
        public void OpenChest(OpenChestRequest req)
        {
            if (_root == null || _chestUxml == null) { req.OnOpen?.Invoke(); return; }
            Push(BuildChestScreen(req));
        }

        private VisualElement BuildChestScreen(OpenChestRequest req)
        {
            bool resolved = false;

            void Resolve()
            {
                if (resolved) return;
                resolved = true;
                CloseAll();                      // закрыть сундук ДО колбэка (его продолжение откроет награду)
                req.OnOpen?.Invoke();
            }

            VisualElement screen = ChestScreenView.Build(_chestUxml, key => _loc?.GetString(key), Resolve);
            screen.RegisterCallback<DetachFromPanelEvent>(_ => { if (!resolved) { resolved = true; req.OnOpen?.Invoke(); } });
            return screen;
        }

        // Экран исхода забега (C2) — на UXML (OutcomeScreen.uxml). «В меню» резолвит OnToMenu; закрытие тоже.
        public void ShowOutcome(OpenOutcomeRequest req)
        {
            if (_root == null || _outcomeUxml == null) { req.OnToMenu?.Invoke(); return; }
            Push(BuildOutcomeScreen(req));
        }

        private VisualElement BuildOutcomeScreen(OpenOutcomeRequest req)
        {
            bool resolved = false;

            void Resolve()
            {
                if (resolved) return;
                resolved = true;
                CloseAll();                      // закрыть исход ДО колбэка (его продолжение откроет меню)
                req.OnToMenu?.Invoke();
            }

            VisualElement screen = OutcomeScreenView.Build(_outcomeUxml, req.Victory, key => _loc?.GetString(key), Resolve);
            screen.RegisterCallback<DetachFromPanelEvent>(_ => { if (!resolved) { resolved = true; req.OnToMenu?.Invoke(); } });
            return screen;
        }

        // Главное меню (D1) — на UXML (MainMenuScreen.uxml). Начать/Продолжить/Выход резолвят OnChoice и закрывают
        // меню; «Настройки» открываются поверх (Push) и меню не закрывают.
        public void OpenMainMenu(OpenMainMenuRequest req)
        {
            if (_root == null || _mainMenuUxml == null) { req.OnChoice?.Invoke(MainMenuChoice.Quit); return; }
            Push(BuildMainMenuScreen(req));
        }

        private VisualElement BuildMainMenuScreen(OpenMainMenuRequest req)
        {
            bool resolved = false;

            void Resolve(MainMenuChoice choice)
            {
                if (resolved) return;
                resolved = true;
                CloseAll();                      // закрыть меню ДО колбэка (его продолжение откроет карту забега)
                req.OnChoice?.Invoke(choice);
            }

            VisualElement screen = MainMenuScreenView.Build(
                _mainMenuUxml,
                req.HasSave,
                key => _loc?.GetString(key),
                onStart:    () => Resolve(MainMenuChoice.StartRun),
                onContinue: () => Resolve(MainMenuChoice.Continue),
                onSettings: () => Push(BuildSettingsScreen()),
                onQuit:     () => Resolve(MainMenuChoice.Quit));

            // Страховка: снятие без выбора = выход, чтобы верхний цикл не завис.
            screen.RegisterCallback<DetachFromPanelEvent>(_ => { if (!resolved) { resolved = true; req.OnChoice?.Invoke(MainMenuChoice.Quit); } });
            return screen;
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
