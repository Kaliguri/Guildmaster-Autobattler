using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Верхняя панель забега (StS-style, план 12 Фаза 2) из UXML-шаблона — общий код для живого HUD и
    /// превью-стенда. Слева: хаб + опции + золото; центр: «Начать» (в бою заменяется таймером боя);
    /// справа: акт + время забега. Разметка/стиль — только из <c>RunTopBar.uxml</c> + дизайн-система.
    /// Инстанс держит ссылки на элементы и обновляется live-сеттерами (<see cref="SetGold"/> и др.);
    /// статический <see cref="Build"/> — тонкая обёртка для стенда (разовый снимок).
    /// </summary>
    public sealed class RunTopBarView
    {
        /// <summary>Корневой элемент панели (добавить в HUD-слой / превью-контейнер).</summary>
        public VisualElement Root { get; }

        private readonly Label  _gold;
        private readonly Label  _act;
        private readonly Label  _runTimer;
        private readonly Label  _battleTimer;
        private readonly Button _start;
        private readonly Func<string, string> _loc;

        public RunTopBarView(
            VisualTreeAsset uxml,
            Func<string, string> localize,
            Action onHub,
            Action onSettings,
            Action onStart)
        {
            _loc = localize;

            VisualElement tree = uxml.CloneTree();
            Root = tree.childCount > 0 ? tree[0] : tree;

            _gold        = Root.Q<Label>("topbar-gold");
            _act         = Root.Q<Label>("topbar-act");
            _runTimer    = Root.Q<Label>("topbar-timer");
            _battleTimer = Root.Q<Label>("battle-timer");
            _start       = Root.Q<Button>("btn-start");

            var hub = Root.Q<Button>("btn-hub");
            if (hub != null) { hub.text = L("ui.run.hub", "Гильдия"); hub.clicked += () => onHub?.Invoke(); }

            var settings = Root.Q<Button>("btn-settings");
            if (settings != null) { settings.text = L("ui.run.settings", "Опции"); settings.clicked += () => onSettings?.Invoke(); }

            if (_start != null) { _start.text = L("ui.run.start", "Начать"); _start.clicked += () => onStart?.Invoke(); }
        }

        /// <summary>Золото гильдии (слева).</summary>
        public void SetGold(int gold) => SetText(_gold, L("ui.run.gold", "Золото") + ": " + gold);

        /// <summary>Номер акта (справа).</summary>
        public void SetAct(int actNumber) => SetText(_act, L("ui.run.act", "Акт") + " " + actNumber);

        /// <summary>Время забега (справа) — форматированная строка mm:ss.</summary>
        public void SetRunTime(string timerText) => SetText(_runTimer, timerText);

        /// <summary>
        /// Переключить центр панели: бой идёт (<paramref name="fighting"/>=true) → таймер боя вместо кнопки
        /// «Начать»; расстановка → кнопка «Начать» вместо таймера.
        /// </summary>
        public void SetFighting(bool fighting, string battleTime)
        {
            if (_start != null)       _start.style.display       = fighting ? DisplayStyle.None : DisplayStyle.Flex;
            if (_battleTimer != null)
            {
                _battleTimer.style.display = fighting ? DisplayStyle.Flex : DisplayStyle.None;
                _battleTimer.text = battleTime;
            }
        }

        /// <summary>Тонкая обёртка для превью-стенда: разовый снимок панели без live-обновления.</summary>
        public static VisualElement Build(
            VisualTreeAsset uxml,
            int gold,
            int actNumber,
            string timerText,
            Func<string, string> localize,
            Action onHub,
            Action onSettings,
            Action onStart)
        {
            var view = new RunTopBarView(uxml, localize, onHub, onSettings, onStart);
            view.SetGold(gold);
            view.SetAct(actNumber);
            view.SetRunTime(timerText);
            return view.Root;
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
