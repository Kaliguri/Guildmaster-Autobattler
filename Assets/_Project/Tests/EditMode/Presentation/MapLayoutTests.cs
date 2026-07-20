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
        public void Resolve_DiffersBetweenSeeds()
        {
            var layout = MapLayout.Default;
            var nodes = Grid(5, 3);

            var a = layout.Resolve(nodes, 1L);
            var b = layout.Resolve(nodes, 2L);

            Assert.IsTrue(a.Any(kv => kv.Value != b[kv.Key]), "Разные забеги — разный рисунок карты.");
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
            layout.FloorDriftY = 0f;

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
        public void Resolve_FloorDriftMovesWholeColumnTogether()
        {
            // Дрейф этажа — общий для всей колонки: он и даёт «живость», не ломая строй внутри этажа.
            var layout = MapLayout.Default;
            layout.JitterY = 0f;
            layout.JitterX = 0f;

            var nodes = Column(3, 5);
            var pos = layout.Resolve(nodes, 777L);

            var gaps = nodes.OrderBy(n => n.Row)
                            .Select(n => pos[n.Id].y)
                            .Zip(nodes.OrderBy(n => n.Row).Skip(1).Select(n => pos[n.Id].y), (a, b) => b - a)
                            .ToList();
            foreach (float gap in gaps)
                Assert.AreEqual(layout.StepY, gap, 0.001f, "Внутри этажа шаг остаётся ровным.");
        }

        [Test]
        public void Resolve_JitterStaysWithinConfiguredShare()
        {
            // Разброс ограничен долей шага: иначе узлы уезжают в соседние ряды и рёбра начинают пересекаться.
            var layout = MapLayout.Default;
            var nodes = Grid(8, 4);
            var jittered = layout.Resolve(nodes, 4242L);

            var clean = layout;
            clean.JitterX     = 0f;
            clean.JitterY     = 0f;
            clean.FloorDriftY = 0f;
            var exact = clean.Resolve(nodes, 4242L);

            float maxY = (layout.JitterY + layout.FloorDriftY) * layout.StepY + 0.001f;
            foreach (var n in nodes)
            {
                Vector2 delta = jittered[n.Id] - exact[n.Id];
                Assert.LessOrEqual(Mathf.Abs(delta.x), layout.JitterX * layout.StepX + 0.001f);
                Assert.LessOrEqual(Mathf.Abs(delta.y), maxY);
            }
        }
    }
}
