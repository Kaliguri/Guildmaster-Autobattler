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
    /// <para><b>Дом выбирается СЛЕДУЮЩИМ экраном и только у Кампании</b> — см.
    /// <see cref="GuildSelectScreenView"/>. Площадка и матч уходят в игру этим же кликом: дома у них нет.</para>
    /// <para><b>Галочка лобби стоит НАД режимами,</b> потому что она про сеанс, а не про режим: сначала
    /// «с кем играем», потом «во что». Под кнопкой-действием она читалась бы как настройка этой кнопки.</para>
    /// </remarks>
    public static class NewGameScreenView
    {
        public static VisualElement Build(
            VisualTreeAsset uxml,
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

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title     = root.Q<Label>("newgame-title");
            var campaign  = root.Q<Button>("btn-mode-campaign");
            var grounds   = root.Q<Button>("btn-mode-grounds");
            var pvp       = root.Q<Button>("btn-mode-pvp");
            var modeHint  = root.Q<Label>("mode-hint");
            var lobby     = root.Q<Guildmaster.UI.Components.ToggleRow>("toggle-lobby");
            var lobbyHint = root.Q<Label>("lobby-hint");
            var back      = root.Q<Button>("btn-back");

            if (title != null)    title.text      = L("ui.newgame.title", "Создать игру");
            if (campaign != null) campaign.text   = L("ui.newgame.mode.campaign", "Кампания");
            if (grounds != null)  grounds.text    = L("ui.newgame.mode.grounds", "Ристалище");
            if (pvp != null)      pvp.text        = L("ui.newgame.mode.pvp", "PvP");
            if (back != null)     back.text       = L("ui.newgame.back", "Назад");
            if (lobby != null)    lobby.LabelText = L("ui.newgame.lobby", "Создать онлайн-лобби");

            // Без Steam лобби не поднять — это внешний отказ, и он честно виден: галочка гаснет и
            // объясняет себя строкой. Тихо оставить её включённой значило бы обещать то, чего не будет.
            if (lobby != null) lobby.SetEnabled(steamReady);
            if (!steamReady && lobby != null) lobby.SetValueWithoutNotify(false);
            if (lobbyHint != null)
                lobbyHint.text = steamReady
                    ? string.Empty
                    : L("ui.newgame.lobby.no_steam", "Steam не запущен — играть можно, звать друзей нет");

            // Подсказка идёт за КУРСОРОМ, а не за выбором: выбора на экране больше нет — клик по режиму
            // уже уводит дальше. Строка держит последнее наведение, чтобы не мигать пустотой.
            void Describe(VisualElement button, string text)
            {
                if (button == null || modeHint == null) return;
                button.RegisterCallback<MouseEnterEvent>(_ => modeHint.text = text);
                button.RegisterCallback<FocusInEvent>(_ => modeHint.text = text);
            }

            string campaignHint = L("ui.newgame.hint.campaign",
                "Забег по акту: карта, узлы, награды. Прогресс живёт в гильдии");
            Describe(campaign, campaignHint);
            Describe(grounds, L("ui.newgame.hint.grounds",
                "Площадка без карты и сейва: свободный состав, свободная расстановка"));
            Describe(pvp, L("ui.newgame.hint.pvp",
                "Матч: чужой строй скрыт, расставлять можно только своих"));

            if (modeHint != null) modeHint.text = campaignHint;

            bool Lobby() => lobby?.Toggle.value ?? false;

            if (campaign != null) campaign.clicked += () => onPick?.Invoke(GameMode.Campaign, Lobby());
            if (grounds != null)  grounds.clicked  += () => onPick?.Invoke(GameMode.ProvingGrounds, Lobby());
            if (pvp != null)      pvp.clicked      += () => onPick?.Invoke(GameMode.Pvp, Lobby());

            if (back != null) back.clicked += () => onBack?.Invoke();

            return root;
        }
    }
}
