using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Строка слайдера: подпись слева + слайдер + значение в процентах справа. Источник для настроек.
    /// Вид — классы <c>.gm-slider-row*</c>/<c>.gm-slider</c>; значение-подпись обновляется само.
    /// </summary>
    [UxmlElement]
    public partial class SliderRow : VisualElement
    {
        private readonly Label _label;
        private readonly Slider _slider;
        private readonly Label _value;

        [UxmlAttribute]
        public string LabelText
        {
            get => _label.text;
            set => _label.text = value;
        }

        [UxmlAttribute]
        public float LowValue
        {
            get => _slider.lowValue;
            set { _slider.lowValue = value; UpdateValueLabel(_slider.value); }
        }

        [UxmlAttribute]
        public float HighValue
        {
            get => _slider.highValue;
            set { _slider.highValue = value; UpdateValueLabel(_slider.value); }
        }

        [UxmlAttribute]
        public float Value
        {
            get => _slider.value;
            set { _slider.value = value; UpdateValueLabel(value); }
        }

        /// <summary>Прямой доступ к слайдеру для проводки (RegisterValueChangedCallback и т.п.).</summary>
        public Slider Slider => _slider;

        /// <summary>Задать значение БЕЗ события (VM → UI), но с обновлением подписи-процента.</summary>
        public void SetValueWithoutNotify(float v)
        {
            _slider.SetValueWithoutNotify(v);
            UpdateValueLabel(v);
        }

        public SliderRow()
        {
            AddToClassList("gm-slider-row");

            _label = new Label { name = "label" };
            _label.AddToClassList("gm-slider-row__label");
            Add(_label);

            _slider = new Slider { name = "slider", lowValue = 0f, highValue = 1f };
            _slider.AddToClassList("gm-slider");
            Add(_slider);

            _value = new Label("0%") { name = "value" };
            _value.AddToClassList("gm-slider-row__value");
            Add(_value);

            _slider.RegisterValueChangedCallback(e => UpdateValueLabel(e.newValue));
            UpdateValueLabel(_slider.value);
        }

        private void UpdateValueLabel(float v)
        {
            float t = Mathf.InverseLerp(_slider.lowValue, _slider.highValue, v);
            _value.text = Mathf.RoundToInt(t * 100f) + "%";
        }
    }
}
