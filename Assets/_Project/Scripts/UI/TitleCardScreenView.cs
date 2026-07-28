using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка boot title card: печать + Cormorant-заголовок «Happy Guildmasters».
    /// Клик по экрану или авто-таймер (~2.2 с) вызывают <paramref name="onDismiss"/>.
    /// </summary>
    public static class TitleCardScreenView
    {
        private const long AutoDismissMs = 2200;

        public static VisualElement Build(
            VisualTreeAsset uxml,
            Sprite seal,
            Func<string, string> localize,
            Action onDismiss)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var sealEl = root.Q<VisualElement>("titlecard-seal");
            var title = root.Q<Label>("titlecard-title");
            var studio = root.Q<Label>("titlecard-studio");
            var hint = root.Q<Label>("titlecard-hint");

            if (sealEl != null && seal != null)
                sealEl.style.backgroundImage = new StyleBackground(seal);

            if (title != null)
                title.text = L("ui.titlecard.title", "Happy Guildmasters");
            if (studio != null)
                studio.text = L("ui.titlecard.studio", "Alebardium");
            if (hint != null)
                hint.text = L("ui.titlecard.hint", "Нажмите, чтобы продолжить");

            bool dismissed = false;
            void Dismiss()
            {
                if (dismissed) return;
                dismissed = true;
                onDismiss?.Invoke();
            }

            root.RegisterCallback<ClickEvent>(_ => Dismiss());
            root.schedule.Execute(Dismiss).StartingIn(AutoDismissMs);

            return root;
        }
    }
}
