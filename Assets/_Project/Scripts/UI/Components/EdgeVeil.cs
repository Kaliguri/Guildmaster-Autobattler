using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>С какой кромки элемента вуаль начинается плотной.</summary>
    public enum VeilSide
    {
        Left,
        Right,
        Top,
        Bottom
    }

    /// <summary>
    /// ВУАЛЬ у кромки: плотный тон у своего края, сходящий на нет к противоположному. Нужна там, где
    /// текст лежит прямо на кадре игры и подложки под ним нет — колонка главного меню поверх боя.
    /// </summary>
    /// <remarks>
    /// <para><b>Зачем вообще.</b> Разбор рефов главного меню (<c>Art_Dev/UI Refs/_teardowns/01-main-menu.md</c>)
    /// даёт по этому месту прямой счёт: из восьми игр, кладущих пункты прямо на фон, пятеро
    /// подкладывают под колонку ЛОКАЛЬНОЕ затемнение (градиент от края — 9 Kings, REPO; плоский
    /// чёрный — Absolum; плита — CotDG), а трое кладут текст на неподготовленный кадр и попадают в
    /// раздел «чего избегать»: у Roboquest нижние пункты теряют контраст на тёмном куске арта.
    /// Живой бой опаснее статичной иллюстрации — яркость под текстом меняется каждый кадр.</para>
    ///
    /// <para><b>Почему контрол, а не картинка.</b> USS не умеет градиентных функций (та же причина,
    /// по которой свечение загрузочного экрана лежит PNG-файлом), а новый PNG — это импорт ассета,
    /// то есть работа в редакторе. Вершинный градиент даёт то же самое кодом, тянется на любой
    /// размер экрана без растяжения пикселей и красится из палитры. Приём в проекте уже принят:
    /// вершинами рисуют <see cref="PanelFrame"/> и <see cref="PlateButton"/>.</para>
    ///
    /// <para><b>Цвет — только из USS</b> (<c>--gm-veil-color</c>), как у всех наших рисующих
    /// контролов: <c>background-color</c> тут не подходит — им UITK заливает ровный прямоугольник.</para>
    ///
    /// <para><b>Затухание квадратичное, а не линейное.</b> У линейного видна кромка: глаз ловит
    /// излом производной там, где градиент упирается в ноль. Квадрат уводит хвост в фон незаметно,
    /// оставляя плотность у самого края нетронутой.</para>
    /// </remarks>
    [UxmlElement]
    public partial class EdgeVeil : VisualElement
    {
        private static readonly CustomStyleProperty<Color> ColorProp = new("--gm-veil-color");

        /// <summary>Полос градиента. Меньше — видно ступени, больше — лишние вершины ни за что.</summary>
        private const int Segments = 16;

        private Color _color = new(0.18f, 0.15f, 0.12f, 0.92f);
        private VeilSide _side = VeilSide.Left;

        /// <summary>Кромка, у которой вуаль плотная. К противоположной она сходит в прозрачность.</summary>
        [UxmlAttribute]
        public VeilSide Side
        {
            get => _side;
            set { _side = value; MarkDirtyRepaint(); }
        }

        public EdgeVeil()
        {
            AddToClassList("gm-edge-veil");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerate;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(ColorProp, out Color c)) _color = c;
            MarkDirtyRepaint();
        }

        private void OnGenerate(MeshGenerationContext ctx)
        {
            float w = localBound.width;
            float h = localBound.height;
            if (w <= 0f || h <= 0f || _color.a <= 0f) return;

            bool horizontal = _side is VeilSide.Left or VeilSide.Right;
            // t идёт ОТ плотной кромки: у Right и Bottom ось развёрнута, поэтому доля считается с конца.
            bool reversed = _side is VeilSide.Right or VeilSide.Bottom;

            MeshWriteData mesh = ctx.Allocate((Segments + 1) * 2, Segments * 6);
            var verts = new Vertex[(Segments + 1) * 2];

            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                float fade = (1f - t) * (1f - t);
                // Перевод в линейное пространство — на нас: Painter2D делает это сам, ручной меш нет
                // (см. PlateButton.VertexColor). Альфа гаммой не трогается.
                Color linear = PlateButton.VertexColor(_color);
                var tint = new Color(linear.r, linear.g, linear.b, _color.a * fade);

                float pos = reversed ? (1f - t) : t;
                float x = horizontal ? pos * w : 0f;
                float y = horizontal ? 0f : pos * h;

                verts[i * 2].position = new Vector3(x, y, Vertex.nearZ);
                verts[i * 2].tint = tint;
                verts[i * 2 + 1].position = new Vector3(horizontal ? x : w, horizontal ? h : y, Vertex.nearZ);
                verts[i * 2 + 1].tint = tint;
            }

            mesh.SetAllVertices(verts);

            var indices = new ushort[Segments * 6];
            for (int i = 0; i < Segments; i++)
            {
                ushort a = (ushort)(i * 2);
                ushort b = (ushort)(i * 2 + 1);
                ushort c = (ushort)(i * 2 + 2);
                ushort d = (ushort)(i * 2 + 3);

                // Обход одинаковый для обоих направлений: при reversed вершины уже расставлены
                // «задом наперёд», и переворачивать намотку ещё раз значило бы отменить это.
                int o = i * 6;
                indices[o] = a; indices[o + 1] = b; indices[o + 2] = c;
                indices[o + 3] = c; indices[o + 4] = b; indices[o + 5] = d;
            }

            mesh.SetAllIndices(indices);
        }
    }
}
