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
