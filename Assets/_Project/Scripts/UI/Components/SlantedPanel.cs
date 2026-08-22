using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Подложка со скошенными торцами (лента верхней панели забега). Рисуется вершинами через
    /// <see cref="Painter2D"/>, а не 9-slice-спрайтом: скос на 9-slice тянется вместе с серединой
    /// и «плывёт» при смене ширины (число иконок в ленте ещё будет меняться).
    ///
    /// Цвета НЕ хардкодятся и не берутся из background-color (его UITK рисует прямоугольником):
    /// контрол читает custom-свойства USS <c>--gm-slant-fill</c> / <c>--gm-slant-stroke</c>, поэтому
    /// вид по-прежнему задаётся токенами в <c>components.uss</c>. Толщина обводки — <c>--gm-slant-width</c>.
    /// </summary>
    [UxmlElement]
    public partial class SlantedPanel : VisualElement
    {
        private static readonly CustomStyleProperty<Color> FillProp   = new("--gm-slant-fill");
        private static readonly CustomStyleProperty<Color> StrokeProp = new("--gm-slant-stroke");
        private static readonly CustomStyleProperty<float> WidthProp  = new("--gm-slant-width");

        /// <summary>
        /// Доля высоты, которую занимает скос. Та же причина, что у пластины и чипа: форма следует
        /// за ростом элемента, а не подбирается числом на каждый размер.
        /// </summary>
        private static readonly CustomStyleProperty<float> SlantRatioProp = new("--gm-slant-ratio");

        // Своих цветов нет — только из USS (--gm-slant-fill / --gm-slant-stroke): см. PlateButton.
        private Color _fill   = Color.clear;
        private Color _stroke = Color.clear;
        private float _strokeWidth = 2f;
        private float _slant = 14f;

        // 0 — доля не задана, играет абсолютное значение.
        private float _slantRatio;

        /// <summary>Горизонтальный вылет скоса в пикселях: 0 — обычный прямоугольник.</summary>
        [UxmlAttribute]
        public float Slant
        {
            get => _slant;
            set { _slant = Mathf.Max(0f, value); MarkDirtyRepaint(); }
        }

        public SlantedPanel()
        {
            AddToClassList("gm-slant");
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ICustomStyle style = evt.customStyle;
            if (style.TryGetValue(FillProp, out Color fill)) _fill = fill;
            if (style.TryGetValue(StrokeProp, out Color stroke)) _stroke = stroke;
            if (style.TryGetValue(WidthProp, out float width)) _strokeWidth = width;
            _slantRatio = style.TryGetValue(SlantRatioProp, out float sr) ? Mathf.Max(0f, sr) : 0f;
            MarkDirtyRepaint();
        }

        // Шестиугольник-лента: торцы сходятся клином к вертикальной середине. Скос кламплю половиной
        // ширины — на узкой ленте фигура иначе вывернулась бы наизнанку.
        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            // localBound, а НЕ contentRect: contentRect — бокс за вычетом padding, по нему фигура
            // поджималась внутрь и содержимое вылезало за обводку тем сильнее, чем больше padding.
            float w = localBound.width;
            float h = localBound.height;
            if (w <= 0f || h <= 0f) return;

            float s = Mathf.Min(_slantRatio > 0f ? h * _slantRatio : _slant, w * 0.5f);
            float mid = h * 0.5f;
            float inset = _strokeWidth * 0.5f; // обводка рисуется по центру линии — поджимаем внутрь

            Painter2D p = ctx.painter2D;
            p.BeginPath();
            p.MoveTo(new Vector2(s, inset));
            p.LineTo(new Vector2(w - s, inset));
            p.LineTo(new Vector2(w - inset, mid));
            p.LineTo(new Vector2(w - s, h - inset));
            p.LineTo(new Vector2(s, h - inset));
            p.LineTo(new Vector2(inset, mid));
            p.ClosePath();

            p.fillColor = _fill;
            p.Fill();

            if (_strokeWidth > 0f)
            {
                p.strokeColor = _stroke;
                p.lineWidth = _strokeWidth;
                p.lineJoin = LineJoin.Miter;
                p.Stroke();
            }
        }
    }
}
