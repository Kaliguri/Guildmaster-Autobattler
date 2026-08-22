using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Строка тумблера: подпись слева (растянута) + чекбокс справа, выровненный в колонку.
    /// Пара к <see cref="SliderRow"/> для страниц настроек. Вид — классы <c>.gm-toggle-row*</c>;
    /// подпись задаётся снаружи (loc), значение проводится через VM (<see cref="Toggle"/> /
    /// <see cref="SetValueWithoutNotify"/>).
    /// </summary>
    /// <remarks>
    /// <b>Переключает вся строка, а не квадратик</b> (правило Макса 22.08.2026: «мы должны мочь нажать
    /// на любую часть элемента (даже на текст и тд), а не только на сам чекбокс»). Мишень в двадцать
    /// пикселей на строке во всю панель — промах по умолчанию, и игрок читает это как «не нажимается».
    /// <para>Клик по самому тумблеру сюда доходит всплытием, и переключить его вторично значило бы
    /// вернуть значение назад — поэтому клики из поддерева тумблера мы пропускаем мимо: он уже
    /// отработал сам.</para>
    /// </remarks>
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
            _label.AddToClassList("gm-text-body");
            _label.AddToClassList("gm-toggle-row__label");
            Add(_label);

            // Тумблер без собственной подписи (текст держит _label слева) — остаётся только чекбокс справа.
            _toggle = new Toggle { name = "toggle" };
            _toggle.AddToClassList("gm-toggle-row__check");
            Add(_toggle);

            RegisterCallback<ClickEvent>(OnRowClicked);
        }

        // Клик мимо самого тумблера — это тоже «переключи меня». Тумблер свой клик обработал сам, и
        // повторное переключение вернуло бы значение обратно, поэтому его поддерево пропускаем.
        private void OnRowClicked(ClickEvent evt)
        {
            if (!enabledInHierarchy) return;
            if (evt.target is VisualElement target && (target == _toggle || _toggle.Contains(target))) return;

            _toggle.value = !_toggle.value;
            evt.StopPropagation();
        }
    }
}
