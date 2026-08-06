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

            public SlotEntry(string id, string name, bool isActive)
            {
                Id       = id;
                Name     = name;
                IsActive = isActive;
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
            bool canLeave,
            Func<string, string> localize,
            Action<string> onSelect,
            Action onCreate,
            Action<string> onDelete,
            Action<ProfileIdentity> onSave,
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
            var identCap   = root.Q<Label>("identity-caption");
            var steamToggle = root.Q<Components.ToggleRow>("toggle-steam-name");
            var nameField  = root.Q<TextField>("field-name");
            var nameHint   = root.Q<Label>("name-hint");
            var colorCap   = root.Q<Label>("color-caption");
            var colorRow   = root.Q<VisualElement>("color-row");
            var cursorCap  = root.Q<Label>("cursor-caption");
            var cursorRow  = root.Q<VisualElement>("cursor-row");
            var save       = root.Q<Button>("btn-save");
            var back       = root.Q<Button>("btn-back");

            if (title != null)     title.text     = L("ui.profile.title", "Профиль");
            if (slotsCap != null)  slotsCap.text  = L("ui.profile.slots", "Слоты");
            if (identCap != null)  identCap.text  = L("ui.profile.identity", "Как меня видят");
            if (colorCap != null)  colorCap.text  = L("ui.profile.color", "Цвет");
            if (cursorCap != null) cursorCap.text = L("ui.profile.cursor", "Курсор");
            if (save != null)      save.text      = L("ui.profile.save", "Сохранить");
            if (back != null)      back.text      = L("ui.profile.back", "Назад");
            if (steamToggle != null)
                steamToggle.LabelText = L("ui.profile.name.steam", "Брать имя из Steam");

            BuildSlots(slotList, slots, slotLimit, L, onSelect, onCreate, onDelete);

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

            BuildColors(colorRow, colorCount, colorIndex, index => colorIndex = index);
            BuildCursors(cursorRow, skins, skinId, id => skinId = id);

            // ── Действия ────────────────────────────────────────────────────
            if (save != null)
                save.clicked += () => onSave?.Invoke(
                    new ProfileIdentity(typedName, useSteam, colorIndex, skinId));

            // Без профиля уходить некуда — тогда «Назад» не показываем вовсе, а не гасим: погашенная
            // кнопка читается как «сломалось», отсутствующая — как «сначала выбери слот».
            if (back != null)
            {
                if (canLeave) back.clicked += () => onBack?.Invoke();
                else          back.style.display = DisplayStyle.None;
            }

            return screen;
        }

        private static void BuildSlots(VisualElement list, IReadOnlyList<SlotEntry> slots, int slotLimit,
                                       Func<string, string, string> L,
                                       Action<string> onSelect, Action onCreate, Action<string> onDelete)
        {
            if (list == null) return;
            list.Clear();

            for (int i = 0; i < slotLimit; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("gm-profile__slot");

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

                    var drop = new Components.PlateButton { text = L("ui.profile.delete", "Удалить") };
                    drop.AddToClassList("gm-button");
                    drop.AddToClassList("gm-profile__slot-delete");
                    drop.clicked += () => onDelete?.Invoke(slot.Id);
                    row.Add(drop);
                }
                else
                {
                    var create = new Components.PlateButton { text = L("ui.profile.create", "Создать профиль") };
                    create.AddToClassList("gm-button");
                    create.AddToClassList("gm-profile__slot-create");
                    create.clicked += () => onCreate?.Invoke();
                    row.Add(create);
                }

                list.Add(row);
            }
        }

        private static void BuildColors(VisualElement row, int count, int selected, Action<int> onPick)
        {
            if (row == null) return;
            row.Clear();

            var swatches = new List<VisualElement>(count);
            for (int i = 0; i < count; i++)
            {
                int index = i;

                var swatch = new VisualElement { name = $"color-{i}", focusable = true };
                swatch.AddToClassList("gm-profile__swatch");
                // Оттенок живёт в USS-токенах: палитра остаётся единственным владельцем цвета.
                swatch.AddToClassList($"gm-profile__swatch--p{i + 1}");
                if (i == selected) swatch.AddToClassList("gm-profile__swatch--picked");

                swatch.RegisterCallback<ClickEvent>(_ =>
                {
                    for (int j = 0; j < swatches.Count; j++)
                        swatches[j].EnableInClassList("gm-profile__swatch--picked", j == index);
                    onPick?.Invoke(index);
                });

                swatches.Add(swatch);
                row.Add(swatch);
            }
        }

        private static void BuildCursors(VisualElement row, IReadOnlyList<CursorSkinData> skins,
                                         string selectedId, Action<string> onPick)
        {
            if (row == null || skins == null) return;
            row.Clear();

            var tiles = new List<VisualElement>(skins.Count);
            for (int i = 0; i < skins.Count; i++)
            {
                CursorSkinData skin = skins[i];
                if (skin == null) continue;

                int index = tiles.Count;

                var tile = new VisualElement { name = $"cursor-{skin.Id}", focusable = true };
                tile.AddToClassList("gm-profile__cursor");
                if (skin.Texture != null) tile.style.backgroundImage = new StyleBackground(skin.Texture);

                bool picked = string.IsNullOrEmpty(selectedId) ? index == 0 : skin.Id == selectedId;
                if (picked) tile.AddToClassList("gm-profile__cursor--picked");

                tile.RegisterCallback<ClickEvent>(_ =>
                {
                    for (int j = 0; j < tiles.Count; j++)
                        tiles[j].EnableInClassList("gm-profile__cursor--picked", j == index);
                    onPick?.Invoke(skin.Id);
                });

                tiles.Add(tile);
                row.Add(tile);
            }
        }
    }
}
