using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Каркас модального оверлея: затемняющий scrim + центр-панель + заголовок + дивайдер + тело.
    /// Дедуплицирует раму, которую повторяет каждый оверлей (награда/ивент/хаб/настройки/пауза).
    /// Дети из UXML/кода попадают в тело (см. <see cref="contentContainer"/>). Вид — классы .gm-*.
    /// </summary>
    [UxmlElement]
    public partial class ModalPanel : VisualElement
    {
        private readonly VisualElement _panel;
        private readonly Label _title;
        private readonly VisualElement _body;
        private string _panelModifier;

        [UxmlAttribute]
        public string Title
        {
            get => _title.text;
            set => _title.text = value;
        }

        /// <summary>Доп. класс-модификатор панели (напр. <c>gm-panel--hub</c>) для ширины/варианта.</summary>
        [UxmlAttribute]
        public string PanelModifier
        {
            get => _panelModifier;
            set
            {
                if (!string.IsNullOrEmpty(_panelModifier)) _panel.RemoveFromClassList(_panelModifier);
                _panelModifier = value;
                if (!string.IsNullOrEmpty(_panelModifier)) _panel.AddToClassList(_panelModifier);
            }
        }

        public override VisualElement contentContainer => _body;

        public ModalPanel()
        {
            AddToClassList("gm-screen");

            _panel = new VisualElement { name = "panel" };
            _panel.AddToClassList("gm-panel");
            hierarchy.Add(_panel);

            _title = new Label { name = "title" };
            _title.AddToClassList("gm-panel__title");
            _panel.Add(_title);

            var divider = new VisualElement { name = "divider" };
            divider.AddToClassList("gm-divider");
            _panel.Add(divider);

            _body = new VisualElement { name = "content" };
            _panel.Add(_body);
        }
    }
}
