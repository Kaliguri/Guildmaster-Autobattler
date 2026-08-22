using System;
using System.Collections.Generic;
using Guildmaster.UI.Tooltips;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>Что панель осмотра знает о юните. Плоская запись — панель не читает состояние сама.</summary>
    public readonly struct InspectSubject
    {
        /// <summary>Имя в шапке.</summary>
        public readonly string Name;

        /// <summary>Реликвия и класс одной строкой: «Щит · Танк · ур. 3».</summary>
        public readonly string Subtitle;

        /// <summary>Статы ИТОГОМ, парами «что» и «сколько». Остаются числами на виду — по ним сравнивают.</summary>
        public readonly IReadOnlyList<(string Label, string Value)> Stats;

        /// <summary>Перки: имя и полярность. Описание живёт в тултипе.</summary>
        public readonly IReadOnlyList<(string Id, string Name, bool Positive)> Traits;

        /// <summary>Надетые вещи по слотам; пустая строка — слот свободен, <c>null</c> — закрыт.</summary>
        public readonly IReadOnlyList<string> Items;

        /// <summary>Профиль поведения человеческим словом.</summary>
        public readonly string Behaviour;

        /// <summary>Id Реликвии; пусто — кнопка «о Реликвии» гаснет.</summary>
        public readonly string RelicId;

        public InspectSubject(string name, string subtitle,
                              IReadOnlyList<(string Label, string Value)> stats,
                              IReadOnlyList<(string Id, string Name, bool Positive)> traits,
                              IReadOnlyList<string> items, string behaviour, string relicId)
        {
            Name      = name;
            Subtitle  = subtitle;
            Stats     = stats;
            Traits    = traits;
            Items     = items;
            Behaviour = behaviour;
            RelicId   = relicId;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Name);
    }

    /// <summary>
    /// Панель осмотра: узкая колонка у правой кромки, открывается по ЛКМ и живёт ВЕЗДЕ, где есть
    /// юнит — подготовка, бой, двор, расстановка (ГДД <c>preparation-screens</c> §3, раскладка III-А).
    /// </summary>
    /// <remarks>
    /// <b>Контрол, а не экран.</b> Один жест обязан давать один и тот же ответ во всей игре; панель,
    /// написанная внутри экрана подготовки, во втором месте появилась бы копией и разошлась с первой.
    /// <para><b>Статы остаются числами, всё остальное — именами.</b> Описания живут в тултипах
    /// (решение Макса 22.08.2026), но панель существует ради СРАВНЕНИЯ двух бойцов, а сравнивать по
    /// всплывающим окнам нельзя: пришлось бы наводиться на каждую строку у каждого.</para>
    /// <para><b>Кнопка «о Реликвии» без Реликвии гаснет текстом, но остаётся живой</b> — правило
    /// <c>ui-feedback</c> §1: мёртвая кнопка читается как сломанный экран.</para>
    /// </remarks>
    public static class InspectPanel
    {
        public const string RootClass     = "gm-inspect";
        public const string SectionClass  = "gm-inspect__section";
        public const string StatRowClass  = "gm-inspect__stat";
        public const string TraitChipClass = "gm-inspect__trait";
        public const string DisabledClass = "gm-inspect__button--off";

        /// <summary>Собрать панель. Пустой субъект даёт панель с приглашением выбрать юнита.</summary>
        public static VisualElement Build(
            in InspectSubject subject,
            Action onAboutVessel = null,
            Action onAboutRelic = null,
            Func<string, string> localize = null)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            var root = new VisualElement();
            root.AddToClassList(RootClass);

            if (subject.IsEmpty)
            {
                root.Add(Text(L("ui.inspect.nobody", "Выберите бойца"), "gm-inspect__hint"));
                return root;
            }

            var head = new VisualElement();
            head.AddToClassList("gm-inspect__head");
            var portrait = new VisualElement();
            portrait.AddToClassList("gm-inspect__portrait");
            head.Add(portrait);
            var titles = new VisualElement();
            titles.Add(Text(subject.Name, "gm-inspect__name"));
            titles.Add(Text(subject.Subtitle ?? string.Empty, "gm-inspect__subtitle"));
            head.Add(titles);
            root.Add(head);

            // Скролл обязателен: содержимое длиннее колонки, и это принятая цена раскладки III-А.
            var body = new ScrollView();
            body.AddToClassList("gm-inspect__body");
            root.Add(body);

            if (subject.Stats != null && subject.Stats.Count > 0)
            {
                body.Add(Section(L("ui.inspect.stats", "СТАТЫ")));
                foreach ((string label, string value) in subject.Stats)
                {
                    var row = new VisualElement();
                    row.AddToClassList(StatRowClass);
                    row.Add(Text(label, "gm-inspect__stat-label"));
                    row.Add(Text(value, "gm-inspect__stat-value"));
                    body.Add(row);
                }
            }

            if (subject.Traits != null && subject.Traits.Count > 0)
            {
                body.Add(Section(L("ui.inspect.traits", "ПЕРКИ")));
                foreach ((string id, string name, bool positive) in subject.Traits)
                    body.Add(TraitChip(id, name, positive));
            }

            if (subject.Items != null && subject.Items.Count > 0)
            {
                body.Add(Section(L("ui.inspect.gear", "СНАРЯЖЕНИЕ")));
                var slots = new VisualElement();
                slots.AddToClassList("gm-inspect__gear");
                foreach (string item in subject.Items) slots.Add(GearSlot(item));
                body.Add(slots);
            }

            if (!string.IsNullOrEmpty(subject.Behaviour))
            {
                body.Add(Section(L("ui.inspect.behaviour", "ПОВЕДЕНИЕ")));
                body.Add(Text(subject.Behaviour, "gm-inspect__behaviour"));
            }

            var buttons = new VisualElement();
            buttons.AddToClassList("gm-inspect__buttons");

            var aboutVessel = new PlateButton { text = L("ui.inspect.about_vessel", "О СОСУДЕ") };
            aboutVessel.clicked += () => onAboutVessel?.Invoke();
            buttons.Add(aboutVessel);

            var aboutRelic = new PlateButton { text = L("ui.inspect.about_relic", "о Реликвии") };
            bool hasRelic = !string.IsNullOrEmpty(subject.RelicId);
            // Гаснет ТЕКСТОМ и остаётся живой: отклик подтверждает, что нажатие увидено, а
            // недоступность сообщает подпись. Отключать элемент средствами движка правило запрещает.
            aboutRelic.EnableInClassList(DisabledClass, !hasRelic);
            aboutRelic.clicked += () => { if (hasRelic) onAboutRelic?.Invoke(); };
            buttons.Add(aboutRelic);

            root.Add(buttons);
            return root;
        }

        private static VisualElement TraitChip(string id, string name, bool positive)
        {
            var chip = new VisualElement();
            chip.AddToClassList(TraitChipClass);
            chip.EnableInClassList("gm-inspect__trait--plus", positive);
            chip.EnableInClassList("gm-inspect__trait--minus", !positive);
            chip.Add(Text(positive ? "+" : "−", "gm-inspect__trait-sign"));
            chip.Add(Text(name ?? id, "gm-inspect__trait-name"));

            // Что перк делает, рассказывает окно по наведению: на панели стоит только имя.
            if (!string.IsNullOrEmpty(id))
                chip.AddManipulator(new TooltipManipulator(() => TooltipRequest.Keyword(id)));

            return chip;
        }

        private static VisualElement GearSlot(string itemId)
        {
            var cell = new VisualElement();
            cell.AddToClassList("gm-inspect__gear-slot");

            bool locked = itemId == null;
            bool filled = !locked && itemId.Length > 0;
            cell.EnableInClassList("gm-inspect__gear-slot--locked", locked);
            cell.EnableInClassList("gm-inspect__gear-slot--filled", filled);

            if (filled)
                cell.AddManipulator(new TooltipManipulator(() => TooltipRequest.Keyword(itemId)));

            return cell;
        }

        private static Label Section(string title) => Text(title, SectionClass);

        private static Label Text(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }
    }
}
