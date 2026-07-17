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
    /// пересекаются крест-накрест) и естественные развилки/схождения. Правила: элитки не раньше
    /// <see cref="MapGenConfig.EliteMinColumn"/>, гарантированно ≥1 магазин до босса.
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

            columns.Add(new List<MapNode> { NewNode("c0r0", MapNodeType.Start, col: 0, row: 0, width: 1) });

            for (int col = 1; col < cfg.Columns - 1; col++)
            {
                int width = rng.NextInt(cfg.MinColumnWidth, cfg.MaxColumnWidth + 1);
                var column = new List<MapNode>(width);
                for (int row = 0; row < width; row++)
                {
                    var type = RollNodeType(rng, cfg, col);
                    column.Add(NewNode($"c{col}r{row}", type, col, row, width));
                }
                columns.Add(column);
            }

            int last = cfg.Columns - 1;
            columns.Add(new List<MapNode> { NewNode($"c{last}r0", MapNodeType.Boss, last, row: 0, width: 1) });

            // 2. Рёбра между соседними колонками (монотонная лестница).
            var edges = new Dictionary<string, List<string>>();
            for (int col = 0; col < columns.Count - 1; col++)
            {
                ConnectColumns(rng, columns[col], columns[col + 1], edges);
            }
            foreach (var node in columns.SelectMany(c => c))
            {
                node.Edges = edges.TryGetValue(node.Id, out var list) ? list.ToArray()
                                                                       : System.Array.Empty<string>();
            }

            // 3. Гарантия: хотя бы один магазин до босса.
            EnsureShopExists(rng, columns);

            // 4. Сборка состояния: игрок стоит на старте (он «пройден»), карта видна целиком.
            var start = columns[0][0];
            start.Cleared = true;
            return new MapState
            {
                CurrentNodeId = start.Id,
                Nodes = columns.SelectMany(c => c).ToArray(),
            };
        }

        private static MapNode NewNode(string id, MapNodeType type, int col, int row, int width) => new MapNode
        {
            Id         = id,
            Type       = type,
            PayloadId  = string.Empty,
            Edges      = System.Array.Empty<string>(),
            Cleared    = false,
            // Раскладка для оверлея: x = колонка, y = ряд, отцентрованный вокруг нуля.
            UiPosition = new Vector2(col, row - (width - 1) * 0.5f),
        };

        /// <summary>Взвешенный ролл типа промежуточного узла; элитка исключается в ранних колонках.</summary>
        private static MapNodeType RollNodeType(IRngService rng, MapGenConfig cfg, int col)
        {
            bool eliteAllowed = col >= cfg.EliteMinColumn;

            // Пары (тип, вес) — порядок стабилен для детерминизма.
            var weighted = new List<(MapNodeType type, int weight)>
            {
                (MapNodeType.Battle,    cfg.WeightBattle),
                (MapNodeType.Elite,     eliteAllowed ? cfg.WeightElite : 0),
                (MapNodeType.TextEvent, cfg.WeightTextEvent),
                (MapNodeType.Shop,      cfg.WeightShop),
                (MapNodeType.Chest,     cfg.WeightChest),
                (MapNodeType.Unknown,   cfg.WeightUnknown),
            };

            int total = weighted.Sum(w => w.weight);
            if (total <= 0) return MapNodeType.Battle; // защита от нулевых весов

            int roll = rng.NextInt(0, total);
            foreach (var (type, weight) in weighted)
            {
                if (weight <= 0) continue;
                roll -= weight;
                if (roll < 0) return type;
            }
            return MapNodeType.Battle; // недостижимо при total>0
        }

        /// <summary>
        /// Связывает две соседние колонки «монотонной лестницей»: два указателя (si, ti) идут от (0,0) к
        /// (ws-1, wt-1), на каждом шаге продвигаясь по источнику, по цели или по обоим. Монотонность даёт
        /// планарность (рёбра не пересекаются), а полный проход указателей — связность (каждый узел покрыт).
        /// </summary>
        private static void ConnectColumns(IRngService rng, List<MapNode> source, List<MapNode> target,
                                            Dictionary<string, List<string>> edges)
        {
            int ws = source.Count, wt = target.Count;
            int si = 0, ti = 0;
            while (true)
            {
                AddEdge(edges, source[si].Id, target[ti].Id);

                if (si == ws - 1 && ti == wt - 1) break;
                if (si == ws - 1) { ti++; continue; }   // источники кончились — идём по целям
                if (ti == wt - 1) { si++; continue; }   // цели кончились — идём по источникам

                switch (rng.NextInt(0, 3))
                {
                    case 0: si++; break;                // схождение: следующий источник → та же цель
                    case 1: ti++; break;                // развилка: тот же источник → следующая цель
                    default: si++; ti++; break;         // диагональ
                }
            }
        }

        private static void AddEdge(Dictionary<string, List<string>> edges, string from, string to)
        {
            if (!edges.TryGetValue(from, out var list))
            {
                list = new List<string>();
                edges[from] = list;
            }
            if (!list.Contains(to)) list.Add(to);
        }

        /// <summary>Если на карте нет ни одного магазина — конвертирует один промежуточный узел в Shop.</summary>
        private static void EnsureShopExists(IRngService rng, List<List<MapNode>> columns)
        {
            var middle = columns.Skip(1).Take(columns.Count - 2).SelectMany(c => c).ToList();
            if (middle.Count == 0 || middle.Any(n => n.Type == MapNodeType.Shop)) return;

            // Предпочитаем не затирать элитки (они редки и значимы).
            var candidates = middle.Where(n => n.Type != MapNodeType.Elite).ToList();
            if (candidates.Count == 0) candidates = middle;
            candidates[rng.NextInt(0, candidates.Count)].Type = MapNodeType.Shop;
        }
    }
}
