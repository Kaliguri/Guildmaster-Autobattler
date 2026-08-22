using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Экран «Профиль»: в каком слоте играем и как нас видят остальные.
    /// </summary>
    /// <remarks>
    /// <b>Слот и идентичность на одном экране,</b> потому что вопрос у игрока один: «кем я захожу».
    /// Разведи их по двум экранам — и смена профиля перестала бы показывать, что вместе с ней меняются
    /// ник, цвет и курсор.
    /// <para><b>Кнопки «Назад» может не быть.</b> На чистой установке профиля нет вовсе, и уходить с
    /// экрана некуда: писать забег без профиля некуда, а молчаливое создание за игрока мы убрали
    /// (решение Макса 03.08.2026). В этом случае экран показывает только слоты и ждёт создания.</para>
    /// <para><b>Цвет и курсор выбираются образцами, а не списком:</b> их узнают глазом, и подпись
    /// «Лазурный» рядом с кружком лазурного цвета — это лишнее слово, а не помощь.</para>
    /// </remarks>
    public static class ProfileScreenView
    {
        /// <summary>Слот в списке: занятый профиль или пустое место под новый.</summary>
        public readonly struct SlotEntry
        {
            public readonly string Id;
            public readonly string Name;
            public readonly bool   IsActive;

            /// <summary>Чем профиль жил: наиграно, дома, забеги, открытия. У пустого слота — ноль.</summary>
            public readonly Core.Persistence.ProfileStats Stats;

            public SlotEntry(string id, string name, bool isActive,
                             Core.Persistence.ProfileStats stats = default)
            {
                Id       = id;
                Name     = name;
                IsActive = isActive;
                Stats    = stats;
            }

            /// <summary>Пустой слот: место есть, профиля нет.</summary>
            public bool IsEmpty => string.IsNullOrEmpty(Id);
        }

        public static VisualElement Build(
            VisualTreeAsset uxml,
            IReadOnlyList<SlotEntry> slots,
            int slotLimit,
            ProfileIdentity identity,
            string steamName,
            IReadOnlyList<CursorSkinData> skins,
            int colorCount,
            GuildmasterPalette palette,
            bool canLeave,
            bool customize,
            Func<string, string> localize,
            Action<string> onSelect,
            Action onCreate,
            Action<string> onDelete,
            Action<ProfileIdentity> onSave,
            Action<ProfileIdentity> onPreview,
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

            var title      = root.Q<Label>("profile-title");
            var slotsCap   = root.Q<Label>("slots-caption");
            var slotList   = root.Q<VisualElement>("slot-list");
            var sideSelect = root.Q<VisualElement>("side-select");
            var sideCustom = root.Q<VisualElement>("side-customize");
            var statList   = root.Q<VisualElement>("stat-list");
            var pick       = root.Q<Button>("btn-pick");
            var panelTitle = root.Q<Label>("panel-title");
            var panelMeta  = root.Q<Label>("panel-meta");
            var drop       = root.Q<Button>("btn-delete");
            var identCap   = root.Q<Label>("identity-caption");
            var steamToggle = root.Q<Components.ToggleRow>("toggle-steam-name");
            var nameField  = root.Q<TextField>("field-name");
            var nameHint   = root.Q<Label>("name-hint");
            var colorCap   = root.Q<Label>("color-caption");
            var colorPick  = root.Q<Components.PickerButton>("color-picker");
            var cursorCap  = root.Q<Label>("cursor-caption");
            var cursorPick = root.Q<Components.PickerButton>("cursor-picker");
            var save       = root.Q<Button>("btn-save");
            var back       = root.Q<Components.BackButton>("btn-back");

            // Показывается РОВНО ОДНО лицо: экран один, вопросов два, и смешивать их нельзя.
            if (sideSelect != null) sideSelect.style.display = customize ? DisplayStyle.None : DisplayStyle.Flex;
            if (sideCustom != null) sideCustom.style.display = customize ? DisplayStyle.Flex : DisplayStyle.None;

            if (title != null)
                title.text = customize
                    ? L("ui.profile.hub.customize.title", "Настроить профиль")
                    : L("ui.profile.hub.select.title", "Сменить профиль");
            if (slotsCap != null)  slotsCap.text  = L("ui.profile.slots", "Слоты");
            if (identCap != null)  identCap.text  = L("ui.profile.identity", "Как меня видят");
            // «ПРЕДПОЧТИТЕЛЬНЫЙ», а не просто «Цвет» (слово Макса 22.08.2026): в одной сессии оттенки
            // уникальны, и занятый кем-то заменяется ближайшим свободным — подпись обязана обещать
            // ровно это, а не «твой цвет навсегда».
            if (colorCap != null)  colorCap.text  = L("ui.profile.color", "Предпочтительный цвет");
            if (cursorCap != null) cursorCap.text = L("ui.profile.cursor", "Курсор");
            if (save != null)      save.text      = L("ui.profile.save", "Сохранить");
            back?.Localize(localize);   // слово и ключ у возврата одни на всю игру — они в самом контроле
            if (steamToggle != null)
                steamToggle.LabelText = L("ui.profile.name.steam", "Брать имя из Steam");

            // ── ЛИЦО «ВЫБОР»: список, статистика подсвеченного, две кнопки ──
            // ПОДСВЕТКА И ПРИМЕНЕНИЕ РАЗВЕДЕНЫ (заказ Макса 21.08.2026: «должны быть кнопки выбрать
            // и удалить»). Клик по слоту показывает его статистику, играть им начинает только
            // «Выбрать»: иначе посмотреть чужой слот нельзя, не сменив свой.
            string highlighted = null;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsEmpty) continue;
                if (slots[i].IsActive) { highlighted = slots[i].Id; break; }
                highlighted ??= slots[i].Id;
            }

            void ShowSide(string id)
            {
                highlighted = id;

                SlotEntry shown = default;
                bool found = false;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].IsEmpty || slots[i].Id != id) continue;
                    shown = slots[i];
                    found = true;
                    break;
                }

                if (panelTitle != null)
                    panelTitle.text = found ? shown.Name : L("ui.profile.none.title", "Профиль не выбран");

                if (panelMeta != null)
                    panelMeta.text = !found
                        ? L("ui.profile.none.hint", "Создайте профиль или выберите слот слева")
                        : shown.IsActive
                            ? L("ui.profile.meta.active", "Текущий профиль")
                            : L("ui.profile.meta.other", "Другой профиль — нажмите «Выбрать»");

                BuildStats(statList, found ? shown.Stats : default, found, L);

                // Кнопки относятся к ПОДСВЕЧЕННОМУ слоту. У активного «Выбрать» гасится: нажимать
                // нечего, а исчезающая кнопка дёргала бы ряд при каждом переключении.
                if (pick != null)
                {
                    pick.style.display = found ? DisplayStyle.Flex : DisplayStyle.None;
                    pick.SetEnabled(found && !shown.IsActive);
                }

                if (drop != null) drop.style.display = found ? DisplayStyle.Flex : DisplayStyle.None;
            }

            BuildSlots(slotList, slots, slotLimit, L, ShowSide, onCreate);

            if (pick != null)
            {
                pick.text = L("ui.profile.pick", "Выбрать");
                pick.clicked += () => { if (highlighted != null) onSelect?.Invoke(highlighted); };
            }

            if (drop != null)
            {
                drop.text = L("ui.profile.delete", "Удалить");
                drop.clicked += () => { if (highlighted != null) onDelete?.Invoke(highlighted); };
            }

            ShowSide(highlighted);

            // ── Идентичность ────────────────────────────────────────────────
            bool useSteam = identity.UseSteamName;
            int  colorIndex = Mathf.Clamp(identity.ColorIndex, 0, Mathf.Max(0, colorCount - 1));
            string skinId = identity.CursorSkinId;
            string typedName = identity.DisplayName;

            void RefreshName()
            {
                if (nameField != null)
                {
                    nameField.SetEnabled(!useSteam);
                    nameField.SetValueWithoutNotify(useSteam ? steamName : typedName);
                }

                if (nameHint != null)
                    nameHint.text = useSteam
                        ? L("ui.profile.name.hint.steam", "Имя берётся из Steam и меняется вместе с ним")
                        : L("ui.profile.name.hint.own", "Это имя увидят остальные игроки");
            }

            if (steamToggle != null)
            {
                steamToggle.SetValueWithoutNotify(useSteam);
                steamToggle.Toggle?.RegisterValueChangedCallback(e =>
                {
                    useSteam = e.newValue;
                    RefreshName();
                });
            }

            nameField?.RegisterValueChangedCallback(e => typedName = e.newValue);
            RefreshName();

            // ВЫБОР ВИДЕН СРАЗУ, но живёт до «Сохранить» (заказ Макса 22.08.2026: «При выборе цвета или
            // курсора - отображаться должно сразу. Но если не нажать кнопку сохранить - при выходе всё
            // вернётся обратно»). Показ идёт мимо профиля: на диск пишет только «Сохранить», а откат
            // делает уход с экрана.
            void Preview() =>
                onPreview?.Invoke(new ProfileIdentity(typedName, useSteam, colorIndex, skinId));

            // Образцы курсора носят ВЫБРАННЫЙ цвет — тот же, каким курсор станет в игре и каким его
            // увидит напарник. Белые образцы рядом с цветным курсором читались бы как другой набор.
            UnityEngine.Color Shade(int index)
                => palette != null && palette.TryGet(Core.Players.PlayerColors.TokenOf(index),
                                                     out UnityEngine.Color found)
                    ? found
                    : UnityEngine.Color.white;

            void FillCursors()
            {
                if (cursorPick == null || skins == null) return;

                var tiles = new List<Components.PickerButton.Option>(skins.Count);
                for (int i = 0; i < skins.Count; i++)
                {
                    CursorSkinData skin = skins[i];
                    if (skin == null) continue;
                    tiles.Add(new Components.PickerButton.Option(skin.Id, image: skin.Texture,
                                                                 tint: Shade(colorIndex), cropToCorner: true));
                }

                if (tiles.Count == 0) return;
                if (string.IsNullOrEmpty(skinId)) skinId = tiles[0].Id;

                cursorPick.SetOptions(tiles, skinId, id => { skinId = id; Preview(); });
            }

            if (colorPick != null)
            {
                var shades = new List<Components.PickerButton.Option>(colorCount);
                for (int i = 0; i < colorCount; i++)
                    shades.Add(new Components.PickerButton.Option(i.ToString(), swatch: Shade(i)));

                colorPick.SetOptions(shades, colorIndex.ToString(), id =>
                {
                    if (!int.TryParse(id, out int picked)) return;
                    colorIndex = picked;
                    FillCursors();   // курсоры перекрашиваются вслед за цветом
                    Preview();
                });
            }

            FillCursors();

            // ── Действия ────────────────────────────────────────────────────
            if (save != null)
            {
                // «Сохранить» относится к идентичности, поэтому в лице выбора его нет вовсе:
                // сохранять там нечего, а погашенная кнопка читалась бы как поломка.
                save.style.display = customize ? DisplayStyle.Flex : DisplayStyle.None;
                save.clicked += () => onSave?.Invoke(
                    new ProfileIdentity(typedName, useSteam, colorIndex, skinId));
            }

            // Без профиля уходить некуда — тогда «Назад» не показываем вовсе, а не гасим: погашенная
            // кнопка читается как «сломалось», отсутствующая — как «сначала выбери слот».
            if (back != null)
            {
                if (canLeave) back.clicked += () => onBack?.Invoke();
                else          back.style.display = DisplayStyle.None;
            }

            return screen;
        }

        /// <summary>
        /// Статистика подсвеченного профиля: строка «что» — «сколько».
        /// </summary>
        /// <remarks>
        /// Порядок строк — от «сколько прожито» к «чего достигнуто»: сперва время и дата, потом
        /// дома и забеги, потом победы и открытия. Нулевые строки НЕ прячутся: у нового профиля
        /// пустая статистика — тоже ответ, а исчезающие строки читаются как поломка экрана.
        /// </remarks>
        private static void BuildStats(VisualElement list, in Core.Persistence.ProfileStats stats,
                                       bool hasProfile, Func<string, string, string> L)
        {
            if (list == null) return;
            list.Clear();
            if (!hasProfile) return;

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

            Row(L("ui.profile.stat.played", "Наиграно"), Duration(stats.PlayedSeconds));
            Row(L("ui.profile.stat.last", "Последняя игра"),
                stats.LastPlayedUtc == default
                    ? L("ui.profile.stat.never", "ещё ни разу")
                    : stats.LastPlayedUtc.ToLocalTime().ToString("dd.MM.yyyy"));
            Row(L("ui.profile.stat.guilds", "Домов"), stats.Guilds.ToString());
            Row(L("ui.profile.stat.runs", "Забегов"), stats.RunsFinished.ToString());
            Row(L("ui.profile.stat.wins", "Побед"), stats.RunsWon.ToString());
            Row(L("ui.profile.stat.best", "Лучший забег"),
                stats.BestRunNodes > 0
                    ? string.Format(L("ui.profile.stat.best.value", "{0} узлов"), stats.BestRunNodes)
                    : "—");
            Row(L("ui.profile.stat.unlocks", "Открытий"), stats.Unlocks.ToString());
        }

        /// <summary>Часы и минуты. Секунды не показываем: наигранное меряется вечерами, не секундами.</summary>
        private static string Duration(long seconds)
        {
            if (seconds <= 0) return "—";

            long hours = seconds / 3600;
            long minutes = (seconds % 3600) / 60;
            return hours > 0 ? $"{hours} ч {minutes} мин" : $"{minutes} мин";
        }

        /// <summary>
        /// Левая колонка: слоты. Кнопки удаления здесь НЕТ — она живёт под панелью выбранного
        /// (приём обоих рефов класса, разбор `_teardowns/06-entry-service-coop.md`): так видно, что
        /// именно удалится, и промахнуться по соседней строке нельзя.
        /// </summary>
        private static void BuildSlots(VisualElement list, IReadOnlyList<SlotEntry> slots, int slotLimit,
                                       Func<string, string, string> L,
                                       Action<string> onSelect, Action onCreate)
        {
            if (list == null) return;
            list.Clear();

            for (int i = 0; i < slotLimit; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("gm-profile__slot");
                row.AddToClassList("gm-entry__row");

                if (i < slots.Count && !slots[i].IsEmpty)
                {
                    SlotEntry slot = slots[i];

                    var pick = new Components.PlateButton { text = slot.Name };
                    pick.AddToClassList("gm-button");
                    // Активный слот — ГЛАВНАЯ кнопка: «текущий профиль» и «главное действие строки»
                    // здесь одно и то же, и отдельный класс означал бы то же самое вторым способом.
                    if (slot.IsActive) pick.AddToClassList("gm-button--primary");
                    pick.clicked += () => onSelect?.Invoke(slot.Id);
                    row.Add(pick);
                }
                else
                {
                    // «ПУСТОЙ СЛОТ», а не «Создать профиль» (слово Макса 22.08.2026): строка называет
                    // МЕСТО, а не действие — действие игрок подтверждает следующим вопросом.
                    var create = new Components.PlateButton { text = L("ui.profile.empty_slot", "Пустой слот") };
                    create.AddToClassList("gm-button");
                    create.AddToClassList("gm-profile__slot-create");
                    create.clicked += () => onCreate?.Invoke();
                    row.Add(create);
                }

                list.Add(row);
            }
        }

    }
}
