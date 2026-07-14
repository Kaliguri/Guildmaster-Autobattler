using System;
using System.Collections.Generic;
using Guildmaster.Core.Input;
using Guildmaster.Data.Definitions;
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

        private readonly Stack<VisualElement> _stack = new();
        private VisualElement _root;
        private VisualTreeAsset _pauseUxml;
        private VisualTreeAsset _settingsUxml;
        private VisualTreeAsset _loadoutUxml;
        private InputContext _prevContext;

        public MenuRouter(IInputService input, SettingsViewModel settingsVm, LoadoutViewModel loadoutVm)
        {
            _input = input;
            _settingsVm = settingsVm;
            _loadoutVm = loadoutVm;
        }

        public bool IsOpen => _stack.Count > 0;

        /// <summary>Бутстрап отдаёт корень панели и UXML-шаблоны экранов (ссылки из сцены, не DI).</summary>
        public void Initialize(VisualElement root, VisualTreeAsset pauseUxml, VisualTreeAsset settingsUxml,
            VisualTreeAsset loadoutUxml = null)
        {
            _root = root;
            _pauseUxml = pauseUxml;
            _settingsUxml = settingsUxml;
            _loadoutUxml = loadoutUxml;
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

            var master = screen.Q<Slider>("slider-master");
            var music = screen.Q<Slider>("slider-music");
            var sfx = screen.Q<Slider>("slider-sfx");
            var masterVal = screen.Q<Label>("val-master");
            var musicVal = screen.Q<Label>("val-music");
            var sfxVal = screen.Q<Label>("val-sfx");

            _settingsVm.BeginEdit();

            void Sync()
            {
                master.SetValueWithoutNotify(_settingsVm.Master);
                music.SetValueWithoutNotify(_settingsVm.Music);
                sfx.SetValueWithoutNotify(_settingsVm.Sfx);
                masterVal.text = Percent(_settingsVm.Master);
                musicVal.text = Percent(_settingsVm.Music);
                sfxVal.text = Percent(_settingsVm.Sfx);
            }

            Sync();

            master.RegisterValueChangedCallback(e => { _settingsVm.SetMaster(e.newValue); masterVal.text = Percent(e.newValue); });
            music.RegisterValueChangedCallback(e => { _settingsVm.SetMusic(e.newValue); musicVal.text = Percent(e.newValue); });
            sfx.RegisterValueChangedCallback(e => { _settingsVm.SetSfx(e.newValue); sfxVal.text = Percent(e.newValue); });

            // VM → слайдеры (Defaults/Cancel меняют значения «снаружи»). Отписка при снятии с панели.
            Action onChanged = Sync;
            _settingsVm.Changed += onChanged;
            screen.RegisterCallback<DetachFromPanelEvent>(_ => _settingsVm.Changed -= onChanged);

            screen.Q<Button>("btn-save").clicked += () => { _settingsVm.Save(); Pop(); };
            screen.Q<Button>("btn-cancel").clicked += () => { _settingsVm.Cancel(); Pop(); };
            screen.Q<Button>("btn-defaults").clicked += () => _settingsVm.ResetToDefaults();
            return screen;
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

        // Экран награды (A3). Построен кодом (без UXML) — не требует правки сцены; дизайн-полиш/переезд на
        // UXML отложены (implement-then-review). Гарантирует ровно один OnResolved, включая закрытие = пропуск.
        public void OpenReward(OpenRewardRequest req)
        {
            if (_root == null) { req.OnResolved?.Invoke(RewardChoiceResult.Skip); return; }
            Push(BuildRewardScreen(req));
        }

        private VisualElement BuildRewardScreen(OpenRewardRequest req)
        {
            RelicData chosen = null;
            string    drop   = null;
            bool      resolved = false;

            var screen = new VisualElement { pickingMode = PickingMode.Position };
            screen.style.position = Position.Absolute;
            screen.style.left = 0; screen.style.top = 0; screen.style.right = 0; screen.style.bottom = 0;
            screen.style.alignItems = Align.Center;
            screen.style.justifyContent = Justify.Center;
            screen.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f); // затемнение боя за оверлеем

            var panel = new VisualElement();
            panel.AddToClassList("gm-panel");
            panel.style.minWidth = 420;
            panel.style.maxWidth = 640;
            screen.Add(panel);

            var title = new Label("Награда — выбери реликвию");
            title.AddToClassList("gm-panel__title");
            panel.Add(title);

            var divider = new VisualElement(); divider.AddToClassList("gm-divider"); panel.Add(divider);

            // ── Витрина выбора ──
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;
            panel.Add(row);

            var takeBtn = new Button { text = "Взять" };
            takeBtn.AddToClassList("gm-button");

            var choiceCards = new List<(RelicData relic, VisualElement card)>();
            var dropRows    = new List<(string id, VisualElement row)>();

            void Refresh()
            {
                foreach (var (relic, card) in choiceCards)
                    card.EnableInClassList("gm-card--selected", ReferenceEquals(relic, chosen));
                foreach (var (id, r) in dropRows)
                    r.style.backgroundColor = id == drop ? new Color(1f, 1f, 1f, 0.12f) : new Color(0, 0, 0, 0);
                bool canTake = chosen != null && (!req.InventoryFull || drop != null);
                takeBtn.SetEnabled(canTake);
            }

            IReadOnlyList<RelicData> choices = req.Choices;
            for (int i = 0; i < choices.Count; i++)
            {
                RelicData relic = choices[i];
                var card = new VisualElement();
                card.AddToClassList("gm-card");
                card.style.width = 150;

                var sprite = new VisualElement();
                sprite.AddToClassList("gm-card__sprite");
                if (relic != null && relic.Icon != null) sprite.style.backgroundImage = new StyleBackground(relic.Icon);
                card.Add(sprite);

                var name = new Label(_loadoutVm.Name(relic));
                name.AddToClassList("gm-card__name");
                card.Add(name);

                card.RegisterCallback<ClickEvent>(_ => { chosen = relic; Refresh(); });
                row.Add(card);
                choiceCards.Add((relic, card));
            }

            // ── Секция сброса (только при полном запасе, §5.4) ──
            if (req.InventoryFull)
            {
                var full = new Label("Запас реликвий полон — выбери, что сбросить:");
                full.AddToClassList("gm-text-muted");
                full.style.whiteSpace = WhiteSpace.Normal;
                full.style.marginTop = 6;
                panel.Add(full);

                IReadOnlyList<string> inv = req.CurrentInventory;
                for (int i = 0; inv != null && i < inv.Count; i++)
                {
                    string id = inv[i];
                    var r = new Label(id);
                    r.AddToClassList("gm-text-muted");
                    r.style.paddingTop = 2; r.style.paddingBottom = 2;
                    r.RegisterCallback<ClickEvent>(_ => { drop = id; Refresh(); });
                    panel.Add(r);
                    dropRows.Add((id, r));
                }
            }

            var footDivider = new VisualElement(); footDivider.AddToClassList("gm-divider"); panel.Add(footDivider);

            void Resolve(RewardChoiceResult result)
            {
                if (resolved) return;
                resolved = true;
                req.OnResolved?.Invoke(result);
                CloseAll();
            }

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.SpaceBetween;
            panel.Add(footer);

            var skipBtn = new Button(() => Resolve(RewardChoiceResult.Skip)) { text = "Пропустить" };
            skipBtn.AddToClassList("gm-button");
            footer.Add(skipBtn);

            takeBtn.clicked += () =>
            {
                if (chosen == null) return;
                Resolve(req.InventoryFull
                    ? RewardChoiceResult.Swap(chosen, drop)
                    : RewardChoiceResult.Take(chosen));
            };
            footer.Add(takeBtn);

            Refresh();

            // Страховка: любое снятие экрана без явного выбора (ESC/CloseAll) = пропуск, чтобы флоу не завис.
            screen.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (!resolved) { resolved = true; req.OnResolved?.Invoke(RewardChoiceResult.Skip); }
            });

            return screen;
        }

        // Экран текстового ивента (StS-style). Построен кодом (без UXML). Выбор фиксирует последствие
        // (колбэк → флоу применяет эффекты), затем показывается текст-результат. Закрытие без выбора = -1.
        public void OpenTextEvent(OpenTextEventRequest req)
        {
            if (_root == null || req.Event == null) { req.OnChosen?.Invoke(-1); return; }
            Push(BuildTextEventScreen(req));
        }

        private VisualElement BuildTextEventScreen(OpenTextEventRequest req)
        {
            TextEventData ev = req.Event;
            bool resolved = false;

            var screen = new VisualElement { pickingMode = PickingMode.Position };
            screen.style.position = Position.Absolute;
            screen.style.left = 0; screen.style.top = 0; screen.style.right = 0; screen.style.bottom = 0;
            screen.style.alignItems = Align.Center;
            screen.style.justifyContent = Justify.Center;
            screen.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);

            var panel = new VisualElement();
            panel.AddToClassList("gm-panel");
            panel.style.minWidth = 460;
            panel.style.maxWidth = 640;
            screen.Add(panel);

            var title = new Label(ev.Title ?? ev.Id);
            title.AddToClassList("gm-panel__title");
            panel.Add(title);

            var divider = new VisualElement(); divider.AddToClassList("gm-divider"); panel.Add(divider);

            if (ev.Image != null)
            {
                var image = new VisualElement();
                image.style.height = 180;
                image.style.marginBottom = 8;
                image.style.backgroundImage = new StyleBackground(ev.Image);
                image.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                panel.Add(image);
            }

            var body = new Label(ev.Body);
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.marginBottom = 10;
            panel.Add(body);

            var buttons = new VisualElement();
            panel.Add(buttons);

            void Resolve(int index)
            {
                if (resolved) return;
                resolved = true;
                req.OnChosen?.Invoke(index);
            }

            void ShowResult(string resultText)
            {
                buttons.Clear();
                if (string.IsNullOrEmpty(resultText)) { CloseAll(); return; }
                body.text = resultText;
                var cont = new Button(CloseAll) { text = "Продолжить" };
                cont.AddToClassList("gm-button");
                buttons.Add(cont);
            }

            IReadOnlyList<EventChoice> choices = ev.Choices;
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i; // захват копии
                EventChoice choice = choices[i];
                var btn = new Button(() => { Resolve(index); ShowResult(choice.ResultText); }) { text = choice.Label };
                btn.AddToClassList("gm-button");
                btn.style.marginTop = 4;
                buttons.Add(btn);
            }

            // Страховка: закрытие без выбора (ESC/CloseAll) = пропуск (-1), чтобы флоу не завис.
            screen.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (!resolved) { resolved = true; req.OnChosen?.Invoke(-1); }
            });

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
