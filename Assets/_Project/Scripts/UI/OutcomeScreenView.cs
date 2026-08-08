using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка экрана исхода (план [[act-map-run-loop]] §4 C2): заголовок Победа/Поражение и выходы.
    /// Общий код для роутера и превью-стенда. Разметка/стиль — из <c>OutcomeScreen.uxml</c> + дизайн-система.
    /// </summary>
    /// <remarks>
    /// <b>Кнопку решает вызывающий.</b> После забега это «заново», «во двор» и «в меню»; на площадке —
    /// «продолжить» и «в меню». <c>null</c> вместо действия означает «здесь этого выхода нет», и кнопки
    /// не будет вовсе: погашенная обещала бы возможность, которой не существует.
    /// <para><b>Главной подсвечивается та, что ведёт вперёд</b>, и она на каждом экране своя: на
    /// площадке — «продолжить», после забега — «заново», а если и его нет — «в меню».</para>
    /// </remarks>
    public static class OutcomeScreenView
    {
        public static VisualElement Build(VisualTreeAsset uxml, bool victory, Func<string, string> localize,
                                          Action onToMenu, Action onContinue = null,
                                          Action onRestart = null, Action onToGuild = null)
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

            if (title != null)
            {
                title.text = victory ? L("ui.outcome.victory", "Победа") : L("ui.outcome.defeat", "Поражение");
                title.EnableInClassList("gm-outcome__title--defeat", !victory);
            }
            if (sub != null)
                sub.text = victory ? L("ui.outcome.victory_sub", "Акт пройден.")
                                   : L("ui.outcome.defeat_sub", "Забег окончен.");

            Bind(root, "btn-continue", L("ui.outcome.continue",  "Продолжить"),      onContinue);
            Bind(root, "btn-restart",  L("ui.outcome.restart",   "Начать заново"),   onRestart);
            Bind(root, "btn-guild",    L("ui.outcome.to_guild",  "Во двор гильдии"), onToGuild);
            Bind(root, "btn-menu",     L("ui.outcome.to_menu",   "В меню"),          onToMenu);

            // Вперёд ведёт первая из существующих: «продолжить» на площадке, «заново» после забега, а
            // когда нет ни того ни другого — остаётся «в меню».
            string primary = onContinue != null ? "btn-continue"
                           : onRestart  != null ? "btn-restart"
                                                : "btn-menu";
            Highlight(root, "btn-continue", primary);
            Highlight(root, "btn-restart",  primary);
            Highlight(root, "btn-guild",    primary);
            Highlight(root, "btn-menu",     primary);

            return root;
        }

        private static void Bind(VisualElement root, string name, string label, Action action)
        {
            var button = root.Q<Button>(name);
            if (button == null) return;

            if (action == null)
            {
                button.style.display = DisplayStyle.None;
                return;
            }

            button.text     = label;
            button.clicked += () => action();
        }

        private static void Highlight(VisualElement root, string name, string primary) =>
            root.Q<Button>(name)?.EnableInClassList("gm-button--primary", name == primary);

        /// <summary>
        /// Показать на общей кнопке, скольких ещё ждём. В одиночку счёт не рисуется: «(1/1)» не сообщает
        /// ничего, а место на кнопке занимает.
        /// </summary>
        /// <remarks>
        /// Отдельный метод, а не параметр сборки: счёт меняется, пока экран открыт, — второй игрок
        /// подтверждает уже после того, как первый увидел итог. Подпись обязана меняться вместе с ним,
        /// иначе подтвердивший смотрит на кнопку, которая молчит.
        /// <para><paramref name="votedHere"/> — голоса именно за ЭТУ кнопку, а не за решение вообще:
        /// после забега кнопок две, и общий счёт на обеих означал бы, что напарник согласился с тобой,
        /// когда он выбрал соседнюю.</para>
        /// </remarks>
        public static void SetSharedCount(VisualElement root, string buttonName, string labelKey,
                                          string fallback, Func<string, string> localize,
                                          int votedHere, int required, bool pickedByUs)
        {
            var button = root?.Q<Button>(buttonName);
            if (button == null || button.style.display == DisplayStyle.None) return;

            string label = localize?.Invoke(labelKey);
            if (string.IsNullOrEmpty(label)) label = fallback;

            button.text = required > 1 ? $"{label} ({votedHere}/{required})" : label;
            button.EnableInClassList("gm-btn--pending", required > 1 && pickedByUs);
        }
    }
}
