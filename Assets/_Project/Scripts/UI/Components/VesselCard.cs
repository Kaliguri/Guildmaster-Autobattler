using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Карточка сосуда команды: имя носителя + строка надетого релика (мини-иконка + имя). Кликабельна.
    /// Источник для ряда команды в хабе. Вид — классы <c>.gm-hub-vessel*</c>; данные/клик подаёт код.
    /// </summary>
    [UxmlElement]
    public partial class VesselCard : VisualElement
    {
        private readonly Label _name;
        private readonly VisualElement _relicIcon;
        private readonly Label _relicName;

        /// <summary>Клик по карточке (уводит в per-unit loadout).</summary>
        public event Action Clicked;

        [UxmlAttribute]
        public string VesselName
        {
            get => _name.text;
            set => _name.text = value;
        }

        [UxmlAttribute]
        public string RelicName
        {
            get => _relicName.text;
            set => _relicName.text = value;
        }

        public VesselCard()
        {
            // Карточка сосуда кликабельна — значит доступна и с клавиатуры (см. RelicCard).
            focusable = true;

            AddToClassList("gm-hub-vessel");

            _name = new Label { name = "vessel-name" };
            _name.AddToClassList("gm-hub-vessel__name");
            Add(_name);

            var row = new VisualElement { name = "relic-row" };
            row.AddToClassList("gm-hub-vessel__relic");
            _relicIcon = new VisualElement { name = "relic-icon", pickingMode = PickingMode.Ignore };
            _relicIcon.AddToClassList("gm-hub-vessel__relic-icon");
            row.Add(_relicIcon);
            _relicName = new Label { name = "relic-name" };
            _relicName.AddToClassList("gm-hub-vessel__relic-name");
            row.Add(_relicName);
            Add(row);

            RegisterCallback<ClickEvent>(_ => Clicked?.Invoke());
        }

        public void SetRelicIcon(Sprite sprite)
        {
            if (sprite != null) _relicIcon.style.backgroundImage = new StyleBackground(sprite);
            else _relicIcon.style.backgroundImage = StyleKeyword.None;
        }
    }
}
