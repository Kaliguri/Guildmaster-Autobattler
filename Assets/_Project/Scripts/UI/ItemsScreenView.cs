using System;
using System.Collections.Generic;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>Носитель и его вещи так, как их видит экран.</summary>
    public readonly struct ItemsRowView
    {
        /// <summary>Индекс места в отряде — по нему уходит команда.</summary>
        public readonly int SlotIndex;

        /// <summary>Имя носителя; пусто — место свободно и вещи вешать не на кого.</summary>
        public readonly string Name;

        /// <summary>Реликвия носителя.</summary>
        public readonly string Relic;

        /// <summary>
        /// Вещи по слотам, длиной в потолок слотов. Пустая строка — слот открыт и свободен,
        /// <c>null</c> — слот ещё не открыт наградой забега.
        /// </summary>
        public readonly IReadOnlyList<string> Items;

        public ItemsRowView(int slotIndex, string name, string relic, IReadOnlyList<string> items)
        {
            SlotIndex = slotIndex;
            Name      = name;
            Relic     = relic;
            Items     = items;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Name);
    }

    /// <summary>
    /// Страница «Предметы»: строка на носителя, четыре квадратных слота в ряд, склад под ними.
    /// <para>Квадрат против круга — не украшение: круглое это человек, квадратное вещь, и два рода
    /// объектов не путаются на любом расстоянии (ГДД <c>preparation-screens</c> §2.2).</para>
    /// </summary>
    /// <remarks>
    /// <b>Перекладывание — это снятие плюс надевание.</b> Вещь всегда где-то лежит: в слоте или в
    /// складе, и никогда в воздухе. Поэтому у экрана нет жеста «перенести напрямую» — есть два, и
    /// оба идут одной командой (журнал «An Item Always Lies Somewhere»).
    /// </remarks>
    public static class ItemsScreenView
    {
        /// <summary>Что игрок сделал с вещами. Экран не меняет состояние сам.</summary>
        public sealed class Actions
        {
            /// <summary>Положить вещь в слот: место в отряде, номер слота, id вещи.</summary>
            public Action<int, int, string> Equip;

            /// <summary>Снять вещь в склад: место в отряде и номер слота.</summary>
            public Action<int, int> Unequip;

            /// <summary>Осмотреть носителя (ЛКМ).</summary>
            public Action<int> Inspect;

            /// <summary>Расширенная карточка носителя (ПКМ).</summary>
            public Action<int> OpenCard;

            /// <summary>Выйти в бой.</summary>
            public Action Battle;
        }

        public const string RowClass       = "gm-items-row";
        public const string SlotClass      = "gm-item-slot";
        public const string SlotFilledClass = "gm-item-slot--filled";
        public const string SlotLockedClass = "gm-item-slot--locked";
        public const string StashItemClass  = "gm-stash-item";

        /// <param name="rows">Носители по порядку мест отряда — только те, кто занимает место.</param>
        /// <param name="stash">Вещи в запасе.</param>
        public static VisualElement Build(
            VisualTreeAsset screenUxml,
            IReadOnlyList<ItemsRowView> rows,
            IReadOnlyList<string> stash,
            Actions actions = null,
            Func<string, string> localize = null)
        {
            if (screenUxml == null) throw new ArgumentNullException(nameof(screenUxml));

            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement tree = screenUxml.CloneTree();
            VisualElement root = tree.childCount > 0 ? tree[0] : tree;

            rows  ??= Array.Empty<ItemsRowView>();
            stash ??= Array.Empty<string>();

            SetLabel(root, "title", L("ui.items.title", "ПОДГОТОВКА"));
            SetLabel(root, "stash-title", L("ui.items.stash", "ПРЕДМЕТЫ В ЗАПАСЕ"));
            SetLabel(root, "stash-note", string.Format(L("ui.items.stash_note", "{0} в запасе"), stash.Count));

            int free = 0, opened = 0;
            foreach (ItemsRowView row in rows)
            {
                if (row.Items == null) continue;
                foreach (string item in row.Items)
                {
                    if (item == null) continue; // закрытый слот
                    opened++;
                    if (item.Length == 0) free++;
                }
            }
            SetLabel(root, "slots-note", string.Format(L("ui.items.slots_note", "свободно {0} из {1}"), free, opened));

            VisualElement host = root.Q<VisualElement>("rows");
            host?.Clear();
            foreach (ItemsRowView row in rows) host?.Add(BuildRow(row, actions, L));

            var stashHost = root.Q<ScrollView>("stash");
            stashHost?.Clear();
            for (int i = 0; i < stash.Count; i++) stashHost?.Add(BuildStashItem(stash[i]));

            var battle = root.Q<PlateButton>("btn-battle");
            if (battle != null)
            {
                battle.text = L("ui.items.to_battle", "В БОЙ");
                battle.clicked += () => actions?.Battle?.Invoke();
            }

            return root;
        }

        private static VisualElement BuildRow(ItemsRowView row, Actions actions, Func<string, string, string> L)
        {
            var line = new VisualElement();
            line.AddToClassList(RowClass);

            var portrait = new VisualElement();
            portrait.AddToClassList("gm-items-row__portrait");
            line.Add(portrait);

            var who = new VisualElement();
            who.AddToClassList("gm-items-row__who");
            who.Add(MakeLabel(row.IsEmpty ? L("ui.items.empty_slot", "пустое место") : row.Name, "gm-items-row__name"));
            who.Add(MakeLabel(row.Relic ?? string.Empty, "gm-items-row__relic"));
            line.Add(who);

            var slots = new VisualElement();
            slots.AddToClassList("gm-items-row__slots");
            IReadOnlyList<string> items = row.Items ?? Array.Empty<string>();
            for (int i = 0; i < items.Count; i++) slots.Add(BuildSlot(row, i, items[i], actions, L));
            line.Add(slots);

            if (!row.IsEmpty)
            {
                int index = row.SlotIndex;
                line.RegisterCallback<ClickEvent>(_ => actions?.Inspect?.Invoke(index));
                line.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 1) return;
                    actions?.OpenCard?.Invoke(index);
                    evt.StopPropagation();
                });
            }

            return line;
        }

        private static VisualElement BuildSlot(ItemsRowView row, int itemSlot, string itemId,
                                               Actions actions, Func<string, string, string> L)
        {
            var cell = new VisualElement();
            cell.AddToClassList(SlotClass);

            bool locked = itemId == null;
            bool filled = !locked && itemId.Length > 0;
            cell.EnableInClassList(SlotLockedClass, locked);
            cell.EnableInClassList(SlotFilledClass, filled);

            if (locked)
            {
                cell.Add(MakeLabel(L("ui.items.locked", "закрыт"), "gm-item-slot__note"));
                return cell;
            }

            if (filled)
            {
                cell.Add(MakeLabel(itemId, "gm-item-slot__id"));
                // Тащить можно только надетое: пустой слот нести нечем.
                cell.AddManipulator(new DragManipulator(
                    () => new DragPayload("item", row.SlotIndex, itemSlot, itemId)));
            }

            // Слот принимает вещь и из склада, и с другого носителя. Второе — снятие плюс надевание:
            // вещь не летает между людьми, она возвращается в запас и берётся оттуда.
            int targetSlot = row.SlotIndex;
            cell.AddManipulator(new DropZoneManipulator(
                payload => payload.Kind == "item" && !row.IsEmpty,
                payload =>
                {
                    if (payload.SubIndex >= 0) actions?.Unequip?.Invoke(payload.SlotIndex, payload.SubIndex);
                    actions?.Equip?.Invoke(targetSlot, itemSlot, payload.Id);
                }));

            return cell;
        }

        private static VisualElement BuildStashItem(string itemId)
        {
            var cell = new VisualElement();
            cell.AddToClassList(StashItemClass);
            cell.Add(MakeLabel(itemId ?? string.Empty, "gm-stash-item__id"));
            // Место в отряде у складской вещи отсутствует: −1 и означает «лежит в запасе».
            cell.AddManipulator(new DragManipulator(() => new DragPayload("item", -1, -1, itemId)));
            return cell;
        }

        private static Label MakeLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        private static void SetLabel(VisualElement root, string name, string text)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = text;
        }
    }
}
