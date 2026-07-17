using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>Info-тег для карточки: иконка (опц.) + готовая (локализованная) подпись. Loc резолвит потребитель.</summary>
    public readonly struct RelicCardTag
    {
        public readonly Sprite Icon;
        public readonly string Label;
        public RelicCardTag(Sprite icon, string label) { Icon = icon; Label = label; }
    }

    /// <summary>
    /// Карточка реликвии: рамка + область ВИЗУАЛА + имя + состояния (текущая/выбранная).
    /// Там, где релик ПИКАЮТ (награда/лоадаут), primary = анимированный спрайт персонажа, не иконка
    /// (утв. дизайн, план 10 §5): визуал подаётся снаружи через шов — <see cref="SetVisual"/>
    /// (RenderTexture с боевого рига idle/attack) или <see cref="SetSprite"/> (статичный портрет/фолбэк).
    /// Один источник для витрины награды, грида лоадаута, хаба. Вид — классы <c>.gm-card*</c>.
    /// </summary>
    [UxmlElement]
    public partial class RelicCard : VisualElement
    {
        private static readonly string[] RarityClasses =
            { "gm-card--rarity-trash", "gm-card--rarity-common", "gm-card--rarity-unique" };

        private readonly VisualElement _sprite;
        private readonly Label _name;
        private readonly VisualElement _tags;
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
            AddToClassList("gm-card");
            _sprite = new VisualElement { name = "sprite", pickingMode = PickingMode.Ignore };
            _sprite.AddToClassList("gm-card__sprite");
            Add(_sprite);
            _name = new Label { name = "name" };
            _name.AddToClassList("gm-card__name");
            Add(_name);
            _tags = new VisualElement { name = "tags", pickingMode = PickingMode.Ignore };
            _tags.AddToClassList("gm-card__tags");
            Add(_tags);
        }

        /// <summary>Статичный визуал карточки: портрет/спрайт (фолбэк, когда нет анимированного рига).</summary>
        public void SetSprite(Sprite sprite)
        {
            if (sprite != null) _sprite.style.backgroundImage = new StyleBackground(sprite);
            else _sprite.style.backgroundImage = StyleKeyword.None;
        }

        /// <summary>
        /// Анимированный визуал: <see cref="RenderTexture"/> с боевого рига (реальный юнит, idle/attack).
        /// Шов под план 10 §5 — риг рендерит `ViewPrefab` релика в RT, карточка лишь показывает её.
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

        /// <summary>
        /// Ряд info-тегов под именем (иконки + подписи). Подаются готовыми: иконка из <see cref="TagData"/>,
        /// подпись — уже локализованная строка (loc резолвит потребитель, как и <see cref="RelicName"/>).
        /// Пустой/пропущенный набор прячет ряд. Чипы без подписей рисуются как «только иконки» (плотнее).
        /// </summary>
        public void SetInfoTags(IReadOnlyList<RelicCardTag> tags)
        {
            _tags.Clear();
            if (tags == null || tags.Count == 0) { _tags.style.display = DisplayStyle.None; return; }
            _tags.style.display = DisplayStyle.Flex;

            for (int i = 0; i < tags.Count; i++)
            {
                RelicCardTag tag = tags[i];
                bool hasLabel = !string.IsNullOrEmpty(tag.Label);

                var chip = new VisualElement { pickingMode = PickingMode.Ignore };
                chip.AddToClassList("gm-card__tag");
                if (!hasLabel) chip.AddToClassList("gm-card__tag--icon-only");

                if (tag.Icon != null)
                {
                    var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                    icon.AddToClassList("gm-card__tag-icon");
                    icon.style.backgroundImage = new StyleBackground(tag.Icon);
                    chip.Add(icon);
                }
                if (hasLabel)
                {
                    var label = new Label(tag.Label) { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("gm-card__tag-label");
                    chip.Add(label);
                }
                _tags.Add(chip);
            }
        }

        /// <summary>Редкость → цвет рамки карточки (класс <c>.gm-card--rarity-*</c>). Интерактив (hover/selected) поверх.</summary>
        public void SetRarity(DropRarity rarity)
        {
            for (int i = 0; i < RarityClasses.Length; i++) RemoveFromClassList(RarityClasses[i]);
            int idx = (int)rarity;
            if (idx >= 0 && idx < RarityClasses.Length) AddToClassList(RarityClasses[idx]);
        }

        /// <summary>Тултип карточки (полное описание релика при наведении). Пустая строка — снять тултип.</summary>
        public void SetTooltip(string text) => tooltip = text ?? string.Empty;
    }
}
