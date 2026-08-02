using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Guild;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Экран «Создать игру»: во что играем, в каком доме и пускаем ли друзей.
    /// </summary>
    /// <remarks>
    /// <b>Один экран собирает заказ целиком</b> (модель Макса 02.08.2026): режим, гильдия и галочка
    /// лобби уезжают в игру одним <see cref="GameStartRequest"/>. Прежде это были три разных входа из
    /// главного меню, и открытость для друзей выбиралась ОТДЕЛЬНО от игры — то есть игрок мог начать
    /// забег, забыв поднять сессию, и узнавал об этом, когда звать друга было уже некуда.
    /// <para><b>Дома показываются только у Кампании.</b> У площадки дома нет вовсе, и пустой список
    /// там читался бы как поломка, а не как «здесь это не нужно».</para>
    /// <para><b>Пары «Начать / Продолжить» здесь нет:</b> гильдия и есть слот сохранения (ТЗ
    /// [[save-system]] §3), поэтому игрок выбирает дом, а забег в нём либо уже идёт, либо начнётся.
    /// Строка дома сама говорит, что там внутри.</para>
    /// </remarks>
    public static class NewGameScreenView
    {
        /// <summary>Что показывать про дом: имя и идёт ли в нём забег.</summary>
        public readonly struct GuildEntry
        {
            public readonly string Id;
            public readonly string Name;
            public readonly bool HasRun;

            public GuildEntry(string id, string name, bool hasRun)
            {
                Id     = id;
                Name   = name;
                HasRun = hasRun;
            }
        }

        public static VisualElement Build(
            VisualTreeAsset uxml,
            IReadOnlyList<GuildEntry> guilds,
            bool guildsFull,
            bool steamReady,
            Func<string, string> localize,
            Action<GameStartRequest> onStart,
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
            var guildBlock = root.Q<VisualElement>("guild-block");
            var guildCaption = root.Q<Label>("guild-caption");
            var guildList = root.Q<ScrollView>("guild-list");
            var lobby     = root.Q<Guildmaster.UI.Components.ToggleRow>("toggle-lobby");
            var lobbyHint = root.Q<Label>("lobby-hint");
            var start     = root.Q<Button>("btn-start");
            var back      = root.Q<Button>("btn-back");

            if (title != null)        title.text        = L("ui.newgame.title", "Создать игру");
            if (campaign != null)     campaign.text     = L("ui.newgame.mode.campaign", "Кампания");
            if (grounds != null)      grounds.text      = L("ui.newgame.mode.grounds", "Ристалище");
            if (pvp != null)          pvp.text          = L("ui.newgame.mode.pvp", "PvP");
            if (guildCaption != null) guildCaption.text = L("ui.newgame.guild", "Гильдия");
            if (start != null)        start.text        = L("ui.newgame.start", "Начать");
            if (back != null)         back.text         = L("ui.newgame.back", "Назад");
            if (lobby != null)        lobby.LabelText   = L("ui.newgame.lobby", "Создать онлайн-лобби");

            // Без Steam лобби не поднять — это внешний отказ, и он честно виден: галочка гаснет и
            // объясняет себя строкой. Тихо оставить её включённой значило бы обещать то, чего не будет.
            if (lobby != null) lobby.SetEnabled(steamReady);
            if (!steamReady && lobby != null) lobby.SetValueWithoutNotify(false);
            if (lobbyHint != null)
                lobbyHint.text = steamReady
                    ? string.Empty
                    : L("ui.newgame.lobby.no_steam", "Steam не запущен — играть можно, звать друзей нет");

            var mode = GameMode.Campaign;
            string guildId = null;      // null = новая гильдия

            void RefreshModes()
            {
                campaign?.EnableInClassList("gm-newgame__mode--active", mode == GameMode.Campaign);
                grounds?.EnableInClassList("gm-newgame__mode--active", mode == GameMode.ProvingGrounds);
                pvp?.EnableInClassList("gm-newgame__mode--active", mode == GameMode.Pvp);

                if (guildBlock != null)
                    guildBlock.style.display = mode == GameMode.Campaign ? DisplayStyle.Flex : DisplayStyle.None;

                if (modeHint != null)
                    modeHint.text = mode switch
                    {
                        GameMode.ProvingGrounds => L("ui.newgame.hint.grounds",
                            "Площадка без карты и сейва: свободный состав, свободная расстановка"),
                        GameMode.Pvp => L("ui.newgame.hint.pvp",
                            "Матч: чужой строй скрыт, расставлять можно только своих"),
                        _ => L("ui.newgame.hint.campaign",
                            "Забег по акту: карта, узлы, награды. Прогресс живёт в гильдии"),
                    };
            }

            var guildButtons = new List<(string id, Button button)>();

            void RefreshGuilds()
            {
                foreach ((string id, Button button) in guildButtons)
                    button.EnableInClassList("gm-newgame__mode--active", id == guildId);
            }

            if (guildList != null)
            {
                // «Новая гильдия» стоит ПЕРВОЙ и выбрана по умолчанию: у нового игрока домов нет вовсе,
                // и список, начинающийся с пустоты, не объясняет, что делать дальше.
                var fresh = new Components.PlateButton
                {
                    name = "btn-guild-new",
                    text = guildsFull
                        ? L("ui.newgame.guild.full", "Домов больше нельзя")
                        : L("ui.newgame.guild.new", "Новая гильдия"),
                };
                fresh.AddToClassList("gm-button");
                fresh.SetEnabled(!guildsFull);
                fresh.clicked += () => { guildId = null; RefreshGuilds(); };
                guildList.Add(fresh);
                guildButtons.Add((null, fresh));

                for (int i = 0; i < (guilds?.Count ?? 0); i++)
                {
                    GuildEntry entry = guilds[i];
                    string label = entry.HasRun
                        ? $"{entry.Name} — {L("ui.newgame.guild.in_run", "забег идёт")}"
                        : entry.Name;

                    var button = new Components.PlateButton { name = "btn-guild-" + entry.Id, text = label };
                    button.AddToClassList("gm-button");
                    string captured = entry.Id;
                    button.clicked += () => { guildId = captured; RefreshGuilds(); };
                    guildList.Add(button);
                    guildButtons.Add((captured, button));
                }

                // Дом с забегом — самый вероятный выбор: игрок вернулся продолжать.
                for (int i = 0; i < (guilds?.Count ?? 0); i++)
                {
                    if (!guilds[i].HasRun) continue;
                    guildId = guilds[i].Id;
                    break;
                }

                // Домов нет, а новый завести нельзя — выбирать не из чего, и «Начать» это скажет.
                if (guildsFull && guildId == null && guilds != null && guilds.Count > 0)
                    guildId = guilds[0].Id;
            }

            void PickMode(GameMode picked)
            {
                mode = picked;
                RefreshModes();
            }

            if (campaign != null) campaign.clicked += () => PickMode(GameMode.Campaign);
            if (grounds != null)  grounds.clicked  += () => PickMode(GameMode.ProvingGrounds);
            if (pvp != null)      pvp.clicked      += () => PickMode(GameMode.Pvp);

            if (start != null)
                start.clicked += () => onStart?.Invoke(new GameStartRequest(
                    mode,
                    mode == GameMode.Campaign ? guildId : null,
                    lobby?.Toggle.value ?? false));

            if (back != null) back.clicked += () => onBack?.Invoke();

            RefreshModes();
            RefreshGuilds();
            return root;
        }

        /// <summary>
        /// Собрать список домов из профиля: имя плюс ответ на вопрос «идёт ли там забег».
        /// </summary>
        /// <remarks>
        /// Наличие забега спрашивается у ДИСКА по ключу каждой гильдии, а не у держателя состояния:
        /// меню живёт вне сеанса, и держателя в этот момент не существует.
        /// </remarks>
        public static List<GuildEntry> ReadGuilds(IProfileService profiles, ISaveService save)
        {
            var list = new List<GuildEntry>();
            if (profiles == null) return list;

            IReadOnlyList<ProfileSummary> guilds = profiles.Guilds;
            string activeId = profiles.ActiveGuild.Id;

            for (int i = 0; i < guilds.Count; i++)
            {
                ProfileSummary guild = guilds[i];

                // Ключ забега строится службой профилей и только для АКТИВНОЙ гильдии, поэтому чужой
                // дом приходится делать активным на время вопроса. Переключение обратно обязательно:
                // иначе просмотр меню молча сменил бы игроку дом.
                profiles.SelectGuild(guild.Id);
                bool hasRun = RunSaves.Exists(save, profiles);
                list.Add(new GuildEntry(guild.Id, guild.Name, hasRun));
            }

            if (!string.IsNullOrEmpty(activeId)) profiles.SelectGuild(activeId);
            return list;
        }
    }
}
