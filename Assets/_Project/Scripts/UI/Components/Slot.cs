using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Слот: рамка-квадрат с опциональной иконкой. Пустой = плейсхолдер (баннеры, запас, инвентарь).
    /// Вид — классы <c>.gm-slot*</c> (components.uss); иконку/размер подаёт код или UXML-атрибут.
    /// </summary>
    [UxmlElement]
    public partial class Slot : VisualElement
    {
        public enum SlotSize { Sm, Md }

        private readonly VisualElement _icon;
        private SlotSize _size = SlotSize.Md;

        /// <summary>Размер слота (sm — компактный ряд, md — крупная ячейка).</summary>
        [UxmlAttribute]
        public SlotSize Size
        {
            get => _size;
            set { _size = value; ApplySize(); }
        }

        public Slot()
        {
            AddToClassList("gm-slot");
            _icon = new VisualElement { name = "icon", pickingMode = PickingMode.Ignore };
            _icon.AddToClassList("gm-slot__icon");
            Add(_icon);
            ApplySize();
            SetIcon(null);
        }

        /// <summary>Заполнить слот иконкой (null → пустой плейсхолдер).</summary>
        public void SetIcon(Sprite sprite)
        {
            if (sprite != null) _icon.style.backgroundImage = new StyleBackground(sprite);
            else _icon.style.backgroundImage = StyleKeyword.None;
            EnableInClassList("gm-slot--empty", sprite == null);
        }

        private void ApplySize()
        {
            EnableInClassList("gm-slot--sm", _size == SlotSize.Sm);
            EnableInClassList("gm-slot--md", _size == SlotSize.Md);
        }
    }
}
