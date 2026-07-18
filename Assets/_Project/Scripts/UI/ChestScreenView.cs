using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка экрана сундука (план [[act-map-run-loop]] §4 B3): фасад с кликабельной крышкой. ЛКМ даёт лёгкую
    /// реакцию (USS-transition через класс) и через миг зовёт <paramref name="onOpen"/> — флоу катит награду 1-из-3.
    /// Арт крышки — плейсхолдер-глиф до пиксельного спрайта.
    /// </summary>
    public static class ChestScreenView
    {
        public static VisualElement Build(VisualTreeAsset uxml, Func<string, string> localize, Action onOpen)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title = root.Q<Label>("chest-title");
            var hint  = root.Q<Label>("chest-hint");
            var lid   = root.Q<VisualElement>("chest-lid");

            if (title != null) title.text = L("ui.chest.title", "Сундук");
            if (hint  != null) hint.text  = L("ui.chest.hint", "Нажми, чтобы открыть");

            if (lid != null)
            {
                bool opened = false;
                lid.RegisterCallback<ClickEvent>(_ =>
                {
                    if (opened) return;
                    opened = true;
                    lid.AddToClassList("gm-chest__lid--open");       // лёгкая реакция (USS-transition)
                    lid.schedule.Execute(() => onOpen?.Invoke()).StartingIn(130);
                });
            }

            return root;
        }
    }
}
