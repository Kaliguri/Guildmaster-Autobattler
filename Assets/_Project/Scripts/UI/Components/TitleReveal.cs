using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Появление титра: знак и крупная надпись, которые въезжают в кадр и гаснут.
    /// </summary>
    /// <remarks>
    /// <b>ОДИН приём на всю игру</b> — вердикт Макса 22.08.2026 (пункт 7А плана
    /// [[ui-uplift]]): «берем такой эффект появления для "Победы" (в конце боя и в конце игры - свое),
    /// "Поражения", входа в бой (В бой! с мечами). Т.е. будет много где, единый стиль и источник и
    /// переюзание под всякие ситуации». Разница между случаями — ДАННЫЕ (слово, знак, тон,
    /// длительность), а не четыре похожих эффекта по экранам: второй такой же, написанный рядом,
    /// разошёлся бы с первым на первой правке.
    /// <para><b>Движение — на переходах USS, а не на твинах в коде.</b> Тем самым оно живёт в теме
    /// рядом с видом: правит его тот же файл, что задаёт кегль и цвет. Классы состояний ставятся с
    /// задержкой в кадр — иначе переход некуда играть: элемент рождается сразу в конечном виде.</para>
    /// <para><b>Титр НИЧЕГО не решает.</b> Он не ждёт ответа, не ловит клики и не закрывает собой
    /// экран: показался и ушёл. Всё, что требует ответа игрока, — это окно сообщений
    /// (<see cref="NoticeDialogView"/>), и смешивать их нельзя.</para>
    /// </remarks>
    [UxmlElement]
    public partial class TitleReveal : VisualElement
    {
        /// <summary>Тон титра: чем этот момент отличается на вид.</summary>
        public enum Tone
        {
            /// <summary>Нейтральный зов: «В бой!».</summary>
            Call,

            /// <summary>Победа.</summary>
            Triumph,

            /// <summary>Поражение.</summary>
            Defeat,
        }

        private readonly Spot _spot;
        private readonly VisualElement _glyph;
        private readonly Label _line;
        private readonly Label _sub;

        public TitleReveal()
        {
            AddToClassList("gm-title-reveal");
            pickingMode = PickingMode.Ignore;   // титр не ловит ввод: он ничего не решает

            // ПЯТНО ПОД ТИТРОМ, а не сплошная заслонка (выбор Макса 22.08.2026: «Затемнение сильное
            // пятном (но не цельная) за текстом»). Радиального градиента USS не умеет вовсе, поэтому
            // пятно рисуется мешем — тем же приёмом, что вуаль под колонкой меню.
            _spot = new Spot();
            Add(_spot);

            _glyph = new VisualElement { name = "title-glyph", pickingMode = PickingMode.Ignore };
            _glyph.AddToClassList("gm-title-reveal__glyph");
            Add(_glyph);

            _line = new Label { name = "title-line", pickingMode = PickingMode.Ignore };
            _line.AddToClassList("gm-text-display");
            _line.AddToClassList("gm-title-reveal__line");
            UiTextCase.Bind(_line);
            Add(_line);

            _sub = new Label { name = "title-sub", pickingMode = PickingMode.Ignore };
            _sub.AddToClassList("gm-text-caption");
            _sub.AddToClassList("gm-title-reveal__sub");
            Add(_sub);
        }

        /// <summary>
        /// Одеть титр: слово, приписка под ним, знак и тон.
        /// </summary>
        /// <param name="line">Крупная строка — то, что игрок прочтёт первым.</param>
        /// <param name="sub">Приписка под строкой. Пусто — строки не будет вовсе.</param>
        /// <param name="glyph">Знак над строкой. <c>null</c> — титр идёт без знака.</param>
        public void Dress(string line, string sub, Texture2D glyph, Tone tone)
        {
            _line.text = line ?? string.Empty;

            _sub.text = sub ?? string.Empty;
            _sub.style.display = string.IsNullOrEmpty(sub) ? DisplayStyle.None : DisplayStyle.Flex;

            if (glyph != null)
            {
                _glyph.style.backgroundImage = new StyleBackground(glyph);
                _glyph.style.display = DisplayStyle.Flex;
            }
            else _glyph.style.display = DisplayStyle.None;

            EnableInClassList(ToneClass(Tone.Call),    tone == Tone.Call);
            EnableInClassList(ToneClass(Tone.Triumph), tone == Tone.Triumph);
            EnableInClassList(ToneClass(Tone.Defeat),  tone == Tone.Defeat);
        }

        /// <summary>
        /// Проиграть появление: въезд, выдержка, уход. <paramref name="onDone"/> зовётся, когда титр
        /// отыграл целиком.
        /// </summary>
        /// <remarks>
        /// Времена приходят СНАРУЖИ, потому что у случаев они разные: «В бой!» обязан уйти до первого
        /// удара, а победа забега держится, пока игрок смотрит. Ставить их внутри значило бы завести
        /// одно время на четыре разных момента.
        /// </remarks>
        public void Play(float holdSeconds, Action onDone = null)
        {
            // Классы ставятся СЛЕДУЮЩИМ кадром: переходу USS нужно, чтобы элемент сперва существовал
            // в начальном виде. Поставь мы их сразу — он родился бы уже в конечном, и играть было бы
            // нечего (та же готча, что у любых transition в UITK).
            schedule.Execute(() => AddToClassList(ShownClass)).ExecuteLater(16);

            // Скобки не для красоты: (long)Mathf.Max(...) * 1000 приводит СЕКУНДЫ к целому ДО
            // умножения, и 1.1 с превращается в 1.0 с, а 0.6 — в ноль.
            long holdMs = (long)(Mathf.Max(0.05f, holdSeconds) * 1000f);
            schedule.Execute(() =>
            {
                RemoveFromClassList(ShownClass);
                AddToClassList(GoneClass);
            }).ExecuteLater(holdMs);

            // Уход длится столько же, сколько появление, — время перехода живёт в теме, здесь запас.
            schedule.Execute(() => onDone?.Invoke()).ExecuteLater(holdMs + 600);
        }

        private static string ToneClass(Tone tone) => tone switch
        {
            Tone.Triumph => "gm-title-reveal--triumph",
            Tone.Defeat  => "gm-title-reveal--defeat",
            _            => "gm-title-reveal--call",
        };

        /// <summary>Класс «титр в кадре» — на нём стоит вся анимация въезда.</summary>
        private const string ShownClass = "gm-title-reveal--shown";

        /// <summary>Класс «титр уходит».</summary>
        private const string GoneClass = "gm-title-reveal--gone";

        /// <summary>
        /// Тёмное пятно под титром: плотное в середине, растворяется к краям.
        /// </summary>
        /// <remarks>
        /// <b>Мешем, потому что USS не умеет радиальных градиентов</b> — ни одного, и не планирует.
        /// Тем же приёмом живёт вуаль под колонкой меню (<see cref="EdgeVeil"/>), и цвет сюда, как и
        /// туда, приходит ТОЛЬКО из темы: своего у пятна нет, иначе у токена появился бы второй
        /// владелец.
        /// <para>Вложенный класс, а не отдельный контрол набора: пятно не самостоятельная вещь, его
        /// не поставить никуда, кроме титра. Понадобится второму месту — тогда и выносить.</para>
        /// </remarks>
        private sealed class Spot : VisualElement
        {
            /// <summary>Сколько лучей у веера. 48 хватает: край пятна и так размыт затуханием.</summary>
            private const int Rays = 48;

            private static readonly CustomStyleProperty<Color> ColorProp =
                new CustomStyleProperty<Color>("--gm-title-spot-color");

            private static readonly CustomStyleProperty<float> RadiusXProp =
                new CustomStyleProperty<float>("--gm-title-spot-radius-x");

            private static readonly CustomStyleProperty<float> RadiusYProp =
                new CustomStyleProperty<float>("--gm-title-spot-radius-y");

            private Color _color = Color.clear;   // не пришёл цвет — пятна нет, и это видно сразу
            private float _radiusX = 0.42f;
            private float _radiusY = 0.34f;

            public Spot()
            {
                name = "title-spot";
                AddToClassList("gm-title-reveal__spot");
                pickingMode = PickingMode.Ignore;
                generateVisualContent += OnGenerate;
                RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
                RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            }

            private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
            {
                if (evt.customStyle.TryGetValue(ColorProp, out Color c)) _color = c;
                if (evt.customStyle.TryGetValue(RadiusXProp, out float rx)) _radiusX = Mathf.Clamp01(rx);
                if (evt.customStyle.TryGetValue(RadiusYProp, out float ry)) _radiusY = Mathf.Clamp01(ry);
                MarkDirtyRepaint();
            }

            /// <summary>
            /// Доля радиуса, до которой пятно держит ПОЛНУЮ плотность, — его ядро.
            /// </summary>
            /// <remarks>
            /// Без ядра пятно линейно тает от самой середины и читается «дымкой», а не затемнением:
            /// цвет между вершинами треугольника интерполируется линейно, другого спада меш не знает.
            /// Просьба Макса 22.08.2026 была именно «сильное пятном» — значит плотная середина и
            /// растворение только по краю.
            /// </remarks>
            private const float Core = 0.5f;

            private void OnGenerate(MeshGenerationContext ctx)
            {
                float w = localBound.width;
                float h = localBound.height;
                if (w <= 0f || h <= 0f || _color.a <= 0f) return;

                float cx = w * 0.5f;
                float cy = h * 0.5f;
                float rx = w * _radiusX;
                float ry = h * _radiusY;

                Color rgb = PlateButton.VertexColor(_color);
                var solid = new Color(rgb.r, rgb.g, rgb.b, _color.a);
                var clear = new Color(rgb.r, rgb.g, rgb.b, 0f);

                // Центр + ДВА кольца: ядро полной плотности и внешний край в прозрачность. Треугольников
                // Rays (веер ядра) + Rays * 2 (полоса затухания).
                int ring = Rays + 1;
                MeshWriteData mesh = ctx.Allocate(1 + ring * 2, Rays * 9);
                var verts = new Vertex[1 + ring * 2];

                verts[0].position = new Vector3(cx, cy, Vertex.nearZ);
                verts[0].tint = solid;

                for (int i = 0; i <= Rays; i++)
                {
                    float angle = Mathf.PI * 2f * i / Rays;
                    float dx = Mathf.Cos(angle);
                    float dy = Mathf.Sin(angle);

                    verts[1 + i].position = new Vector3(cx + dx * rx * Core, cy + dy * ry * Core, Vertex.nearZ);
                    verts[1 + i].tint = solid;

                    verts[1 + ring + i].position = new Vector3(cx + dx * rx, cy + dy * ry, Vertex.nearZ);
                    verts[1 + ring + i].tint = clear;
                }

                mesh.SetAllVertices(verts);

                // Намотка ПО ЧАСОВОЙ в экранных координатах (ось Y вниз): угол растёт, синус кладёт
                // точку ниже — обход выходит по часовой сам. Против часовой меш молча не рисуется.
                var indices = new ushort[Rays * 9];
                for (int i = 0; i < Rays; i++)
                {
                    ushort inner = (ushort)(1 + i);
                    ushort innerNext = (ushort)(1 + i + 1);
                    ushort outer = (ushort)(1 + ring + i);
                    ushort outerNext = (ushort)(1 + ring + i + 1);

                    int o = i * 9;
                    indices[o] = 0; indices[o + 1] = inner; indices[o + 2] = innerNext;

                    indices[o + 3] = inner; indices[o + 4] = outer;     indices[o + 5] = outerNext;
                    indices[o + 6] = inner; indices[o + 7] = outerNext; indices[o + 8] = innerNext;
                }

                mesh.SetAllIndices(indices);
            }
        }
    }
}
