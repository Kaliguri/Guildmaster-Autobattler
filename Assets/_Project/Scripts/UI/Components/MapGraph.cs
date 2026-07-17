using System;
using System.Collections.Generic;
using Guildmaster.Guild;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Холст графа карты акта (план [[act-map-run-loop]] §3.2, A3): раскладывает узлы абсолютно по
    /// <see cref="MapNode.UiPosition"/> (слева→направо) и рисует рёбра через <c>painter2D</c>. Доступные узлы
    /// подсвечены и кликабельны, рёбра из текущего узла к доступным — акцентным цветом. Цвета рёбер берутся из
    /// USS-кастом-свойств (<c>--gm-map-edge</c>/<c>--gm-map-edge-active</c>) — токены остаются источником правды.
    /// <para>Создаётся кодом из <c>MapScreenView</c> (в UXML не размещается), потому не <c>[UxmlElement]</c>.</para>
    /// </summary>
    public sealed class MapGraph : VisualElement
    {
        private static readonly CustomStyleProperty<Color> EdgeProp       = new("--gm-map-edge");
        private static readonly CustomStyleProperty<Color> EdgeActiveProp = new("--gm-map-edge-active");

        private MapState _map;
        private ISet<string> _available = new HashSet<string>();
        private readonly Dictionary<string, VisualElement> _nodeEls = new();

        // Фолбэки на случай, если кастом-свойства ещё не разрешены (ink-300 / brass-400).
        private Color _edge       = new Color(0.29f, 0.23f, 0.15f, 1f);
        private Color _edgeActive = new Color(0.78f, 0.60f, 0.29f, 1f);

        public MapGraph()
        {
            AddToClassList("gm-map__graph");
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            RegisterCallback<GeometryChangedEvent>(_ => LayoutNodes());
        }

        /// <summary>Наполнить граф: узлы, состояния, клики. <paramref name="labelFor"/> даёт подпись узла (лок-строка).</summary>
        public void SetData(MapState map, ISet<string> availableIds, Action<string> onChosen, Func<MapNode, string> labelFor)
        {
            _map       = map;
            _available = availableIds ?? new HashSet<string>();
            Clear();
            _nodeEls.Clear();
            if (map?.Nodes == null) return;

            foreach (MapNode node in map.Nodes)
            {
                var el = new VisualElement();
                el.AddToClassList("gm-map__node");
                if (node.Cleared)                    el.AddToClassList("gm-map__node--cleared");
                if (node.Id == map.CurrentNodeId)    el.AddToClassList("gm-map__node--current");

                var accent = new VisualElement();
                accent.AddToClassList("gm-map__node-accent");
                accent.AddToClassList("gm-map__node-accent--" + TypeSuffix(node.Type));
                el.Add(accent);

                var label = new Label(labelFor?.Invoke(node) ?? node.Type.ToString());
                label.AddToClassList("gm-map__node-label");
                el.Add(label);

                if (_available.Contains(node.Id))
                {
                    el.AddToClassList("gm-map__node--available");
                    string id = node.Id; // копия для замыкания
                    el.RegisterCallback<ClickEvent>(_ => onChosen?.Invoke(id));
                }

                Add(el);
                _nodeEls[node.Id] = el;
            }

            LayoutNodes();
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent e)
        {
            if (e.customStyle.TryGetValue(EdgeProp, out Color edge))             _edge = edge;
            if (e.customStyle.TryGetValue(EdgeActiveProp, out Color edgeActive)) _edgeActive = edgeActive;
            MarkDirtyRepaint();
        }

        // Абсолютная раскладка: X — по колонке (UiPosition.x), Y — равномерным разбросом узлов ВНУТРИ колонки
        // по их порядку. Это держит рёбра-лестницу визуально непересекающимися (они монотонны по индексу) и даёт
        // ровный вертикальный ритм независимо от ширины колонок.
        private void LayoutNodes()
        {
            if (_map?.Nodes == null || _nodeEls.Count == 0) return;
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0f || h <= 0f) return;

            // Группировка по колонкам (ключ = округлённый UiPosition.x).
            var byColumn = new Dictionary<int, List<MapNode>>();
            int minCol = int.MaxValue, maxCol = int.MinValue;
            foreach (MapNode node in _map.Nodes)
            {
                int col = Mathf.RoundToInt(node.UiPosition.x);
                if (!byColumn.TryGetValue(col, out var list)) byColumn[col] = list = new List<MapNode>();
                list.Add(node);
                minCol = Mathf.Min(minCol, col); maxCol = Mathf.Max(maxCol, col);
            }
            float spanCol = Mathf.Max(1, maxCol - minCol);

            // Размер узла берём из разрешённого стиля (USS), не хардкодим.
            VisualElement sample = null;
            foreach (var el in _nodeEls.Values) { sample = el; break; }
            float nodeW = sample != null && sample.resolvedStyle.width  > 0f ? sample.resolvedStyle.width  : 132f;
            float nodeH = sample != null && sample.resolvedStyle.height > 0f ? sample.resolvedStyle.height : 92f;

            float padX = nodeW * 0.6f, padY = nodeH * 0.6f;
            float availW = Mathf.Max(1f, w - 2f * padX - nodeW);
            float availH = Mathf.Max(1f, h - 2f * padY - nodeH);

            foreach (var kv in byColumn)
            {
                List<MapNode> column = kv.Value;
                column.Sort((a, b) => a.UiPosition.y.CompareTo(b.UiPosition.y)); // сверху вниз по ряду
                int count = column.Count;
                float nx = (kv.Key - minCol) / spanCol;

                for (int i = 0; i < count; i++)
                {
                    if (!_nodeEls.TryGetValue(column[i].Id, out var el)) continue;
                    float ny = count == 1 ? 0.5f : (float)i / (count - 1);
                    el.style.left = padX + nx * availW;
                    el.style.top  = padY + ny * availH;
                }
            }

            MarkDirtyRepaint();
        }

        // Рёбра: линия между центрами узлов; из текущего к доступному — акцентным цветом.
        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (_map?.Nodes == null) return;
            var painter = mgc.painter2D;
            painter.lineWidth = 3f;

            foreach (MapNode node in _map.Nodes)
            {
                if (node.Edges == null || !_nodeEls.TryGetValue(node.Id, out var from)) continue;
                bool fromCurrent = node.Id == _map.CurrentNodeId;

                foreach (string edgeId in node.Edges)
                {
                    if (!_nodeEls.TryGetValue(edgeId, out var to)) continue;
                    painter.strokeColor = fromCurrent && _available.Contains(edgeId) ? _edgeActive : _edge;
                    painter.BeginPath();
                    painter.MoveTo(from.layout.center);
                    painter.LineTo(to.layout.center);
                    painter.Stroke();
                }
            }
        }

        private static string TypeSuffix(MapNodeType type) => type switch
        {
            MapNodeType.Start     => "start",
            MapNodeType.Battle    => "battle",
            MapNodeType.Elite     => "elite",
            MapNodeType.Boss      => "boss",
            MapNodeType.Shop      => "shop",
            MapNodeType.Chest     => "chest",
            MapNodeType.TextEvent => "textevent",
            MapNodeType.Unknown   => "unknown",
            _                     => "unknown",
        };
    }
}
