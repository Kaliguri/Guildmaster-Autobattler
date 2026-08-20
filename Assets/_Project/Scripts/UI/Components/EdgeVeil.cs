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
    /// <para><b>Затухание — smoothstep, а не линейное и не квадрат.</b> У линейного видна кромка:
    /// глаз ловит излом производной там, где градиент упирается в ноль. Квадрат чинит только дальний
    /// конец, а ближний оставляет с изломом — и при непустом плато этот излом садится ровно на стык,
    /// где спад начинается, отчего вуаль читается как «резкая» (наход. Макса 04.08.2026: «слишком
    /// сильное и резкое»). У smoothstep производная нулевая с ОБЕИХ сторон, поэтому ни начало спада,
    /// ни его конец не видны как граница.</para>
    /// </remarks>
    [UxmlElement]
    public partial class EdgeVeil : VisualElement
    {
        private static readonly CustomStyleProperty<Color> ColorProp = new("--gm-veil-color");
        private static readonly CustomStyleProperty<float> PlateauProp = new("--gm-veil-plateau");

        /// <summary>Полос градиента. Меньше — видно ступени, больше — лишние вершины ни за что.</summary>
        private const int Segments = 32;

        /// <summary>
        /// Доля ширины у своей кромки, которую вуаль держит в ПОЛНУЮ силу; затухание начинается после
        /// неё. Ноль — чистый градиент от края.
        /// </summary>
        /// <remarks>
        /// Нужно потому, что квадратичное затухание съедает половину плотности уже к трети пути, и
        /// текст, лежащий не у самой кромки (титул главного меню тянется до 42% экрана), оказывается
        /// на почти голом фоне. Поднимать альфу тут бесполезно: она задаёт максимум У КРАЯ, а не под
        /// текстом. Плато переносит начало спада за колонку, оставляя хвосту ту же мягкость.
        /// </remarks>
        private float _plateau;

        /// <summary>
        /// Своего цвета у вуали НЕТ — он приходит из USS и только оттуда. Прежний дефолт
        /// (0.18, 0.15, 0.12, 0.92) был вторым владельцем токена <c>--gm-color-menu-shade</c> и уже
        /// разошёлся с ним по альфе; отсюда прозрачный: не пришёл цвет — вуали не будет, и это
        /// видно сразу, а не через месяц как «почему-то другой оттенок».
        /// </summary>
        private Color _color = Color.clear;
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
            if (evt.customStyle.TryGetValue(PlateauProp, out float p)) _plateau = Mathf.Clamp01(p);
            MarkDirtyRepaint();
        }

        private void OnGenerate(MeshGenerationContext ctx)
        {
            float w = localBound.width;
            float h = localBound.height;
            if (w <= 0f || h <= 0f || _color.a <= 0f) return;

            bool horizontal = _side is VeilSide.Left or VeilSide.Right;
            // Плотность у СВОЕЙ кромки: у Right и Bottom она в конце оси, поэтому доля считается с конца.
            // Разворачивается только затухание — не позиции: ось всегда идёт вперёд, иначе перевернётся
            // намотка (см. ниже), и вуаль этих двух сторон исчезнет.
            bool reversed = _side is VeilSide.Right or VeilSide.Bottom;

            MeshWriteData mesh = ctx.Allocate((Segments + 1) * 2, Segments * 6);
            var verts = new Vertex[(Segments + 1) * 2];

            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                // Доля пути ОТ своей кромки: плато держит единицу, после него спад по квадрату.
                float fromEdge = reversed ? 1f - t : t;
                float tail = Mathf.Max(1f - _plateau, 0.0001f);
                float u = Mathf.Clamp01((fromEdge - _plateau) / tail);
                float fade = 1f - u * u * (3f - 2f * u);
                // Цвет уходит в вершину КАК ЕСТЬ — шейдер UITK конвертирует его сам (см.
                // PlateButton.VertexColor: ручная конверсия душила цвет втрое). Альфа не трогается.
                Color rgb = PlateButton.VertexColor(_color);
                var tint = new Color(rgb.r, rgb.g, rgb.b, _color.a * fade);

                float x = horizontal ? t * w : 0f;
                float y = horizontal ? 0f : t * h;

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

                // ОБХОД ПО ЧАСОВОЙ — требование UI-рендерера (Manual: Generate 2D visual content).
                // Треугольник, намотанный против неё, молча отбрасывается: элемент есть, размер есть,
                // цвет есть, а на экране пусто. Так вуаль и пролежала невидимой с самого рождения
                // (поймано замером кадра 04.08.2026).
                // Ось Y здесь растёт ВНИЗ, поэтому «по часовой» читается как левый-верх → правый-верх →
                // правый-низ. Пара для horizontal — это (верх, низ), для vertical — (лево, право),
                // и порядок обхода у них поэтому разный.
                int o = i * 6;
                if (horizontal)
                {
                    indices[o] = a; indices[o + 1] = c; indices[o + 2] = d;
                    indices[o + 3] = a; indices[o + 4] = d; indices[o + 5] = b;
                }
                else
                {
                    indices[o] = a; indices[o + 1] = b; indices[o + 2] = d;
                    indices[o + 3] = a; indices[o + 4] = d; indices[o + 5] = c;
                }
            }

            mesh.SetAllIndices(indices);
        }
    }
}
