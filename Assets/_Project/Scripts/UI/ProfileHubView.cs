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
    /// <para>Раскладку и карточки держит <see cref="ModeChoiceView"/> — общий вид развилки; здесь
    /// только состав дверей и их тексты.</para>
    /// </remarks>
    public static class ProfileHubView
    {
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

            var cards = new[]
            {
                new ModeChoiceView.Card(
                    L("ui.profile.hub.select.title", "Сменить профиль"),
                    L("ui.profile.hub.select.desc",
                      "Каким профилем играть. Видно наигранное, дома и открытия."),
                    SelectGlyph, onSelectProfile),

                new ModeChoiceView.Card(
                    L("ui.profile.hub.customize.title", "Настроить профиль"),
                    L("ui.profile.hub.customize.desc",
                      "Каким вас видят остальные: имя, цвет и курсор."),
                    CustomGlyph, onCustomize),
            };

            return ModeChoiceView.Build(
                "profile-hub",
                L("ui.profile.hub.title", "Профиль"),
                L("ui.profile.hub.hint", "Нажмите, чтобы выбрать"),
                cards,
                L("ui.profile.back", "Назад"),
                onBack);
        }
    }
}
