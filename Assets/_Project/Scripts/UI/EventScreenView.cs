using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка экрана текстового ивента (StS-style) из UXML-шаблона — общий код для живого роутера
    /// (<see cref="MenuRouter"/>) и превью-стенда (<c>UiPreviewCatalog</c>). Разметка/стиль — только из
    /// <c>EventScreen.uxml</c> + классы дизайн-системы; здесь наполнение вариантами и переход
    /// «выбор → текст-результат → Продолжить». Выбор фиксирует последствие через <paramref name="onChosen"/>
    /// (флоу применяет эффекты), закрытие — через <paramref name="onContinue"/>.
    /// </summary>
    public static class EventScreenView
    {
        /// <summary>
        /// Построить экран-оверлей ивента. <paramref name="localize"/> — строка по ключу (null/пусто →
        /// RU-фолбэк). Пустой текст-результат варианта = закрыть сразу (последствие уже зафиксировано).
        /// </summary>
        public static VisualElement Build(
            VisualTreeAsset uxml,
            TextEventData ev,
            Func<string, string> localize,
            Action<int> onChosen,
            Action onContinue)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title      = root.Q<Label>("event-title");
            var image      = root.Q<VisualElement>("event-image");
            var body       = root.Q<Label>("event-body");
            var choicesBox = root.Q<VisualElement>("event-choices");

            if (title != null) title.text = L(ev.TitleKey, ev.Id);
            if (body  != null) body.text  = L(ev.BodyKey, string.Empty);
            if (image != null && ev.Image != null)
            {
                image.style.display = DisplayStyle.Flex;
                image.style.backgroundImage = new StyleBackground(ev.Image);
            }

            void ShowResult(string resultText)
            {
                choicesBox?.Clear();
                if (string.IsNullOrEmpty(resultText)) { onContinue?.Invoke(); return; }
                if (body != null) body.text = resultText;
                var cont = new Button(() => onContinue?.Invoke()) { text = L("ui.event.continue", "Продолжить") };
                cont.AddToClassList("gm-button");
                cont.AddToClassList("gm-event-choice");
                choicesBox?.Add(cont);
            }

            IReadOnlyList<EventChoice> choices = ev.Choices;
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i; // захват копии для замыкания
                string label  = L(ev.ChoiceLabelKey(i),  $"Вариант {i + 1}");
                string result = L(ev.ChoiceResultKey(i), string.Empty);
                var btn = new Button(() => { onChosen?.Invoke(index); ShowResult(result); }) { text = label };
                btn.AddToClassList("gm-button");
                btn.AddToClassList("gm-event-choice");
                choicesBox?.Add(btn);
            }

            return root;
        }
    }
}
