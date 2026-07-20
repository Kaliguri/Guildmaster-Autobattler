using System;
using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Input;
using Shapes;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// World-слой карты акта (фаза D): узлы и рёбра рисуются в мире, клик берётся мировым пикингом.
    /// Живёт постоянно в persist-мире и включается/гасится по состоянию — шаблон <c>DeploymentController</c>,
    /// а не спавн-на-время (спавн требовал бы сноса по ct и плодил висячие объекты при отмене забега, QA #37).
    /// <para>D1 — скелет: узлы кругами, рёбра сплошными линиями. Пунктир, фон, «фишка» и VFX ложатся сверху
    /// следующими шагами, уже поверх играбельного.</para>
    /// </summary>
    public sealed class WorldMapView : MonoBehaviour, IWorldMapView
    {
        [Header("Раскладка")]
        [Tooltip("Радиус кружка узла (мировые единицы).")]
        [SerializeField] private float _nodeRadius = 0.5f;
        [Tooltip("Множитель зоны хвата относительно видимого радиуса: попасть по узлу должно быть легче, " +
                 "чем выглядит (QA #24 — та же логика, что с хваталкой юнитов).")]
        [SerializeField] private float _pickRadiusScale = 1.4f;
        [Tooltip("Толщина линии ребра.")]
        [SerializeField] private float _edgeThickness = 0.08f;
        [Tooltip("Поле вокруг узлов при расчёте границ карты (в них камера клампится).")]
        [SerializeField] private float _boundsPadding = 2f;

        [Header("Сортировка")]
        [Tooltip("Слой сортировки для фигур карты. Shapes по умолчанию рисуются на Default (самый нижний) — " +
                 "если под картой появится спрайт-фон, он перекроет узлы; тогда выставить слой выше фона.")]
        [SerializeField] private string _sortingLayerName = "Default";

        // Состояния рисуем прозрачностью поверх цвета типа: пройденное тускнеет, доступное горит.
        private const float AlphaLocked    = 0.35f;
        private const float AlphaCleared   = 0.5f;
        private const float AlphaAvailable = 1f;
        private const float AlphaCurrent   = 1f;
        private const float EdgeAlpha      = 0.4f;
        private const float NodeZ          = 0f;

        private IInputService _input;
        private CameraModeController _cameraModes; // null в headless
        private WorldMapViewLink _link; // мост к петле забега (она живёт в корневом скоупе, выше мирового)

        private readonly List<Disc> _nodeDiscs = new List<Disc>(24);
        private readonly List<Line> _edgeLines = new List<Line>(48);
        // Кликабельные узлы текущего показа: позиция + id. Только Available — по прочим клик игнорируется.
        private readonly List<(string Id, Vector2 Pos)> _pickable = new List<(string, Vector2)>(8);

        private Transform _nodeRoot;
        private Transform _edgeRoot;
        private bool _shown;
        private int _sortingLayerId;
        private bool _layerResolved;

        /// <inheritdoc/>
        public event Action<string> NodeClicked;

        /// <inheritdoc/>
        public Rect2D Bounds { get; private set; }

        // Камеру берём здесь, а не в петле выбора узла: слой карты живёт в том же скоупе, что камера-риг,
        // и сам отвечает за свой кадр. Петле (Game) тогда хватает одного контракта IWorldMapView.
        [Inject]
        public void Construct(IInputService input, CameraModeController cameraModes, WorldMapViewLink link)
        {
            _input       = input;
            _cameraModes = cameraModes;
            _link        = link;
        }

        private void Awake()
        {
            _nodeRoot = new GameObject("Nodes").transform;
            _nodeRoot.SetParent(transform, false);
            _edgeRoot = new GameObject("Edges").transform;
            _edgeRoot.SetParent(transform, false);
            SetLayerActive(false);
        }

        // Подписку держим в Start/OnDestroy, а не OnEnable: инъекция VContainer приходит во время Build,
        // и OnEnable успел бы отработать с _input == null (та же причина, что в CameraModeController).
        private void Start()
        {
            if (_input != null) _input.PointerPressed += OnPointerPressed;
            _link?.Bind(this);
        }

        private void OnDestroy()
        {
            if (_input != null) _input.PointerPressed -= OnPointerPressed;
            _link?.Unbind(this);
        }

        /// <inheritdoc/>
        public void Show(IReadOnlyList<MapNodeVisual> nodes, IReadOnlyList<(string From, string To)> edges)
        {
            ReleaseAll();
            if (nodes == null || nodes.Count == 0) { Bounds = new Rect2D(Vector2.zero, Vector2.zero); return; }

            var byId = new Dictionary<string, Vector2>(nodes.Count);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < nodes.Count; i++)
            {
                MapNodeVisual n = nodes[i];
                // Позиции приходят ЛОКАЛЬНЫЕ (раскладка сетки), в мир переводим трансформом слоя: то есть
                // «где в мире живёт карта» задаётся положением этого объекта в сцене. Так зона карты
                // разнесена от боевой арены наглядно — видно прямо в Scene view, без магии в коде.
                Vector2 pos = transform.TransformPoint(n.Position);
                byId[n.Id] = pos;
                if (n.State == MapNodeVisualState.Available) _pickable.Add((n.Id, pos));

                Disc disc = RentDisc();
                disc.transform.position = new Vector3(pos.x, pos.y, NodeZ);
                disc.Radius = _nodeRadius;
                // Текущий узел — кольцом (где стоим), прочие — заливкой.
                disc.Type   = n.State == MapNodeVisualState.Current ? DiscType.Ring : DiscType.Disc;
                disc.Thickness = _nodeRadius * 0.3f;
                disc.Color  = WithAlpha(n.Color, AlphaFor(n.State));

                if (pos.x < minX) minX = pos.x;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.y < minY) minY = pos.y;
                if (pos.y > maxY) maxY = pos.y;
            }

            if (edges != null)
            {
                for (int i = 0; i < edges.Count; i++)
                {
                    if (!byId.TryGetValue(edges[i].From, out Vector2 a)) continue;
                    if (!byId.TryGetValue(edges[i].To,   out Vector2 b)) continue;

                    // ГОТЧА Shapes: Start/End у Line — ЛОКАЛЬНЫЕ координаты объекта. Слой карты смещён в свою
                    // зону мира, поэтому мировые точки сюда класть нельзя (линии уезжают на величину смещения).
                    // Ставим объект линии в первую точку, а концы задаём относительно неё.
                    Line line = RentLine();
                    line.transform.position = new Vector3(a.x, a.y, NodeZ);
                    line.Start = Vector3.zero;
                    line.End   = new Vector3(b.x - a.x, b.y - a.y, 0f);
                    line.Thickness = _edgeThickness;
                    line.Color = new Color(1f, 1f, 1f, EdgeAlpha);
                }
            }

            var center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            var size   = new Vector2(maxX - minX + _boundsPadding * 2f, maxY - minY + _boundsPadding * 2f);
            Bounds = new Rect2D(center, size);

            _shown = true;
            SetLayerActive(true);
            // Кадр и границы клампа — по фактическому разбросу узлов: боевая зона арены к карте
            // отношения не имеет (карта разнесена в мире и в арену не влезает).
            _cameraModes?.EnterMap(Bounds);
        }

        /// <inheritdoc/>
        public void Hide()
        {
            _shown = false;
            _pickable.Clear();
            SetLayerActive(false);
        }

        // Клик по узлу: мировой пикинг, а не UITK. Клик, попавший в UI (топбар/кнопки поверх карты),
        // до мира не доходит — иначе нажатие на кнопку заодно выбирало бы узел под ней.
        private void OnPointerPressed()
        {
            if (!_shown || _pickable.Count == 0) return;
            if (_input == null || _input.PointerOverUI) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 screen = _input.PointerScreenPosition;
            Vector2 world  = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));

            float pickRadius = _nodeRadius * _pickRadiusScale;
            float bestSqr = pickRadius * pickRadius;
            string bestId = null;

            for (int i = 0; i < _pickable.Count; i++)
            {
                float sqr = (_pickable[i].Pos - world).sqrMagnitude;
                if (sqr <= bestSqr) { bestSqr = sqr; bestId = _pickable[i].Id; }
            }

            if (bestId != null) NodeClicked?.Invoke(bestId);
        }

        private static float AlphaFor(MapNodeVisualState state) => state switch
        {
            MapNodeVisualState.Available => AlphaAvailable,
            MapNodeVisualState.Current   => AlphaCurrent,
            MapNodeVisualState.Cleared   => AlphaCleared,
            _                            => AlphaLocked,
        };

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private void SetLayerActive(bool active)
        {
            if (_nodeRoot != null) _nodeRoot.gameObject.SetActive(active);
            if (_edgeRoot != null) _edgeRoot.gameObject.SetActive(active);
        }

        // Фигуры пулятся: за акт карта перерисовывается на каждом узле, а Shapes допускает лишь ОДИН
        // ShapeRenderer на GameObject — поэтому пулы раздельные по форме (как в CombatAreaFlash).
        private void ReleaseAll()
        {
            _pickable.Clear();
            for (int i = 0; i < _nodeDiscs.Count; i++) _nodeDiscs[i].gameObject.SetActive(false);
            for (int i = 0; i < _edgeLines.Count; i++) _edgeLines[i].gameObject.SetActive(false);
            _rentedDiscs = 0;
            _rentedLines = 0;
        }

        private int _rentedDiscs;
        private int _rentedLines;

        private Disc RentDisc()
        {
            if (_rentedDiscs < _nodeDiscs.Count)
            {
                Disc reused = _nodeDiscs[_rentedDiscs++];
                reused.gameObject.SetActive(true);
                return reused;
            }

            var go = new GameObject("MapNode");
            go.transform.SetParent(_nodeRoot, false);
            var disc = go.AddComponent<Disc>();
            disc.Geometry       = DiscGeometry.Flat2D;
            disc.SortingLayerID = SortingLayerId();
            disc.SortingOrder   = 1; // узлы поверх рёбер
            _nodeDiscs.Add(disc);
            _rentedDiscs++;
            return disc;
        }

        private Line RentLine()
        {
            if (_rentedLines < _edgeLines.Count)
            {
                Line reused = _edgeLines[_rentedLines++];
                reused.gameObject.SetActive(true);
                return reused;
            }

            var go = new GameObject("MapEdge");
            go.transform.SetParent(_edgeRoot, false);
            var line = go.AddComponent<Line>();
            line.Geometry       = LineGeometry.Flat2D;
            line.ThicknessSpace = ThicknessSpace.Meters;
            line.SortingLayerID = SortingLayerId();
            line.SortingOrder   = 0;
            _edgeLines.Add(line);
            _rentedLines++;
            return line;
        }

        // Резолвим лениво: SortingLayer.NameToID нельзя звать из инициализатора поля MonoBehaviour.
        private int SortingLayerId()
        {
            if (!_layerResolved)
            {
                _sortingLayerId = SortingLayer.NameToID(_sortingLayerName);
                if (_sortingLayerId == 0 && _sortingLayerName != "Default")
                    Debug.LogWarning($"[WorldMapView] Слой сортировки «{_sortingLayerName}» не найден — рисуем на Default.");
                _layerResolved = true;
            }
            return _sortingLayerId;
        }
    }
}
