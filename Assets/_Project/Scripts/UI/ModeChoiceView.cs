using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Экран-развилка: несколько крупных дверей в ряд, у каждой знак, заголовок и описание.
    /// </summary>
    /// <remarks>
    /// <b>Один вид на все развилки.</b> Приём взят с Heroes Olden Era (реф
    /// <c>Heroes Olden Era Mode Choice.png</c>) и живёт здесь в единственном экземпляре: на нём стоят
    /// и «Профиль» (<see cref="ProfileHubView"/>), и «Создать игру» (<see cref="NewGameScreenView"/>).
    /// Скопировать карточки во второй экран значило бы завести им второго владельца — разошлись бы
    /// они на первой же правке отступов.
    ///
    /// <para><b>Панели у экрана нет</b> — карточки лежат прямо на затемнённом кадре, как у рефа.
    /// Рамка вокруг них отняла бы половину кадра, и крупные двери превратились бы в марки; доли
    /// карточек считались бы от панели, а не от экрана.</para>
    ///
    /// <para><b>Подсказки «нажмите, чтобы выбрать» здесь нет</b> (снята по слову Макса 21.08.2026:
    /// «мы игроков за дурочков не держим»). Ряд крупных дверей и так читается как выбор — подпись
    /// объясняла бы жест, который экран показывает собой.</para>
    ///
    /// <para><b>Карточка — это кнопка</b> (<see cref="Components.PlateButton"/>): фаска, состояния
    /// наведения и фокус клавиатуры приходят от контрола, а не пишутся заново.</para>
    /// </remarks>
    public static class ModeChoiceView
    {
        /// <summary>Одна дверь развилки: что написано и что делает.</summary>
        public readonly struct Card
        {
            /// <summary>Заголовок капсом под знаком.</summary>
            public readonly string Title;

            /// <summary>Строка-две под заголовком: чем эта дверь отличается от соседних.</summary>
            public readonly string Description;

            /// <summary>
            /// Класс-модификатор знака (<c>gm-mode-card__glyph--*</c>). Код называет РОЛЬ знака, а
            /// адрес картинки — дело темы: иконки лежат в <c>Art/UI/Icons-gm</c>, а не в
            /// <c>Resources</c>, и загрузка кодом вернула бы <c>null</c> молча.
            /// </summary>
            public readonly string GlyphClass;

            /// <summary>Что делает клик по карточке.</summary>
            public readonly Action OnPick;

            public Card(string title, string description, string glyphClass, Action onPick)
            {
                Title = title;
                Description = description;
                GlyphClass = glyphClass;
                OnPick = onPick;
            }
        }

        /// <param name="footerExtra">
        /// Необязательный довесок футера слева от «Назад» — то, что относится ко всему экрану, а не к
        /// отдельной двери (галочка лобби у «Создать игру»). Общий вид о его содержимом не знает.
        /// </param>
        /// <param name="localize">
        /// Служба перевода — нужна ровно для подписи возврата: ключ у неё один на всю игру и живёт в
        /// самом контроле <see cref="Components.BackButton"/>, а не в вызывающем экране.
        /// </param>
        public static VisualElement Build(
            string name,
            string title,
            IReadOnlyList<Card> cards,
            Func<string, string> localize,
            Action onBack,
            VisualElement footerExtra)
        {
            var screen = new VisualElement { name = name };
            screen.AddToClassList("gm-screen");

            var body = new VisualElement { name = name + "-body" };
            body.AddToClassList("gm-mode-screen");
            body.pickingMode = PickingMode.Position;

            var head = new Label(title);
            head.AddToClassList("gm-text-title");
            head.AddToClassList("gm-mode-screen__title");
            Components.UiTextCase.Bind(head);
            body.Add(head);

            var row = new VisualElement();
            row.AddToClassList("gm-mode-cards");
            // Доля карточки зависит от того, сколько их в ряду: по рефу дверей четыре и каждая
            // занимает 19.6% кадра, а пара по тем же числам висела бы двумя марками посреди пустоты.
            row.AddToClassList(cards.Count >= 3 ? "gm-mode-cards--many" : "gm-mode-cards--pair");
            for (int i = 0; i < cards.Count; i++) row.Add(BuildCard(cards[i]));
            body.Add(row);

            var footer = new VisualElement();
            footer.AddToClassList("gm-mode-screen__footer");
            if (footerExtra != null)
            {
                footerExtra.AddToClassList("gm-mode-screen__footer-extra");
                footer.Add(footerExtra);
            }

            body.Add(footer);

            screen.Add(body);

            // Возврат кладётся в КОРЕНЬ экрана, а не в футер: место ему задаёт абсолют у кромки кадра,
            // а абсолют в UI Toolkit считается от непосредственного родителя. В футере он и вставал
            // посреди кадра — ровно поверх средней карточки (наход. Макса 23.08.2026).
            Components.BackButton.PlaceOn(screen, onBack, localize);
            return screen;
        }

        /// <summary>Одна дверь: знак, заголовок, описание. Всё внутри кнопки-пластины.</summary>
        private static VisualElement BuildCard(in Card card)
        {
            Action act = card.OnPick;
            var plate = new Components.PlateButton(() => act?.Invoke());
            plate.AddToClassList("gm-mode-card");
            // Собственная подпись кнопки не используется: заголовок и описание идут своими метками,
            // потому что их два и у них разные роли текста.
            plate.text = string.Empty;

            var art = new VisualElement { pickingMode = PickingMode.Ignore };
            art.AddToClassList("gm-mode-card__art");

            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("gm-mode-card__glyph");
            if (!string.IsNullOrEmpty(card.GlyphClass)) glyph.AddToClassList(card.GlyphClass);
            art.Add(glyph);
            plate.Add(art);

            var head = new Label(card.Title);
            head.AddToClassList("gm-text-body");
            head.AddToClassList("gm-mode-card__title");
            Components.UiTextCase.Bind(head);
            plate.Add(head);

            var desc = new Label(card.Description);
            desc.AddToClassList("gm-text-caption");
            desc.AddToClassList("gm-text--muted");
            desc.AddToClassList("gm-mode-card__desc");
            plate.Add(desc);

            return plate;
        }
    }
}
