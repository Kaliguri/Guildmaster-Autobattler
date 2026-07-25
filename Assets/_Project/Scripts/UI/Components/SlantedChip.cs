using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Чип со скошенным торцом — крайние табы ленты режимов. Наследует весь вид и поведение
    /// <see cref="Chip"/> (иконка + подпись, состояния), но подсветку рисует не прямоугольником,
    /// а клином, повторяющим торец <see cref="SlantedPanel"/>: у ленты скошенные концы, и
    /// прямоугольная заливка крайней кнопки срезала угол «ступенькой».
    /// <para>
    /// Цвет заливки НЕ берётся из <c>background-color</c> (UITK рисует им прямоугольник) —
    /// контрол читает custom-свойство USS <c>--gm-chip-fill</c>. Благодаря этому состояния
    /// (<c>:hover</c>, <c>--active</c>) по-прежнему задаются в <c>components.uss</c>, а не в C#.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class SlantedChip : Chip
    {
        /// <summary>С какой стороны у чипа скошен торец.</summary>
        public enum Side { Left, Right }

        private static readonly CustomStyleProperty<Color> FillProp = new("--gm-chip-fill");

        private Color _fill = new(0f, 0f, 0f, 0f);
        private float _slant = 12f;
        private Side _side = Side.Left;

        /// <summary>Горизонтальный вылет скоса в пикселях (0 — обычный прямоугольник).</summary>
        [UxmlAttribute]
        public float Slant
        {
            get => _slant;
            set { _slant = Mathf.Max(0f, value); MarkDirtyRepaint(); }
        }

        /// <summary>Скошенная сторона: левая у первого таба ленты, правая — у последнего.</summary>
        [UxmlAttribute]
        public Side SlantSide
        {
            get => _side;
            set { _side = value; MarkDirtyRepaint(); }
        }

        public SlantedChip()
        {
            AddToClassList("gm-chip--slanted");
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            // Псевдосостояния (:hover/:active) меняют --gm-chip-fill, но перерисовку заказывать
            // всё равно нужно нам: смена состояния сама по себе не помечает mesh грязным.
            RegisterCallback<PointerEnterEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<PointerLeaveEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<PointerDownEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<PointerUpEvent>(_ => MarkDirtyRepaint());
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(FillProp, out Color fill)) _fill = fill;
            MarkDirtyRepaint();
        }

        // Пятиугольник: три прямых угла + клин с выбранной стороны, вершина клина — на середине
        // высоты, как у торца ленты.
        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            float w = localBound.width;
            float h = localBound.height;
            if (w <= 0f || h <= 0f || _fill.a <= 0f) return;

            float s = Mathf.Min(_slant, w * 0.5f);
            float mid = h * 0.5f;
            // Отступ от торца: обводка ленты рисуется по её краю, и заливка вплотную ложилась бы
            // прямо на латунную линию, съедая её. Клин заканчивается чуть раньше — линия остаётся видна.
            const float edgeInset = 3f;

            Painter2D p = ctx.painter2D;
            p.BeginPath();
            if (_side == Side.Left)
            {
                p.MoveTo(new Vector2(s, 0f));
                p.LineTo(new Vector2(w, 0f));
                p.LineTo(new Vector2(w, h));
                p.LineTo(new Vector2(s, h));
                p.LineTo(new Vector2(edgeInset, mid));
            }
            else
            {
                p.MoveTo(new Vector2(0f, 0f));
                p.LineTo(new Vector2(w - s, 0f));
                p.LineTo(new Vector2(w - edgeInset, mid));
                p.LineTo(new Vector2(w - s, h));
                p.LineTo(new Vector2(0f, h));
            }
            p.ClosePath();

            p.fillColor = _fill;
            p.Fill();
        }
    }
}
