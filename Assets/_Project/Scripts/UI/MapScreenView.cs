using System;
using System.Collections.Generic;
using Guildmaster.Guild;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка экрана карты акта из UXML-шаблона (план [[act-map-run-loop]] §3.2, A3) — общий код для живого
    /// роутера (<see cref="MenuRouter"/>) и превью-стенда. Разметка/стиль — только из <c>MapScreen.uxml</c> +
    /// дизайн-система; здесь — вставка холста графа (<see cref="MapGraph"/>) и подписи узлов через лок-ключи.
    /// Выбор доступного узла возвращается через <paramref name="onChosen"/> (id узла).
    /// </summary>
    public static class MapScreenView
    {
        public static VisualElement Build(
            VisualTreeAsset uxml,
            MapState map,
            ISet<string> availableIds,
            Func<string, string> localize,
            Action<string> onChosen)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title = root.Q<Label>("map-title");
            if (title != null) title.text = L("ui.map.title", "Карта акта");

            var host = root.Q<VisualElement>("map-graph");
            if (host != null)
            {
                var graph = new MapGraph();
                host.Add(graph);
                graph.SetData(map, availableIds, onChosen, node => NodeLabel(node, L));
            }

            return root;
        }

        private static string NodeLabel(MapNode node, Func<string, string, string> L) => node.Type switch
        {
            MapNodeType.Start     => L("ui.map.node.start", "Старт"),
            MapNodeType.Battle    => L("ui.map.node.battle", "Бой"),
            MapNodeType.Elite     => L("ui.map.node.elite", "Элита"),
            MapNodeType.Boss      => L("ui.map.node.boss", "Босс"),
            MapNodeType.Shop      => L("ui.map.node.shop", "Лавка"),
            MapNodeType.Chest     => L("ui.map.node.chest", "Сундук"),
            MapNodeType.TextEvent => L("ui.map.node.event", "Событие"),
            MapNodeType.Unknown   => L("ui.map.node.unknown", "?"),
            _                     => L("ui.map.node.unknown", "?"),
        };
    }
}
