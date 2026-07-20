using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Глобальная панель забега (app-shell, редизайн 2026-07-20): всегда сверху во время забега, тело
    /// экранов сдвинуто под неё. Слева гильдия + возвышение + акт одной строкой; центр — лента-островок
    /// с иконками-режимами и отделённым бургером; справа — капсулы золота и перезапусков. Время забега
    /// выключено (узел жив как шов). Подпись режима показывается подсказкой при наведении: иконка без
    /// имени — угадайка. Разметка/стиль — только из <c>RunModeBar.uxml</c> + дизайн-система.
    /// </summary>
    public sealed class RunModeBarView
    {
        public VisualElement Root { get; }

        private readonly Label  _gold;
        private readonly Label  _act;
        private readonly Label  _runTimer;
        private readonly Label  _battleTimer;
        private readonly Label  _restarts;
        private readonly Button _start;
        private readonly Func<string, string> _loc;
        private readonly Dictionary<string, Button> _modes = new();

        public RunModeBarView(
            VisualTreeAsset uxml,
            Func<string, string> localize,
            Action onMap, Action onBattle, Action onInventory, Action onTactics, Action onCompendium,
            Action onMenu, Action onStart)
        {
            _loc = localize;

            VisualElement tree = uxml.CloneTree();
            Root = tree.childCount > 0 ? tree[0] : tree;

            _gold        = Root.Q<Label>("topbar-gold");
            _act         = Root.Q<Label>("topbar-act");
            _runTimer    = Root.Q<Label>("topbar-timer");
            _battleTimer = Root.Q<Label>("battle-timer");
            _restarts    = Root.Q<Label>("topbar-hp");
            _start       = Root.Q<Button>("btn-start");

            WireMode("map",        "ui.mode.map",        "Карта",      onMap);
            WireMode("battle",     "ui.mode.battle",     "Бой",        onBattle);
            WireMode("inventory",  "ui.mode.inventory",  "Инвентарь",  onInventory);
            WireMode("tactics",    "ui.mode.tactics",    "Тактика",    onTactics);
            WireMode("compendium", "ui.mode.compendium", "Компендиум", onCompendium);

            var menu = Root.Q<Button>("btn-menu");
            if (menu != null) menu.clicked += () => onMenu?.Invoke();

            if (_start != null) { _start.text = L("ui.run.start", "Начать"); _start.clicked += () => onStart?.Invoke(); }
        }

        private void WireMode(string key, string locKey, string ru, Action action)
        {
            var btn = Root.Q<Button>("mode-" + key);
            if (btn == null) return;
            btn.clicked += () => action?.Invoke();
            // Ключ имени режима резолвим и без подсказки: он держит loc-ключ живым до прихода общей
            // системы тултипов, которая и будет показывать подпись при наведении.
            _ = L(locKey, ru);
            _modes[key] = btn;
        }

        /// <summary>Подсветить активный режим (остальные — тусклые). null — снять подсветку со всех.</summary>
        public void SetActiveMode(string key)
        {
            foreach (var kv in _modes)
                kv.Value.EnableInClassList("gm-runbar__tab--active", kv.Key == key);
        }

        public void SetGold(int gold) => SetText(_gold, gold.ToString());
        public void SetAct(int actNumber) => SetText(_act, "· " + L("ui.run.act", "Акт") + " " + actNumber);

        /// <summary>Время забега выключено (реш. 2026-07-20): узел скрыт классом, сеттер держит шов живым.</summary>
        public void SetRunTime(string timerText) => SetText(_runTimer, timerText);

        /// <summary>ХП забега = перезапуски-на-акт (реш. №65): показываем счётчиком «остаток / максимум».</summary>
        public void SetRestarts(int remaining, int max) => SetText(_restarts, remaining + " / " + max);

        /// <summary>Нет боя (карта/магазин): ни «Начать», ни таймера боя.</summary>
        public void HideBattleCenter()
        {
            if (_start != null)       _start.style.display       = DisplayStyle.None;
            if (_battleTimer != null) _battleTimer.style.display = DisplayStyle.None;
        }

        /// <summary>Бой идёт → таймер боя вместо «Начать»; расстановка → «Начать» вместо таймера.</summary>
        public void SetFighting(bool fighting, string battleTime)
        {
            if (_start != null)       _start.style.display       = fighting ? DisplayStyle.None : DisplayStyle.Flex;
            if (_battleTimer != null)
            {
                _battleTimer.style.display = fighting ? DisplayStyle.Flex : DisplayStyle.None;
                _battleTimer.text = battleTime;
            }
        }

        private string L(string key, string ru)
        {
            string v = _loc?.Invoke(key);
            return string.IsNullOrEmpty(v) ? ru : v;
        }

        private static void SetText(Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
