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
                $"{n.Id}:{n.Type}:{n.Floor},{n.Row}:[{string.Join(",", n.Edges)}]"));

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
            int columns = map.Nodes.Select(n => n.Floor).Distinct().Count();
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
            Assert.AreEqual(0, starts[0].Floor, "Старт — первая колонка.");
            int maxFloor = map.Nodes.Max(n => n.Floor);
            Assert.AreEqual(maxFloor, bosses[0].Floor, "Босс — последняя колонка.");
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
        public void Generate_DefaultDepth_Is15Columns()
        {
            // Дефолт (реш. Макса 2026-07-20): Start + 13 испытаний + Boss.
            var map = Generate(3UL);
            int columns = map.Nodes.Select(n => n.Floor).Distinct().Count();
            Assert.AreEqual(15, columns, "Дефолтная глубина = Start + 13 испытаний + Boss.");
        }

        [Test]
        public void Generate_Waist_FunnelsThroughChest_ThenFansIntoCamps()
        {
            // Талия акта (реш. Макса 2026-07-20): широкая середина сходится в ОДИН сундук, и уже из него
            // веер в ТРИ привала. Это ритмическая пауза — если сундук снова станет колонкой, пропадёт и
            // общая точка, и осмысленность следующей развилки.
            var cfg = new MapGenConfig();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var map = Generate(seed, cfg);

                var chests = map.Nodes.Where(n => n.Floor == 7).ToList();
                var camps  = map.Nodes.Where(n => n.Floor == 8).ToList();

                Assert.AreEqual(1, chests.Count, $"Этаж 7 — ровно один сундук (сид {seed}).");
                Assert.AreEqual(MapNodeType.Chest, chests[0].Type, $"Этаж 7 — сундук (сид {seed}).");
                Assert.AreEqual(3, camps.Count, $"Этаж 8 — ровно три привала (сид {seed}).");
                Assert.IsTrue(camps.All(n => n.Type == MapNodeType.Camp), $"Этаж 8 — привалы (сид {seed}).");

                CollectionAssert.AreEquivalent(camps.Select(c => c.Id), chests[0].Edges,
                    $"Из сундука ведут пути ровно во все три привала (сид {seed}).");

                foreach (var before in map.Nodes.Where(n => n.Floor == 6))
                    CollectionAssert.AreEqual(new[] { chests[0].Id }, before.Edges,
                        $"Этаж 6 целиком сходится в сундук (сид {seed}).");
            }
        }

        [Test]
        public void Generate_CampStandsBeforeBoss()
        {
            // Перед боссом — привал: последняя возможность потратить действия отряда до финала акта.
            var cfg = new MapGenConfig();
            int bossFloor = cfg.Columns - 1;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var map = Generate(seed, cfg);
                var beforeBoss = map.Nodes.Where(n => n.Floor == bossFloor - 1).ToList();

                Assert.IsNotEmpty(beforeBoss, $"Этаж перед боссом существует (сид {seed}).");
                Assert.IsTrue(beforeBoss.All(n => n.Type == MapNodeType.Camp),
                    $"Этаж перед боссом — привалы (сид {seed}).");
            }
        }

        [Test]
        public void Generate_NoElite_InWarmupZone()
        {
            var cfg = new MapGenConfig();
            int firstEliteFloor = FirstFloorAllowing(cfg, MapNodeType.Elite);
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var map = Generate(seed, cfg);
                foreach (var elite in map.Nodes.Where(n => n.Type == MapNodeType.Elite))
                    Assert.GreaterOrEqual(elite.Floor, firstEliteFloor,
                        $"Элитка на этаже {elite.Floor} раньше зоны, разрешающей элиту ({firstEliteFloor}) (сид {seed}).");
            }
        }

        [Test]
        public void Generate_WarmupZone_OnlyBattleOrUnknown()
        {
            // Зона разогрева (этажи 1–4): игрока не встречает сундук/элита/магазин в лицо. Событие своего
            // узла не имеет вовсе — оно приходит изнутри «?», потому «?» здесь и стоит.
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var map = Generate(seed);
                foreach (var n in map.Nodes.Where(x => x.Floor >= 1 && x.Floor <= 4))
                    Assert.IsTrue(n.Type == MapNodeType.Battle || n.Type == MapNodeType.Unknown,
                        $"Разогрев (этаж {n.Floor}): только бой/«?», не {n.Type} (сид {seed}).");
            }
        }

        [Test]
        public void Generate_TextEvent_NeverGetsItsOwnNode()
        {
            // Решение Макса 2026-07-20: событие живёт только внутри «?». Отдельный узел-свиток объявлял бы
            // неизвестность заранее и тем самым её снимал.
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var map = Generate(seed);
                Assert.IsFalse(map.Nodes.Any(n => n.Type == MapNodeType.TextEvent),
                    $"Текстовое событие не должно стоять на карте своим узлом (сид {seed}).");
            }
        }

        [Test]
        public void Generate_ShopFallback_NeverOverwritesAnchoredFloors()
        {
            // Страховка «хотя бы один магазин» не имеет права трогать якорные этажи: они и есть заданный
            // ритм акта. Пока магазин сам был якорем, это молчало; сняли якорь — привалы стали лавками.
            var cfg = new MapGenConfig().Validated();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var map = Generate(seed, cfg);
                foreach (AnchorRule anchor in cfg.Anchors)
                    foreach (var n in map.Nodes.Where(x => x.Floor == anchor.Floor))
                        Assert.AreEqual(anchor.Type, n.Type,
                            $"Якорный этаж {anchor.Floor} перекрашен в {n.Type} (сид {seed}).");
            }
        }

        [Test]
        public void Generate_AnchorFloors_ForceWholeColumnType()
        {
            // Якорь-этаж (сундук-ряд, привал перед боссом) окрашивает ВСЮ свою колонку в один тип.
            var cfg = new MapGenConfig();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var map = Generate(seed, cfg);
                foreach (var anchor in cfg.Anchors)
                {
                    var atFloor = map.Nodes.Where(n => n.Floor == anchor.Floor).ToList();
                    Assert.IsNotEmpty(atFloor, $"Этаж-якорь {anchor.Floor} существует (сид {seed}).");
                    Assert.IsTrue(atFloor.All(n => n.Type == anchor.Type),
                        $"Этаж {anchor.Floor} — вся колонка типа {anchor.Type} (сид {seed}).");
                }
            }
        }

        // Первый этаж, где зоны разрешают данный тип (вес > 0). int.MaxValue = не разрешён нигде.
        private static int FirstFloorAllowing(MapGenConfig cfg, MapNodeType type)
        {
            int best = int.MaxValue;
            foreach (var zone in cfg.Zones)
                foreach (var w in zone.Weights)
                    if (w.Type == type && w.Weight > 0) { best = System.Math.Min(best, zone.FromFloor); break; }
            return best;
        }

        [Test]
        public void Generate_ActNarrowsAtBothEnds()
        {
            // Профиль акта (реш. Макса 2026-07-20): начинается узко, раздаётся к середине и снова сужается
            // к боссу — силуэт пути, а не однородная решётка.
            var cfg = new MapGenConfig();
            int lastFloor = cfg.Columns - 2;

            for (ulong seed = 1; seed <= 20; seed++)
            {
                var map = Generate(seed, cfg);
                var widthOf = map.Nodes.GroupBy(n => n.Floor).ToDictionary(g => g.Key, g => g.Count());

                for (int floor = 1; floor <= lastFloor; floor++)
                {
                    // Якорь может задать этажу свою ширину (сундук-ряд — горловина посреди акта), и она
                    // перекрывает общий профиль.
                    int anchored = cfg.Anchors?.FirstOrDefault(a => a.Floor == floor && a.Width > 0).Width ?? 0;
                    bool narrow = floor <= cfg.EdgeColumns || floor > lastFloor - cfg.EdgeColumns;

                    if (anchored > 0)
                        Assert.AreEqual(anchored, widthOf[floor],
                            $"Якорный этаж {floor} = {anchored} узла (сид {seed}).");
                    else if (narrow)
                        Assert.AreEqual(cfg.EdgeColumnWidth, widthOf[floor],
                            $"Горловина: этаж {floor} = {cfg.EdgeColumnWidth} узла (сид {seed}).");
                    else
                        Assert.That(widthOf[floor], Is.InRange(cfg.MinColumnWidth, cfg.MaxColumnWidth),
                            $"Середина: этаж {floor} в диапазоне ширины (сид {seed}).");
                }
            }
        }

        [Test]
        public void Generate_RowsAreContiguousWithinFloor()
        {
            // Ряды нумеруются подряд от нуля: на этом держится и центрирование в презентере, и монотонность
            // рёбер-лестницы. Дыра в нумерации ломала бы раскладку молча.
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var map = Generate(seed);
                foreach (var floor in map.Nodes.GroupBy(n => n.Floor))
                {
                    var rows = floor.Select(n => n.Row).OrderBy(r => r).ToList();
                    for (int i = 0; i < rows.Count; i++)
                        Assert.AreEqual(i, rows[i], $"Этаж {floor.Key}: ряды идут подряд от 0 (сид {seed}).");
                }
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
