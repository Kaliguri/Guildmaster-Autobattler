using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using Guildmaster.Presentation.Map;
using MessagePipe;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Фаза D-реализация <see cref="IMapNodeChooser"/>: выбор узла на world-карте. Раскладывает граф в мир,
    /// показывает слой карты, переводит камеру в режим карты и ждёт клик по доступному узлу.
    /// <para>Контракт петли (<c>ActRunner</c>) не меняется — подменяет <see cref="MapScreenNodeChooser"/> в DI
    /// одной строкой, и такой же одной строкой откатывается обратно на UITK-карту.</para>
    /// <para>Слой склейки: только здесь встречаются <c>Guildmaster.Guild</c> (граф карты) и
    /// <c>Guildmaster.Presentation</c> (отрисовка) — сама презентация про типы узлов не знает.</para>
    /// </summary>
    public sealed class WorldMapNodeChooser : IMapNodeChooser
    {
        private readonly IWorldMapView _view;
        private readonly IPublisher<WorldMapSpaceChangedEvent> _spacePub;

        // Шаг сетки: MapNode.UiPosition — это (колонка, ряд), а не мир. Разносим в мировые единицы.
        private const float StepX = 3.2f;
        private const float StepY = 2.4f;

        public WorldMapNodeChooser(IWorldMapView view, IPublisher<WorldMapSpaceChangedEvent> spacePub)
        {
            _view     = view;
            _spacePub = spacePub;
        }

        public async UniTask<MapNode> ChooseAsync(MapState map, IReadOnlyList<MapNode> available, CancellationToken ct = default)
        {
            var availableIds = new HashSet<string>();
            foreach (MapNode node in available) availableIds.Add(node.Id);

            _view.Show(BuildVisuals(map, availableIds), BuildEdges(map)); // слой карты сам заведёт свою камеру
            _spacePub?.Publish(new WorldMapSpaceChangedEvent(true));

            var tcs = new UniTaskCompletionSource<string>();
            void OnClicked(string id) { if (availableIds.Contains(id)) tcs.TrySetResult(id); }
            _view.NodeClicked += OnClicked;

            try
            {
                // AttachExternalCancellation: отмена забега («В меню») размотает ожидание исключением — а finally
                // ниже гарантированно снимет слой карты и Sheet. Без этого карта осталась бы висеть в мире (QA #37).
                string chosenId = await tcs.Task.AttachExternalCancellation(ct);
                foreach (MapNode node in available)
                    if (node.Id == chosenId) return node;
                return null;
            }
            finally
            {
                _view.NodeClicked -= OnClicked;
                _view.Hide();
                _spacePub?.Publish(new WorldMapSpaceChangedEvent(false));
            }
        }

        // Граф → визуальные данные. Позиции ЛОКАЛЬНЫЕ: в мир их переводит сам слой карты своим трансформом
        // (то есть «где живёт карта» задаётся положением объекта в сцене, а не числом здесь).
        private static List<MapNodeVisual> BuildVisuals(MapState map, HashSet<string> availableIds)
        {
            var list = new List<MapNodeVisual>(map.Nodes.Length);
            foreach (MapNode node in map.Nodes)
            {
                var pos = new Vector2(node.UiPosition.x * StepX, node.UiPosition.y * StepY);
                list.Add(new MapNodeVisual(node.Id, pos, StateOf(node, map, availableIds), ColorOf(node.Type)));
            }
            return list;
        }

        private static List<(string From, string To)> BuildEdges(MapState map)
        {
            var edges = new List<(string, string)>();
            foreach (MapNode node in map.Nodes)
            {
                if (node.Edges == null) continue;
                foreach (string to in node.Edges) edges.Add((node.Id, to));
            }
            return edges;
        }

        private static MapNodeVisualState StateOf(MapNode node, MapState map, HashSet<string> availableIds)
        {
            if (node.Id == map.CurrentNodeId)   return MapNodeVisualState.Current;
            if (availableIds.Contains(node.Id)) return MapNodeVisualState.Available;
            return node.Cleared ? MapNodeVisualState.Cleared : MapNodeVisualState.Locked;
        }

        // Цвет по типу узла — временная читаемость D1 (скелет). Иконки по типам придут вместе с артом.
        private static Color ColorOf(MapNodeType type) => type switch
        {
            MapNodeType.Start     => new Color(0.75f, 0.75f, 0.75f),
            MapNodeType.Battle    => new Color(0.85f, 0.35f, 0.30f),
            MapNodeType.Elite     => new Color(0.70f, 0.20f, 0.55f),
            MapNodeType.TextEvent => new Color(0.35f, 0.65f, 0.90f),
            MapNodeType.Shop      => new Color(0.95f, 0.80f, 0.30f),
            MapNodeType.Chest     => new Color(0.45f, 0.85f, 0.55f),
            MapNodeType.Boss      => new Color(0.95f, 0.25f, 0.15f),
            _                     => new Color(0.55f, 0.55f, 0.55f),
        };
    }
}
