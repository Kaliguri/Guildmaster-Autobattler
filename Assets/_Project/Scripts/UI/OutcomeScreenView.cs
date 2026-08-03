using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка экрана исхода (план [[act-map-run-loop]] §4 C2): заголовок Победа/Поражение и выход.
    /// Общий код для роутера и превью-стенда. Разметка/стиль — из <c>OutcomeScreen.uxml</c> + дизайн-система.
    /// </summary>
    /// <remarks>
    /// <b>«Продолжить» показывается не всегда.</b> После забега продолжать нечего — акт кончился, и
    /// единственный выход ведёт в меню. На площадке наоборот: состав и расстановка целы, драться тем же
    /// строем снова — обычное дело, а не исключение. Поэтому кнопку решает вызывающий: <c>null</c>
    /// означает «здесь продолжать нечем», и кнопки не будет вовсе.
    /// </remarks>
    public static class OutcomeScreenView
    {
        public static VisualElement Build(VisualTreeAsset uxml, bool victory, Func<string, string> localize,
                                          Action onToMenu, Action onContinue = null)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title       = root.Q<Label>("outcome-title");
            var sub         = root.Q<Label>("outcome-sub");
            var menu        = root.Q<Button>("btn-menu");
            var continueBtn = root.Q<Button>("btn-continue");

            if (title != null)
            {
                title.text = victory ? L("ui.outcome.victory", "Победа") : L("ui.outcome.defeat", "Поражение");
                title.EnableInClassList("gm-outcome__title--defeat", !victory);
            }
            if (sub != null)
                sub.text = victory ? L("ui.outcome.victory_sub", "Акт пройден.")
                                   : L("ui.outcome.defeat_sub", "Забег окончен.");

            if (continueBtn != null)
            {
                // Кнопки нет, а не «есть, но погашена»: продолжать после забега нечем в принципе, и
                // погашенная кнопка обещала бы возможность, которой не существует.
                if (onContinue == null) continueBtn.style.display = DisplayStyle.None;
                else
                {
                    continueBtn.text = L("ui.outcome.continue", "Продолжить");
                    continueBtn.clicked += () => onContinue();
                }
            }

            if (menu != null)
            {
                menu.text = L("ui.outcome.to_menu", "В меню");
                menu.clicked += () => onToMenu?.Invoke();
                // Главной остаётся та кнопка, которая на этом экране ведёт вперёд.
                menu.EnableInClassList("gm-button--primary", onContinue == null);
            }

            return root;
        }

        /// <summary>
        /// Показать на «Продолжить», скольких ещё ждём. В одиночку счёт не рисуется: «(1/1)» не сообщает
        /// ничего, а место на кнопке занимает.
        /// </summary>
        /// <remarks>
        /// Отдельный метод, а не параметр сборки: счёт меняется, пока экран открыт, — второй игрок
        /// подтверждает уже после того, как первый увидел итог. Подпись обязана меняться вместе с ним,
        /// иначе подтвердивший смотрит на кнопку, которая молчит.
        /// </remarks>
        public static void SetContinueCount(VisualElement root, Func<string, string> localize,
                                            int ready, int required, bool locallyReady)
        {
            var button = root?.Q<Button>("btn-continue");
            if (button == null || button.style.display == DisplayStyle.None) return;

            string label = localize?.Invoke("ui.outcome.continue");
            if (string.IsNullOrEmpty(label)) label = "Продолжить";

            button.text = required > 1 ? $"{label} ({ready}/{required})" : label;
            button.EnableInClassList("gm-btn--pending", required > 1 && locallyReady);
        }
    }
}
