using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Всплывающая подсказка при наведении (план 10 §5): заголовок + мета-строка + теги + описание.
    /// Абсолютно спозиционирована владельцем экрана; сама некликабельна. Вид — классы <c>.gm-tooltip*</c>.
    /// Пустые поля скрываются. Структурный компонент — контент подаёт экран из данных релика.
    /// </summary>
    [UxmlElement]
    public partial class Tooltip : VisualElement
    {
        private readonly Label _title;
        private readonly Label _meta;
        private readonly Label _tags;
        private readonly Label _desc;

        public Tooltip()
        {
            AddToClassList("gm-tooltip");
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.display = DisplayStyle.None;

            _title = new Label(); _title.AddToClassList("gm-tooltip__title"); Add(_title);
            _meta  = new Label(); _meta.AddToClassList("gm-tooltip__meta"); Add(_meta);
            _tags  = new Label(); _tags.AddToClassList("gm-tooltip__tags"); _tags.style.whiteSpace = WhiteSpace.Normal; Add(_tags);
            _desc  = new Label(); _desc.AddToClassList("gm-tooltip__desc"); _desc.style.whiteSpace = WhiteSpace.Normal; Add(_desc);
        }

        /// <summary>Заполнить подсказку; пустые строки скрывают свою строку.</summary>
        public void Set(string title, string meta, string tags, string desc)
        {
            SetLine(_title, title);
            SetLine(_meta, meta);
            SetLine(_tags, tags);
            SetLine(_desc, desc);
        }

        /// <summary>Показать в точке панели (координаты левого-верхнего угла).</summary>
        public void ShowAt(Vector2 panelPos)
        {
            style.left = panelPos.x;
            style.top  = panelPos.y;
            style.display = DisplayStyle.Flex;
        }

        public void Hide() => style.display = DisplayStyle.None;

        private static void SetLine(Label label, string text)
        {
            label.text = text ?? string.Empty;
            label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
