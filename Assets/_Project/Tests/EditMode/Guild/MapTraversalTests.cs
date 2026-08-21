using System.Linq;
using Guildmaster.Core.Random;
using Guildmaster.Guild;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Хелперы обхода карты (план [[act-map-run-loop]] §3.2, шаг A1): доступные соседи, продвижение позиции,
    /// защита от входа в недоступный/пройденный узел, детект завершения акта.
    /// </summary>
    public sealed class MapTraversalTests
    {
        private static MapState FreshMap(ulong seed = 42UL) =>
            MapGenerator.Generate(new XorShiftRng(seed), new MapGenConfig());

        [Test]
        public void Current_IsStart_Initially()
        {
            var map = FreshMap();
            Assert.AreEqual(MapNodeType.Start, MapTraversal.Current(map).Type);
        }

        [Test]
        public void AvailableNext_FromStart_EqualsStartEdges()
        {
            var map = FreshMap();
            var start = MapTraversal.Current(map);
            var available = MapTraversal.AvailableNext(map).Select(n => n.Id).OrderBy(x => x);
            Assert.AreEqual(start.Edges.OrderBy(x => x), available);
            Assert.IsNotEmpty(available, "У старта есть доступные узлы.");
        }

        [Test]
        public void Advance_MarksCleared_AndMovesCurrent()
        {
            var map = FreshMap();
            var target = MapTraversal.AvailableNext(map)[0];

            Assert.IsTrue(MapTraversal.Advance(map, target.Id));
            Assert.AreEqual(target.Id, map.CurrentNodeId, "Текущий узел сместился.");
            Assert.IsTrue(MapTraversal.NodeById(map, target.Id).Cleared, "Узел помечен пройденным.");
        }

        /// <summary>
        /// Длина забега — это узлы, ПРОЙДЕННЫЕ ногами. Стартовый помечен пройденным при генерации
        /// (драться там не с кем), и без вычета любой забег был бы длиннее себя на единицу — включая
        /// брошенный на первом шаге, где пройдено ноль. Статистика профиля читает именно это число
        /// (<c>RunStatsRecorder</c>), поэтому промах виден игроку как «лучший забег: 1 узел» у того,
        /// кто не сделал ни шага.
        /// </summary>
        [Test]
        public void ClearedCount_IgnoresStart_AndGrowsWithEachStep()
        {
            var map = FreshMap();
            Assert.AreEqual(0, MapTraversal.ClearedCount(map),
                            "Свежая карта: старт пройденным помечен, но ногами не пройдено ничего.");

            var first = MapTraversal.AvailableNext(map)[0];
            Assert.IsTrue(MapTraversal.Advance(map, first.Id));
            Assert.AreEqual(1, MapTraversal.ClearedCount(map), "Один шаг — один пройденный узел.");

            var second = MapTraversal.AvailableNext(map)[0];
            Assert.IsTrue(MapTraversal.Advance(map, second.Id));
            Assert.AreEqual(2, MapTraversal.ClearedCount(map), "Второй шаг считается тоже.");
        }

        /// <summary>Карты нет вовсе (забег свёрнут) — ноль, а не падение: считать нечего.</summary>
        [Test]
        public void ClearedCount_OnMissingMap_IsZero()
        {
            Assert.AreEqual(0, MapTraversal.ClearedCount(null));
            Assert.AreEqual(0, MapTraversal.ClearedCount(new MapState()));
        }

        [Test]
        public void Advance_RejectsNonNeighbour_WithoutMutation()
        {
            var map = FreshMap();
            var boss = map.Nodes.First(n => n.Type == MapNodeType.Boss);
            string before = map.CurrentNodeId;

            Assert.IsFalse(MapTraversal.Advance(map, boss.Id), "Прыжок к боссу через всю карту недопустим.");
            Assert.AreEqual(before, map.CurrentNodeId, "Позиция не изменилась.");
            Assert.IsFalse(boss.Cleared, "Босс не помечен пройденным.");
        }

        [Test]
        public void AvailableNext_ExcludesClearedNodes()
        {
            var map = FreshMap();
            var target = MapTraversal.AvailableNext(map)[0];
            MapTraversal.Advance(map, target.Id);

            // Вернуться в уже пройденный узел нельзя (если бы на него было ребро).
            Assert.IsFalse(MapTraversal.AvailableNext(map).Any(n => n.Cleared));
        }

        [Test]
        public void WalkToBoss_CompletesAct()
        {
            var map = FreshMap();

            // Жадный проход: рёбра строго вперёд на колонку, из любого узла есть путь к боссу.
            int guard = 0;
            while (!MapTraversal.IsActComplete(map) && guard++ < 100)
            {
                var next = MapTraversal.AvailableNext(map);
                Assert.IsNotEmpty(next, "Пока не босс — всегда есть куда идти (нет тупиков).");
                Assert.IsTrue(MapTraversal.Advance(map, next[0].Id));
            }

            Assert.IsTrue(MapTraversal.IsActComplete(map), "Дошли до босса и прошли его — акт завершён.");
            Assert.AreEqual(MapNodeType.Boss, MapTraversal.Current(map).Type);
        }

        [Test]
        public void IsActComplete_False_BeforeBossCleared()
        {
            var map = FreshMap();
            Assert.IsFalse(MapTraversal.IsActComplete(map), "На старте акт не завершён.");
        }
    }
}
