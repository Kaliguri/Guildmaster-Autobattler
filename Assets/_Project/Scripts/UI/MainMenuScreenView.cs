using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка главного меню (план [[act-map-run-loop]] §4 D1): Начать / Продолжить / Настройки / Выход. Общий код
    /// для роутера и превью-стенда. «Продолжить» активна только при наличии автосейва (<paramref name="hasSave"/>).
    /// </summary>
    public static class MainMenuScreenView
    {
        public static VisualElement Build(
            VisualTreeAsset uxml,
            bool hasSave,
            Func<string, string> localize,
            Action onStart,
            Action onContinue,
            Action onSettings,
            Action onQuit)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title    = root.Q<Label>("menu-title");
            var start    = root.Q<Button>("btn-start");
            var cont     = root.Q<Button>("btn-continue");
            var settings = root.Q<Button>("btn-settings");
            var quit     = root.Q<Button>("btn-quit");

            if (title != null) title.text = L("ui.mainmenu.title", "Happy Guildmasters");

            if (start != null)    { start.text    = L("ui.mainmenu.start", "Начать забег"); start.clicked += () => onStart?.Invoke(); }
            if (cont != null)
            {
                cont.text = L("ui.mainmenu.continue", "Продолжить");
                cont.SetEnabled(hasSave);
                cont.clicked += () => onContinue?.Invoke();
            }
            if (settings != null) { settings.text = L("ui.mainmenu.settings", "Настройки"); settings.clicked += () => onSettings?.Invoke(); }
            if (quit != null)     { quit.text     = L("ui.mainmenu.quit", "Выход"); quit.clicked += () => onQuit?.Invoke(); }

            return root;
        }
    }
}
