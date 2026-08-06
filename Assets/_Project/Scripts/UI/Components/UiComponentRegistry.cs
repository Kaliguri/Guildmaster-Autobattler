using System;
using System.Collections.Generic;

namespace Guildmaster.UI.Components
{
    /// <summary>Состояния интерактивного элемента — те, что UI Toolkit выражает псевдоклассами.</summary>
    /// <remarks>
    /// Набор закрытый и повторяет псевдоклассы USS один в один (<c>:hover</c>, <c>:active</c>,
    /// <c>:focus</c>, <c>:disabled</c>, <c>:checked</c>). Свои псевдоклассы Unity заводить не даёт,
    /// поэтому расширять перечень нечем и не нужно: всё остальное — модификаторы BEM
    /// (<c>--active</c>, <c>--selected</c>), а они живут в <see cref="UiComponentEntry.Variants"/>.
    /// <c>:inactive</c> и <c>:enabled</c> сознательно не заведены — мы ими не пользуемся, а пустая
    /// строка в перечне читается как «забыли реализовать».
    /// </remarks>
    [Flags]
    public enum UiElementState
    {
        None     = 0,
        Hover    = 1 << 0,
        Active   = 1 << 1,
        Focus    = 1 << 2,
        Disabled = 1 << 3,
        Checked  = 1 << 4,
    }

    /// <summary>Раздел витрины: по нему контактный лист группирует образцы.</summary>
    public enum UiComponentGroup
    {
        Buttons,
        Tabs,
        Cards,
        Rows,
        Panels,
        Overlays,
        Dev,
    }

    /// <summary>Один элемент интерфейса: его корневой класс, его варианты и обязательные состояния.</summary>
    public sealed class UiComponentEntry
    {
        /// <summary>Человекочитаемое имя — подпись под образцом в контактном листе.</summary>
        public string Label { get; }

        /// <summary>Корневой BEM-класс без точки: <c>gm-button</c>, <c>gm-slot</c>.</summary>
        public string Block { get; }

        /// <summary>Классы-модификаторы этого блока. Показываются в витрине, состояний не требуют.</summary>
        public IReadOnlyList<string> Variants { get; }

        /// <summary>Состояния, которые обязаны быть объявлены в USS. <see cref="UiElementState.None"/> — элемент декоративный.</summary>
        public UiElementState Required { get; }

        public UiComponentGroup Group { get; }

        /// <summary>
        /// Блок, от которого этот наследует состояния, или <c>null</c>. Пустая строка не годится:
        /// «нет базы» — это факт, а не пропуск.
        /// </summary>
        /// <remarks>
        /// Наследование здесь не выдумано, а взято с разметки: <c>gm-runbar__tab</c> висит на
        /// <c>gm:Chip</c> и потому отвечает на курсор правилами <c>.gm-chip:hover</c>, своих не имея.
        /// Без этого поля гейт требовал бы у него дубля — то есть заставлял бы городить правило ради
        /// зелёного там, где элемент и так работает.
        /// </remarks>
        public string Base { get; }

        /// <summary>Элемент принимает указатель: по нему кликают, он звучит, он обязан иметь состояния.</summary>
        public bool IsInteractive => Required != UiElementState.None;

        internal UiComponentEntry(string label, string block, UiComponentGroup group,
                                  UiElementState required, string baseBlock, string[] variants)
        {
            Label    = label;
            Block    = block;
            Group    = group;
            Required = required;
            Base     = baseBlock;
            Variants = variants ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Единый перечень элементов интерфейса и их состояний. Источник для гейта состояний, для
    /// контактного листа и для звука интерфейса.
    /// </summary>
    /// <remarks>
    /// <b>Зачем перечень вообще.</b> До 06.08.2026 его не было ни в каком виде, и это стоило дорого:
    /// <c>:checked</c> не был объявлен ни разу (галочка в настройках не отличалась включённой от
    /// выключенной), <c>:active</c> отсутствовал у двенадцати кликабельных классов, <c>:focus</c> — у
    /// тринадцати, а «таб» существовал в трёх несовместимых видах. Причина одна: набор состояний
    /// приходилось помнить, а помнить его негде — <c>components.uss</c> к тому дню был лентой на
    /// 3994 строки. Макс сформулировал это как «нет единого цельного набора всех элементов, их
    /// состояний».
    ///
    /// <para><b>Почему данными, а не документом.</b> Документ отстаёт молча и никого не роняет.
    /// Перечень читают трое — гейт (<c>UiStateGateTests</c>), контактный лист и
    /// <c>UiSoundSystem</c>, — и расхождение с деревом становится красным тестом в тот же день.
    /// Перечень и правка компонента уезжают одним диффом, потому что лежат в одной сборке.</para>
    ///
    /// <para><b>Почему без ссылок на UIElements.</b> Здесь только строки и флаги: как построить
    /// образец элемента, знает редакторный контактный лист, а не рантайм. Иначе рантайм-сборка
    /// потащила бы знание о витрине, которая нужна одному инструменту.</para>
    ///
    /// <para><b>Состояния требуются с БЛОКА, не с варианта.</b> Вариант (<c>--primary</c>,
    /// <c>--selected</c>) наследует состояния блока и переопределяет их по надобности; требовать
    /// свой <c>:hover</c> у каждого варианта значило бы плодить пустые правила. Варианты здесь ради
    /// витрины: их надо ВИДЕТЬ, а не гейтить.</para>
    ///
    /// <para><b>Обязательный набор — решение Макса от 06.08.2026:</b> наведение, нажатие,
    /// выключенность и фокус у всего, что кликается. Фокус нужен не мыши, а клавиатуре и геймпаду:
    /// без него интерфейс непроходим на Steam Deck, и заметить это по монитору невозможно.</para>
    /// </remarks>
    public static class UiComponentRegistry
    {
        /// <summary>Полный набор для элемента, который принимает указатель.</summary>
        public const UiElementState Interactive =
            UiElementState.Hover | UiElementState.Active | UiElementState.Focus | UiElementState.Disabled;

        /// <summary>
        /// Облегчённый набор для дев-тулинга: наведение и нажатие.
        /// </summary>
        /// <remarks>
        /// Дев-консоль и пикеры игрок не видит никогда, а фокус и выключенность там не несут смысла:
        /// строки не выключаются, а клавиатура ходит по своему полю ввода. Требовать с них полный
        /// набор значило бы держать гейт красным ради экранов, которых нет в игре.
        /// </remarks>
        public const UiElementState DevTooling = UiElementState.Hover | UiElementState.Active;

        /// <summary>
        /// Набор строки настроек: всё, кроме фокуса.
        /// </summary>
        /// <remarks>
        /// Фокус у строки принимает её контрол, а не она сама — подробности при записях строк ниже.
        /// Отдельная константа, а не «забыли флаг»: разница между «не нужно» и «не дописали» должна
        /// читаться из кода, иначе следующий заход вернёт требование обратно.
        /// </remarks>
        public const UiElementState RowInteractive =
            UiElementState.Hover | UiElementState.Active | UiElementState.Disabled;

        private const string OfButton = "gm-button";
        private const string OfPlate  = "gm-plate-button";
        private const string OfChip   = "gm-chip";

        private static readonly UiComponentEntry[] Entries =
        {
            // --- КНОПКИ ---
            New("Кнопка", "gm-button", UiComponentGroup.Buttons, Interactive, null,
                "gm-button--primary", "gm-button--danger", "gm-button--fill", "gm-button--display",
                "gm-button--unaffordable"),
            New("Пластина", "gm-plate-button", UiComponentGroup.Buttons, Interactive, null),
            New("Пункт главного меню", "gm-mainmenu__btn", UiComponentGroup.Buttons, Interactive, OfButton,
                "gm-mainmenu__btn--primary", "gm-mainmenu__btn--group-start"),
            New("Начать забег", "gm-loadout__start-btn", UiComponentGroup.Buttons, Interactive, OfPlate),
            New("Сортировка инвентаря", "gm-loadout__sort", UiComponentGroup.Buttons, Interactive, OfButton),
            New("Вариант события", "gm-event-choice", UiComponentGroup.Buttons, Interactive, OfButton,
                "gm-event-choice--unaffordable"),
            New("Слот профиля", "gm-profile__slot-pick", UiComponentGroup.Buttons, Interactive, OfButton,
                "gm-profile__slot-pick--active"),
            New("Слот гильдии", "gm-guilds__slot-pick", UiComponentGroup.Buttons, Interactive, OfButton,
                "gm-guilds__slot-pick--create"),

            // --- ВКЛАДКИ И ЧИПЫ ---
            New("Вкладка", "gm-tab", UiComponentGroup.Tabs, Interactive, OfPlate, "gm-tab--active"),
            New("Чип", "gm-chip", UiComponentGroup.Tabs, Interactive, null,
                "gm-chip--active", "gm-chip--muted", "gm-chip--sm", "gm-chip--collapsible"),
            New("Чип скошенный", "gm-chip--slanted", UiComponentGroup.Tabs, Interactive, OfChip),
            New("Таб ленты забега", "gm-runbar__tab", UiComponentGroup.Tabs, Interactive, OfChip),
            New("Фильтр инвентаря", "gm-filter-tab", UiComponentGroup.Tabs, Interactive, OfChip,
                "gm-filter-tab--last"),

            // --- КАРТОЧКИ И СЛОТЫ ---
            New("Карточка релика", "gm-card", UiComponentGroup.Cards, Interactive, null,
                "gm-card--selected", "gm-card--current", "gm-card--reward"),
            New("Карточка арканы", "gm-arcana-card", UiComponentGroup.Cards, Interactive, null,
                "gm-arcana-card--selected", "gm-arcana-card--locked"),
            New("Карточка лавки", "gm-shop__card", UiComponentGroup.Cards, Interactive, null,
                "gm-shop__card--sold"),
            New("Строка запаса", "gm-shop__stash-row", UiComponentGroup.Cards, Interactive, null),
            New("Сосуд во Дворе", "gm-hub-vessel", UiComponentGroup.Cards, Interactive, null),
            New("Слот", "gm-slot", UiComponentGroup.Cards, Interactive, null,
                "gm-slot--selected", "gm-slot--empty", "gm-slot--sm", "gm-slot--md", "gm-slot--lg"),
            New("Строка дропа", "gm-reward-drop__row", UiComponentGroup.Cards, Interactive, null,
                "gm-reward-drop__row--selected"),
            New("Ячейка стата", "gm-stat", UiComponentGroup.Cards, Interactive, null),
            New("Цвет профиля", "gm-profile__swatch", UiComponentGroup.Cards, Interactive, null,
                "gm-profile__swatch--picked"),
            New("Курсор профиля", "gm-profile__cursor", UiComponentGroup.Cards, Interactive, null,
                "gm-profile__cursor--picked"),
            New("Крышка сундука", "gm-chest__lid", UiComponentGroup.Cards, Interactive, null,
                "gm-chest__lid--open"),

            // --- СТРОКИ НАСТРОЕК ---
            // ФОКУСА У СТРОКИ НЕТ, и это решение, а не пропуск: фокус принимает её контрол (Toggle,
            // DropdownField, Slider) — он и есть цель клавиатуры. Подсветить строку от фокуса
            // потомка USS не может (`:focus-within` в UI Toolkit не существует), а сделать строку
            // focusable значит поставить Tab две остановки подряд на одном и том же месте.
            // Переключатель — единственный элемент с `:checked`: псевдокласс ставится на сам Toggle,
            // поэтому правило пишется через потомка.
            New("Переключатель", "gm-toggle-row", UiComponentGroup.Rows,
                RowInteractive | UiElementState.Checked, null),
            New("Выбор", "gm-select-row", UiComponentGroup.Rows, RowInteractive, null,
                "gm-select-row--disabled"),
            New("Слайдер", "gm-slider-row", UiComponentGroup.Rows, RowInteractive, null),

            // --- ПАНЕЛИ И ДЕКОР ---
            New("Панель", "gm-panel", UiComponentGroup.Panels, UiElementState.None, null,
                "gm-panel--dialog", "gm-panel--menu", "gm-panel--system"),
            New("Оправа", "gm-panel__frame", UiComponentGroup.Panels, UiElementState.None, null),
            New("Скошенная подложка", "gm-slant", UiComponentGroup.Panels, UiElementState.None, null),
            New("Вуаль кромки", "gm-edge-veil", UiComponentGroup.Panels, UiElementState.None, null),

            // --- ПОВЕРХ ВСЕГО ---
            New("Подсказка", "gm-tooltip", UiComponentGroup.Overlays, UiElementState.None, null,
                "gm-tooltip--wide", "gm-tooltip--sticky"),
            New("Карточка подсказки", "gm-tooltip__card", UiComponentGroup.Overlays, UiElementState.None, null,
                "gm-tooltip__card--wide"),

            // --- ДЕВ-ТУЛИНГ ---
            New("Инструмент консоли", "gm-console__tool", UiComponentGroup.Dev, DevTooling, null,
                "gm-console__tool--active"),
            New("Совпадение консоли", "gm-console__hit", UiComponentGroup.Dev, DevTooling, null,
                "gm-console__hit--selected"),
            New("Строка пикера", "gm-picker__row", UiComponentGroup.Dev, DevTooling, null,
                "gm-picker__row--selected", "gm-picker__row--head"),
            New("Заголовок пикера", "gm-picker__head-cell", UiComponentGroup.Dev, DevTooling, null,
                "gm-picker__head-cell--sorted"),
        };

        /// <summary>Весь перечень в порядке объявления.</summary>
        public static IReadOnlyList<UiComponentEntry> All => Entries;

        /// <summary>
        /// Классы всех интерактивных элементов. Зовётся из <c>UiSoundSystem</c>: звук наведения и
        /// клика идёт по тому же перечню, что и гейт состояний.
        /// </summary>
        /// <remarks>
        /// До перевода на реестр список кликабельных классов лежал в самом <c>UiSoundSystem</c> и
        /// отставал: сосуд во Дворе, карточка лавки, строка пикера, строка дропа и свотч профиля
        /// кликались молча. Ровно тот случай, ради которого перечень и заводился.
        ///
        /// <para>Отдаётся массивом, а не <c>IEnumerable</c> с <c>yield</c>: потребитель ходит по нему
        /// на каждое движение указателя, и энумератор аллоцировал бы на кадр.</para>
        /// </remarks>
        public static IReadOnlyList<string> InteractiveBlocks { get; } = BuildInteractiveBlocks();

        private static string[] BuildInteractiveBlocks()
        {
            var blocks = new List<string>(Entries.Length);
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].IsInteractive) blocks.Add(Entries[i].Block);
            }
            return blocks.ToArray();
        }

        private static UiComponentEntry New(string label, string block, UiComponentGroup group,
                                            UiElementState required, string baseBlock,
                                            params string[] variants)
            => new(label, block, group, required, baseBlock, variants);
    }
}
