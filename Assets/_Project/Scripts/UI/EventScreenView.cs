using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.UI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка экрана текстового ивента (StS-style) из UXML-шаблона — общий код для живого роутера
    /// (<see cref="MenuRouter"/>) и превью-стенда (<c>UiPreviewCatalog</c>). Разметка/стиль — только из
    /// <c>EventScreen.uxml</c> + классы дизайн-системы; здесь наполнение вариантами и переход
    /// «выбор → текст-прощание». Выбор фиксирует последствие через <paramref name="onChosen"/>
    /// (флоу применяет эффекты); экран после этого ОСТАЁТСЯ — уводят с него кнопки бита, которые петля
    /// акта кладёт поверх, а снимает его вход в следующий узел (QA #49).
    /// </summary>
    public static class EventScreenView
    {
        /// <summary>
        /// Построить экран-оверлей ивента. <paramref name="localize"/> — строка по ключу (null/пусто →
        /// RU-фолбэк). У варианта без написанного исхода показывается общий текст-прощание.
        /// </summary>
        /// <param name="gold">
        /// Золото забега: вариант дороже него показывается погашенным. Цена написана в тексте самого
        /// варианта — автор пишет её один раз там же, где задаёт последствия.
        /// </param>
        public static VisualElement Build(
            VisualTreeAsset uxml,
            TextEventData ev,
            Func<string, string> localize,
            Action<int> onChosen,
            int gold)
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

            // Выбор сделан → варианты уходят, на их месте остаётся текст-прощание. Экран НЕ закрывается: он
            // держит фон и текст всю передышку, а увести с него могут только кнопки бита («Продолжить» /
            // «К построению»), которые петля кладёт поверх (QA #49). Своей кнопки «Продолжить» здесь больше
            // нет — она давала второй, конкурирующий выход с того же экрана.
            void ShowResult(string resultText)
            {
                choicesBox?.Clear();
                if (body != null)
                    body.text = string.IsNullOrEmpty(resultText)
                        ? L("ui.event.result.fallback", "Вы двинулись дальше.") // у варианта не написан исход
                        : resultText;
            }

            IReadOnlyList<EventChoice> choices = ev.Choices;
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i; // захват копии для замыкания
                string label  = L(ev.ChoiceLabelKey(i),  $"Вариант {i + 1}");
                string result = L(ev.ChoiceResultKey(i), string.Empty);
                var btn = new PlateButton(() => { onChosen?.Invoke(index); ShowResult(result); }) { text = label };
                btn.AddToClassList("gm-button");

                // Не по карману — вариант виден, но не нажимается: игрок должен видеть, что упускает.
                // Пропустить его нажатие нельзя — цена списывается транзакцией, и applier откажет вслух.
                if (choices[i].GoldCost > gold) btn.SetEnabled(false);

                choicesBox?.Add(btn);
            }

            return root;
        }
    }
}
