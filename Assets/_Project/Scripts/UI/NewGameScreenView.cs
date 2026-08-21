using System;
using Guildmaster.Guild;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Экран «Создать игру»: во что играем и пускаем ли друзей.
    /// </summary>
    /// <remarks>
    /// <b>Режим — это действие, а не галочка</b> (модель Макса 04.08.2026): клик по режиму сразу ведёт
    /// дальше, поэтому кнопки «Начать» здесь нет вовсе. Прежде экран собирал заказ целиком — режим, дом
    /// и лобби на одном листе, — и дом приходилось показывать рядом с режимами, которым он не нужен:
    /// половина экрана то появлялась, то исчезала, а «Начать» стояло третьим шагом после двух выборов.
    ///
    /// <para><b>Режимы показаны карточками</b> (заказ Макса 21.08.2026) — тем же приёмом, что развилка
    /// профиля: раскладку держит <see cref="ModeChoiceView"/>. Строки-кнопки с подсказкой за курсором
    /// ушли: описание режима теперь видно на самой карточке всегда, а не только под мышью.</para>
    ///
    /// <para><b>Дом выбирается СЛЕДУЮЩИМ экраном и только у Кампании</b> — см.
    /// <see cref="GuildSelectScreenView"/>. Площадка и матч уходят в игру этим же кликом: дома у них нет.</para>
    ///
    /// <para><b>Галочка лобби живёт в футере</b> (решение Макса 21.08.2026): она про сеанс, а не про
    /// режим, и на поле карточек ей места нет — ряд дверей должен читаться рядом дверей.</para>
    /// </remarks>
    public static class NewGameScreenView
    {
        // Знаки режимов: код называет роль, картинку подставляет тема (см. mode-card.uss).
        private const string CampaignGlyph = "gm-mode-card__glyph--campaign";
        private const string GroundsGlyph  = "gm-mode-card__glyph--grounds";
        private const string PvpGlyph      = "gm-mode-card__glyph--pvp";

        public static VisualElement Build(
            bool steamReady,
            Func<string, string> localize,
            Action<GameMode, bool> onPick,
            Action onBack)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            // Галочка собирается ДО карточек: клик по режиму спрашивает её значение, а не наоборот.
            var lobby = new Components.ToggleRow
            {
                LabelText = L("ui.newgame.lobby", "Создать онлайн-лобби"),
            };
            lobby.SetValueWithoutNotify(true);

            // Без Steam лобби не поднять — это внешний отказ, и он честно виден: галочка гаснет и
            // объясняет себя строкой. Тихо оставить её включённой значило бы обещать то, чего не будет.
            lobby.SetEnabled(steamReady);
            if (!steamReady) lobby.SetValueWithoutNotify(false);

            var footerExtra = new VisualElement();
            footerExtra.Add(lobby);
            if (!steamReady)
            {
                var noSteam = new Label(L("ui.newgame.lobby.no_steam",
                    "Steam не запущен — играть можно, звать друзей нет"));
                noSteam.AddToClassList("gm-text-caption");
                noSteam.AddToClassList("gm-text--muted");
                footerExtra.Add(noSteam);
            }

            bool Lobby() => lobby.Toggle.value;

            var cards = new[]
            {
                new ModeChoiceView.Card(
                    L("ui.newgame.mode.campaign", "Кампания"),
                    L("ui.newgame.hint.campaign",
                      "Забег по акту: карта, узлы, награды. Прогресс живёт в гильдии"),
                    CampaignGlyph, () => onPick?.Invoke(GameMode.Campaign, Lobby())),

                new ModeChoiceView.Card(
                    L("ui.newgame.mode.grounds", "Ристалище"),
                    L("ui.newgame.hint.grounds",
                      "Площадка без карты и сейва: свободный состав, свободная расстановка"),
                    GroundsGlyph, () => onPick?.Invoke(GameMode.ProvingGrounds, Lobby())),

                new ModeChoiceView.Card(
                    L("ui.newgame.mode.pvp", "PvP"),
                    L("ui.newgame.hint.pvp",
                      "Матч: чужой строй скрыт, расставлять можно только своих"),
                    PvpGlyph, () => onPick?.Invoke(GameMode.Pvp, Lobby())),
            };

            return ModeChoiceView.Build(
                "newgame",
                L("ui.newgame.title", "Создать игру"),
                cards,
                L("ui.newgame.back", "Назад"),
                onBack,
                footerExtra);
        }
    }
}
