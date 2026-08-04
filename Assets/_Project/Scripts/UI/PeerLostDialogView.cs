using System;
using System.Collections.Generic;
using Guildmaster.Core.Net;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Диалог разрыва связи: кого потеряли, что это значит и что можно сделать.
    /// </summary>
    /// <remarks>
    /// <b>Кнопки собираются из вариантов, а не лежат в разметке:</b> у хоста их три, у гостя две, а
    /// экран обязан остаться одним — иначе два почти одинаковых разъедутся на первой же правке текста.
    /// </remarks>
    public static class PeerLostDialogView
    {
        public static VisualElement Build(
            VisualTreeAsset uxml,
            in PeerLostRequest request,
            Func<string, string> localize,
            Action closed)
        {
            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title = root.Q<Label>("peer-lost-title");
            var body  = root.Q<Label>("peer-lost-body");
            var cons  = root.Q<Label>("peer-lost-consequence");
            var slot  = root.Q<VisualElement>("peer-lost-actions");

            if (title != null) title.text = request.Title;
            if (body  != null) body.text  = request.Body;

            if (cons != null)
            {
                cons.text = request.Consequence;
                // Пустая строка последствия — не «пустой текст», а отсутствие строки: иначе под телом
                // остаётся зазор, который читается как недогруженный экран.
                cons.style.display = string.IsNullOrEmpty(request.Consequence)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            if (slot == null) return root;

            IReadOnlyList<PeerLostOption> options = request.Options;
            for (int i = 0; options != null && i < options.Count; i++)
            {
                PeerLostOption option = options[i];

                string text = localize?.Invoke(option.LocKey);
                if (string.IsNullOrEmpty(text)) text = option.Fallback;

                var button = new PlateButton { text = text };
                button.AddToClassList("gm-button");
                if (option.Primary) button.AddToClassList("gm-button--primary");

                Action act = option.Act;
                button.clicked += () =>
                {
                    // Экран снимается ПЕРЕД действием: половина вариантов уводит игрока отсюда совсем
                    // (главное меню, оверлей Steam), и диалог, снятый после, успел бы мигнуть поверх
                    // уже нового места.
                    closed?.Invoke();
                    act?.Invoke();
                };

                slot.Add(button);
            }

            return root;
        }
    }
}
