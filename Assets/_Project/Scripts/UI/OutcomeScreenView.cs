using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка экрана исхода забега (план [[act-map-run-loop]] §4 C2): заголовок Победа/Поражение + «В меню».
    /// Общий код для роутера и превью-стенда. Разметка/стиль — из <c>OutcomeScreen.uxml</c> + дизайн-система.
    /// </summary>
    public static class OutcomeScreenView
    {
        public static VisualElement Build(VisualTreeAsset uxml, bool victory, Func<string, string> localize, Action onToMenu)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title = root.Q<Label>("outcome-title");
            var sub   = root.Q<Label>("outcome-sub");
            var menu  = root.Q<Button>("btn-menu");

            if (title != null)
            {
                title.text = victory ? L("ui.outcome.victory", "Победа") : L("ui.outcome.defeat", "Поражение");
                title.EnableInClassList("gm-outcome__title--defeat", !victory);
            }
            if (sub != null)
                sub.text = victory ? L("ui.outcome.victory_sub", "Акт пройден.")
                                   : L("ui.outcome.defeat_sub", "Забег окончен.");
            if (menu != null) { menu.text = L("ui.outcome.to_menu", "В меню"); menu.clicked += () => onToMenu?.Invoke(); }

            return root;
        }
    }
}
