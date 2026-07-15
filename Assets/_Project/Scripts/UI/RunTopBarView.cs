using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка верхней панели забега (StS-style, Фаза 2) из UXML-шаблона — общий код для живого UI и
    /// превью-стенда. Слева: хаб + опции + золото; центр: «Начать» (в бою заменяется таймером боя);
    /// справа: акт + таймер забега. Разметка/стиль — только из <c>RunTopBar.uxml</c> + дизайн-система.
    /// </summary>
    public static class RunTopBarView
    {
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
            string L(string key, string ru)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? ru : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;

            SetText(root, "topbar-gold",  L("ui.run.gold", "Золото") + ": " + gold);
            SetText(root, "topbar-act",   L("ui.run.act", "Акт") + " " + actNumber);
            SetText(root, "topbar-timer", timerText);

            var hub = root.Q<Button>("btn-hub");
            if (hub != null) { hub.text = L("ui.run.hub", "Гильдия"); hub.clicked += () => onHub?.Invoke(); }

            var settings = root.Q<Button>("btn-settings");
            if (settings != null) { settings.text = L("ui.run.settings", "Опции"); settings.clicked += () => onSettings?.Invoke(); }

            var start = root.Q<Button>("btn-start");
            if (start != null) { start.text = L("ui.run.start", "Начать"); start.clicked += () => onStart?.Invoke(); }

            return root;
        }

        private static void SetText(VisualElement root, string name, string text)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = text;
        }
    }
}
