using System;
using System.Collections.Generic;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>Одно место отряда так, как его видит экран: без ссылок на состояние забега и на контент.</summary>
    /// <remarks>
    /// Плоская запись, а не <c>RosterSlot</c>: вью не должен уметь читать durable-состояние, иначе
    /// превью экрана потребовало бы живого забега, а тесты — половины игры.
    /// </remarks>
    public readonly struct PartySlotView
    {
        /// <summary>Индекс места в отряде. Едет обратно в команду, поэтому обязателен.</summary>
        public readonly int Index;

        /// <summary>Имя человека; пусто — место свободно.</summary>
        public readonly string Name;

        /// <summary>Имя Реликвии, которую он несёт.</summary>
        public readonly string Relic;

        /// <summary>Выходит ли в бой.</summary>
        public readonly bool InBattle;

        /// <summary>Открыто ли место. Закрытые показываются замком и ничего не принимают.</summary>
        public readonly bool Open;

        public PartySlotView(int index, string name, string relic, bool inBattle, bool open)
        {
            Index    = index;
            Name     = name;
            Relic    = relic;
            InBattle = inBattle;
            Open     = open;
        }

        /// <summary>Место открыто, но пусто.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Name);
    }

    /// <summary>
    /// Страница «Отряд»: состав стоит телами на живой арене, а интерфейс несёт ленту мест и действия.
    /// Собирается статическим <see cref="Build"/> — тем же приёмом, что и остальные экраны, чтобы
    /// превью показывало ровно то, что увидит игрок, без живого забега.
    /// </summary>
    /// <remarks>
    /// <b>Тела рисует мир, а не этот экран</b> (<c>WorldStageController</c> ставит отряд на арену вне
    /// боя). Отсюда «дырка» в разметке: зона сцены прозрачна для ввода, и клик по бойцу доходит до
    /// мира. Экран отвечает за ленту, подписи и кнопки — то есть за решения, а не за показ боя.
    /// <para><b>Что показывает плитка, задано дизайном:</b> имя и Реликвия, и больше ничего. Перки,
    /// Судьба и травмы живут в панели осмотра и расширенной карточке — на плитке они превратили бы
    /// ленту в таблицу (ГДД <c>preparation-screens</c> §2.1).</para>
    /// </remarks>
    public static class PartyScreenView
    {
        /// <summary>Что игрок сделал с местом отряда. Экран не меняет состояние сам — он только зовёт.</summary>
        public sealed class Actions
        {
            /// <summary>Вывести на арену или увести в запас: индекс места и куда.</summary>
            public Action<int, bool> SetInBattle;

            /// <summary>Поменять местами два места отряда.</summary>
            public Action<int, int> Swap;

            /// <summary>Осмотреть: ЛКМ по месту.</summary>
            public Action<int> Inspect;

            /// <summary>Открыть расширенную карточку: ПКМ по месту.</summary>
            public Action<int> OpenCard;

            /// <summary>Выйти в бой.</summary>
            public Action Battle;
        }

        public const string SlotClass       = "gm-party-slot";
        public const string SlotBattleClass = "gm-party-slot--battle";
        public const string SlotEmptyClass  = "gm-party-slot--empty";
        public const string SlotLockedClass = "gm-party-slot--locked";

        /// <param name="screenUxml">Разметка <c>PartyScreen.uxml</c>.</param>
        /// <param name="slots">Все места отряда по порядку — и открытые, и закрытые.</param>
        /// <param name="localize">Ключ → строка; пусто — берётся запасная подпись.</param>
        public static VisualElement Build(
            VisualTreeAsset screenUxml,
            IReadOnlyList<PartySlotView> slots,
            Actions actions = null,
            Func<string, string> localize = null,
            int battleSlots = 4)
        {
            if (screenUxml == null) throw new ArgumentNullException(nameof(screenUxml));

            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement tree = screenUxml.CloneTree();
            VisualElement root = tree.childCount > 0 ? tree[0] : tree;

            SetLabel(root, "title", L("ui.party.title", "ПОДГОТОВКА"));
            SetLabel(root, "bench-title", L("ui.party.bench", "ОТРЯД ГИЛЬДИИ"));

            slots ??= Array.Empty<PartySlotView>();
            int open = 0, taken = 0, inBattle = 0;
            foreach (PartySlotView s in slots)
            {
                if (s.Open) open++;
                if (!s.IsEmpty) taken++;
                if (s.InBattle) inBattle++;
            }

            SetLabel(root, "capacity", string.Format(L("ui.party.capacity", "в отряде {0} / {1}"), taken, open));
            SetLabel(root, "bench-note", string.Format(
                L("ui.party.bench_note", "{0} мест · {1} открыто · в бою {2} из {3}"),
                slots.Count, open, inBattle, battleSlots));

            VisualElement bench = root.Q<VisualElement>("bench");
            VisualElement labels = root.Q<VisualElement>("stage-labels");
            bench?.Clear();
            labels?.Clear();

            foreach (PartySlotView slot in slots)
            {
                bench?.Add(BuildSlot(slot, actions, L));
                // Подпись под телом — только у тех, кто на арене: остальных на сцене попросту нет.
                if (slot.InBattle && !slot.IsEmpty) labels?.Add(BuildStageLabel(slot));
            }

            var battle = root.Q<PlateButton>("btn-battle");
            if (battle != null)
            {
                battle.text = L("ui.party.to_battle", "В БОЙ");
                battle.clicked += () => actions?.Battle?.Invoke();
            }

            return root;
        }

        private static VisualElement BuildSlot(PartySlotView slot, Actions actions, Func<string, string, string> L)
        {
            var cell = new VisualElement();
            cell.AddToClassList(SlotClass);
            cell.EnableInClassList(SlotBattleClass, slot.InBattle);
            cell.EnableInClassList(SlotEmptyClass, slot.Open && slot.IsEmpty);
            cell.EnableInClassList(SlotLockedClass, !slot.Open);

            if (!slot.Open)
            {
                cell.Add(new Label(L("ui.party.locked", "закрыто")) { });
                return cell;
            }

            if (slot.IsEmpty)
            {
                cell.Add(new Label(L("ui.party.empty", "пусто")));
                MakeDropZone(cell, slot, actions);
                return cell;
            }

            var portrait = new VisualElement();
            portrait.AddToClassList("gm-party-slot__portrait");
            cell.Add(portrait);

            var name = new Label(slot.Name);
            name.AddToClassList("gm-party-slot__name");
            cell.Add(name);

            var relic = new Label(slot.Relic ?? string.Empty);
            relic.AddToClassList("gm-party-slot__relic");
            cell.Add(relic);

            var mark = new Label(slot.InBattle ? L("ui.party.in_battle", "в бою") : L("ui.party.in_reserve", "в запасе"));
            mark.AddToClassList("gm-party-slot__mark");
            cell.Add(mark);

            // ЛКМ — осмотр, ПКМ — карточка, перетаскивание — перенос. Порог жеста внутри
            // DragManipulator и отделяет клик от драга, поэтому оба живут на одном элементе.
            int index = slot.Index;
            cell.RegisterCallback<ClickEvent>(_ => actions?.Inspect?.Invoke(index));
            cell.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1) return; // правая
                actions?.OpenCard?.Invoke(index);
                evt.StopPropagation();
            });
            cell.AddManipulator(new DragManipulator(() => new DragPayload("vessel", index, id: slot.Name)));
            MakeDropZone(cell, slot, actions);

            return cell;
        }

        /// <summary>
        /// Место принимает «Сосуда»: пришедший из другого места меняется с ним местами. Экран решает
        /// только «похоже ли это на допустимый жест» — можно ли на самом деле, знает владелец состояния.
        /// </summary>
        private static void MakeDropZone(VisualElement cell, PartySlotView slot, Actions actions)
        {
            int index = slot.Index;
            cell.AddManipulator(new DropZoneManipulator(
                payload => payload.Kind == "vessel" && payload.SlotIndex != index && slot.Open,
                payload => actions?.Swap?.Invoke(payload.SlotIndex, index)));
        }

        private static VisualElement BuildStageLabel(PartySlotView slot)
        {
            var block = new VisualElement();
            block.AddToClassList("gm-party-label");

            var name = new Label(slot.Name);
            name.AddToClassList("gm-party-label__name");
            block.Add(name);

            var relic = new Label(slot.Relic ?? string.Empty);
            relic.AddToClassList("gm-party-label__relic");
            block.Add(relic);

            return block;
        }

        /// <summary>Подписать метку по имени. Молча пропускает отсутствующую: разметка может не иметь
        /// служебных строк, и экран из-за этого падать не должен.</summary>
        private static void SetLabel(VisualElement root, string name, string text)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = text;
        }
    }
}
