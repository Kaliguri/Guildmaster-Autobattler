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
    /// <para><b>Слоты показываются ВСЕ, включая пустые</b> — тем же приёмом, что на экране профиля:
    /// игрок видит, сколько домов ему полагается, а не только те, что успел завести. Число слотов
    /// приходит из <c>GameConfig.MaxGuildsPerProfile</c>, а не из разметки.</para>
    /// <para><b>Пары «Начать / Продолжить» здесь нет:</b> гильдия и есть слот сохранения (ТЗ
    /// [[save-system]] §3), поэтому игрок выбирает дом, а забег в нём либо уже идёт, либо начнётся.
    /// Строка дома сама говорит, что там внутри.</para>
    /// </remarks>
    public static class GuildSelectScreenView
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

        /// <param name="onPick">Выбран дом; <c>null</c> — завести новый в свободном слоте.</param>
        public static VisualElement Build(
            VisualTreeAsset uxml,
            IReadOnlyList<GuildEntry> guilds,
            int slotLimit,
            Func<string, string> localize,
            Action<string> onPick,
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

            var title   = root.Q<Label>("guilds-title");
            var caption = root.Q<Label>("guilds-caption");
            var list    = root.Q<VisualElement>("guild-list");
            var back    = root.Q<Button>("btn-back");

            if (title != null)   title.text   = L("ui.guilds.title", "Гильдия");
            if (caption != null) caption.text = L("ui.guilds.caption", "Дом хранит прогресс: забег, ростер, открытия");
            if (back != null)
            {
                back.text = L("ui.guilds.back", "Назад");
                back.clicked += () => onBack?.Invoke();
            }

            BuildSlots(list, guilds, slotLimit, L, onPick);
            return root;
        }

        private static void BuildSlots(VisualElement list, IReadOnlyList<GuildEntry> guilds, int slotLimit,
                                       Func<string, string, string> L, Action<string> onPick)
        {
            if (list == null) return;
            list.Clear();

            int count = guilds?.Count ?? 0;

            // Слотов ровно столько, сколько разрешает конфиг: и занятые, и свободные. Список, кончающийся
            // на последнем заведённом доме, не отвечает на вопрос «а можно ещё?» — а он у игрока первый.
            for (int i = 0; i < Math.Max(1, slotLimit); i++)
            {
                var row = new VisualElement();
                row.AddToClassList("gm-guilds__slot");

                if (i < count)
                {
                    GuildEntry entry = guilds[i];
                    string label = entry.HasRun
                        ? $"{entry.Name} — {L("ui.guilds.in_run", "забег идёт")}"
                        : entry.Name;

                    var pick = new Components.PlateButton { name = "btn-guild-" + entry.Id, text = label };
                    pick.AddToClassList("gm-button");
                    string captured = entry.Id;
                    pick.clicked += () => onPick?.Invoke(captured);
                    row.Add(pick);
                }
                else
                {
                    var fresh = new Components.PlateButton
                    {
                        name = "btn-guild-new-" + i,
                        text = L("ui.guilds.new", "Новая гильдия"),
                    };
                    fresh.AddToClassList("gm-button");
                    fresh.AddToClassList("gm-guilds__slot-create");
                    fresh.clicked += () => onPick?.Invoke(null);
                    row.Add(fresh);
                }

                list.Add(row);
            }
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
