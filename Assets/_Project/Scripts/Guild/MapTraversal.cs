using System.Collections.Generic;
using System.Linq;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Хелперы обхода карты акта (план [[act-map-run-loop]] §3.2). Вынесены из <see cref="MapState"/>, чтобы DTO
    /// остался чистым сейв-контрактом без логики. Позиция игрока — <see cref="MapState.CurrentNodeId"/>;
    /// продвижение и пометка пройденного — только через <see cref="Advance"/> (единая точка мутации карты).
    /// </summary>
    public static class MapTraversal
    {
        /// <summary>Узел по id, либо <c>null</c> если такого нет.</summary>
        public static MapNode NodeById(MapState map, string id)
        {
            if (map?.Nodes == null || string.IsNullOrEmpty(id)) return null;
            foreach (var node in map.Nodes)
                if (node.Id == id) return node;
            return null;
        }

        /// <summary>Текущий узел, на котором стоит игрок.</summary>
        public static MapNode Current(MapState map) => NodeById(map, map?.CurrentNodeId);

        /// <summary>
        /// Узлы, доступные для входа с текущей позиции: соседи по <see cref="MapNode.Edges"/> текущего узла,
        /// ещё не пройденные. Пустой список = тупик или акт завершён.
        /// </summary>
        public static IReadOnlyList<MapNode> AvailableNext(MapState map)
        {
            var current = Current(map);
            if (current?.Edges == null) return System.Array.Empty<MapNode>();

            var result = new List<MapNode>(current.Edges.Length);
            foreach (var edgeId in current.Edges)
            {
                var node = NodeById(map, edgeId);
                if (node != null && !node.Cleared) result.Add(node);
            }
            return result;
        }

        /// <summary>Можно ли войти в узел <paramref name="nodeId"/> прямо сейчас (сосед текущего, не пройден).</summary>
        public static bool CanEnter(MapState map, string nodeId) =>
            AvailableNext(map).Any(n => n.Id == nodeId);

        /// <summary>
        /// Продвигает позицию в узел <paramref name="nodeId"/>: помечает его пройденным и делает текущим.
        /// Зовётся оркестратором после успешного прохождения узла. Возвращает <c>false</c>, если вход недоступен
        /// (карту не мутирует) — защита от рассинхрона.
        /// </summary>
        public static bool Advance(MapState map, string nodeId)
        {
            if (!CanEnter(map, nodeId)) return false;
            var node = NodeById(map, nodeId);
            node.Cleared = true;
            map.CurrentNodeId = nodeId;
            // Узел пройден — входить в него больше некуда: снова развилка, и узлы снова горят. Сброс
            // живёт здесь, потому что здесь же и кончается вход, а два места про один факт разошлись бы.
            map.EnteringNodeId = null;
            return true;
        }

        /// <summary>Акт пройден: игрок стоит на боссе и тот пройден.</summary>
        public static bool IsActComplete(MapState map)
        {
            var current = Current(map);
            return current != null && current.Type == MapNodeType.Boss && current.Cleared;
        }
    }
}
