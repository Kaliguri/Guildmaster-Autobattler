using System.Collections.Generic;
using System.Linq;
using Guildmaster.Core.Random;
using Guildmaster.Guild;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Генератор карты акта (план [[act-map-run-loop]] §3.1, шаг A1). Закрепляет контракт топологии:
    /// детерминизм по сиду, связность старт↔босс в обе стороны (нет недостижимых узлов и тупиков),
    /// и правила размещения (элитки не в ранних колонках, гарантированный магазин).
    /// </summary>
    public sealed class MapGeneratorTests
    {
        private static MapState Generate(ulong seed, MapGenConfig cfg = null) =>
            MapGenerator.Generate(new XorShiftRng(seed), cfg ?? new MapGenConfig());

        private static string Fingerprint(MapState map) => string.Join("|",
            map.Nodes.OrderBy(n => n.Id).Select(n =>
                $"{n.Id}:{n.Type}:{n.UiPosition.x},{n.UiPosition.y}:[{string.Join(",", n.Edges)}]"));

        [Test]
        public void Generate_IsDeterministic_ForSameSeed()
        {
            Assert.AreEqual(Fingerprint(Generate(12345UL)), Fingerprint(Generate(12345UL)));
        }

        [Test]
        public void Generate_DifferentSeeds_ProduceDifferentMaps()
        {
            var prints = Enumerable.Range(1, 6).Select(s => Fingerprint(Generate((ulong)s))).Distinct().ToList();
            Assert.Greater(prints.Count, 1, "Разные сиды должны давать хотя бы иногда разные карты.");
        }

        [Test]
        public void Generate_ColumnCount_MatchesConfig()
        {
            var cfg = new MapGenConfig { Columns = 9 };
            var map = Generate(7UL, cfg);
            int columns = map.Nodes.Select(n => n.UiPosition.x).Distinct().Count();
            Assert.AreEqual(9, columns);
        }

        [Test]
        public void Generate_StartAndBoss_AreSingletonEndpoints()
        {
            var map = Generate(7UL);
            var starts = map.Nodes.Where(n => n.Type == MapNodeType.Start).ToList();
            var bosses = map.Nodes.Where(n => n.Type == MapNodeType.Boss).ToList();

            Assert.AreEqual(1, starts.Count, "Ровно один старт.");
            Assert.AreEqual(1, bosses.Count, "Ровно один босс.");
            Assert.AreEqual(map.CurrentNodeId, starts[0].Id, "Игрок стартует на старте.");
            Assert.IsTrue(starts[0].Cleared, "Старт помечен пройденным (игрок на нём стоит).");
            Assert.AreEqual(0f, starts[0].UiPosition.x, "Старт — первая колонка.");
            float maxX = map.Nodes.Max(n => n.UiPosition.x);
            Assert.AreEqual(maxX, bosses[0].UiPosition.x, "Босс — последняя колонка.");
            Assert.IsEmpty(bosses[0].Edges, "У босса нет исходящих рёбер.");
        }

        [Test]
        public void Generate_AllNodesReachableFromStart()
        {
            var map = Generate(99UL);
            var reachable = ForwardReachable(map, map.CurrentNodeId);
            Assert.AreEqual(map.Nodes.Length, reachable.Count, "Каждый узел достижим из старта по рёбрам.");
        }

        [Test]
        public void Generate_AllNodesCanReachBoss()
        {
            var map = Generate(99UL);
            var boss = map.Nodes.First(n => n.Type == MapNodeType.Boss);
            var canReachBoss = BackwardReachable(map, boss.Id);
            Assert.AreEqual(map.Nodes.Length, canReachBoss.Count, "Из каждого узла есть путь к боссу (нет тупиков).");
        }

        [Test]
        public void Generate_NoElite_BeforeEliteMinColumn()
        {
            var cfg = new MapGenConfig { EliteMinColumn = 2 };
            // Прогоняем несколько сидов — правило должно держаться на всех.
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var map = Generate(seed, cfg);
                foreach (var elite in map.Nodes.Where(n => n.Type == MapNodeType.Elite))
                    Assert.GreaterOrEqual(elite.UiPosition.x, cfg.EliteMinColumn,
                        $"Элитка в колонке {elite.UiPosition.x} раньше EliteMinColumn (сид {seed}).");
            }
        }

        [Test]
        public void Generate_HasAtLeastOneShop()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var map = Generate(seed);
                Assert.IsTrue(map.Nodes.Any(n => n.Type == MapNodeType.Shop),
                    $"На карте должен быть хотя бы один магазин (сид {seed}).");
            }
        }

        // --- BFS-хелперы по рёбрам карты ---

        private static HashSet<string> ForwardReachable(MapState map, string startId)
        {
            var byId = map.Nodes.ToDictionary(n => n.Id);
            var seen = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(startId);
            seen.Add(startId);
            while (queue.Count > 0)
            {
                foreach (var next in byId[queue.Dequeue()].Edges)
                    if (seen.Add(next)) queue.Enqueue(next);
            }
            return seen;
        }

        private static HashSet<string> BackwardReachable(MapState map, string targetId)
        {
            // Обратная смежность: to → список from.
            var incoming = new Dictionary<string, List<string>>();
            foreach (var node in map.Nodes)
            foreach (var edge in node.Edges)
            {
                if (!incoming.TryGetValue(edge, out var list))
                    incoming[edge] = list = new List<string>();
                list.Add(node.Id);
            }

            var seen = new HashSet<string> { targetId };
            var queue = new Queue<string>();
            queue.Enqueue(targetId);
            while (queue.Count > 0)
            {
                if (!incoming.TryGetValue(queue.Dequeue(), out var froms)) continue;
                foreach (var from in froms)
                    if (seen.Add(from)) queue.Enqueue(from);
            }
            return seen;
        }
    }
}
