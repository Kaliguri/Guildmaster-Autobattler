using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Карточка мементо: рамка + область ВИЗУАЛА + имя + состояния (текущая/выбранная).
    /// Там, где Мементо ПИКАЮТ (награда/лоадаут), primary = анимированный спрайт персонажа, не иконка
    /// (утв. дизайн, план 10 §5): визуал подаётся снаружи через шов — <see cref="SetVisual"/>
    /// (RenderTexture с боевого рига idle/attack) или <see cref="SetSprite"/> (статичный портрет/фолбэк).
    /// Один источник для витрины награды, грида лоадаута, хаба. Вид — классы <c>.gm-card*</c>.
    /// </summary>
    [UxmlElement]
    public partial class RelicCard : VisualElement
    {
        private readonly VisualElement _sprite;
        private readonly Label _name;
        private bool _selected;
        private bool _current;

        [UxmlAttribute]
        public string RelicName
        {
            get => _name.text;
            set => _name.text = value;
        }

        /// <summary>Подсветка выбранной карточки (латунная рамка).</summary>
        [UxmlAttribute]
        public bool Selected
        {
            get => _selected;
            set { _selected = value; EnableInClassList("gm-card--selected", value); }
        }

        /// <summary>Метка «сейчас надета/активна».</summary>
        [UxmlAttribute]
        public bool Current
        {
            get => _current;
            set { _current = value; EnableInClassList("gm-card--current", value); }
        }

        public RelicCard()
        {
            // Карточка — это выбор игрока (награда, инвентарь), и он обязан быть доступен без мыши.
            focusable = true;

            AddToClassList("gm-card");
            _sprite = new VisualElement { name = "sprite", pickingMode = PickingMode.Ignore };
            _sprite.AddToClassList("gm-card__sprite");
            Add(_sprite);
            _name = new Label { name = "name" };
            _name.AddToClassList("gm-text-name");
            _name.AddToClassList("gm-card__name");
            Add(_name);
        }

        /// <summary>Статичный визуал карточки: портрет/спрайт (фолбэк, когда нет анимированного рига).</summary>
        public void SetSprite(Sprite sprite)
        {
            if (sprite != null) _sprite.style.backgroundImage = new StyleBackground(sprite);
            else _sprite.style.backgroundImage = StyleKeyword.None;
        }

        /// <summary>
        /// Анимированный визуал: <see cref="RenderTexture"/> с боевого рига (реальный юнит, idle/attack).
        /// Шов под план 10 §5 — риг рендерит `ViewPrefab` Мементо в RT, карточка лишь показывает его.
        /// </summary>
        public void SetVisual(Texture texture)
        {
            if (texture is RenderTexture rt)
                _sprite.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            else if (texture is Texture2D t2)
                _sprite.style.backgroundImage = new StyleBackground(t2);
            else
                _sprite.style.backgroundImage = StyleKeyword.None;
        }

    }
}
