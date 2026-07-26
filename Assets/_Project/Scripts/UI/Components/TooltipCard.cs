using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Содержимое тултипа: заголовок, мета-строка, теги, описание и строки «подпись — значение».
    /// Пустые части скрываются, поэтому одна карточка обслуживает и короткое пояснение, и разбор стата.
    /// </summary>
    /// <remarks>
    /// Карточка НЕ рисует рамку и фон и ничего не знает о своей позиции — это делает окно
    /// (<c>.gm-tooltip</c>) на стороне системы. Так вид окна (размерные модификаторы, флип у края)
    /// живёт в одном месте, а видов содержимого может стать сколько угодно.
    /// </remarks>
    [UxmlElement]
    public partial class TooltipCard : VisualElement
    {
        private readonly Label _title;
        private readonly Label _meta;
        private readonly Label _tags;
        private readonly Label _desc;
        private readonly VisualElement _lines;

        public TooltipCard()
        {
            AddToClassList("gm-tooltip__card");
            pickingMode = PickingMode.Ignore;

            _title = Line("gm-tooltip__title");
            _meta  = Line("gm-tooltip__meta");
            _tags  = Line("gm-tooltip__tags");
            _tags.style.whiteSpace = WhiteSpace.Normal;

            _lines = new VisualElement { pickingMode = PickingMode.Ignore };
            _lines.AddToClassList("gm-tooltip__lines");
            _lines.style.display = DisplayStyle.None;
            Add(_lines);

            _desc = Line("gm-tooltip__desc");
            _desc.style.whiteSpace = WhiteSpace.Normal;
            _desc.enableRichText = true; // keyword-разметка в описаниях (Трек Т, вложенные тултипы)
        }

        public void SetTitle(string text) => Fill(_title, text);
        public void SetMeta(string text)  => Fill(_meta, text);
        public void SetTags(string text)  => Fill(_tags, text);
        public void SetDesc(string text)  => Fill(_desc, text);

        /// <summary>
        /// Просьба к окну стать шире: подпись со значением в узкой колонке переносится и читается
        /// как каша. Класс-подсказка, а не прямая правка ширины — размер остаётся делом окна.
        /// </summary>
        public const string WideHintClass = "gm-tooltip__card--wide";

        /// <summary>Строка разбора: подпись слева, значение справа (стат-сводка сосуда).</summary>
        public void AddLine(string label, string value)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("gm-tooltip__line");

            var caption = new Label(label ?? string.Empty) { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("gm-tooltip__line-label");
            var amount = new Label(value ?? string.Empty) { pickingMode = PickingMode.Ignore };
            amount.AddToClassList("gm-tooltip__line-value");

            row.Add(caption);
            row.Add(amount);
            _lines.Add(row);
            _lines.style.display = DisplayStyle.Flex;
            AddToClassList(WideHintClass);
        }

        public void ClearLines()
        {
            _lines.Clear();
            _lines.style.display = DisplayStyle.None;
            RemoveFromClassList(WideHintClass);
        }

        /// <summary>Сбросить всё содержимое — карточка переиспользуется между показами.</summary>
        public void Reset()
        {
            SetTitle(null);
            SetMeta(null);
            SetTags(null);
            SetDesc(null);
            ClearLines();
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
