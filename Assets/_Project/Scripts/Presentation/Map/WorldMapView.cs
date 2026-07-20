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
    /// World-слой карты акта: узлы и пути рисуются в мире, клик берётся мировым пикингом.
    /// Живёт постоянно в persist-мире и включается/гасится по состоянию — шаблон <c>DeploymentController</c>,
    /// а не спавн-на-время (спавн требовал бы сноса по ct и плодил висячие объекты при отмене забега, QA #37).
    /// </summary>
    public sealed class WorldMapView : MonoBehaviour, IWorldMapView
    {
        [Header("Раскладка карты")]
        [Tooltip("Шаги сетки, разброс и правила дистанции. Разброс выводится из сида забега — в данных карты его нет.")]
        [SerializeField] private MapLayout _layout = MapLayout.Default;

        [Tooltip("Сколько этажей показывать в кадре при входе на карту. Камера встаёт крупно у текущего узла.")]
        [SerializeField] private float _floorsInView = 4f;

        [Header("Вид узлов")]
        [Tooltip("Набор иконок по типам узлов. Пусто — узлы рисуются одними подложками.")]
        [SerializeField] private MapIconSet _icons;

        [Tooltip("Радиус подложки узла (мировые единицы).")]
        [SerializeField] private float _nodeRadius = 0.6f;

        [Tooltip("Множитель зоны хвата относительно видимого радиуса: попасть по узлу должно быть легче, " +
                 "чем выглядит (QA #24 — та же логика, что с хваталкой юнитов).")]
        [SerializeField] private float _pickRadiusScale = 1.4f;

        [Header("Пути")]
        [Tooltip("Толщина линии пути.")]
        [SerializeField] private float _edgeThickness = 0.09f;
        [Tooltip("Длина штриха и промежутка пунктира (мировые единицы).")]
        [SerializeField] private float _dashSize = 0.35f;
        [SerializeField] private float _dashSpacing = 0.35f;
        [Tooltip("Изгиб пути в долях его длины: 0 = прямые линии.")]
        [SerializeField] private float _edgeCurve = 0.12f;
        [Tooltip("На сколько отрезков дробится кривая. Больше — глаже, но больше объектов.")]
        [SerializeField] private int _edgeSegments = 6;

        [Header("Отклик")]
        [Tooltip("Насколько подрастает узел под курсором.")]
        [SerializeField] private float _hoverScale = 1.18f;
        [Tooltip("Насколько вдавливается узел при нажатии.")]
        [SerializeField] private float _pressScale = 0.9f;
        [Tooltip("Амплитуда пульса доступных узлов.")]
        [SerializeField] private float _pulseAmount = 0.06f;
        [Tooltip("Скорость пульса доступных узлов.")]
        [SerializeField] private float _pulseSpeed = 2.2f;

        [Header("Фишка игрока")]
        [Tooltip("Спрайт фишки. Пусто — фишка рисуется кружком.")]
        [SerializeField] private Sprite _pawnSprite;
        [SerializeField] private Color _pawnColor = new Color(1f, 0.92f, 0.6f);
        [SerializeField] private float _pawnHeight = 0.9f;
        [Tooltip("Сколько едет фишка между узлами (секунды).")]
        [SerializeField] private float _pawnTravelSeconds = 1.5f;
        [Tooltip("Во сколько раз ускоряется поездка по повторному клику (дабл-клик).")]
        [SerializeField] private float _pawnSkipSpeed = 6f;

        [Tooltip("Насколько фишка приподнята над узлом. Иначе она садится ровно на иконку и закрывает её — " +
                 "не видно, что это за узел, на котором стоит отряд.")]
        [SerializeField] private float _pawnLift = 0.85f;

        [Header("Сортировка")]
        [Tooltip("Слой сортировки для фигур карты. Shapes по умолчанию рисуются на Default (самый нижний) — " +
                 "если под картой появится спрайт-фон, он перекроет узлы; тогда выставить слой выше фона.")]
        [SerializeField] private string _sortingLayerName = "Default";

        // Состояния рисуем прозрачностью поверх цвета типа: пройденное тускнеет, доступное горит.
        private const float AlphaLocked    = 0.35f;
        private const float AlphaCleared   = 0.5f;
        private const float AlphaAvailable = 1f;
        private const float AlphaCurrent   = 1f;
        private const float BackingAlpha   = 0.75f;

        // Глубина вместо слоёв сортировки: Shapes и SpriteRenderer — разные системы рисования, и порядок
        // между ними слоями надёжно не задаётся (иконки уходили ПОД подложки). По Z порядок однозначен.
        private const float EdgeZ = 0.2f;
        private const float NodeZ = 0f;
        private const float IconZ = -0.1f;
        private const float PawnZ = -0.2f;

        private IInputService _input;
        private CameraModeController _cameraModes; // null в headless
        private WorldMapViewLink _link; // мост к петле забега (она живёт в корневом скоупе, выше мирового)

        private readonly List<Disc> _nodeDiscs = new List<Disc>(24);
        private readonly List<SpriteRenderer> _nodeIcons = new List<SpriteRenderer>(24);
        private readonly List<Line> _edgeLines = new List<Line>(96);

        // Узлы текущего показа: всё, по чему можно попасть мышью. Выбор проходит только для Available.
        private struct NodeHit
        {
            public string Id;
            public Vector2 Pos;
            public Transform Disc;
            public Transform Icon;
            public bool Selectable;
        }
        private readonly List<NodeHit> _hits = new List<NodeHit>(48);

        private Transform _nodeRoot;
        private Transform _edgeRoot;
        private SpriteRenderer _pawn;
        private Disc _pawnDisc;

        private bool _shown;
        private int _sortingLayerId;
        private bool _layerResolved;

        private int _hoverIndex = -1;
        private bool _pressed;
        private float _nudgeUntil;
        private int _nudgeIndex = -1;

        // Поездка фишки: пока едет, выбор заблокирован, а событие выбора ждёт приезда.
        private bool _travelling;
        private float _travelT;
        private float _travelSpeedScale = 1f;
        private Vector2 _travelFrom, _travelCtrl, _travelTo;
        private string _travelNodeId;

        /// <inheritdoc/>
        public event Action<string> NodeClicked;

        /// <inheritdoc/>
        public Rect2D Bounds { get; private set; }

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
            if (_input != null)
            {
                _input.PointerPressed  += OnPointerPressed;
                _input.PointerReleased += OnPointerReleased;
            }
            _link?.Bind(this);
        }

        private void OnDestroy()
        {
            if (_input != null)
            {
                _input.PointerPressed  -= OnPointerPressed;
                _input.PointerReleased -= OnPointerReleased;
            }
            _link?.Unbind(this);
        }

        /// <inheritdoc/>
        public void Show(IReadOnlyList<MapNodeVisual> nodes, IReadOnlyList<(string From, string To)> edges, long seed)
        {
            ReleaseAll();
            if (nodes == null || nodes.Count == 0) { Bounds = new Rect2D(Vector2.zero, Vector2.zero); return; }

            // Раскладка — здесь: домен отдаёт только топологию (этаж/ряд), координаты не его забота.
            Dictionary<string, Vector2> local = _layout.Resolve(nodes, seed);

            var byId = new Dictionary<string, Vector2>(nodes.Count);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            Vector2 focus = Vector2.zero;
            bool hasFocus = false;

            for (int i = 0; i < nodes.Count; i++)
            {
                MapNodeVisual n = nodes[i];
                // Раскладка локальна, в мир переводим трансформом слоя: «где в мире живёт карта» задаётся
                // положением этого объекта в сцене — зона карты разнесена от арены наглядно, видно в Scene view.
                Vector2 pos = transform.TransformPoint(local[n.Id]);
                byId[n.Id] = pos;

                MapNodeIcon look = _icons != null ? _icons.Resolve(n.Kind) : default;
                float alpha = AlphaFor(n.State);

                Disc disc = RentDisc();
                disc.transform.position = new Vector3(pos.x, pos.y, NodeZ);
                disc.transform.localScale = Vector3.one;
                disc.Radius = _nodeRadius;
                disc.Type   = n.State == MapNodeVisualState.Current ? DiscType.Ring : DiscType.Disc;
                disc.Thickness = _nodeRadius * 0.3f;
                disc.Color  = WithAlpha(look.Backing, alpha * BackingAlpha);

                Transform iconTf = null;
                if (look.Icon != null)
                {
                    SpriteRenderer icon = RentIcon();
                    icon.transform.position = new Vector3(pos.x, pos.y, IconZ);
                    icon.sprite = look.Icon;
                    icon.color  = new Color(1f, 1f, 1f, alpha);
                    // Масштаб считаем от нужной мировой высоты: спрайты набора идут с разным PPU,
                    // и доверять импорту нельзя (32-й набор импортирован с PPU 8).
                    float h = look.Icon.bounds.size.y;
                    icon.transform.localScale = Vector3.one * (h > 0f ? _icons.WorldHeight / h : 1f);
                    iconTf = icon.transform;
                }

                _hits.Add(new NodeHit
                {
                    Id = n.Id, Pos = pos, Disc = disc.transform, Icon = iconTf,
                    Selectable = n.State == MapNodeVisualState.Available,
                });

                if (n.State == MapNodeVisualState.Current) { focus = pos; hasFocus = true; }

                if (pos.x < minX) minX = pos.x;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.y < minY) minY = pos.y;
                if (pos.y > maxY) maxY = pos.y;
            }

            if (edges != null) BuildEdges(edges, byId, nodes);
            PlacePawn(hasFocus ? focus : new Vector2(minX, (minY + maxY) * 0.5f));

            const float padding = 2f;
            var center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            var size   = new Vector2(maxX - minX + padding * 2f, maxY - minY + padding * 2f);
            Bounds = new Rect2D(center, size);

            _shown = true;
            SetLayerActive(true);

            // Кадр и границы клампа: смотрим КРУПНО на текущий узел, а не на весь акт сразу.
            _cameraModes?.EnterMap(Bounds, hasFocus ? focus : center, _floorsInView * _layout.StepX);
        }

        /// <inheritdoc/>
        public void Hide()
        {
            _shown = false;
            _travelling = false;
            _hits.Clear();
            SetLayerActive(false);
            _cameraModes?.ExitMap(); // вернуть взгляд туда, откуда пришли (карту могли открыть посреди боя)
        }

        // Пути: кривая вместо прямой (карта, а не схема) + пунктир. Пунктир умеет только Line, поэтому кривая
        // собирается из отрезков, а фаза пунктира продолжается вдоль всей кривой — иначе на стыках был бы сбой.
        private void BuildEdges(IReadOnlyList<(string From, string To)> edges,
                                Dictionary<string, Vector2> byId,
                                IReadOnlyList<MapNodeVisual> nodes)
        {
            var stateOf = new Dictionary<string, MapNodeVisualState>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++) stateOf[nodes[i].Id] = nodes[i].State;

            int segments = Mathf.Max(1, _edgeSegments);
            for (int i = 0; i < edges.Count; i++)
            {
                if (!byId.TryGetValue(edges[i].From, out Vector2 a)) continue;
                if (!byId.TryGetValue(edges[i].To,   out Vector2 b)) continue;

                // Путь из текущего узла — яркий (сюда можно шагнуть), пройденный — тусклый, прочие — средние.
                stateOf.TryGetValue(edges[i].From, out MapNodeVisualState from);
                stateOf.TryGetValue(edges[i].To,   out MapNodeVisualState to);
                float alpha = from == MapNodeVisualState.Current && to == MapNodeVisualState.Available ? 0.9f
                            : from == MapNodeVisualState.Cleared ? 0.22f
                            : 0.4f;

                // Контрольная точка кривой — перпендикулярно хорде, сторона и величина детерминированы парой id.
                Vector2 mid = (a + b) * 0.5f;
                Vector2 dir = b - a;
                var perp = new Vector2(-dir.y, dir.x).normalized;
                float bend = _edgeCurve * dir.magnitude * CurveSign(edges[i].From, edges[i].To);
                Vector2 ctrl = mid + perp * bend;

                float dashOffset = 0f;
                Vector2 prev = a;
                for (int s = 1; s <= segments; s++)
                {
                    Vector2 next = Bezier(a, ctrl, b, s / (float)segments);

                    Line line = RentLine();
                    line.transform.position = new Vector3(prev.x, prev.y, EdgeZ);
                    line.Start = Vector3.zero;
                    line.End   = new Vector3(next.x - prev.x, next.y - prev.y, 0f);
                    line.Thickness = _edgeThickness;
                    line.Color = new Color(1f, 1f, 1f, alpha);
                    line.Dashed = true;
                    line.DashSpace = DashSpace.Meters;
                    line.DashSize = _dashSize;
                    line.DashSpacing = _dashSpacing;
                    // ГОТЧА: снаппинг подгоняет пунктир под концы ОТРЕЗКА, а кривая нарезана на короткие
                    // сегменты — штрих растягивался на весь сегмент, и путь выходил сплошным. Выключаем,
                    // тогда пунктир идёт по метрам, а накопительный offset держит фазу вдоль всей кривой.
                    line.DashSnap = DashSnapping.Off;
                    line.DashOffset = dashOffset;

                    dashOffset += (next - prev).magnitude;
                    prev = next;
                }
            }
        }

        private static Vector2 Bezier(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        // Сторона изгиба — от пары id: путь всегда гнётся одинаково, но соседние пути не параллельны.
        private static float CurveSign(string from, string to)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < from.Length; i++) { h ^= from[i]; h *= 16777619u; }
                for (int i = 0; i < to.Length; i++)   { h ^= to[i];   h *= 16777619u; }
                return ((h & 1u) == 0u ? 1f : -1f) * (0.6f + (h >> 8 & 0xFF) / 255f * 0.8f);
            }
        }

        private void PlacePawn(Vector2 pos)
        {
            EnsurePawn();
            _pawnAt = pos;
            var p = new Vector3(pos.x, pos.y + _pawnLift, PawnZ);
            if (_pawn != null) _pawn.transform.position = p;
            if (_pawnDisc != null) _pawnDisc.transform.position = p;
        }

        private void EnsurePawn()
        {
            if (_pawn != null || _pawnDisc != null) return;

            var go = new GameObject("Pawn");
            go.transform.SetParent(transform, false);
            if (_pawnSprite != null)
            {
                _pawn = go.AddComponent<SpriteRenderer>();
                _pawn.sprite = _pawnSprite;
                _pawn.color  = _pawnColor;
                _pawn.sortingLayerID = SortingLayerId();
                _pawn.sortingOrder   = 5;
                float h = _pawnSprite.bounds.size.y;
                go.transform.localScale = Vector3.one * (h > 0f ? _pawnHeight / h : 1f);
            }
            else
            {
                // Спрайта фишки нет — рисуем кружком, чтобы «где я» читалось и без арта.
                _pawnDisc = go.AddComponent<Disc>();
                _pawnDisc.Geometry = DiscGeometry.Flat2D;
                _pawnDisc.Type     = DiscType.Disc;
                _pawnDisc.Radius   = _pawnHeight * 0.35f;
                _pawnDisc.Color    = _pawnColor;
                _pawnDisc.SortingLayerID = SortingLayerId();
                _pawnDisc.SortingOrder   = 5;
            }
        }

        // Клик по узлу: мировой пикинг, а не UITK. Клик, попавший в UI (топбар/кнопки поверх карты),
        // до мира не доходит — иначе нажатие на кнопку заодно выбирало бы узел под ней.
        private void OnPointerPressed()
        {
            if (!_shown) return;

            // Пока фишка едет, повторный клик = «пропустить»: ускоряем поездку, а не выбираем заново.
            if (_travelling) { _travelSpeedScale = _pawnSkipSpeed; return; }

            if (_input == null || _input.PointerOverUI) return;

            int hit = HitTest();
            if (hit < 0) return;

            _pressed = true;
            if (_hits[hit].Selectable) StartTravel(hit);
            else { _nudgeIndex = hit; _nudgeUntil = Time.unscaledTime + NudgeDuration; }
        }

        private void OnPointerReleased() => _pressed = false;

        private void StartTravel(int hit)
        {
            _travelFrom = PawnPosition();
            _travelTo   = _hits[hit].Pos;
            Vector2 dir = _travelTo - _travelFrom;
            var perp = new Vector2(-dir.y, dir.x).normalized;
            _travelCtrl = (_travelFrom + _travelTo) * 0.5f + perp * (_edgeCurve * dir.magnitude);

            _travelNodeId     = _hits[hit].Id;
            _travelT          = 0f;
            _travelSpeedScale = 1f;
            _travelling       = true;
        }

        // Логическая позиция фишки — узел, на котором она стоит (БЕЗ подъёма): подъём чисто визуальный,
        // и подмешивать его в маршрут нельзя, иначе поездка стартует на подъём выше цели.
        private Vector2 _pawnAt;

        private Vector2 PawnPosition() => _pawnAt;

        private int HitTest()
        {
            Camera cam = Camera.main;
            if (cam == null || _input == null) return -1;

            Vector3 screen = _input.PointerScreenPosition;
            Vector2 world  = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));

            float pickRadius = _nodeRadius * _pickRadiusScale;
            float bestSqr = pickRadius * pickRadius;
            int best = -1;
            for (int i = 0; i < _hits.Count; i++)
            {
                float sqr = (_hits[i].Pos - world).sqrMagnitude;
                if (sqr <= bestSqr) { bestSqr = sqr; best = i; }
            }
            return best;
        }

        private const float NudgeDuration = 0.28f;

        // Отклик и поездка фишки. Всё на unscaled — карта живёт и на паузе боя.
        private void Update()
        {
            if (!_shown) return;

            _hoverIndex = (_travelling || _input == null || _input.PointerOverUI) ? -1 : HitTest();
            float now = Time.unscaledTime;

            for (int i = 0; i < _hits.Count; i++)
            {
                float scale = 1f;

                if (_hits[i].Selectable)
                    scale += Mathf.Sin(now * _pulseSpeed) * _pulseAmount; // доступные тихо дышат

                if (i == _hoverIndex) scale *= _pressed ? _pressScale : _hoverScale;

                if (i == _nudgeIndex)
                {
                    float left = _nudgeUntil - now;
                    if (left > 0f)
                    {
                        float t = left / NudgeDuration;
                        scale *= 1f + Mathf.Sin(t * Mathf.PI * 3f) * t * 0.18f; // отказной отклик
                    }
                    else _nudgeIndex = -1;
                }

                if (_hits[i].Disc != null) _hits[i].Disc.localScale = Vector3.one * scale;
                if (_hits[i].Icon != null)
                {
                    // У иконки свой базовый масштаб (от PPU спрайта) — множим, а не перетираем.
                    Vector3 baseScale = _hits[i].Icon.localScale;
                    float baseUniform = _iconBaseScale.TryGetValue(_hits[i].Icon, out float b) ? b : baseScale.x;
                    _iconBaseScale[_hits[i].Icon] = baseUniform;
                    _hits[i].Icon.localScale = Vector3.one * (baseUniform * scale);
                }
            }

            if (_travelling) TickTravel();
        }

        private readonly Dictionary<Transform, float> _iconBaseScale = new Dictionary<Transform, float>(48);

        private void TickTravel()
        {
            float dur = Mathf.Max(0.01f, _pawnTravelSeconds);
            _travelT += Time.unscaledDeltaTime / dur * _travelSpeedScale;

            if (_travelT >= 1f)
            {
                PlacePawn(_travelTo);
                _travelling = false;
                string id = _travelNodeId;
                _travelNodeId = null;
                NodeClicked?.Invoke(id); // выбор засчитывается ПОСЛЕ приезда
                return;
            }

            // Плавный разгон-торможение: линейный ход читается как рывок.
            float t = Mathf.SmoothStep(0f, 1f, _travelT);
            PlacePawn(Bezier(_travelFrom, _travelCtrl, _travelTo, t));
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
            if (_pawn != null) _pawn.gameObject.SetActive(active);
            if (_pawnDisc != null) _pawnDisc.gameObject.SetActive(active);
        }

        // Фигуры пулятся: за акт карта перерисовывается на каждом узле, а Shapes допускает лишь ОДИН
        // ShapeRenderer на GameObject — поэтому пулы раздельные по форме (как в CombatAreaFlash).
        private void ReleaseAll()
        {
            _hits.Clear();
            _hoverIndex = -1;
            _nudgeIndex = -1;
            _iconBaseScale.Clear();

            for (int i = 0; i < _nodeDiscs.Count; i++)
            {
                _nodeDiscs[i].transform.localScale = Vector3.one;
                _nodeDiscs[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < _nodeIcons.Count; i++) _nodeIcons[i].gameObject.SetActive(false);
            for (int i = 0; i < _edgeLines.Count; i++) _edgeLines[i].gameObject.SetActive(false);
            _rentedDiscs = 0;
            _rentedIcons = 0;
            _rentedLines = 0;
        }

        private int _rentedDiscs;
        private int _rentedIcons;
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
            disc.SortingOrder   = 1;
            _nodeDiscs.Add(disc);
            _rentedDiscs++;
            return disc;
        }

        private SpriteRenderer RentIcon()
        {
            if (_rentedIcons < _nodeIcons.Count)
            {
                SpriteRenderer reused = _nodeIcons[_rentedIcons++];
                reused.gameObject.SetActive(true);
                return reused;
            }

            var go = new GameObject("MapNodeIcon");
            go.transform.SetParent(_nodeRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = SortingLayerId();
            sr.sortingOrder   = 3;
            _nodeIcons.Add(sr);
            _rentedIcons++;
            return sr;
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
