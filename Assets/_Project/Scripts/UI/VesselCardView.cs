using System;
using System.Collections.Generic;
using Guildmaster.UI.Components;
using Guildmaster.UI.Tooltips;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>Всё, что расширенная карточка знает о человеке. Плоская запись — вью не читает состояние.</summary>
    public readonly struct VesselCardSubject
    {
        public readonly string Name;
        public readonly string Subtitle;

        /// <summary>Статы итогом.</summary>
        public readonly IReadOnlyList<(string Label, string Value)> Stats;

        /// <summary>Перки: id для тултипа, имя, полярность.</summary>
        public readonly IReadOnlyList<(string Id, string Name, bool Positive)> Traits;

        /// <summary>Вещи по слотам: пустая строка — свободен, <c>null</c> — закрыт.</summary>
        public readonly IReadOnlyList<string> Items;

        /// <summary>Занятые места травм по ступеням: Ушибы, Раны, Увечья.</summary>
        public readonly (int Bruises, int Wounds, int Maimings) Injuries;

        /// <summary>Названия полученных травм — по ним игрок узнаёт, чем именно ранен.</summary>
        public readonly IReadOnlyList<string> InjuryNames;

        /// <summary>Ступень Закалки словом; пусто — не заработана.</summary>
        public readonly string Mettle;

        /// <summary>Лор: кто и откуда, как выглядит. Строками, как есть.</summary>
        public readonly IReadOnlyList<string> Lore;

        /// <summary>Статистика: пары «что» и «сколько».</summary>
        public readonly IReadOnlyList<(string Label, string Value)> Statistics;

        /// <summary>Реликвия, к карточке которой ведёт переход; пусто — перехода нет.</summary>
        public readonly string RelicId;

        public VesselCardSubject(string name, string subtitle,
                                 IReadOnlyList<(string, string)> stats,
                                 IReadOnlyList<(string, string, bool)> traits,
                                 IReadOnlyList<string> items,
                                 (int, int, int) injuries,
                                 IReadOnlyList<string> injuryNames,
                                 string mettle,
                                 IReadOnlyList<string> lore,
                                 IReadOnlyList<(string, string)> statistics,
                                 string relicId)
        {
            Name        = name;
            Subtitle    = subtitle;
            Stats       = stats;
            Traits      = traits;
            Items       = items;
            Injuries    = injuries;
            InjuryNames = injuryNames;
            Mettle      = mettle;
            Lore        = lore;
            Statistics  = statistics;
            RelicId     = relicId;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Name);
    }

    /// <summary>
    /// Расширенная карточка «Сосуда»: разворот с двумя табами (ГДД <c>preparation-screens</c> §4,
    /// раскладка VI-В «Клетки на развороте» и VII-А «Лор и статистика»).
    /// </summary>
    /// <remarks>
    /// <b>Единственный дом травм</b> до появления панели эффектов: на страницах подготовки их нет по
    /// решению 22.08.2026. Поэтому шесть мест ступенями 3/2/1 показываются целиком — и занятые, и
    /// свободные: «сколько мне осталось смертей» должно читаться без счёта в уме.
    /// </remarks>
    public static class VesselCardView
    {
        /// <summary>Ёмкости ступеней: три Ушиба, две Раны, одно Увечье (ГДД <c>injuries-mettle</c>).</summary>
        private static readonly int[] Capacities = { 3, 2, 1 };

        public const string InjuryPipClass  = "gm-vcard__pip";
        public const string InjuryTakenClass = "gm-vcard__pip--taken";

        public static VisualElement Build(
            in VesselCardSubject subject,
            int activeTab,
            Func<string, string> localize,
            Action<int> onTab = null,
            Action onClose = null,
            Action<string> onRelic = null)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            if (subject.IsEmpty)
                return SpreadCard.Build(L("ui.vcard.nobody", "Никого"), null, null, 0, null, null, onClose);

            string[] tabs =
            {
                L("ui.vcard.tab_main", "I · ОСНОВНОЕ"),
                L("ui.vcard.tab_extra", "II · ДОПОЛНИТЕЛЬНО"),
            };

            VesselCardSubject copy = subject;
            return SpreadCard.Build(
                subject.Name, subject.Subtitle, tabs, activeTab,
                (left, right) =>
                {
                    if (activeTab == 0) FillMain(left, right, copy, onRelic, L);
                    else FillExtra(left, right, copy, L);
                },
                onTab, onClose);
        }

        /// <summary>Таб «Основное»: облик колонкой слева, четыре клетки — статы, перки, снаряжение, травмы.</summary>
        private static void FillMain(VisualElement left, VisualElement right, in VesselCardSubject s,
                                     Action<string> onRelic, Func<string, string, string> L)
        {
            var figure = new VisualElement();
            figure.AddToClassList("gm-vcard__figure");
            figure.Add(SpreadCard.Label(L("ui.vcard.figure", "вид в облачении"), "gm-vcard__figure-note"));
            left.Add(figure);

            VisualElement stats = SpreadCard.Cell(L("ui.vcard.stats", "СТАТЫ"));
            if (s.Stats != null)
                foreach ((string label, string value) in s.Stats)
                {
                    var row = new VisualElement();
                    row.AddToClassList("gm-vcard__stat");
                    row.Add(SpreadCard.Label(label, "gm-vcard__stat-label"));
                    row.Add(SpreadCard.Label(value, "gm-vcard__stat-value"));
                    stats.Add(row);
                }
            left.Add(stats);

            VisualElement traits = SpreadCard.Cell(L("ui.vcard.traits", "ПЕРКИ"));
            if (s.Traits != null && s.Traits.Count > 0)
                foreach ((string id, string name, bool positive) in s.Traits)
                    traits.Add(TraitChip(id, name, positive));
            else
                traits.Add(SpreadCard.Label(L("ui.vcard.no_traits", "перков нет"), "gm-vcard__muted"));
            left.Add(traits);

            VisualElement gear = SpreadCard.Cell(L("ui.vcard.gear", "СНАРЯЖЕНИЕ"));
            var slots = new VisualElement();
            slots.AddToClassList("gm-vcard__gear");
            if (s.Items != null)
                foreach (string item in s.Items) slots.Add(GearSlot(item));
            gear.Add(slots);
            if (!string.IsNullOrEmpty(s.RelicId))
            {
                string relicId = s.RelicId;
                // Титул арканы, а не строковый id: до 23.08.2026 кнопка звалась «Реликвия «relic.bulwark» →».
                var toRelic = new PlateButton
                {
                    text = string.Format(L("ui.vcard.to_relic", "Реликвия «{0}» →"), ContentTitle.Arcana(relicId)),
                };
                toRelic.AddToClassList("gm-vcard__to-relic");
                toRelic.clicked += () => onRelic?.Invoke(relicId);
                gear.Add(toRelic);
            }
            right.Add(gear);

            VisualElement injuries = SpreadCard.Cell(L("ui.vcard.injuries", "ТРАВМЫ"));
            injuries.Add(InjuryPips(s.Injuries));
            if (s.InjuryNames != null && s.InjuryNames.Count > 0)
                injuries.Add(SpreadCard.Label(string.Join(" · ", s.InjuryNames), "gm-vcard__muted"));
            if (!string.IsNullOrEmpty(s.Mettle))
                injuries.Add(SpreadCard.Label(string.Format(L("ui.vcard.mettle", "Закалка: {0}"), s.Mettle), "gm-vcard__muted"));
            right.Add(injuries);
        }

        /// <summary>Таб «Дополнительно»: слева кто и откуда, справа числа прожитого.</summary>
        private static void FillExtra(VisualElement left, VisualElement right, in VesselCardSubject s,
                                      Func<string, string, string> L)
        {
            left.Add(SpreadCard.Section(L("ui.vcard.lore", "КТО И ОТКУДА")));
            if (s.Lore != null && s.Lore.Count > 0)
                foreach (string line in s.Lore) left.Add(SpreadCard.Label(line, "gm-vcard__lore"));
            else
                left.Add(SpreadCard.Label(L("ui.vcard.no_lore", "досье ещё не написано"), "gm-vcard__muted"));

            right.Add(SpreadCard.Section(L("ui.vcard.statistics", "СТАТИСТИКА")));
            if (s.Statistics != null && s.Statistics.Count > 0)
                foreach ((string label, string value) in s.Statistics)
                {
                    var row = new VisualElement();
                    row.AddToClassList("gm-vcard__stat");
                    row.Add(SpreadCard.Label(label, "gm-vcard__stat-label"));
                    row.Add(SpreadCard.Label(value, "gm-vcard__stat-value"));
                    right.Add(row);
                }
            else
                right.Add(SpreadCard.Label(L("ui.vcard.no_stats", "счёт ещё не начат"), "gm-vcard__muted"));
        }

        /// <summary>
        /// Шесть мест ступенями 3/2/1: занятые залиты, свободные пусты. Группы разделены пробелом —
        /// ступень читается расстоянием, подписи в строку не влезают.
        /// </summary>
        private static VisualElement InjuryPips((int Bruises, int Wounds, int Maimings) taken)
        {
            var row = new VisualElement();
            row.AddToClassList("gm-vcard__pips");

            int[] occupied = { taken.Bruises, taken.Wounds, taken.Maimings };
            for (int grade = 0; grade < Capacities.Length; grade++)
            {
                var group = new VisualElement();
                group.AddToClassList("gm-vcard__pip-group");
                for (int i = 0; i < Capacities[grade]; i++)
                {
                    var pip = new VisualElement();
                    pip.AddToClassList(InjuryPipClass);
                    pip.EnableInClassList(InjuryTakenClass, i < occupied[grade]);
                    group.Add(pip);
                }
                row.Add(group);
            }
            return row;
        }

        private static VisualElement TraitChip(string id, string name, bool positive)
        {
            var chip = new VisualElement();
            chip.AddToClassList("gm-vcard__trait");
            chip.Add(SpreadCard.Label(positive ? "+" : "−", "gm-vcard__trait-sign"));
            chip.Add(SpreadCard.Label(name ?? id, null));
            if (!string.IsNullOrEmpty(id))
                chip.AddManipulator(new TooltipManipulator(() => TooltipRequest.Keyword(id)));
            return chip;
        }

        private static VisualElement GearSlot(string itemId)
        {
            var cell = new VisualElement();
            cell.AddToClassList("gm-vcard__gear-slot");
            bool locked = itemId == null;
            bool filled = !locked && itemId.Length > 0;
            cell.EnableInClassList("gm-vcard__gear-slot--locked", locked);
            cell.EnableInClassList("gm-vcard__gear-slot--filled", filled);
            if (filled) cell.AddManipulator(new TooltipManipulator(() => TooltipRequest.Keyword(itemId)));
            return cell;
        }
    }
}
