using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Заготовленные заливки пластины. Цвета у всех ОДНИ И ТЕ ЖЕ — различаются только откуда идёт
    /// градиент и какого он типа (решение Макса 04.08.2026, приём взят с разбора Guildrun).
    /// </summary>
    public enum PlateFill
    {
        /// <summary>Сверху вниз — заготовка по умолчанию.</summary>
        Down,
        Up,
        Left,
        Right,
        /// <summary>От центра к краям.</summary>
        Radial
    }

    /// <summary>
    /// Где у пластины проходит кромка. Заведено 05.08.2026 по разбору Guildrun: у активной вкладки
    /// там СВЕТЯТСЯ ТОЛЬКО БОКА, верхней и нижней грани нет вовсе, а книзу боковая линия
    /// растворяется в собственной заливке (замер: кромка/заливка 1.42 вверху против 1.09 внизу).
    /// Полный контур на её месте читается «кнопкой», а не ярлыком, выступающим из рельсы.
    /// </summary>
    public enum PlateStroke
    {
        /// <summary>Замкнутый контур по всей фигуре — поведение по умолчанию.</summary>
        Full,
        /// <summary>Только левая и правая грани, с затуханием книзу.</summary>
        Sides,
        /// <summary>Только нижняя грань — черта под подписью, отклик наведения у вкладки.</summary>
        Bottom
    }

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
        private static readonly CustomStyleProperty<Color> FillFarProp = new("--gm-plate-fill-far");
        private static readonly CustomStyleProperty<string> FillModeProp = new("--gm-plate-fill-mode");
        private static readonly CustomStyleProperty<Color> StrokeProp = new("--gm-plate-stroke");
        private static readonly CustomStyleProperty<float> StrokeWidthProp = new("--gm-plate-stroke-width");
        private static readonly CustomStyleProperty<string> StrokeModeProp = new("--gm-plate-stroke-mode");
        private static readonly CustomStyleProperty<float> ChamferProp = new("--gm-plate-chamfer");
        private static readonly CustomStyleProperty<float> CapProp = new("--gm-plate-cap");

        private readonly Label _label;

        // Подпись хранится ИСХОДНОЙ, а показывается по правилу регистра из USS. Инициализатор поля
        // выполняется до базового конструктора, поэтому обращение к text из ctor базы безопасно.
        private string _sourceText = string.Empty;
        private UiTextCaseMode _textCase = UiTextCaseMode.None;

        // СВОИХ цветов у контрола нет: и заливка, и кайма приходят только из USS. Прежние дефолты
        // (тёмно-коричневый и латунь) были вторым владельцем токенов и однажды разошлись бы с ними
        // молча — ровно так это случилось у вуали, где хардкод 0.92 жил рядом с токеном 0.88.
        // clear здесь значит «цвет не пришёл» и честно не рисует ничего, вместо того чтобы подсунуть
        // правдоподобный. Инвариант держит UiColorPipelineTests.
        private Color _fill = Color.clear;
        private Color _fillFar = Color.clear;          // clear = «второго цвета нет», заливка сплошная
        private PlateFill _fillMode = PlateFill.Down;
        private Color _stroke = Color.clear;
        private float _strokeWidth = 2f;
        private PlateStroke _strokeMode = PlateStroke.Full;
        private float _chamfer = 10f;

        // Размер КОНЦА пластины в пикселях; 0 — концов нет. Заведено 21.08.2026 по разбору Heroes
        // Olden Era: там каждая кнопка меню кончается шевроном с обеих сторон, и именно эта мелочь
        // отличает «сделанный» интерфейс от набора прямоугольников. Своего цвета у конца нет — он
        // берёт цвет обводки, потому что это её продолжение, а не отдельная деталь.
        private float _cap;

        /// <summary>
        /// Подпись кнопки. Перенаправлена в дочерний <see cref="Label"/> — база остаётся с пустым
        /// текстом и ничего не рисует (см. «почему текст в ребёнке» в описании класса).
        /// </summary>
        /// <remarks>
        /// Отдаёт ИСХОДНУЮ строку, а не показанную: регистр — это вид, задаваемый USS-свойством
        /// <c>--gm-text-case</c> (см. <see cref="UiTextCase"/>), и код, читающий подпись, должен
        /// получить то, что положил. Иначе сравнение вида <c>button.text == "Отмена"</c> ломалось бы
        /// от одной строчки в теме.
        /// </remarks>
        public override string text
        {
            // Геттер обязан пережить обращение ДО нашего конструктора: базовый Button может тронуть
            // text в своём ctor, а _label к тому моменту ещё null.
            get => _sourceText;
            set
            {
                _sourceText = value ?? string.Empty;
                ApplyTextCase();
            }
        }

        private void ApplyTextCase()
        {
            if (_label == null) return;
            _label.text = UiTextCase.Apply(_sourceText, _textCase);
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
            if (style.TryGetValue(CapProp, out float cap)) _cap = Mathf.Max(0f, cap);

            // Второй цвет сбрасывается ЯВНО, когда правило его не задаёт: без этого пластина,
            // унаследовавшая градиент от одного состояния, тащила бы его в состояние со сплошной
            // заливкой — значения custom-свойств переживают смену псевдокласса.
            _fillFar = style.TryGetValue(FillFarProp, out Color far) ? far : Color.clear;
            _fillMode = style.TryGetValue(FillModeProp, out string mode) ? ParseFill(mode) : PlateFill.Down;
            _strokeMode = style.TryGetValue(StrokeModeProp, out string sm) ? ParseStroke(sm) : PlateStroke.Full;

            // Регистр сбрасывается так же явно, как второй цвет заливки: правило, не задающее
            // --gm-text-case, обязано вернуть подпись к исходной, а не унаследовать капс от
            // состояния, из которого кнопка вышла.
            UiTextCaseMode textCase = style.TryGetValue(UiTextCase.Property, out string tc)
                ? UiTextCase.Parse(tc)
                : UiTextCaseMode.None;
            if (textCase != _textCase)
            {
                _textCase = textCase;
                ApplyTextCase();
            }

            MarkDirtyRepaint();
        }

        /// <summary>Направление или тип заготовленного градиента. Неизвестное имя — «сверху вниз».</summary>
        private static PlateFill ParseFill(string mode)
        {
            switch (mode?.Trim().ToLowerInvariant())
            {
                case "up":     return PlateFill.Up;
                case "left":   return PlateFill.Left;
                case "right":  return PlateFill.Right;
                case "radial": return PlateFill.Radial;
                default:       return PlateFill.Down;
            }
        }

        /// <summary>Где проходит кромка. Неизвестное имя — замкнутый контур.</summary>
        private static PlateStroke ParseStroke(string mode)
        {
            switch (mode?.Trim().ToLowerInvariant())
            {
                case "sides":  return PlateStroke.Sides;
                case "bottom": return PlateStroke.Bottom;
                default:       return PlateStroke.Full;
            }
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

            Span<Vector2> corners = stackalloc Vector2[8];
            corners[0] = new Vector2(left + c, top);
            corners[1] = new Vector2(right - c, top);
            corners[2] = new Vector2(right, top + c);
            corners[3] = new Vector2(right, bottom - c);
            corners[4] = new Vector2(right - c, bottom);
            corners[5] = new Vector2(left + c, bottom);
            corners[6] = new Vector2(left, bottom - c);
            corners[7] = new Vector2(left, top + c);

            if (_fill.a > 0f)
            {
                if (_fillFar.a > 0f) FillGradient(ctx, corners, w, h);
                else FillSolid(ctx, corners);
            }

            if (_stroke.a > 0f && _strokeWidth > 0f)
            {
                if (_strokeMode == PlateStroke.Sides) StrokeSides(ctx, w, h);
                else if (_strokeMode == PlateStroke.Bottom) StrokeBottom(ctx, w, h);
                else
                {
                    Painter2D p = ctx.painter2D;
                    p.BeginPath();
                    p.MoveTo(corners[0]);
                    for (int i = 1; i < corners.Length; i++) p.LineTo(corners[i]);
                    p.ClosePath();
                    p.strokeColor = _stroke;
                    p.lineWidth = _strokeWidth;
                    p.lineJoin = LineJoin.Miter;
                    p.Stroke();
                }
            }

            if (_cap > 0f && _stroke.a > 0f) DrawCaps(ctx, w, h);
        }

        /// <summary>
        /// Концы пластины: по шеврону у левой и правой кромки, остриями внутрь.
        /// </summary>
        /// <remarks>
        /// <b>Зачем.</b> Разбор Heroes Olden Era 21.08.2026: у него каждая кнопка меню кончается
        /// такой парой, и это единственное отличие его кнопки от нашей при равных размерах и
        /// одинаковой фаске. Приём дешёвый — три отрезка на конец, — а видно его на каждом экране,
        /// где есть кнопки.
        /// <para><b>Остриями ВНУТРЬ, а не наружу</b>: наружу они читаются стрелками «сюда», то есть
        /// обещают перемещение, которого кнопка не делает. Внутрь — это обрамление подписи, взгляд
        /// сходится к центру пластины.</para>
        /// <para>Размер приходит из USS (<c>--gm-plate-cap</c>) и по умолчанию НУЛЕВОЙ: концы — не
        /// повадка всякой кнопки, а решение темы для тех мест, где кнопка крупная и одна в ряду.
        /// На мелкой кнопке шеврон съедает подпись.</para>
        /// </remarks>
        private void DrawCaps(MeshGenerationContext ctx, float w, float h)
        {
            float size = Mathf.Min(_cap, h * 0.30f);
            if (size <= 0.5f) return;

            // Отступ от кромки — фаска плюс половина размера: иначе шеврон садится на скос угла и
            // ломает силуэт, ради которого фаска и заведена.
            float pad = Mathf.Min(_chamfer, Mathf.Min(w, h) * 0.5f) + size * 0.5f;
            float midY = h * 0.5f;

            Painter2D p = ctx.painter2D;
            p.strokeColor = new Color(_stroke.r, _stroke.g, _stroke.b, _stroke.a * 0.85f);
            p.lineWidth = Mathf.Max(1f, _strokeWidth * 0.75f);
            p.lineJoin = LineJoin.Miter;
            p.lineCap = LineCap.Butt;

            // Левый: остриё смотрит вправо, к подписи.
            p.BeginPath();
            p.MoveTo(new Vector2(pad, midY - size));
            p.LineTo(new Vector2(pad + size * 0.7f, midY));
            p.LineTo(new Vector2(pad, midY + size));
            p.Stroke();

            // Правый — зеркально.
            p.BeginPath();
            p.MoveTo(new Vector2(w - pad, midY - size));
            p.LineTo(new Vector2(w - pad - size * 0.7f, midY));
            p.LineTo(new Vector2(w - pad, midY + size));
            p.Stroke();
        }

        /// <summary>
        /// Боковые грани с затуханием книзу — две полосы шириной с обводку, нарисованные мешем.
        /// </summary>
        /// <remarks>
        /// Мешем, а не <see cref="Painter2D"/>: у него один <c>strokeColor</c> на весь путь, а здесь
        /// цвет обязан меняться по высоте — иначе линия внизу спорит со светлой частью заливки,
        /// ровно то, чего у рефа нет. Та же причина, по которой градиентная заливка тоже собирается
        /// вершинами.
        /// </remarks>
        private void StrokeSides(MeshGenerationContext ctx, float w, float h)
        {
            // 0.35 внизу — не «на глаз»: у рефа контраст кромки к заливке падает с 1.42 вверху до
            // 1.09 внизу, то есть линия почти растворяется, но не исчезает совсем.
            Color top = _stroke;
            Color bottom = new(_stroke.r, _stroke.g, _stroke.b, _stroke.a * 0.35f);
            float t = Mathf.Max(1f, _strokeWidth);

            MeshWriteData mesh = ctx.Allocate(8, 12);
            var v = new Vertex[8];
            // левая полоса
            v[0].position = new Vector3(0f, 0f, Vertex.nearZ);      v[0].tint = top;
            v[1].position = new Vector3(t, 0f, Vertex.nearZ);       v[1].tint = top;
            v[2].position = new Vector3(t, h, Vertex.nearZ);        v[2].tint = bottom;
            v[3].position = new Vector3(0f, h, Vertex.nearZ);       v[3].tint = bottom;
            // правая полоса
            v[4].position = new Vector3(w - t, 0f, Vertex.nearZ);   v[4].tint = top;
            v[5].position = new Vector3(w, 0f, Vertex.nearZ);       v[5].tint = top;
            v[6].position = new Vector3(w, h, Vertex.nearZ);        v[6].tint = bottom;
            v[7].position = new Vector3(w - t, h, Vertex.nearZ);    v[7].tint = bottom;
            mesh.SetAllVertices(v);
            mesh.SetAllIndices(new ushort[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 });
        }

        /// <summary>
        /// Нижняя грань — ровная черта во всю ширину. Отклик наведения у вкладки: поверхность там
        /// появляться не должна (это повадка кнопки), а линия под словом читается «сюда можно».
        /// </summary>
        private void StrokeBottom(MeshGenerationContext ctx, float w, float h)
        {
            float t = Mathf.Max(1f, _strokeWidth);
            MeshWriteData mesh = ctx.Allocate(4, 6);
            var v = new Vertex[4];
            v[0].position = new Vector3(0f, h - t, Vertex.nearZ); v[0].tint = _stroke;
            v[1].position = new Vector3(w, h - t, Vertex.nearZ);  v[1].tint = _stroke;
            v[2].position = new Vector3(w, h, Vertex.nearZ);      v[2].tint = _stroke;
            v[3].position = new Vector3(0f, h, Vertex.nearZ);     v[3].tint = _stroke;
            mesh.SetAllVertices(v);
            mesh.SetAllIndices(new ushort[] { 0, 1, 2, 0, 2, 3 });
        }

        private void FillSolid(MeshGenerationContext ctx, Span<Vector2> corners)
        {
            Painter2D p = ctx.painter2D;
            p.BeginPath();
            p.MoveTo(corners[0]);
            for (int i = 1; i < corners.Length; i++) p.LineTo(corners[i]);
            p.ClosePath();
            p.fillColor = _fill;
            p.Fill();
        }

        /// <summary>
        /// Заливка градиентом: ВЕЕР из центра — центральная вершина плюс углы восьмиугольника.
        /// </summary>
        /// <remarks>
        /// <para>Веер выбран потому, что одной раскладкой вершин обслуживает оба типа: линейному
        /// градиенту цвет вершины даёт проекция на ось, радиальному — расстояние до центра. Через
        /// <see cref="Painter2D"/> градиента не получить вовсе: у него один <c>fillColor</c> на всю
        /// фигуру, а USS градиентных функций не знает — та же причина, по которой вуаль меню
        /// рисуется вершинами.</para>
        /// <para>Цвета берутся из USS (<c>--gm-plate-fill</c> / <c>--gm-plate-fill-far</c>), режим —
        /// из <c>--gm-plate-fill-mode</c>. Набор заготовок один на весь интерфейс: меняется не цвет,
        /// а откуда градиент идёт и какого он типа.</para>
        /// </remarks>
        private void FillGradient(MeshGenerationContext ctx, Span<Vector2> corners, float w, float h)
        {
            int n = corners.Length;
            MeshWriteData mesh = ctx.Allocate(n + 1, n * 3);

            var center = new Vector2(w * 0.5f, h * 0.5f);
            float maxRadius = Mathf.Max(0.0001f, new Vector2(w * 0.5f, h * 0.5f).magnitude);

            var verts = new Vertex[n + 1];
            verts[0].position = new Vector3(center.x, center.y, Vertex.nearZ);
            verts[0].tint = ColorAt(center, w, h, center, maxRadius);

            for (int i = 0; i < n; i++)
            {
                verts[i + 1].position = new Vector3(corners[i].x, corners[i].y, Vertex.nearZ);
                verts[i + 1].tint = ColorAt(corners[i], w, h, center, maxRadius);
            }

            mesh.SetAllVertices(verts);

            var indices = new ushort[n * 3];
            for (int i = 0; i < n; i++)
            {
                indices[i * 3] = 0;
                indices[i * 3 + 1] = (ushort)(i + 1);
                indices[i * 3 + 2] = (ushort)(i + 1 == n ? 1 : i + 2);
            }

            mesh.SetAllIndices(indices);
        }

        /// <summary>Доля пути от ближнего цвета к дальнему в точке — по режиму заготовки.</summary>
        private Color ColorAt(Vector2 point, float w, float h, Vector2 center, float maxRadius)
        {
            float t = _fillMode switch
            {
                PlateFill.Up     => 1f - point.y / Mathf.Max(h, 0.0001f),
                PlateFill.Right  => point.x / Mathf.Max(w, 0.0001f),
                PlateFill.Left   => 1f - point.x / Mathf.Max(w, 0.0001f),
                PlateFill.Radial => Vector2.Distance(point, center) / maxRadius,
                _                => point.y / Mathf.Max(h, 0.0001f),
            };

            return VertexColor(Color.Lerp(_fill, _fillFar, Mathf.Clamp01(t)));
        }

        /// <summary>
        /// Цвет для ВЕРШИНЫ — отдаётся КАК ЕСТЬ, без ручного перевода в линейное пространство.
        /// </summary>
        /// <remarks>
        /// <para>Шейдер UI Toolkit конвертирует вершинный tint сам, как это делает Canvas у uGUI.
        /// Ручной <c>c.linear</c> поверх этого — вторая конверсия подряд, и она душит цвет примерно
        /// втрое: заданное <c>rgb(44,140,146)</c> приходило на экран как <c>rgb(6,62,69)</c>.</para>
        ///
        /// <para><b>Замер 05.08.2026</b> (пиксели кадра, не глаз): с ручной конверсией поле кнопки
        /// давало 6,62,69; при подаче предкомпенсированного <c>c.gamma</c> — 41,133,139, то есть ровно
        /// заданное. Отсюда вывод: конверсий должно быть ноль. Предыдущая правка (04.08) добавила
        /// <c>.linear</c> по впечатлению «кнопка светлее заданной» и как раз СОЗДАЛА жалобу «кнопки
        /// слишком тёмные», с которой начался разбор 05.08.</para>
        ///
        /// <para><b>Цвет проверяется замером пикселя, а не взглядом на скрин.</b> На тёмной гамме
        /// разница между «задано» и «нарисовано» читается как «дизайнерское решение», а не как
        /// дефект, и живёт месяцами.</para>
        /// </remarks>
        public static Color VertexColor(Color c) => c;
    }
}
