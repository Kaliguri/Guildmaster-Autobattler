using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Строка тумблера: подпись слева (растянута) + чекбокс справа, выровненный в колонку.
    /// Пара к <see cref="SliderRow"/> для страниц настроек. Вид — классы <c>.gm-toggle-row*</c>;
    /// подпись задаётся снаружи (loc), значение проводится через VM (<see cref="Toggle"/> /
    /// <see cref="SetValueWithoutNotify"/>).
    /// </summary>
    [UxmlElement]
    public partial class ToggleRow : VisualElement
    {
        private readonly Label _label;
        private readonly Toggle _toggle;

        [UxmlAttribute]
        public string LabelText
        {
            get => _label.text;
            set => _label.text = value;
        }

        [UxmlAttribute]
        public bool Value
        {
            get => _toggle.value;
            set => _toggle.SetValueWithoutNotify(value);
        }

        /// <summary>Прямой доступ к тумблеру для проводки (RegisterValueChangedCallback и т.п.).</summary>
        public Toggle Toggle => _toggle;

        /// <summary>Задать значение БЕЗ события (VM → UI), иначе словишь эхо и зациклишься.</summary>
        public void SetValueWithoutNotify(bool v) => _toggle.SetValueWithoutNotify(v);

        public ToggleRow()
        {
            AddToClassList("gm-toggle-row");

            _label = new Label { name = "label" };
            _label.AddToClassList("gm-toggle-row__label");
            Add(_label);

            // Тумблер без собственной подписи (текст держит _label слева) — остаётся только чекбокс справа.
            _toggle = new Toggle { name = "toggle" };
            _toggle.AddToClassList("gm-toggle-row__check");
            Add(_toggle);
        }
    }
}
