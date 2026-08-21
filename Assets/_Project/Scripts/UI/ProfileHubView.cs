using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// «Профиль» — две двери: сменить профиль или настроить его.
    /// </summary>
    /// <remarks>
    /// <b>Зачем экран-развилка.</b> Заказ Макса 21.08.2026: кнопка «Профиль» в меню открывает две
    /// крупные карточки, а не сразу список. До этого выбор слота и настройка ника жили на одном
    /// экране, и правая колонка отвечала сразу на два разных вопроса — «каким профилем играю» и
    /// «как меня видят». Разведены по двум дверям: у каждой свой вопрос и свой экран.
    ///
    /// <para><b>Приём взят с Heroes Olden Era</b> (реф <c>Heroes Olden Era Mode Choice.png</c>):
    /// крупная карточка со знаком сверху, заголовком капсом и коротким описанием, подсказка
    /// действия — одна на весь ряд, а не на каждой карточке.</para>
    ///
    /// <para><b>Карточка — это кнопка</b> (<see cref="Components.PlateButton"/>): фаска, состояния
    /// наведения и фокус клавиатуры приходят от контрола. Панель с обработчиком клика пришлось бы
    /// учить всему этому заново — и она бы отстала при первой правке кнопок.</para>
    /// </remarks>
    public static class ProfileHubView
    {
        /// <summary>
        /// Знак карточки — модификатор класса, а КАРТИНКА ПРИХОДИТ ИЗ ТЕМЫ.
        /// </summary>
        /// <remarks>
        /// Не <c>Resources.Load</c>: иконки живут в <c>Art/UI/Icons-gm</c>, а не в <c>Resources</c>,
        /// и загрузка кодом вернула бы <c>null</c> молча. Путь к файлу — дело USS (<c>url(...)</c>),
        /// как у всех остальных картинок интерфейса; код называет РОЛЬ знака, а не его адрес.
        /// </remarks>
        private const string SelectGlyph = "gm-mode-card__glyph--select";
        private const string CustomGlyph = "gm-mode-card__glyph--customize";

        public static VisualElement Build(Func<string, string> localize,
                                          Action onSelectProfile, Action onCustomize, Action onBack)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            var screen = new VisualElement { name = "profile-hub" };
            screen.AddToClassList("gm-screen");

            // ПАНЕЛИ ЗДЕСЬ НЕТ — и это по рефу, а не от лени. У Heroes карточки лежат прямо на
            // затемнённом кадре: рамка вокруг них отняла бы у карточек half экрана и превратила
            // крупные двери в марки. Первый заход именно на этом и споткнулся — внутри диалога
            // 23% x 76% считались от панели, а не от кадра.
            var panel = new VisualElement { name = "profile-hub-body" };
            panel.AddToClassList("gm-mode-screen");
            panel.pickingMode = PickingMode.Position;

            var title = new Label(L("ui.profile.hub.title", "Профиль"));
            title.AddToClassList("gm-text-title");
            title.AddToClassList("gm-mode-screen__title");
            Components.UiTextCase.Bind(title);
            panel.Add(title);

            var row = new VisualElement();
            row.AddToClassList("gm-mode-cards");

            row.Add(Card(
                L("ui.profile.hub.select.title", "Сменить профиль"),
                L("ui.profile.hub.select.desc",
                  "Каким профилем играть. Видно наигранное, дома и открытия."),
                SelectGlyph, onSelectProfile));

            row.Add(Card(
                L("ui.profile.hub.customize.title", "Настроить профиль"),
                L("ui.profile.hub.customize.desc",
                  "Каким вас видят остальные: имя, цвет и курсор."),
                CustomGlyph, onCustomize));

            panel.Add(row);

            var hint = new Label(L("ui.profile.hub.hint", "Нажмите, чтобы выбрать"));
            hint.AddToClassList("gm-text-caption");
            hint.AddToClassList("gm-text--muted");
            hint.AddToClassList("gm-mode-hint");
            panel.Add(hint);

            var footer = new VisualElement();
            footer.AddToClassList("gm-mode-screen__footer");
            var back = new Components.PlateButton(() => onBack?.Invoke())
            {
                text = L("ui.profile.back", "Назад"),
            };
            back.AddToClassList("gm-button");
            footer.Add(back);
            panel.Add(footer);

            screen.Add(panel);
            return screen;
        }

        /// <summary>Одна дверь: знак, заголовок, описание. Всё внутри кнопки-пластины.</summary>
        private static VisualElement Card(string title, string description, string glyphClass, Action act)
        {
            var card = new Components.PlateButton(() => act?.Invoke());
            card.AddToClassList("gm-mode-card");
            // Собственная подпись кнопки не используется: заголовок и описание идут своими метками,
            // потому что их два и у них разные роли текста.
            card.text = string.Empty;

            var art = new VisualElement { pickingMode = PickingMode.Ignore };
            art.AddToClassList("gm-mode-card__art");

            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("gm-mode-card__glyph");
            glyph.AddToClassList(glyphClass);
            art.Add(glyph);
            card.Add(art);

            var head = new Label(title);
            head.AddToClassList("gm-text-body");
            head.AddToClassList("gm-mode-card__title");
            Components.UiTextCase.Bind(head);
            card.Add(head);

            var desc = new Label(description);
            desc.AddToClassList("gm-text-caption");
            desc.AddToClassList("gm-text--muted");
            desc.AddToClassList("gm-mode-card__desc");
            card.Add(desc);

            return card;
        }
    }
}
