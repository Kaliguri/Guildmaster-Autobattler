using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Кнопка-ПЛАСТИНА со срезанными углами. Вторая форма интерфейса рядом с прямоугольником:
    /// до неё вся дизайн-система умела ровно один силуэт (прямоугольник + латунный контур 2px),
    /// и шесть пунктов меню читались списком одинаковых полос — иерархию нёс только цвет заливки.
    ///
    /// <para>Форма рисуется вершинами через <see cref="Painter2D"/>, а не 9-slice-картинкой: фаска на
    /// 9-slice тянется вместе с серединой и «плывёт» при смене ширины кнопки, а ширина у нас разная
    /// в каждой локали. Тот же приём, что у <see cref="SlantedPanel"/> ленты режимов — язык скоса в
    /// проекте уже есть, пластина его продолжает.</para>
    ///
    /// <para>ПОЧЕМУ ТЕКСТ В ДОЧЕРНЕМ <see cref="Label"/>, а не свой у <see cref="Button"/>: базовый
    /// <see cref="TextElement"/> подписывается на <c>generateVisualContent</c> в СВОЁМ конструкторе,
    /// то есть раньше нас, и наша заливка легла бы ПОВЕРХ надписи. Дети же рисуются после содержимого
    /// родителя — поэтому подпись переезжает в ребёнка, а <c>text</c> перенаправляется в него
    /// (свойство виртуальное, UXML-атрибут <c>text</c> продолжает работать без правок разметки).</para>
    ///
    /// <para>Цвета НЕ берутся из <c>background-color</c> (им UITK рисует прямоугольник): контрол читает
    /// custom-свойства USS <c>--gm-plate-fill</c> / <c>--gm-plate-stroke</c> / <c>--gm-plate-stroke-width</c>
    /// / <c>--gm-plate-chamfer</c>. Благодаря этому все состояния (<c>:hover</c>, <c>:active</c>,
    /// <c>:disabled</c>, <c>--primary</c>) по-прежнему живут в components.uss, а не в C#.</para>
    ///
    /// <para>Наследует <see cref="Button"/> намеренно: полсотни мест ищут кнопки через
    /// <c>Q&lt;Button&gt;(...)</c> и вешают <c>clicked</c> — смена базового типа потребовала бы
    /// переписать их все ради формы.</para>
    /// </summary>
    [UxmlElement]
    public partial class PlateButton : Button
    {
        private static readonly CustomStyleProperty<Color> FillProp = new("--gm-plate-fill");
        private static readonly CustomStyleProperty<Color> StrokeProp = new("--gm-plate-stroke");
        private static readonly CustomStyleProperty<float> StrokeWidthProp = new("--gm-plate-stroke-width");
        private static readonly CustomStyleProperty<float> ChamferProp = new("--gm-plate-chamfer");

        private readonly Label _label;

        private Color _fill = new(0.11f, 0.08f, 0.05f, 1f);
        private Color _stroke = new(0.72f, 0.53f, 0.23f, 1f);
        private float _strokeWidth = 2f;
        private float _chamfer = 10f;

        /// <summary>
        /// Подпись кнопки. Перенаправлена в дочерний <see cref="Label"/> — база остаётся с пустым
        /// текстом и ничего не рисует (см. «почему текст в ребёнке» в описании класса).
        /// </summary>
        public override string text
        {
            // Геттер обязан пережить обращение ДО нашего конструктора: базовый Button может тронуть
            // text в своём ctor, а _label к тому моменту ещё null.
            get => _label != null ? _label.text : string.Empty;
            set { if (_label != null) _label.text = value; }
        }

        /// <summary>Кнопка с обработчиком клика — как у <see cref="Button"/>, чтобы места вида
        /// <c>new Button(() =&gt; ...)</c> переезжали на пластину заменой одного имени типа.</summary>
        public PlateButton(System.Action clickEvent) : this()
        {
            if (clickEvent != null) clicked += clickEvent;
        }

        public PlateButton()
        {
            AddToClassList("gm-plate-button");
            _label = new Label { pickingMode = PickingMode.Ignore };
            _label.AddToClassList("gm-plate-button__label");
            Add(_label);

            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            // Псевдосостояния меняют --gm-plate-*, но перерисовку меша заказывать всё равно нам:
            // смена состояния сама по себе грязным его не помечает (та же готча, что у SlantedChip).
            RegisterCallback<PointerEnterEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<PointerLeaveEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<PointerDownEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<PointerUpEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<FocusEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<BlurEvent>(_ => MarkDirtyRepaint());
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ICustomStyle style = evt.customStyle;
            if (style.TryGetValue(FillProp, out Color fill)) _fill = fill;
            if (style.TryGetValue(StrokeProp, out Color stroke)) _stroke = stroke;
            if (style.TryGetValue(StrokeWidthProp, out float width)) _strokeWidth = width;
            if (style.TryGetValue(ChamferProp, out float chamfer)) _chamfer = Mathf.Max(0f, chamfer);
            MarkDirtyRepaint();
        }

        // Восьмиугольник: прямоугольник со срезанными углами. Фаска кламплется половиной МЕНЬШЕЙ
        // стороны — на низкой кнопке фигура иначе вывернулась бы наизнанку.
        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            // localBound, а НЕ contentRect: contentRect — бокс за вычетом padding, по нему пластина
            // поджималась бы внутрь и подпись вылезала за обводку тем сильнее, чем больше padding.
            float w = localBound.width;
            float h = localBound.height;
            if (w <= 0f || h <= 0f) return;

            float c = Mathf.Min(_chamfer, Mathf.Min(w, h) * 0.5f);
            float inset = _strokeWidth * 0.5f; // обводка идёт по центру линии — поджимаем внутрь
            float left = inset, top = inset, right = w - inset, bottom = h - inset;

            Painter2D p = ctx.painter2D;
            p.BeginPath();
            p.MoveTo(new Vector2(left + c, top));
            p.LineTo(new Vector2(right - c, top));
            p.LineTo(new Vector2(right, top + c));
            p.LineTo(new Vector2(right, bottom - c));
            p.LineTo(new Vector2(right - c, bottom));
            p.LineTo(new Vector2(left + c, bottom));
            p.LineTo(new Vector2(left, bottom - c));
            p.LineTo(new Vector2(left, top + c));
            p.ClosePath();

            if (_fill.a > 0f)
            {
                p.fillColor = _fill;
                p.Fill();
            }

            if (_stroke.a > 0f && _strokeWidth > 0f)
            {
                p.strokeColor = _stroke;
                p.lineWidth = _strokeWidth;
                p.lineJoin = LineJoin.Miter;
                p.Stroke();
            }
        }
    }
}
