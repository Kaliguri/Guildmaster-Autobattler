using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Чип: иконка + подпись как ОДИН элемент. Единый источник вида для любых «иконка + название»
    /// (фильтры инвентаря, теги юнита и т.п.) — вид/состояния задаются классами <c>.gm-chip*</c>
    /// в components.uss, иконку даёт код (<see cref="SetIcon"/>) или класс-модификатор на самом чипе.
    /// </summary>
    [UxmlElement]
    public partial class Chip : VisualElement
    {
        private readonly VisualElement _icon;
        private readonly Label _label;

        // Как у пластины: подпись хранится исходной, регистр приходит из USS (см. UiTextCase).
        private string _sourceText = string.Empty;
        private UiTextCaseMode _textCase = UiTextCaseMode.None;

        /// <summary>Подпись чипа (текст справа от иконки). Отдаётся исходной, без применённого регистра.</summary>
        [UxmlAttribute]
        public string Text
        {
            get => _sourceText;
            set
            {
                _sourceText = value ?? string.Empty;
                ApplyTextCase();
            }
        }

        private void ApplyTextCase() => _label.text = UiTextCase.Apply(_sourceText, _textCase);

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            UiTextCaseMode textCase = evt.customStyle.TryGetValue(UiTextCase.Property, out string raw)
                ? UiTextCase.Parse(raw)
                : UiTextCaseMode.None;
            if (textCase == _textCase) return;

            _textCase = textCase;
            ApplyTextCase();
        }

        public Chip()
        {
            // Фокус: чип кликают мышью, значит до него обязана доходить и клавиатура с геймпадом —
            // на Steam Deck курсора нет вовсе. VisualElement фокус не принимает по умолчанию, и до
            // 06.08.2026 правило `.gm-chip:focus` в теме было мёртвым: состояние не наступало никогда.
            focusable = true;

            AddToClassList("gm-chip");
            _icon = new VisualElement { name = "icon", pickingMode = PickingMode.Ignore };
            _icon.AddToClassList("gm-chip__icon");
            _label = new Label { pickingMode = PickingMode.Ignore };
            _label.AddToClassList("gm-text-note");
            _label.AddToClassList("gm-text--muted");
            _label.AddToClassList("gm-chip__label");
            Add(_icon);
            Add(_label);

            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        /// <summary>Подсветить как выбранный (активный фильтр, надетый тег и т.п.).</summary>
        public void SetActive(bool on) => EnableInClassList("gm-chip--active", on);

        /// <summary>Иконка чипа спрайтом (null → пусто; иконку можно задать и классом-модификатором).</summary>
        public void SetIcon(Sprite sprite)
        {
            if (sprite != null) _icon.style.backgroundImage = new StyleBackground(sprite);
            else _icon.style.backgroundImage = StyleKeyword.None;
        }
    }
}
