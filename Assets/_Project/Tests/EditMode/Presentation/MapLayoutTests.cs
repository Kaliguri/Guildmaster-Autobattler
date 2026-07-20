using System.Collections.Generic;
using System.Linq;
using Guildmaster.Presentation.Map;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Раскладка карты — чистая функция презентации: домен хранит только топологию (этаж/ряд), а координаты
    /// и разброс считаются здесь. Проверяем, что разброс стабилен, не рушит чтение колонок и не хранится.
    /// </summary>
    public sealed class MapLayoutTests
    {
        private static List<MapNodeVisual> Column(int floor, int width) =>
            Enumerable.Range(0, width)
                      .Select(row => new MapNodeVisual($"c{floor}r{row}", floor, row,
                                                       MapNodeVisualState.Locked, "Battle"))
                      .ToList();

        private static List<MapNodeVisual> Grid(int floors, int width) =>
            Enumerable.Range(0, floors).SelectMany(f => Column(f, width)).ToList();

        [Test]
        public void Resolve_IsDeterministic_ForSameSeed()
        {
            var layout = MapLayout.Default;
            var nodes = Grid(5, 3);

            var a = layout.Resolve(nodes, 12345L);
            var b = layout.Resolve(nodes, 12345L);

            foreach (var kv in a) Assert.AreEqual(kv.Value, b[kv.Key], $"Узел {kv.Key} должен лечь одинаково.");
        }

        [Test]
        public void Resolve_LaysFloorsAsStraightColumns()
        {
            // Дефолт — РОВНАЯ сетка: узлы этажа стоят строго друг под другом (требование Макса по play-QA).
            // Разброс убран: живость рисунка даёт форма графа, а не шум поверх раскладки, и именно шум
            // мешал этажу читаться столбиком.
            var layout = MapLayout.Default;
            var nodes = Grid(5, 3);
            var pos = layout.Resolve(nodes, 777L);

            for (int floor = 0; floor < 5; floor++)
            {
                var xs = nodes.Where(n => n.Floor == floor).Select(n => pos[n.Id].x).ToList();
                Assert.That(xs.Max() - xs.Min(), Is.LessThan(0.0001f),
                    $"Этаж {floor} обязан стоять ровным столбиком.");
            }
        }

        [Test]
        public void Resolve_AppliesJitterFromSeedWhenConfigured()
        {
            // Разброс отключён в дефолте, но сам механизм жив и обязан оставаться завязанным на сид:
            // если его вернут ради стиля, рисунок должен различаться между забегами и переживать сейв.
            var layout = MapLayout.Default;
            layout.JitterY = 0.2f;
            var nodes = Grid(5, 3);

            var a = layout.Resolve(nodes, 1L);
            var b = layout.Resolve(nodes, 2L);

            Assert.IsTrue(a.Any(kv => kv.Value != b[kv.Key]), "С разбросом разные забеги дают разный рисунок.");
        }

        [Test]
        public void Resolve_KeepsFloorsSeparated()
        {
            // Разброс вдоль пути намеренно слабый: соседние этажи не должны перемешиваться по X,
            // иначе теряется чтение «сколько вариантов на этаже» (требование Макса).
            var layout = MapLayout.Default;
            var nodes = Grid(6, 4);
            var pos = layout.Resolve(nodes, 99L);

            for (int floor = 0; floor < 5; floor++)
            {
                float maxHere = nodes.Where(n => n.Floor == floor).Max(n => pos[n.Id].x);
                float minNext = nodes.Where(n => n.Floor == floor + 1).Min(n => pos[n.Id].x);
                Assert.Less(maxHere, minNext, $"Этаж {floor} не должен налезать на {floor + 1}.");
            }
        }

        [Test]
        public void Resolve_CentersRowsAroundZero()
        {
            // Ряд центрируется по фактической ширине этажа — узкие и широкие колонки висят на одной оси.
            // Дрейф этажа гасим: он двигает колонку целиком и к центрированию отношения не имеет.
            var layout = MapLayout.Default;
            layout.JitterX     = 0f;
            layout.JitterY     = 0f;

            var nodes = Column(0, 3).Concat(Column(1, 6)).ToList();
            var pos = layout.Resolve(nodes, 7L);

            float centerA = nodes.Where(n => n.Floor == 0).Average(n => pos[n.Id].y);
            float centerB = nodes.Where(n => n.Floor == 1).Average(n => pos[n.Id].y);

            Assert.AreEqual(0f, centerA, 0.001f);
            Assert.AreEqual(0f, centerB, 0.001f);
        }

        [Test]
        public void Resolve_KeepsRowOrderAndGapWithinFloor()
        {
            // Разброс не должен сближать соседей по этажу: иначе узлы слипаются, а рёбра-лестница
            // начинают пересекаться (её непересекаемость доказана только для ровной сетки).
            var layout = MapLayout.Default;
            var nodes = Grid(10, 6);

            for (long seed = 1; seed <= 50; seed++)
            {
                var pos = layout.Resolve(nodes, seed);
                foreach (var floor in nodes.GroupBy(n => n.Floor))
                {
                    var ordered = floor.OrderBy(n => n.Row).ToList();
                    for (int i = 1; i < ordered.Count; i++)
                    {
                        float gap = pos[ordered[i].Id].y - pos[ordered[i - 1].Id].y;
                        Assert.Greater(gap, layout.StepY * 0.5f,
                            $"Сид {seed}, этаж {floor.Key}: ряды {i - 1} и {i} сошлись слишком близко.");
                    }
                }
            }
        }

        [Test]
        public void Resolve_KeepsMinDistanceBetweenAllNodes()
        {
            // Главное правило раскладки: НИКАКИЕ два узла не стоят слишком близко — в том числе с РАЗНЫХ
            // этажей. Именно эта пара и налезала друг на друга (сундуки на скрине play-QA Макса):
            // разброс сам по себе такого не гарантирует, поэтому раскладка расталкивается после него.
            var layout = MapLayout.Default;
            var nodes = Grid(14, 6);

            for (long seed = 1; seed <= 50; seed++)
            {
                var pos = layout.Resolve(nodes, seed);
                for (int i = 0; i < nodes.Count; i++)
                {
                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        float dist = Vector2.Distance(pos[nodes[i].Id], pos[nodes[j].Id]);
                        Assert.GreaterOrEqual(dist, layout.MinDistance - 0.01f,
                            $"Сид {seed}: {nodes[i].Id} и {nodes[j].Id} стоят ближе минимума ({dist:F2}).");
                    }
                }
            }
        }

        [Test]
        public void Resolve_KeepsRowOrderAfterRelaxation()
        {
            // Расталкивание не должно переставлять узлы местами: порядок рядов держит непересекаемость рёбер.
            var layout = MapLayout.Default;
            var nodes = Grid(14, 6);

            for (long seed = 1; seed <= 50; seed++)
            {
                var pos = layout.Resolve(nodes, seed);
                foreach (var floor in nodes.GroupBy(n => n.Floor))
                {
                    var ordered = floor.OrderBy(n => n.Row).ToList();
                    for (int i = 1; i < ordered.Count; i++)
                        Assert.Greater(pos[ordered[i].Id].y, pos[ordered[i - 1].Id].y,
                            $"Сид {seed}, этаж {floor.Key}: ряд {i} должен остаться ниже ряда {i - 1}.");
                }
            }
        }

        [Test]
        public void Resolve_JitterStaysWithinConfiguredShare()
        {
            // Разброс ограничен долей шага: иначе узлы уезжают в соседние ряды и рёбра начинают пересекаться.
            // Расталкивание здесь выключено — оно двигает узлы сверх разброса и проверяется отдельно.
            var layout = MapLayout.Default;
            layout.MinDistance = 0f;
            var nodes = Grid(8, 4);
            var jittered = layout.Resolve(nodes, 4242L);

            var clean = layout;
            clean.JitterX     = 0f;
            clean.JitterY     = 0f;
            var exact = clean.Resolve(nodes, 4242L);

            float maxY = layout.JitterY * layout.StepY + 0.001f;
            foreach (var n in nodes)
            {
                Vector2 delta = jittered[n.Id] - exact[n.Id];
                Assert.LessOrEqual(Mathf.Abs(delta.x), layout.JitterX * layout.StepX + 0.001f);
                Assert.LessOrEqual(Mathf.Abs(delta.y), maxY);
            }
        }
    }
}
