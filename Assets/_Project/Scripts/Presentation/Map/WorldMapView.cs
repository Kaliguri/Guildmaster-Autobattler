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

        [Header("Вид")]
        [Tooltip("Префаб узла — ОДИН на все типы, внутри все иконки. Тип включается кодом.")]
        [SerializeField] private MapNodeView _nodePrefab;

        [Tooltip("Палитра карты: тон, состояния, пути, фишка. Единственный источник цвета.")]
        [SerializeField] private MapPalette _palette;

        [Tooltip("Множитель зоны хвата относительно радиуса из префаба: попасть по узлу должно быть легче, " +
                 "чем выглядит (QA #24 — та же логика, что с хваталкой юнитов).")]
        [SerializeField] private float _pickRadiusScale = 1.4f;

        [Header("Пути")]
        [Tooltip("Радиус точки пути.")]
        [SerializeField] private float _dotRadius = 0.07f;

        [Tooltip("Расстояние между точками пути. Шаг считается по длине дуги, поэтому ритм одинаков " +
                 "на путях любой длины — ровно то, чего не давал пунктир Shapes.")]
        [SerializeField] private float _dotSpacing = 0.32f;

        [Tooltip("Отступ точек от центров узлов, чтобы дорожка не влезала под иконку.")]
        [SerializeField] private float _dotMargin = 0.7f;

        [Tooltip("Изгиб пути в долях его длины. 0 = прямые линии — так и надо: изгиб случайной стороны " +
                 "заставлял соседние пути наезжать друг на друга и читался как каша (play-QA Макса).")]
        [SerializeField] private float _edgeCurve;

        [Header("Отклик и анимация")]
        [Tooltip("Насколько подрастает узел под курсором.")]
        [SerializeField] private float _hoverScale = 1.18f;

        [Tooltip("Насколько вдавливается узел при нажатии.")]
        [SerializeField] private float _pressScale = 0.9f;

        [Tooltip("Глубина дыхания доступных узлов по ЯРКОСТИ (не по размеру: размерный пульс суетлив).")]
        [SerializeField, Range(0f, 0.6f)] private float _availableBreath = 0.22f;

        [Tooltip("Скорость дыхания доступных узлов.")]
        [SerializeField] private float _breathSpeed = 2.2f;

        [Tooltip("Скорость бега точек по пути к доступному узлу (метров в секунду).")]
        [SerializeField] private float _dotFlowSpeed = 2.6f;

        [Tooltip("Длина светящегося участка бегущей волны (метры).")]
        [SerializeField] private float _dotFlowLength = 1.4f;

        [Header("Фишка отряда")]
        [Tooltip("Радиус точки отряда. Фишка — та же точка, что на пути, только крупнее и ярче.")]
        [SerializeField] private float _pawnRadius = 0.2f;

        [Tooltip("Сколько едет фишка между узлами (секунды).")]
        [SerializeField] private float _pawnTravelSeconds = 1.5f;

        [Tooltip("Во сколько раз ускоряется поездка по повторному клику (дабл-клик).")]
        [SerializeField] private float _pawnSkipSpeed = 6f;

        [Header("Сортировка")]
        [Tooltip("Слой сортировки для фигур карты. Shapes по умолчанию рисуются на Default (самый нижний) — " +
                 "если под картой появится спрайт-фон, он перекроет узлы; тогда выставить слой выше фона.")]
        [SerializeField] private string _sortingLayerName = "Default";

        // Глубина вместо слоёв сортировки: Shapes и SpriteRenderer — разные системы рисования, и порядок
        // между ними слоями надёжно не задаётся (иконки уходили ПОД подложки). По Z порядок однозначен.
        private const float EdgeZ = 0.2f;
        private const float NodeZ = 0f;
        private const float PawnZ = -0.3f;

        private IInputService _input;
        private CameraModeController _cameraModes; // null в headless
        private WorldMapViewLink _link; // мост к петле забега (она живёт в корневом скоупе, выше мирового)

        // Узлы текущего показа: всё, по чему можно попасть мышью. Выбор проходит только для Available.
        private struct NodeHit
        {
            public string Id;
            public Vector2 Pos;
            public MapNodeView View;
            public MapNodeVisualState State;
            public float PickRadius;
            public bool Selectable;
        }
        private readonly List<NodeHit> _hits = new List<NodeHit>(48);

        // Точка пути. Хранит путь вдоль дуги, чтобы волна «бежала» к доступному узлу с ровным шагом.
        private struct PathDot
        {
            public Disc Shape;
            public Color Base;
            public float Along;   // расстояние от начала пути
            public bool Flowing;  // путь к доступному узлу — по нему бежит волна
        }
        private readonly List<PathDot> _dots = new List<PathDot>(256);

        private readonly List<MapNodeView> _nodePool = new List<MapNodeView>(48);
        private readonly List<Disc> _dotPool = new List<Disc>(256);

        private Transform _nodeRoot;
        private Transform _edgeRoot;
        private Disc _pawn;

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
        private Vector2 _pawnAt;

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
            _edgeRoot = new GameObject("Paths").transform;
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

                MapNodeView view = RentNode();
                if (view != null)
                {
                    view.transform.position = new Vector3(pos.x, pos.y, NodeZ);
                    view.SetVisualScale(1f);
                    view.ShowKind(n.Kind);
                    view.Apply(n.State, _palette);
                }

                _hits.Add(new NodeHit
                {
                    Id = n.Id, Pos = pos, View = view, State = n.State,
                    PickRadius = view != null ? view.PickRadius : 0f,
                    Selectable = n.State == MapNodeVisualState.Available,
                });

                if (n.State == MapNodeVisualState.Current) { focus = pos; hasFocus = true; }

                if (pos.x < minX) minX = pos.x;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.y < minY) minY = pos.y;
                if (pos.y > maxY) maxY = pos.y;
            }

            if (edges != null) BuildPaths(edges, byId, nodes);
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

        // Пути — ЦЕПОЧКИ ТОЧЕК, а не пунктирные линии. Пунктир Shapes на кривой рвал ритм: штрихи
        // подгонялись под концы каждого сегмента, и на путях разной длины рисунок выходил разным.
        // Точки ставим сами с шагом по длине дуги — ритм одинаков везде, и это же семейство форм,
        // что фишка отряда: дорожка и тот, кто по ней идёт, выглядят как одно целое.
        private void BuildPaths(IReadOnlyList<(string From, string To)> edges,
                                Dictionary<string, Vector2> byId,
                                IReadOnlyList<MapNodeVisual> nodes)
        {
            HashSet<string> travelled = TravelledRoute(nodes);

            var stateOf = new Dictionary<string, MapNodeVisualState>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++) stateOf[nodes[i].Id] = nodes[i].State;

            for (int i = 0; i < edges.Count; i++)
            {
                if (!byId.TryGetValue(edges[i].From, out Vector2 a)) continue;
                if (!byId.TryGetValue(edges[i].To,   out Vector2 b)) continue;

                stateOf.TryGetValue(edges[i].From, out MapNodeVisualState from);
                stateOf.TryGetValue(edges[i].To,   out MapNodeVisualState to);

                bool isTravelled = travelled.Contains(EdgeKey(edges[i].From, edges[i].To));
                bool isOpen      = from == MapNodeVisualState.Current && to == MapNodeVisualState.Available;

                Color color = isTravelled ? _palette.PathTravelled
                            : isOpen      ? _palette.PathAvailable
                            : _palette.PathIdle;

                // Контрольная точка кривой — перпендикулярно хорде, сторона и величина детерминированы парой id.
                Vector2 mid = (a + b) * 0.5f;
                Vector2 dir = b - a;
                var perp = new Vector2(-dir.y, dir.x).normalized;
                Vector2 ctrl = mid + perp * (_edgeCurve * dir.magnitude * CurveSign(edges[i].From, edges[i].To));

                ScatterDots(a, ctrl, b, color, isOpen);
            }
        }

        // Точки ставим по РАВНОЙ длине дуги: идём мелким шагом по параметру, копим пройденное расстояние
        // и роняем точку каждые _dotSpacing метров. Равномерный шаг по t дал бы сгущение на изгибе.
        private void ScatterDots(Vector2 a, Vector2 ctrl, Vector2 b, Color color, bool flowing)
        {
            float chord = (b - a).magnitude;
            if (chord <= _dotMargin * 2f) return;

            const int walk = 64;
            Vector2 prev = a;
            float travelled = 0f;
            float nextDrop = _dotMargin;
            float total = 0f;

            for (int s = 1; s <= walk; s++)
            {
                Vector2 point = Bezier(a, ctrl, b, s / (float)walk);
                float step = (point - prev).magnitude;
                total += step;
                prev = point;
            }
            float stopAt = total - _dotMargin;

            prev = a;
            for (int s = 1; s <= walk; s++)
            {
                Vector2 point = Bezier(a, ctrl, b, s / (float)walk);
                float step = (point - prev).magnitude;

                while (travelled + step >= nextDrop && nextDrop <= stopAt)
                {
                    float t = step > 0f ? (nextDrop - travelled) / step : 0f;
                    Vector2 at = Vector2.Lerp(prev, point, t);

                    Disc dot = RentDot();
                    dot.transform.position = new Vector3(at.x, at.y, EdgeZ);
                    dot.Radius = _dotRadius;
                    dot.Color  = color;
                    _dots.Add(new PathDot { Shape = dot, Base = color, Along = nextDrop, Flowing = flowing });

                    nextDrop += _dotSpacing;
                }

                travelled += step;
                prev = point;
            }
        }

        // Пройденный маршрут выводится из данных, а не хранится: игрок проходит ровно один узел на этаж,
        // поэтому цепочка «пройденные по возрастанию этажа + текущий» и есть его путь. Ничего не добавляем
        // ни в домен, ни в сейв — старые забеги читаются как есть.
        // ГОТЧА на будущее: если появится возврат назад или два узла на этаже, вывод сломается и придётся
        // хранить явный список посещённых.
        private static HashSet<string> TravelledRoute(IReadOnlyList<MapNodeVisual> nodes)
        {
            var chain = new List<MapNodeVisual>(16);
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].State == MapNodeVisualState.Cleared || nodes[i].State == MapNodeVisualState.Current)
                    chain.Add(nodes[i]);

            chain.Sort((x, y) => x.Floor.CompareTo(y.Floor));

            var keys = new HashSet<string>();
            for (int i = 1; i < chain.Count; i++)
                keys.Add(EdgeKey(chain[i - 1].Id, chain[i].Id));
            return keys;
        }

        // Ключ ребра без направления: граф хранит связь с обеих сторон, и маршрут должен опознаваться
        // независимо от того, каким концом ребро пришло.
        private static string EdgeKey(string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? a + " " + b : b + " " + a;

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
            if (_pawn != null) _pawn.transform.position = new Vector3(pos.x, pos.y, PawnZ);
        }

        // Фишка — точка того же семейства, что дорожка: «кто-то идёт по пунктиру», а не значок поверх узла.
        // Прежний спрайт-шлем закрывал собой иконку узла и требовал подъёма над ним.
        private void EnsurePawn()
        {
            if (_pawn != null) return;

            var go = new GameObject("Pawn");
            go.transform.SetParent(transform, false);
            _pawn = go.AddComponent<Disc>();
            _pawn.Geometry = DiscGeometry.Flat2D;
            _pawn.Type     = DiscType.Disc;
            _pawn.Radius   = _pawnRadius;
            _pawn.Color    = _palette != null ? _palette.Pawn : Color.white;
            _pawn.SortingLayerID = SortingLayerId();
            _pawn.SortingOrder   = 6;
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

        /// <inheritdoc/>
        public IReadOnlyList<string> NodeIds
        {
            get
            {
                var ids = new List<string>(_hits.Count);
                for (int i = 0; i < _hits.Count; i++) ids.Add(_hits[i].Id);
                return ids;
            }
        }

        /// <inheritdoc/>
        public void PreviewTravel(string nodeId)
        {
            if (!_shown) return;
            for (int i = 0; i < _hits.Count; i++)
            {
                if (!string.Equals(_hits[i].Id, nodeId, StringComparison.Ordinal)) continue;
                StartTravel(i, silent: true);
                return;
            }
        }

        /// <inheritdoc/>
        public void ResetPawn()
        {
            _travelling = false;
            _travelNodeId = null;

            for (int i = 0; i < _hits.Count; i++)
                if (_hits[i].State == MapNodeVisualState.Current) { PlacePawn(_hits[i].Pos); return; }

            if (_hits.Count > 0) PlacePawn(_hits[0].Pos);
        }

        private void StartTravel(int hit) => StartTravel(hit, silent: false);

        // silent = проехать и НЕ засчитывать выбор: дев-обход карты не должен уводить петлю забега в узел.
        private void StartTravel(int hit, bool silent)
        {
            _travelFrom = _pawnAt;
            _travelTo   = _hits[hit].Pos;
            Vector2 dir = _travelTo - _travelFrom;
            var perp = new Vector2(-dir.y, dir.x).normalized;
            _travelCtrl = (_travelFrom + _travelTo) * 0.5f + perp * (_edgeCurve * dir.magnitude);

            _travelNodeId     = _hits[hit].Id;
            _travelSilent     = silent;
            _travelT          = 0f;
            _travelSpeedScale = 1f;
            _travelling       = true;
        }

        private bool _travelSilent;

        private int HitTest()
        {
            Camera cam = Camera.main;
            if (cam == null || _input == null) return -1;

            Vector3 screen = _input.PointerScreenPosition;
            Vector2 world  = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));

            // Радиус хвата у каждого узла свой — он задан в префабе. Поэтому сравниваем не сырую
            // дистанцию, а её долю от радиуса.
            float bestRatio = 1f;
            int best = -1;
            for (int i = 0; i < _hits.Count; i++)
            {
                float r = _hits[i].PickRadius * _pickRadiusScale;
                if (r <= 0f) continue;
                float ratio = (_hits[i].Pos - world).sqrMagnitude / (r * r);
                if (ratio <= bestRatio) { bestRatio = ratio; best = i; }
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

            AnimateNodes(now);
            AnimateDots(now);

            if (_travelling) TickTravel();
        }

        private void AnimateNodes(float now)
        {
            // Доступность показывается ЦВЕТОМ (дыхание яркости), размер трогает только курсор:
            // пульсирующие размером узлы читались как «всё шевелится», а не «сюда можно».
            float breath = 1f + Mathf.Sin(now * _breathSpeed) * _availableBreath;

            for (int i = 0; i < _hits.Count; i++)
            {
                if (_hits[i].View == null) continue;

                float scale = 1f;
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

                _hits[i].View.SetVisualScale(scale);

                if (_hits[i].Selectable && _palette != null)
                    _hits[i].View.SetBrightness(breath);
            }
        }

        // Волна вдоль дорожки к доступному узлу: точка ярче, когда волна проходит через неё. Это и есть
        // «сюда можно» — направление читается само, без стрелок.
        private void AnimateDots(float now)
        {
            if (_palette == null) return;

            float head = now * _dotFlowSpeed;
            for (int i = 0; i < _dots.Count; i++)
            {
                PathDot dot = _dots[i];
                if (!dot.Flowing || dot.Shape == null) continue;

                float phase = Mathf.Repeat(head - dot.Along, _dotFlowLength * 2.5f);
                float glow  = Mathf.Clamp01(1f - phase / _dotFlowLength);
                dot.Shape.Color = Color.Lerp(_palette.PathIdle, _palette.PathAvailable, 0.35f + glow * 0.65f);
            }
        }

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
                bool silent = _travelSilent;
                _travelSilent = false;
                if (!silent) NodeClicked?.Invoke(id); // выбор засчитывается ПОСЛЕ приезда
                return;
            }

            // Плавный разгон-торможение: линейный ход читается как рывок.
            float t = Mathf.SmoothStep(0f, 1f, _travelT);
            PlacePawn(Bezier(_travelFrom, _travelCtrl, _travelTo, t));
        }

        private void SetLayerActive(bool active)
        {
            if (_nodeRoot != null) _nodeRoot.gameObject.SetActive(active);
            if (_edgeRoot != null) _edgeRoot.gameObject.SetActive(active);
            if (_pawn != null) _pawn.gameObject.SetActive(active);
        }

        // Всё пулится: за акт карта перерисовывается на каждом узле. Узлы — один префаб на все типы,
        // точки пути — голые Disc (Shapes допускает лишь ОДИН ShapeRenderer на GameObject).
        private void ReleaseAll()
        {
            _hits.Clear();
            _dots.Clear();
            _hoverIndex = -1;
            _nudgeIndex = -1;

            for (int i = 0; i < _nodePool.Count; i++)
            {
                _nodePool[i].SetVisualScale(1f);
                _nodePool[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < _dotPool.Count; i++) _dotPool[i].gameObject.SetActive(false);
            _rentedNodes = 0;
            _rentedDots  = 0;
        }

        private int _rentedNodes;
        private int _rentedDots;

        private MapNodeView RentNode()
        {
            if (_nodePrefab == null) return null;

            if (_rentedNodes < _nodePool.Count)
            {
                MapNodeView reused = _nodePool[_rentedNodes++];
                reused.gameObject.SetActive(true);
                return reused;
            }

            MapNodeView spawned = Instantiate(_nodePrefab, _nodeRoot);
            _nodePool.Add(spawned);
            _rentedNodes++;
            return spawned;
        }

        private Disc RentDot()
        {
            if (_rentedDots < _dotPool.Count)
            {
                Disc reused = _dotPool[_rentedDots++];
                reused.gameObject.SetActive(true);
                return reused;
            }

            var go = new GameObject("PathDot");
            go.transform.SetParent(_edgeRoot, false);
            var dot = go.AddComponent<Disc>();
            dot.Geometry       = DiscGeometry.Flat2D;
            dot.Type           = DiscType.Disc;
            dot.SortingLayerID = SortingLayerId();
            dot.SortingOrder   = 0;
            _dotPool.Add(dot);
            _rentedDots++;
            return dot;
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
