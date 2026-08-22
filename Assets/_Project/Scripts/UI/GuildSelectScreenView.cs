using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Guild;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Экран выбора дома: в какой гильдии играем Кампанию.
    /// </summary>
    /// <remarks>
    /// <b>Свой экран, а не блок внутри «Создать игру»</b> (модель Макса 04.08.2026): дом нужен только
    /// Кампании, и на общем экране его список то появлялся, то исчезал вслед за режимом — то есть
    /// половина листа отвечала на вопрос, которого при двух режимах из трёх не существует.
    /// <para><b>Каркас — тот же, что у профиля</b> (заказ Макса 22.08.2026: «делаем UI выбора гильдии,
    /// создания и удаления в духе профиля»): список слева, выбранный дом справа, под ним «Играть» и
    /// «Удалить». Подсветка и вход разведены — посмотреть чужой дом, не начав в нём играть, раньше
    /// было нельзя.</para>
    /// <para><b>Слоты показываются ВСЕ, включая пустые</b> — игрок видит, сколько домов ему полагается,
    /// а не только те, что успел завести. Число слотов приходит из <c>GameConfig.MaxGuildsPerProfile</c>,
    /// а не из разметки.</para>
    /// <para><b>Пары «Начать / Продолжить» здесь нет:</b> гильдия и есть слот сохранения (ТЗ
    /// [[save-system]] §3), поэтому игрок выбирает дом, а забег в нём либо уже идёт, либо начнётся.</para>
    /// </remarks>
    public static class GuildSelectScreenView
    {
        /// <summary>Что показывать про дом: имя, знак и идёт ли в нём забег.</summary>
        public readonly struct GuildEntry
        {
            public readonly string Id;
            public readonly string Name;
            public readonly bool HasRun;

            /// <summary>Знак дома и его цвет — их рисует строка списка рядом с именем.</summary>
            public readonly string EmblemId;
            public readonly int    EmblemColorIndex;

            public GuildEntry(string id, string name, bool hasRun,
                              string emblemId = null, int emblemColorIndex = 0)
            {
                Id               = id;
                Name             = name;
                HasRun           = hasRun;
                EmblemId         = emblemId ?? string.Empty;
                EmblemColorIndex = emblemColorIndex;
            }
        }

        /// <param name="onPick">Играть в этом доме.</param>
        /// <param name="onCreate">Завести дом в свободном слоте — через экран заведения.</param>
        /// <param name="onDelete">Снести дом. Спрашивает подтверждение тот, кто сносит.</param>
        public static VisualElement Build(
            VisualTreeAsset uxml,
            IReadOnlyList<GuildEntry> guilds,
            int slotLimit,
            Func<string, string> localize,
            Func<string, UnityEngine.Texture2D> emblemOf,
            Func<int, UnityEngine.Color> shadeOf,
            Action<string> onPick,
            Action onBack,
            Action onCreate = null,
            Action<string> onDelete = null)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title      = root.Q<Label>("guilds-title");
            var caption    = root.Q<Label>("guilds-caption");
            var list       = root.Q<VisualElement>("guild-list");
            var panelTitle = root.Q<Label>("panel-title");
            var panelMeta  = root.Q<Label>("panel-meta");
            var stats      = root.Q<VisualElement>("stat-list");
            var pick       = root.Q<Button>("btn-pick");
            var drop       = root.Q<Button>("btn-delete");
            var back       = root.Q<Components.BackButton>("btn-back");

            if (title != null)   title.text   = L("ui.guilds.title", "Гильдия");
            if (caption != null) caption.text = L("ui.guilds.caption", "Дома");
            if (back != null)
            {
                back.Localize(localize);
                back.clicked += () => onBack?.Invoke();
            }

            // ПОДСВЕТКА И ВХОД РАЗВЕДЕНЫ — тем же приёмом, что на экране профиля: клик по строке
            // показывает дом справа, играть в нём начинает «Играть».
            string highlighted = guilds != null && guilds.Count > 0 ? guilds[0].Id : null;

            void ShowSide(string id)
            {
                highlighted = id;

                GuildEntry shown = default;
                bool found = false;
                for (int i = 0; guilds != null && i < guilds.Count; i++)
                {
                    if (guilds[i].Id != id) continue;
                    shown = guilds[i];
                    found = true;
                    break;
                }

                if (panelTitle != null)
                    panelTitle.text = found ? shown.Name : L("ui.guilds.none.title", "Дом не выбран");

                if (panelMeta != null)
                    panelMeta.text = !found
                        ? L("ui.guilds.none.hint", "Выберите дом слева или заведите новый")
                        : shown.HasRun
                            ? L("ui.guilds.meta.in_run", "Забег идёт — можно продолжить")
                            : L("ui.guilds.meta.fresh", "Забега нет — начнётся новый");

                BuildStats(stats, found, shown, L);

                if (pick != null) pick.style.display = found ? DisplayStyle.Flex : DisplayStyle.None;
                if (drop != null) drop.style.display = found ? DisplayStyle.Flex : DisplayStyle.None;
            }

            BuildSlots(list, guilds, slotLimit, L, ShowSide, onCreate, emblemOf, shadeOf);

            if (pick != null)
            {
                pick.text = L("ui.guilds.play", "Играть");
                pick.clicked += () => { if (highlighted != null) onPick?.Invoke(highlighted); };
            }

            if (drop != null)
            {
                drop.text = L("ui.guilds.delete", "Удалить");
                drop.clicked += () => { if (highlighted != null) onDelete?.Invoke(highlighted); };
            }

            ShowSide(highlighted);
            return root;
        }

        /// <summary>Что дом успел накопить. Пока — идёт ли забег: остальное живёт в самом доме.</summary>
        private static void BuildStats(VisualElement list, bool found, in GuildEntry guild,
                                       Func<string, string, string> L)
        {
            if (list == null) return;
            list.Clear();
            if (!found) return;

            void Row(string caption, string value)
            {
                var row = new VisualElement();
                row.AddToClassList("gm-entry__stat");

                var name = new Label(caption);
                name.AddToClassList("gm-text-caption");
                name.AddToClassList("gm-text--muted");
                row.Add(name);

                var amount = new Label(value);
                amount.AddToClassList("gm-text-body");
                row.Add(amount);

                list.Add(row);
            }

            Row(L("ui.guilds.stat.run", "Забег"),
                guild.HasRun ? L("ui.guilds.stat.run.yes", "идёт") : L("ui.guilds.stat.run.no", "не начат"));
        }

        /// <summary>
        /// Левая колонка: дома и свободные слоты. Кнопки удаления здесь НЕТ — она под панелью
        /// выбранного, как в профиле: так видно, что именно удалится.
        /// </summary>
        private static void BuildSlots(VisualElement list, IReadOnlyList<GuildEntry> guilds, int slotLimit,
                                       Func<string, string, string> L, Action<string> onHighlight,
                                       Action onCreate,
                                       Func<string, UnityEngine.Texture2D> emblemOf,
                                       Func<int, UnityEngine.Color> shadeOf)
        {
            if (list == null) return;
            list.Clear();

            int count = guilds?.Count ?? 0;

            for (int i = 0; i < Math.Max(1, slotLimit); i++)
            {
                var row = new VisualElement();
                row.AddToClassList("gm-guilds__slot");
                row.AddToClassList("gm-entry__row");

                if (i < count)
                {
                    GuildEntry entry = guilds[i];
                    string captured = entry.Id;

                    var slot = new Components.PlateButton { name = "btn-guild-" + entry.Id, text = entry.Name };
                    slot.AddToClassList("gm-button");
                    slot.clicked += () => onHighlight?.Invoke(captured);

                    // Знак дома — слева от имени, покрашенный своим цветом. Знака нет — строка просто
                    // остаётся именем: пустое место под значок читалось бы как «картинка не загрузилась».
                    UnityEngine.Texture2D emblem = emblemOf?.Invoke(entry.EmblemId);
                    if (emblem != null)
                    {
                        var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                        mark.AddToClassList("gm-guilds__emblem");
                        mark.style.backgroundImage = new StyleBackground(emblem);
                        if (shadeOf != null)
                            mark.style.unityBackgroundImageTintColor = shadeOf(entry.EmblemColorIndex);
                        slot.Insert(0, mark);
                    }

                    row.Add(slot);
                }
                else
                {
                    // «Пустой слот», как в профиле: строка называет МЕСТО, а заводит дом следующий экран.
                    var fresh = new Components.PlateButton
                    {
                        name = "btn-guild-new-" + i,
                        text = L("ui.guilds.empty_slot", "Пустой слот"),
                    };
                    fresh.AddToClassList("gm-button");
                    fresh.AddToClassList("gm-guilds__slot-create");
                    fresh.clicked += () => onCreate?.Invoke();
                    row.Add(fresh);
                }

                list.Add(row);
            }
        }

        /// <summary>
        /// Собрать список домов из профиля: имя, знак и ответ на вопрос «идёт ли там забег».
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
                list.Add(new GuildEntry(guild.Id, guild.Name, hasRun, guild.EmblemId, guild.EmblemColorIndex));
            }

            if (!string.IsNullOrEmpty(activeId)) profiles.SelectGuild(activeId);
            return list;
        }
    }
}
