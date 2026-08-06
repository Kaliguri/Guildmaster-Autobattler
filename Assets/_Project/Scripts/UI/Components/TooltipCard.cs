using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Содержимое тултипа — ЕДИНЫЙ каркас для всех видов подсказок: шапка (заголовок + мета),
    /// разделитель, тело (теги, описание, строки «подпись — значение», определения терминов).
    /// </summary>
    /// <remarks>
    /// <b>Шапка есть ВСЕГДА</b> (решение Макса 2026-07-26): и у реликвии, и у характеристики, и у
    /// короткого пояснения. Подсказка без заголовка выглядит обрывком чужого текста — игрок не понимает,
    /// к чему она относится, если увёл взгляд от курсора. Заголовок задаёт место показа, а не карточка:
    /// пустой заголовок — это дефект запроса, и <c>TooltipRequest</c> такой запрос просто не создаёт.
    /// <para>Карточка НЕ рисует рамку и фон и не знает своей позиции — это делает окно
    /// (<c>.gm-tooltip</c>) на стороне системы.</para>
    /// </remarks>
    [UxmlElement]
    public partial class TooltipCard : VisualElement
    {
        /// <summary>
        /// Просьба к окну стать шире: подпись со значением в узкой колонке переносится и читается
        /// как каша. Класс-подсказка, а не прямая правка ширины — размер остаётся делом окна.
        /// </summary>
        public const string WideHintClass = "gm-tooltip__card--wide";

        private readonly VisualElement _header;
        private readonly Label _title;
        private readonly Label _meta;
        private readonly VisualElement _divider;
        private readonly Label _tags;
        private readonly Label _desc;
        private readonly VisualElement _lines;
        private readonly VisualElement _glossary;

        public TooltipCard()
        {
            AddToClassList("gm-tooltip__card");
            pickingMode = PickingMode.Ignore;

            // --- Шапка: заголовок слева, мета справа (кит, категория, раздел глоссария) ---
            _header = new VisualElement { pickingMode = PickingMode.Ignore };
            _header.AddToClassList("gm-tooltip__header");
            _title = new Label { pickingMode = PickingMode.Ignore };
            _title.AddToClassList("gm-text-name");
            _title.AddToClassList("gm-tooltip__title");
            _meta = new Label { pickingMode = PickingMode.Ignore };
            _meta.AddToClassList("gm-text-label");
            _meta.AddToClassList("gm-text--muted");
            _meta.AddToClassList("gm-tooltip__meta");
            _header.Add(_title);
            _header.Add(_meta);
            Add(_header);

            _divider = new VisualElement { pickingMode = PickingMode.Ignore };
            _divider.AddToClassList("gm-tooltip__divider");
            Add(_divider);

            // --- Тело ---
            _tags = Line("gm-tooltip__tags");
            _tags.style.whiteSpace = WhiteSpace.Normal;

            _lines = new VisualElement { pickingMode = PickingMode.Ignore };
            _lines.AddToClassList("gm-tooltip__lines");
            _lines.style.display = DisplayStyle.None;
            Add(_lines);

            _desc = Line("gm-tooltip__desc");
            _desc.AddToClassList("gm-text-note");
            _desc.AddToClassList("gm-text--muted");
            _desc.style.whiteSpace = WhiteSpace.Normal;
            _desc.enableRichText = true; // разметка терминов: [Скрытность] со ссылкой

            _glossary = new VisualElement { pickingMode = PickingMode.Ignore };
            _glossary.AddToClassList("gm-tooltip__glossary");
            _glossary.style.display = DisplayStyle.None;
            Add(_glossary);
        }

        /// <summary>Текст описания — единственная часть, по которой ходят ссылки терминов.</summary>
        public Label Description => _desc;

        public void SetTitle(string text)
        {
            Fill(_title, text);
            SyncHeader();
        }

        public void SetMeta(string text)
        {
            Fill(_meta, text);
            SyncHeader();
        }

        public void SetTags(string text) => Fill(_tags, text);
        public void SetDesc(string text) => Fill(_desc, text);

        /// <summary>Строка разбора: подпись слева, значение справа (стат-сводка).</summary>
        public void AddLine(string label, string value)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("gm-tooltip__line");

            var caption = new Label(label ?? string.Empty) { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("gm-text-label");
            caption.AddToClassList("gm-text--muted");
            var amount = new Label(value ?? string.Empty) { pickingMode = PickingMode.Ignore };
            amount.AddToClassList("gm-text-note");
            amount.AddToClassList("gm-tooltip__line-value");

            row.Add(caption);
            row.Add(amount);
            _lines.Add(row);
            _lines.style.display = DisplayStyle.Flex;
            AddToClassList(WideHintClass);
        }

        /// <summary>
        /// Определение упомянутого термина ВНУТРИ этой же карточки (план §II.10.5, слой 2).
        /// Ссылок в нём нет намеренно: глоссарий плоский, и рекурсии нечем начаться.
        /// </summary>
        public void AddGlossaryEntry(string term, string definition)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("gm-tooltip__glossary-row");

            var name = new Label(term ?? string.Empty) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("gm-tooltip__glossary-term");
            var text = new Label(definition ?? string.Empty) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("gm-text-label");
            text.AddToClassList("gm-text--muted");
            text.style.whiteSpace = WhiteSpace.Normal;

            row.Add(name);
            row.Add(text);
            _glossary.Add(row);
            _glossary.style.display = DisplayStyle.Flex;
            AddToClassList(WideHintClass);
        }

        public void ClearLines()
        {
            _lines.Clear();
            _lines.style.display = DisplayStyle.None;
            if (_glossary.childCount == 0) RemoveFromClassList(WideHintClass);
        }

        /// <summary>Сбросить всё содержимое — карточка переиспользуется между показами.</summary>
        public void Reset()
        {
            SetTitle(null);
            SetMeta(null);
            SetTags(null);
            SetDesc(null);
            ClearLines();
            _glossary.Clear();
            _glossary.style.display = DisplayStyle.None;
            RemoveFromClassList(WideHintClass);
        }

        // Шапка и разделитель под ней живут, только если в шапке что-то есть: пустая полоса
        // с чертой выглядит как оборванная вёрстка.
        private void SyncHeader()
        {
            bool has = !string.IsNullOrEmpty(_title.text) || !string.IsNullOrEmpty(_meta.text);
            _header.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            _divider.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private Label Line(string ussClass)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(ussClass);
            label.style.display = DisplayStyle.None;
            Add(label);
            return label;
        }

        private static void Fill(Label label, string text)
        {
            label.text = text ?? string.Empty;
            label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
