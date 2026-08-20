using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Строка выбора: подпись слева + выпадающий список справа. Третья к <see cref="SliderRow"/> и
    /// <see cref="ToggleRow"/> для страниц настроек — там, где вариантов больше двух и они не числовые
    /// (разрешение, режим окна, частота обновления).
    /// <para>Вид — классы <c>.gm-select-row*</c>; подпись и варианты задаются снаружи (loc/данные),
    /// значение проводится через VM.</para>
    /// </summary>
    [UxmlElement]
    public partial class SelectRow : VisualElement
    {
        private readonly Label         _label;
        private readonly DropdownField _dropdown;

        [UxmlAttribute]
        public string LabelText
        {
            get => _label.text;
            set => _label.text = value;
        }

        /// <summary>Прямой доступ к списку для проводки (RegisterValueChangedCallback и т.п.).</summary>
        public DropdownField Dropdown => _dropdown;

        /// <summary>Индекс выбранного варианта.</summary>
        public int Index => _dropdown.index;

        /// <summary>Заполнить варианты и выбрать один — без события, иначе словишь эхо от собственной записи.</summary>
        public void SetChoices(List<string> choices, int selectedIndex)
        {
            _dropdown.choices = choices;
            _dropdown.SetValueWithoutNotify(
                selectedIndex >= 0 && selectedIndex < choices.Count ? choices[selectedIndex] : null);
        }

        /// <summary>
        /// Погасить строку целиком (подпись тоже). Нужна там, где вариант недоступен не по нашей воле:
        /// частоту обновления Unity меняет только в эксклюзивном полноэкранном, и живой список,
        /// который ничего не делает, врал бы игроку.
        /// </summary>
        /// <remarks>
        /// Класса-модификатора здесь больше нет: он вешался ровно вместе с <c>SetEnabled(false)</c>
        /// и потому был вторым владельцем одного факта. Вид выключенной строки держит псевдокласс
        /// <c>:disabled</c> — тот, что движок поднимает сам.
        /// </remarks>
        public void SetRowEnabled(bool enabled) => SetEnabled(enabled);

        public SelectRow()
        {
            AddToClassList("gm-select-row");

            _label = new Label { name = "label" };
            _label.AddToClassList("gm-text-body");
            _label.AddToClassList("gm-select-row__label");
            Add(_label);

            _dropdown = new DropdownField { name = "dropdown" };
            _dropdown.AddToClassList("gm-select-row__dropdown");
            Add(_dropdown);
        }
    }
}
