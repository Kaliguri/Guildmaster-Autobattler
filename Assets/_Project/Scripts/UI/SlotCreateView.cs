using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Экран заведения слота: как он будет называться и каким знаком помечен.
    /// </summary>
    /// <remarks>
    /// <b>Один экран на профиль и на дом</b> (заказ Макса 22.08.2026: «делаем UI выбора гильдии,
    /// создания и удаления в духе профиля»). Заводят их одинаково — именем и знаком, — и второй
    /// почти такой же экран разошёлся бы с первым на первой же правке. Разница между случаями — только
    /// в подписях и в том, кого потом заводит роутер.
    /// <para><b>Знак и его цвет выбираются пикером</b>, а не рядом образцов: двадцать знаков рядом
    /// заняли бы половину экрана, а нужны ровно в момент выбора.</para>
    /// <para><b>Пустого имени не бывает.</b> Кнопка «Создать» гаснет, пока поле пусто: слот без имени
    /// в списке читался бы как пустой, и игрок потерял бы его среди свободных мест.</para>
    /// </remarks>
    public static class SlotCreateView
    {
        /// <summary>Что заводим — от этого зависят только подписи.</summary>
        public enum SlotKind
        {
            Profile,
            Guild,
        }

        public static VisualElement Build(
            VisualTreeAsset uxml,
            SlotKind kind,
            string suggestedName,
            GuildEmblemCatalog emblems,
            GuildmasterPalette palette,
            int colorCount,
            Func<string, string> localize,
            Action<SlotCreationRequest> onCreate,
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

            var title       = root.Q<Label>("slot-create-title");
            var nameCap     = root.Q<Label>("name-caption");
            var nameField   = root.Q<TextField>("field-name");
            var nameHint    = root.Q<Label>("name-hint");
            var emblemBlock = root.Q<VisualElement>("section-emblem");
            var emblemCap   = root.Q<Label>("emblem-caption");
            var emblemPick  = root.Q<Components.PickerButton>("emblem-picker");
            var shadeBlock  = root.Q<VisualElement>("section-emblem-color");
            var shadeCap    = root.Q<Label>("emblem-color-caption");
            var shadePick   = root.Q<Components.PickerButton>("emblem-color-picker");
            var create      = root.Q<Button>("btn-create");
            var back        = root.Q<Components.BackButton>("btn-back");

            bool profile = kind == SlotKind.Profile;

            if (title != null)
                title.text = profile
                    ? L("ui.slot.create.profile.title", "Новый профиль")
                    : L("ui.slot.create.guild.title", "Новая гильдия");

            if (nameCap != null)
                nameCap.text = profile
                    ? L("ui.slot.create.profile.name", "Название профиля")
                    : L("ui.slot.create.guild.name", "Название гильдии");

            if (nameHint != null)
                nameHint.text = profile
                    ? L("ui.slot.create.profile.hint", "Так профиль будет назван в списке слотов")
                    : L("ui.slot.create.guild.hint", "Так дом будет назван в списке гильдий");

            if (emblemCap != null) emblemCap.text = L("ui.slot.create.emblem", "Знак");
            if (shadeCap != null)  shadeCap.text  = L("ui.slot.create.emblem_color", "Цвет знака");

            // ── Имя ─────────────────────────────────────────────────────────
            string typedName = suggestedName ?? string.Empty;
            nameField?.SetValueWithoutNotify(typedName);

            // ── Цвет знака ──────────────────────────────────────────────────
            int shadeIndex = 0;

            Color Shade(int index)
                => palette != null && palette.TryGet(Core.Players.PlayerColors.TokenOf(index), out Color found)
                    ? found
                    : Color.white;

            string emblemId = null;

            void FillEmblems()
            {
                if (emblemPick == null) return;

                IReadOnlyList<GuildEmblemCatalog.Entry> set =
                    emblems != null ? emblems.Emblems : Array.Empty<GuildEmblemCatalog.Entry>();

                // Знаков нет вовсе — прячем обе секции: спрашивать не о чем, а пустой пикер читался бы
                // как «не догрузилось».
                if (set.Count == 0)
                {
                    if (emblemBlock != null) emblemBlock.style.display = DisplayStyle.None;
                    if (shadeBlock != null)  shadeBlock.style.display  = DisplayStyle.None;
                    return;
                }

                var tiles = new List<Components.PickerButton.Option>(set.Count);
                for (int i = 0; i < set.Count; i++)
                {
                    GuildEmblemCatalog.Entry entry = set[i];
                    if (entry.Image == null || string.IsNullOrEmpty(entry.Id)) continue;
                    tiles.Add(new Components.PickerButton.Option(entry.Id, image: entry.Image,
                                                                 tint: Shade(shadeIndex)));
                }

                if (tiles.Count == 0) return;
                if (string.IsNullOrEmpty(emblemId)) emblemId = tiles[0].Id;

                emblemPick.SetOptions(tiles, emblemId, id => emblemId = id);
            }

            if (shadePick != null)
            {
                var shades = new List<Components.PickerButton.Option>(colorCount);
                for (int i = 0; i < colorCount; i++)
                    shades.Add(new Components.PickerButton.Option(i.ToString(), swatch: Shade(i)));

                shadePick.SetOptions(shades, shadeIndex.ToString(), id =>
                {
                    if (!int.TryParse(id, out int picked)) return;
                    shadeIndex = picked;
                    FillEmblems();   // знак перекрашивается вслед за цветом
                });
            }

            FillEmblems();

            // ── Действия ────────────────────────────────────────────────────
            void SyncCreate()
            {
                if (create == null) return;
                create.SetEnabled(!string.IsNullOrWhiteSpace(typedName));
            }

            nameField?.RegisterValueChangedCallback(e => { typedName = e.newValue; SyncCreate(); });

            if (create != null)
            {
                create.text = L("ui.slot.create.confirm", "Создать");
                create.clicked += () =>
                    onCreate?.Invoke(new SlotCreationRequest(typedName, emblemId, shadeIndex));
            }

            SyncCreate();

            back?.Localize(localize);
            if (back != null) back.clicked += () => onBack?.Invoke();

            return screen;
        }
    }
}
