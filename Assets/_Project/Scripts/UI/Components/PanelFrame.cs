using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// ОПРАВА панели: двойная кайма по периметру и угловые накладки, выступающие наружу.
    /// </summary>
    /// <remarks>
    /// Заведена 2026-08-03 по разбору Макса: «нет рамки вокруг какой-то», «очень скучная форма».
    /// До неё панель мета-слоя несла одну линию сверху (стеклянный регистр), и фактура пергамента
    /// легла на голый прямоугольник — материал появился, а предмет нет.
    /// <para>Рисуется вершинами, а не 9-slice-картинкой: накладки обязаны держать РАЗМЕР при любой
    /// ширине панели, а растянутая картинка тянет их вместе с серединой. Тот же приём, что у
    /// <see cref="SlantedPanel"/> и <see cref="PlateButton"/> — язык формы в проекте уже есть.</para>
    /// <para>Оверлей, а не контейнер: элемент лежит поверх панели, растянут по ней и прозрачен для
    /// мыши. Так оправу можно навесить одной строкой разметки, не переписывая структуру экранов и не
    /// трогая фон — фактуру по-прежнему держит <c>background-image</c> самой панели.</para>
    /// <para>Углы накладок ВЫСТУПАЮТ за периметр: именно вылет ломает силуэт прямоугольника. Если
    /// прижать их к кромке, оправа снова читается «рамкой в рамке», а не оковкой.</para>
    /// </remarks>
    [UxmlElement]
    public partial class PanelFrame : VisualElement
    {
        private static readonly CustomStyleProperty<Color> LineProp = new("--gm-frame-line");
        private static readonly CustomStyleProperty<Color> InnerProp = new("--gm-frame-inner");
        private static readonly CustomStyleProperty<float> WidthProp = new("--gm-frame-width");
        private static readonly CustomStyleProperty<float> GapProp = new("--gm-frame-gap");

        private Color _line = new(0.72f, 0.53f, 0.23f, 1f);
        private Color _inner = new(0.54f, 0.37f, 0.16f, 1f);
        private float _width = 2f;
        private float _gap = 7f;
        private float _corner = 26f;
        private float _overhang = 7f;
        private bool _innerLine = true;

        /// <summary>Длина угловой накладки вдоль каждой стороны. 0 — накладок нет, останется кайма.</summary>
        [UxmlAttribute]
        public float Corner
        {
            get => _corner;
            set { _corner = Mathf.Max(0f, value); MarkDirtyRepaint(); }
        }

        /// <summary>Насколько накладка выходит ЗА кромку панели. Это и ломает прямоугольник.</summary>
        [UxmlAttribute]
        public float Overhang
        {
            get => _overhang;
            set { _overhang = Mathf.Max(0f, value); MarkDirtyRepaint(); }
        }

        /// <summary>Вторая, тихая линия внутри каймы. Выключается, когда оправа должна быть тоньше.</summary>
        [UxmlAttribute]
        public bool InnerLine
        {
            get => _innerLine;
            set { _innerLine = value; MarkDirtyRepaint(); }
        }

        public PanelFrame()
        {
            AddToClassList("gm-panel__frame");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerate;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ICustomStyle style = evt.customStyle;
            if (style.TryGetValue(LineProp, out Color line)) _line = line;
            if (style.TryGetValue(InnerProp, out Color inner)) _inner = inner;
            if (style.TryGetValue(WidthProp, out float w)) _width = w;
            if (style.TryGetValue(GapProp, out float g)) _gap = g;
            MarkDirtyRepaint();
        }

        private void OnGenerate(MeshGenerationContext ctx)
        {
            float w = localBound.width;
            float h = localBound.height;
            if (w <= 0f || h <= 0f) return;

            Painter2D p = ctx.painter2D;
            float half = _width * 0.5f;

            // 1. Кайма по периметру. Поджата на полтолщины: обводка идёт по ЦЕНТРУ линии, и без
            //    поджатия внешняя половина уезжает за пределы элемента и срезается.
            p.lineWidth = _width;
            p.strokeColor = _line;
            p.lineJoin = LineJoin.Miter;
            p.BeginPath();
            p.MoveTo(new Vector2(half, half));
            p.LineTo(new Vector2(w - half, half));
            p.LineTo(new Vector2(w - half, h - half));
            p.LineTo(new Vector2(half, h - half));
            p.ClosePath();
            p.Stroke();

            // 2. Вторая линия внутри — та самая «двойная кайма» гроссбуха. Тише первой: она читается
            //    толщиной оправы, а не собственным контуром.
            if (_innerLine && _gap > 0f && w > _gap * 2f && h > _gap * 2f)
            {
                p.lineWidth = 1f;
                p.strokeColor = _inner;
                p.BeginPath();
                p.MoveTo(new Vector2(_gap, _gap));
                p.LineTo(new Vector2(w - _gap, _gap));
                p.LineTo(new Vector2(w - _gap, h - _gap));
                p.LineTo(new Vector2(_gap, h - _gap));
                p.ClosePath();
                p.Stroke();
            }

            // 3. Угловые накладки. Каждая — уголок из двух отрезков, выходящий за кромку: снаружи
            //    остаётся короткий хвост, поэтому силуэт перестаёт быть ровным прямоугольником.
            if (_corner <= 0f) return;
            float c = Mathf.Min(_corner, Mathf.Min(w, h) * 0.4f);
            float o = _overhang;
            p.lineWidth = _width + 1f;   // накладка заметно толще каймы, иначе читается её продолжением
            p.strokeColor = _line;

            DrawCorner(p, new Vector2(half, half), new Vector2(1f, 0f), new Vector2(0f, 1f), c, o);
            DrawCorner(p, new Vector2(w - half, half), new Vector2(-1f, 0f), new Vector2(0f, 1f), c, o);
            DrawCorner(p, new Vector2(w - half, h - half), new Vector2(-1f, 0f), new Vector2(0f, -1f), c, o);
            DrawCorner(p, new Vector2(half, h - half), new Vector2(1f, 0f), new Vector2(0f, -1f), c, o);
        }

        /// <summary>Один уголок: два отрезка от вершины вдоль сторон, каждый с вылетом наружу.</summary>
        private static void DrawCorner(Painter2D p, Vector2 pivot, Vector2 along, Vector2 down, float len, float over)
        {
            Vector2 outward = -(along + down).normalized * over;

            p.BeginPath();
            p.MoveTo(pivot + along * len);
            p.LineTo(pivot + outward);
            p.LineTo(pivot + down * len);
            p.Stroke();
        }
    }
}
