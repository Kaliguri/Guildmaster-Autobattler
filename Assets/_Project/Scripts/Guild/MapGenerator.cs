using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Guildmaster.Core.Random;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Строит граф карты акта (<see cref="MapState"/>) из детерминированного <see cref="IRngService"/> — план
    /// [[act-map-run-loop]] §3.1. Топология слева→направо в духе Across the Obelisk: колонки узлов, ветвление
    /// и схождение. Один и тот же сид → одинаковая карта на любой платформе (весь рандом идёт через RNG).
    /// </summary>
    /// <remarks>
    /// Связь соседних колонок — алгоритмом «монотонной лестницы» (см. <see cref="ConnectColumns"/>): он
    /// гарантирует связность (у каждого узла есть входящее и исходящее ребро), планарность (рёбра не
    /// пересекаются крест-накрест) и естественные развилки/схождения. Типы узлов — по ЗОНАМ этажей и
    /// ЯКОРЯМ (<see cref="MapGenConfig.Zones"/>/<see cref="MapGenConfig.Anchors"/>); гарантированно ≥1 магазин до босса.
    /// <para><b>Payload узлов не назначается здесь</b> — генератор строит только топологию и типы; конкретный
    /// контент (энкаунтер/ивент/пул) выбирает <c>NodeResolver</c> на входе в узел (фаза A2).</para>
    /// </remarks>
    public static class MapGenerator
    {
        /// <summary>Строит карту акта из <paramref name="rng"/> по параметрам <paramref name="config"/>.</summary>
        public static MapState Generate(IRngService rng, MapGenConfig config)
        {
            var cfg = (config ?? new MapGenConfig()).Validated();

            // 1. Колонки: [Start] · промежуточные (случайной ширины, типы по весам) · [Boss].
            var columns = new List<List<MapNode>>(cfg.Columns);

            columns.Add(new List<MapNode> { NewNode("c0r0", MapNodeType.Start, floor: 0, row: 0) });

            for (int col = 1; col < cfg.Columns - 1; col++)
            {
                int width = ColumnWidth(rng, cfg, col);
                var column = new List<MapNode>(width);
                for (int row = 0; row < width; row++)
                {
                    var type = RollNodeType(rng, cfg, col);
                    column.Add(NewNode($"c{col}r{row}", type, col, row));
                }
                columns.Add(column);
            }

            int last = cfg.Columns - 1;
            columns.Add(new List<MapNode> { NewNode($"c{last}r0", MapNodeType.Boss, last, row: 0) });

            // 2. Рёбра между соседними колонками (монотонная лестница).
            var edges = new Dictionary<string, List<string>>();
            for (int col = 0; col < columns.Count - 1; col++)
            {
                ConnectColumns(rng, columns[col], columns[col + 1], edges, cfg.MaxEdgesPerNode);
            }
            foreach (var node in columns.SelectMany(c => c))
            {
                node.Edges = edges.TryGetValue(node.Id, out var list) ? list.ToArray()
                                                                       : System.Array.Empty<string>();
            }

            // 3. Гарантия: хотя бы один магазин до босса.
            EnsureShopExists(rng, columns, cfg);

            // 4. Сборка состояния: игрок стоит на старте (он «пройден»), карта видна целиком.
            var start = columns[0][0];
            start.Cleared = true;
            return new MapState
            {
                CurrentNodeId = start.Id,
                Nodes = columns.SelectMany(c => c).ToArray(),
            };
        }

        private static MapNode NewNode(string id, MapNodeType type, int floor, int row) => new MapNode
        {
            Id        = id,
            Type      = type,
            PayloadId = string.Empty,
            Edges     = System.Array.Empty<string>(),
            Cleared   = false,
            Floor     = floor,
            Row       = row,
        };

        /// <summary>
        /// Ширина колонки по профилю акта: горловины у краёв (узко), середина — рандом в диапазоне.
        /// Акт читается как путь — начинается узко, раздаётся и снова сужается к боссу.
        /// </summary>
        private static int ColumnWidth(IRngService rng, MapGenConfig cfg, int col)
        {
            // Якорь может задать свою ширину этажа — так сундук-ряд становится горловиной посреди акта.
            if (cfg.Anchors != null)
                for (int i = 0; i < cfg.Anchors.Length; i++)
                    if (cfg.Anchors[i].Floor == col && cfg.Anchors[i].Width > 0)
                        return cfg.Anchors[i].Width;

            int lastFloor = cfg.Columns - 2;                       // последний этаж-испытание (перед Boss)
            bool nearStart = col <= cfg.EdgeColumns;
            bool nearBoss  = col > lastFloor - cfg.EdgeColumns;
            if (nearStart || nearBoss) return cfg.EdgeColumnWidth;

            return rng.NextInt(cfg.MinColumnWidth, cfg.MaxColumnWidth + 1);
        }


        /// <summary>
        /// Тип промежуточного узла на этаже <paramref name="col"/> по ЗОНАМ и ЯКОРЯМ конфига. Якорь этажа
        /// перекрывает зонные веса (сундук-ряд / привал перед боссом); иначе — взвешенный ролл по первой
        /// покрывающей зоне; этаж вне зон/якорей → безопасный дефолт (Бой).
        /// </summary>
        private static MapNodeType RollNodeType(IRngService rng, MapGenConfig cfg, int col)
        {
            // Якорь перекрывает всё: вся колонка этого этажа — фиксированного типа.
            for (int i = 0; i < cfg.Anchors.Length; i++)
                if (cfg.Anchors[i].Floor == col) return cfg.Anchors[i].Type;

            // Первая зона, покрывающая этаж → взвешенный ролл по её типам.
            for (int i = 0; i < cfg.Zones.Length; i++)
            {
                ZoneRule zone = cfg.Zones[i];
                if (!zone.Covers(col)) continue;
                return WeightedPick(rng, zone.Weights);
            }

            return MapNodeType.Battle; // этаж вне зон и якорей — безопасный дефолт
        }

        /// <summary>Взвешенный выбор типа по весам зоны (порядок стабилен для детерминизма). Пусто/нули → Бой.</summary>
        private static MapNodeType WeightedPick(IRngService rng, NodeTypeWeight[] weights)
        {
            if (weights == null || weights.Length == 0) return MapNodeType.Battle;

            int total = 0;
            for (int i = 0; i < weights.Length; i++)
                if (weights[i].Weight > 0) total += weights[i].Weight;
            if (total <= 0) return MapNodeType.Battle;

            int roll = rng.NextInt(0, total);
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i].Weight <= 0) continue;
                roll -= weights[i].Weight;
                if (roll < 0) return weights[i].Type;
            }
            return MapNodeType.Battle; // недостижимо при total>0
        }

        /// <summary>
        /// Связывает две соседние колонки «монотонной лестницей»: два указателя (si, ti) идут от (0,0) к
        /// (ws-1, wt-1), на каждом шаге продвигаясь по источнику, по цели или по обоим. Монотонность даёт
        /// планарность (рёбра не пересекаются), а полный проход указателей — связность (каждый узел покрыт).
        /// </summary>
        private static void ConnectColumns(IRngService rng, List<MapNode> source, List<MapNode> target,
                                            Dictionary<string, List<string>> edges, int maxEdges)
        {
            int ws = source.Count, wt = target.Count;
            int si = 0, ti = 0;
            var outgoing = new int[ws];
            var incoming = new int[wt];

            while (true)
            {
                AddEdge(edges, source[si].Id, target[ti].Id);
                outgoing[si]++;
                incoming[ti]++;

                if (si == ws - 1 && ti == wt - 1) break;
                if (si == ws - 1) { ti++; continue; }   // источники кончились — идём по целям
                if (ti == wt - 1) { si++; continue; }   // цели кончились — идём по источникам

                // Указатели ведём вдоль ДИАГОНАЛИ колонок: сравниваем не индексы, а доли пройденного.
                // Раньше шаг был чистым рандомом, и на скачках ширины (5→7→3) один узел набирал веер
                // рёбер через полкарты — именно это делало карту нечитаемой (play-QA Макса).
                // Широкие развилки при этом остаются: они нужны как выбор биома, режется только ДЛИНА.
                float s = si / (float)(ws - 1);
                float t = ti / (float)(wt - 1);

                bool canFan   = outgoing[si] < maxEdges;   // сколько путей выходит из узла
                bool canMerge = incoming[ti] < maxEdges;   // сколько путей в узел входит

                if (s < t - Epsilon || !canFan) si++;            // источник отстаёт — подтянуть его
                else if (s > t + Epsilon || !canMerge) ti++;     // цель отстаёт — подтянуть её
                else
                {
                    switch (rng.NextInt(0, 3))
                    {
                        case 0: si++; break;                // схождение: следующий источник → та же цель
                        case 1: ti++; break;                // развилка: тот же источник → следующая цель
                        default: si++; ti++; break;         // диагональ
                    }
                }
            }
        }

        private const float Epsilon = 0.0001f;

        private static void AddEdge(Dictionary<string, List<string>> edges, string from, string to)
        {
            if (!edges.TryGetValue(from, out var list))
            {
                list = new List<string>();
                edges[from] = list;
            }
            if (!list.Contains(to)) list.Add(to);
        }

        /// <summary>
        /// Если на карте нет ни одного магазина — конвертирует один промежуточный узел в Shop.
        /// <para>ЯКОРНЫЕ этажи неприкосновенны: они и есть заданный ритм акта (сундук-талия, ряд привалов
        /// перед боссом). Пока магазин сам был якорем, это не всплывало; стоило якорь снять — страховка
        /// принялась перекрашивать привалы в лавки, и целый этаж терял свой смысл (поймано тестом).</para>
        /// </summary>
        private static void EnsureShopExists(IRngService rng, List<List<MapNode>> columns, MapGenConfig cfg)
        {
            var middle = columns.Skip(1).Take(columns.Count - 2).SelectMany(c => c).ToList();
            if (middle.Count == 0 || middle.Any(n => n.Type == MapNodeType.Shop)) return;

            // Ставить магазин можно только туда, где его РАЗРЕШАЕТ зона: страховка чинит невезучий ролл,
            // а не отменяет правила акта. Иначе лавка вылезала в разогрев, куда её не пускает ни одна зона.
            bool Allowed(MapNode n)
            {
                if (cfg.Anchors.Any(a => a.Floor == n.Floor)) return false; // якорный этаж — заданный ритм
                foreach (ZoneRule zone in cfg.Zones)
                {
                    if (!zone.Covers(n.Floor)) continue;
                    return zone.Weights != null
                        && zone.Weights.Any(w => w.Type == MapNodeType.Shop && w.Weight > 0);
                }
                return false;
            }

            var free = middle.Where(Allowed).ToList();
            if (free.Count == 0) return; // ставить магазин просто некуда — лучше без него, чем ломая ритм

            var candidates = free.Where(n => n.Type != MapNodeType.Elite).ToList();
            if (candidates.Count == 0) candidates = free;
            candidates[rng.NextInt(0, candidates.Count)].Type = MapNodeType.Shop;
        }
    }
}
