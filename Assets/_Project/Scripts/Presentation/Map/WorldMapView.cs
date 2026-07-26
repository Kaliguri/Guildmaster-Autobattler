using System;
using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Input;
using Guildmaster.Presentation.Effects;
using Guildmaster.Presentation.Tempo;
using Shapes;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// World-слой карты акта: узлы и пути рисуются в мире, клик берётся мировым пикингом.
    /// Живёт постоянно в persist-мире и включается/гасится по состоянию — шаблон <c>DeploymentController</c>,
    /// а не спавн-на-время (спавн требовал бы сноса по ct и плодил висячие объекты при отмене забега, QA #37).
    /// <para>НАСТРОЕК ЗДЕСЬ НЕТ: все числа и цвета живут в <see cref="MapStyle"/>, вид узла — в его префабе.
    /// Поля-настройки на компоненте уходили в сериализацию сцены и переставали слушаться кода — дважды
    /// стоило раунда play-QA. В сцене остаются только ССЫЛКИ.</para>
    /// </summary>
    public sealed class WorldMapView : MonoBehaviour, IWorldMapView
    {
        [Header("Ссылки")]
        [Tooltip("Стиль карты: раскладка, цвета, пути, отклик. ЕДИНОЕ место настройки.")]
        [SerializeField] private MapStyle _style;

        [Tooltip("Префаб узла — ОДИН на все типы, внутри все иконки. Тип включается кодом.")]
        [SerializeField] private MapNodeView _nodePrefab;

        [Tooltip("Префаб точки дорожки. Префаб, а не AddComponent в рантайме: Shapes при добавлении " +
                 "компонента дёргает SendMessage и сыпет предупреждениями — по одному на каждую точку.")]
        [SerializeField] private Disc _dotPrefab;

        [Tooltip("Слой сортировки для фигур карты. Shapes по умолчанию рисуются на Default (самый нижний) — " +
                 "если под картой появится спрайт-фон, он перекроет узлы; тогда выставить слой выше фона.")]
        [SerializeField] private string _sortingLayerName = "Default";

        // Глубина вместо слоёв сортировки: Shapes и SpriteRenderer — разные системы рисования, и порядок
        // между ними слоями надёжно не задаётся (иконки уходили ПОД подложки). По Z порядок однозначен.
        private const float TableZ = 2f;    // ещё дальше листа: поверхность, на которой карта лежит
        private const float BackdropZ = 1f; // позади всего: узлы, дорожки и фишка рисуются поверх полотна
        private const float EdgeZ = 0.2f;
        private const float NodeZ = 0f;
        private const float FogZ  = -0.15f; // над узлами, но под фишкой: отряд идёт ПОВЕРХ дымки
        private const float PawnZ = -0.3f;

        private IInputService _input;
        private IVisualTempo _tempo; // единый метроном: биение узлов и волна дорожек идут от него
        private VisualToggles _toggles; // общий реестр «включить/выключить эффект»

        // Состояния тумблеров. Отдельными полями, потому что сами объекты (лист, туман) создаются лениво
        // при первом показе карты, а переключить эффект могут раньше.
        private bool _tableOn = true;
        private bool _sheetOn = true;
        private bool _fogOn;      // туман по умолчанию ВЫКЛЮЧЕН (решение Макса: «вообще не то, мб позже»)
        private bool _pulseOn = true;
        private bool _pathFlowOn = true;
        private bool _travelOn;   // поездка фишки ВЫКЛЮЧЕНА: шаг по карте идёт шторкой (решение Макса)
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
            public float Along;   // расстояние от начала пути
            public bool Flowing;  // путь к доступному узлу — по нему бежит волна
        }
        private readonly List<PathDot> _dots = new List<PathDot>(256);

        private readonly List<MapNodeView> _nodePool = new List<MapNodeView>(48);
        private readonly List<Disc> _dotPool = new List<Disc>(256);

        private Transform _nodeRoot;
        private Transform _edgeRoot;
        private Disc _pawn;
        private MeshRenderer _table;
        private MeshRenderer _backdrop;
        private MeshRenderer _fog;
        private MaterialPropertyBlock _fogBlock;

        private bool _shown;
        private int _sortingLayerId;
        private bool _layerResolved;

        private int _hoverIndex = -1;
        private bool _pressed;
        private float _nudgeUntil;
        private int _nudgeIndex = -1;

        // Шторка перехода: закрыть кадр → засчитать выбор → открыть. Заменяет поездку фишки как основной
        // способ шагнуть по карте (решение Макса 2026-07-20): поездка отвечала «отряд идёт», но каждый шаг
        // стоил полутора секунд ожидания, а шагов за акт четырнадцать.
        //
        // Сами фазы карта НЕ ведёт (QA #53): выбор, засчитанный на закрытом кадре, уводит игрока с карты,
        // и вести переход дальше стало бы некому — карта скрывается в его середине. Она только заказывает
        // моргание и получает управление на закрытом кадре.
        private Core.Flow.IScreenTransition _transition;
        private Core.Audio.IAudioService _audio;   // карта немая по природе: свои звуки зовёт сама
        private int _lastHoverSounded = -1;        // ребро наведения: звук на вход в узел, не каждый кадр
        private bool _stepping;        // мы заказали переход и ждём закрытого кадра
        private Vector2 _stepTargetPos; // узел, в который «ныряем» — к нему же наезжает камера

        // Поездка фишки: пока едет, выбор заблокирован, а событие выбора ждёт приезда.
        private bool _travelling;
        private bool _travelSilent;
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
        public void Construct(IInputService input, CameraModeController cameraModes, WorldMapViewLink link,
                              IVisualTempo tempo, VisualToggles toggles,
                              Core.Flow.IScreenTransition transition,
                              Core.Audio.IAudioService audio)
        {
            _audio       = audio;
            _input       = input;
            _cameraModes = cameraModes;
            _link        = link;
            _tempo       = tempo;
            _toggles     = toggles;
            _transition  = transition;
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
            RegisterToggles();
        }

        // Регистрируем эффекты карты в общем реестре: гасить и возвращать их можно из консоли (gm_fx),
        // не пересобирая сцену и не правя ассеты.
        private void RegisterToggles()
        {
            if (_toggles == null) return;

            _toggles.Register("map.table", "Стол под картой (поверхность и свет за краем листа)",
                on => { _tableOn = on; if (_table != null) _table.enabled = on; });

            _toggles.Register("map.sheet", "Лист карты (бумага под графом)",
                on => { _sheetOn = on; if (_backdrop != null) _backdrop.enabled = on; });

            _toggles.Register("map.fog", "Туман над непройденной частью акта",
                on => { _fogOn = on; if (_fog != null) _fog.enabled = on; }, defaultEnabled: false);

            _toggles.Register("map.pulse", "Пульс доступных узлов (моргание размером в такт)",
                on => _pulseOn = on);

            _toggles.Register("map.pathflow", "Бегущая волна по дорожкам",
                on => _pathFlowOn = on);

            // Выключен по умолчанию: шаг по карте идёт шторкой. Поездка не удалена намеренно — Макс
            // оставил её про запас, включается одной командой.
            _toggles.Register("map.travel", "Поездка фишки по дорожке (выкл = переход затемнением)",
                on => _travelOn = on, defaultEnabled: false);
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
            if (_style == null)
            {
                Debug.LogError("[WorldMapView] Не назначен MapStyle — рисовать карту нечем.");
                return;
            }
            if (nodes == null || nodes.Count == 0) { Bounds = new Rect2D(Vector2.zero, Vector2.zero); return; }

            // Раскладка — здесь: домен отдаёт только топологию (этаж/ряд), координаты не его забота.
            MapLayout layout = _style.Layout;
            Dictionary<string, Vector2> local = layout.Resolve(nodes, seed);

            var byId = new Dictionary<string, Vector2>(nodes.Count);
            var radiusOf = new Dictionary<string, float>(nodes.Count);
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
                    view.Apply(n.State, _style);
                }

                radiusOf[n.Id] = view != null ? view.VisualRadius : 0f;

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

            if (edges != null) BuildPaths(edges, byId, radiusOf, nodes);
            PlacePawn(hasFocus ? focus : new Vector2(minX, (minY + maxY) * 0.5f));

            const float padding = 2f;
            var center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            var size   = new Vector2(maxX - minX + padding * 2f, maxY - minY + padding * 2f);
            Bounds = new Rect2D(center, size);

            PlaceTable(center, size);
            PlaceBackdrop(center, size);
            PlaceFog(center, size);

            _shown = true;
            SetLayerActive(true);

            // Кадр и границы клампа: смотрим КРУПНО на текущий узел, а не на весь акт сразу.
            _cameraModes?.EnterMap(Bounds, hasFocus ? focus : center, _style.FloorsInView * layout.StepX);
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
                                Dictionary<string, float> radiusOf,
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

                Color color = isTravelled ? _style.PathTravelled
                            : isOpen      ? _style.PathAvailable
                            : _style.PathIdle;

                // Контрольная точка кривой — перпендикулярно хорде, сторона и величина детерминированы парой id.
                Vector2 mid = (a + b) * 0.5f;
                Vector2 dir = b - a;
                var perp = new Vector2(-dir.y, dir.x).normalized;
                Vector2 ctrl = mid + perp * (_style.EdgeCurve * dir.magnitude * CurveSign(edges[i].From, edges[i].To));

                radiusOf.TryGetValue(edges[i].From, out float ra);
                radiusOf.TryGetValue(edges[i].To,   out float rb);

                ScatterDots(a, ctrl, b, MarginFor(ra), MarginFor(rb), color, isOpen);
            }
        }

        // Дорожка начинается от ВНЕШНЕГО края узла, а не от его центра, и с запасом на увеличение под
        // курсором: иначе на hover узел наезжает на собственную дорожку и съедает первые точки.
        private float MarginFor(float visualRadius) =>
            visualRadius * _style.HoverScale * _style.DotClearance;

        // Точки ставим по РАВНОЙ длине дуги: идём мелким шагом по параметру, копим пройденное расстояние
        // и роняем точку каждые DotSpacing метров. Равномерный шаг по t дал бы сгущение на изгибе.
        private void ScatterDots(Vector2 a, Vector2 ctrl, Vector2 b,
                                 float marginStart, float marginEnd, Color color, bool flowing)
        {
            const int walk = 64;

            // Полная длина дуги — нужна, чтобы отмерить отступ с ДАЛЬНЕГО конца.
            float total = 0f;
            Vector2 prev = a;
            for (int s = 1; s <= walk; s++)
            {
                Vector2 point = Bezier(a, ctrl, b, s / (float)walk);
                total += (point - prev).magnitude;
                prev = point;
            }

            float stopAt = total - marginEnd;
            if (stopAt <= marginStart) return; // узлы слишком близко — дорожке между ними места нет

            float spacing = Mathf.Max(0.01f, _style.DotSpacing);
            float travelled = 0f;
            float nextDrop = marginStart;
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
                    if (dot == null) return; // нет префаба точки — дорожек не будет, но карта живёт

                    dot.transform.position = new Vector3(at.x, at.y, EdgeZ);
                    dot.Radius = _style.DotRadius;
                    dot.Color  = color;
                    _dots.Add(new PathDot { Shape = dot, Along = nextDrop, Flowing = flowing });

                    nextDrop += spacing;
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
            string.CompareOrdinal(a, b) <= 0 ? a + " " + b : b + " " + a;

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
                uint h = Guildmaster.Core.Random.DeterministicHash.Of32(from, to);
                return ((h & 1u) == 0u ? 1f : -1f) * (0.6f + (h >> 8 & 0xFF) / 255f * 0.8f);
            }
        }

        private void PlacePawn(Vector2 pos)
        {
            EnsurePawn();
            _pawnAt = pos;
            if (_pawn != null) _pawn.transform.position = new Vector3(pos.x, pos.y, PawnZ);
        }

        // Поверхность, на которой лежит лист. Собирается технически, без единого нарисованного пикселя:
        // тайл из Kenney pattern-pack, пятно света из Kenney light-masks, цвета — в материале. Квад берётся
        // с большим запасом по краям: увидеть в кадре КРАЙ стола хуже, чем не увидеть стола вовсе.
        private void PlaceTable(Vector2 center, Vector2 size)
        {
            if (_style == null || _style.TableMaterial == null) return;

            if (_table == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Table";
                Destroy(go.GetComponent<Collider>()); // поверхность не должна ловить пикинг узлов
                go.transform.SetParent(transform, false);
                _table = go.GetComponent<MeshRenderer>();
                _table.sharedMaterial = _style.TableMaterial;
                _table.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _table.receiveShadows = false;
            }

            _table.enabled = _tableOn;

            float over = Mathf.Max(1f, _style.TablePadding);
            var sheet = SheetSize(size);
            var top = new Vector2(sheet.x * over, sheet.y * over);
            _table.transform.position   = new Vector3(center.x, center.y, TableZ);
            _table.transform.localScale = new Vector3(top.x, top.y, 1f);

            // Пропорции — в шейдер, чтобы тайл не растянулся в полосы вдоль длинной стороны стола.
            // Свет и тайлинг тоже отсюда, а не из материала: тот же стол лежит за главным меню, и подача
            // у двух мест разная — оба набора чисел живут рядом в MapStyle, чтобы их было видно вместе.
            _tableBlock ??= new MaterialPropertyBlock();
            _table.GetPropertyBlock(_tableBlock);
            _tableBlock.SetFloat(AspectXId, top.y > 0.01f ? top.x / top.y : 1f);
            _tableBlock.SetFloat(LightStrengthId, _style.TableLight);
            _tableBlock.SetFloat(AmbientId, _style.TableAmbient);
            _tableBlock.SetFloat(PatternTilingId, _style.TableTiling);
            _table.SetPropertyBlock(_tableBlock);
        }

        private MaterialPropertyBlock _tableBlock;

        // Полотно под картой. Без него позади узлов пустота, которую камера заливает своим цветом очистки —
        // именно поэтому карта выглядела «синей». Растягивается по фактическому размеру карты с запасом,
        // чтобы при отъезде камеры за краем не показалась та же пустота.
        private void PlaceBackdrop(Vector2 center, Vector2 size)
        {
            if (_style == null || _style.BackdropMaterial == null) return;

            if (_backdrop == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Backdrop";
                Destroy(go.GetComponent<Collider>()); // полотно не должно ловить пикинг узлов
                go.transform.SetParent(transform, false);
                _backdrop = go.GetComponent<MeshRenderer>();
                _backdrop.sharedMaterial = _style.BackdropMaterial;
                _backdrop.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _backdrop.receiveShadows = false;
            }

            _backdrop.enabled = _sheetOn;

            var sheet = SheetSize(size);
            _backdrop.transform.position   = new Vector3(center.x, center.y, BackdropZ);
            _backdrop.transform.localScale = new Vector3(sheet.x, sheet.y, 1f);

            // Пропорции листа — в шейдер: рваность края считается по UV, и без поправки на вытянутость
            // она превратилась бы вдоль длинной стороны в пологую волну, а вдоль короткой осталась частой.
            _backdropBlock ??= new MaterialPropertyBlock();
            _backdrop.GetPropertyBlock(_backdropBlock);
            _backdropBlock.SetFloat(AspectXId, sheet.y > 0.01f ? sheet.x / sheet.y : 1f);
            _backdrop.SetPropertyBlock(_backdropBlock);
        }

        private MaterialPropertyBlock _backdropBlock;
        private static readonly int AspectXId = Shader.PropertyToID("_AspectX");
        private static readonly int LightStrengthId = Shader.PropertyToID("_LightStrength");
        private static readonly int AmbientId = Shader.PropertyToID("_Ambient");
        private static readonly int PatternTilingId = Shader.PropertyToID("_PatternTiling");

        // Размер листа. Поля по ширине и по высоте РАЗНЫЕ: карта сильно вытянута, и единый множитель дал бы
        // сверху узкую полоску (там нужно место под название акта), а по краям — пустые вёрсты бумаги.
        // Считается в одном месте, потому что от листа пляшут и стол, и туман.
        private Vector2 SheetSize(Vector2 graph) => new Vector2(
            graph.x * Mathf.Max(1f, _style.BackdropPadding),
            graph.y * Mathf.Max(1f, _style.BackdropPaddingY));

        // Слой тумана — ЧИСТО АТМОСФЕРА. Лежит над картой, но ничего не скрывает и не мешает: узлы под ним
        // видны и кликаются (пикинг идёт своей математикой, а не рейкастом), коллайдера у полотна нет.
        // Смысл — впереди акт затянут дымкой, а за отрядом она разошлась.
        private void PlaceFog(Vector2 center, Vector2 size)
        {
            if (_style == null || _style.FogMaterial == null) return;

            if (_fog == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Fog";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                _fog = go.GetComponent<MeshRenderer>();
                _fog.sharedMaterial = _style.FogMaterial;
                _fog.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _fog.receiveShadows = false;
            }

            _fog.enabled = _fogOn;

            var sheet = SheetSize(size);
            _fog.transform.position   = new Vector3(center.x, center.y, FogZ);
            _fog.transform.localScale = new Vector3(sheet.x, sheet.y, 1f);
            UpdateFogReveal();
        }

        // Фронт развеивания едет за отрядом. Через MaterialPropertyBlock, а не правкой материала:
        // материал — общий ассет, писать в него из рантайма значит пачкать файл на диске.
        private void UpdateFogReveal()
        {
            if (_fog == null || _style == null) return;

            _fogBlock ??= new MaterialPropertyBlock();
            _fog.GetPropertyBlock(_fogBlock);
            _fogBlock.SetFloat(RevealXId, _pawnAt.x);
            _fogBlock.SetFloat(FalloffId, Mathf.Max(0.01f, _style.FogFalloff));
            _fogBlock.SetFloat(TrailId,   _style.FogTrail);
            _fog.SetPropertyBlock(_fogBlock);
        }

        private static readonly int RevealXId = Shader.PropertyToID("_RevealX");
        private static readonly int FalloffId = Shader.PropertyToID("_Falloff");
        private static readonly int TrailId   = Shader.PropertyToID("_Trail");

        // Фишка — точка того же семейства, что дорожка: «кто-то идёт по пунктиру», а не значок поверх узла.
        // Прежний спрайт-шлем закрывал собой иконку узла и требовал подъёма над ним.
        private void EnsurePawn()
        {
            if (_pawn != null || _style == null || _dotPrefab == null) return;

            // Фишка собирается из ТОГО ЖЕ префаба, что точки дорожки: она и должна быть той же точкой,
            // только крупнее и ярче — тогда «кто-то идёт по дорожке» читается само.
            _pawn = Instantiate(_dotPrefab, transform);
            _pawn.name   = "Pawn";
            _pawn.Radius = _style.PawnRadius;
            _pawn.Color  = _style.Pawn;
            _pawn.SortingLayerID = SortingLayerId();
            _pawn.SortingOrder   = 6;
        }

        // Клик по узлу: мировой пикинг, а не UITK. Клик, попавший в UI (топбар/кнопки поверх карты),
        // до мира не доходит — иначе нажатие на кнопку заодно выбирало бы узел под ней.
        private void OnPointerPressed()
        {
            if (!_shown) return;

            // Пока фишка едет, повторный клик = «пропустить»: ускоряем поездку, а не выбираем заново.
            if (_travelling) { _travelSpeedScale = _style != null ? _style.PawnSkipSpeed : 6f; return; }

            // Пока идёт шторка, карта клики не принимает: выбор уже сделан и вот-вот засчитается.
            if (_stepping) return;

            if (_input == null || _input.PointerOverUI) return;

            int hit = HitTest();
            if (hit < 0) return;

            _pressed = true;
            if (_hits[hit].Selectable)
            {
                _audio?.Play("map.node_select.ui");
                BeginStep(hit, silent: false);
            }
            else
            {
                // Отказной «nudge» без звука читается как подвисание, а не как «сюда нельзя».
                _audio?.Play("map.node_locked.ui");
                _nudgeIndex = hit; _nudgeUntil = Time.unscaledTime + NudgeDuration;
            }
        }

        // Шаг по карте: шторкой (по умолчанию) или поездкой фишки (тумблер map.travel). Развилка одна на
        // все входы — и клик, и дев-обход, — чтобы способ перехода нельзя было забыть в одном из них.
        private void BeginStep(int hit, bool silent)
        {
            if (!silent) _audio?.Play("map.travel_start.ui");
            if (_travelOn || _transition == null || _transition.Busy)
            {
                StartTravel(hit, silent);
                return;
            }

            _stepTargetPos = _hits[hit].Pos;
            string id      = silent ? null : _hits[hit].Id;
            _stepping      = true;

            var shape = new Core.Flow.ScreenTransitionShape(
                _style.TransitionInSeconds, _style.TransitionHoldSeconds, _style.TransitionOutSeconds,
                ScreenUvOf(_stepTargetPos), _style.TransitionInkDelay);

            _transition.Play(shape, OnStepClosing, () => OnStepCovered(id));
        }

        // Пока кадр закрывается, камера ныряет к выбранному узлу: чернила схлопываются к нему, а он сам
        // едет навстречу — вместе это читается как вход в точку, а не как затемнение рядом с ней.
        //
        // Наезд ТОРМОЗИТ к концу (чернила, наоборот, ускоряются): бросок вперёд случается в первые кадры,
        // пока экран ещё чистый, и игрок успевает увидеть, куда его несёт, прежде чем кадр затянет.
        private void OnStepClosing(float progress)
        {
            if (!_shown) return;
            float p = Mathf.Clamp01(progress);
            _cameraModes?.DiveMapTo(_stepTargetPos, 1f - (1f - p) * (1f - p));
        }

        // Кадр закрыт: переставляем отряд и засчитываем выбор. Всё, что видно за чернилами, меняется здесь —
        // подмены игрок не увидит. Кадр карты возвращаем сразу же: следующий её показ должен начаться с того
        // вида, который игрок оставил, а не изнутри узла, куда мы только что нырнули.
        private void OnStepCovered(string id)
        {
            _stepping = false;
            PlacePawn(_stepTargetPos);
            _cameraModes?.SurfaceMap();
            if (id != null) NodeClicked?.Invoke(id);
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
                BeginStep(i, silent: true);
                return;
            }
        }

        /// <inheritdoc/>
        public void ResetPawn()
        {
            _travelling = false;
            _travelNodeId = null;
            _stepping = false;

            for (int i = 0; i < _hits.Count; i++)
                if (_hits[i].State == MapNodeVisualState.Current) { PlacePawn(_hits[i].Pos); return; }

            if (_hits.Count > 0) PlacePawn(_hits[0].Pos);
        }

        // silent = проехать и НЕ засчитывать выбор: дев-обход карты не должен уводить петлю забега в узел.
        private void StartTravel(int hit, bool silent)
        {
            _travelFrom = _pawnAt;
            _travelTo   = _hits[hit].Pos;
            Vector2 dir = _travelTo - _travelFrom;
            var perp = new Vector2(-dir.y, dir.x).normalized;
            _travelCtrl = (_travelFrom + _travelTo) * 0.5f + perp * (_style.EdgeCurve * dir.magnitude);

            _travelNodeId     = _hits[hit].Id;
            _travelSilent     = silent;
            _travelT          = 0f;
            _travelSpeedScale = 1f;
            _travelling       = true;
        }

        private int HitTest()
        {
            Camera cam = Camera.main;
            if (cam == null || _input == null || _style == null) return -1;

            Vector3 screen = _input.PointerScreenPosition;
            Vector2 world  = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));

            // Радиус хвата у каждого узла свой — он задан в префабе. Поэтому сравниваем не сырую
            // дистанцию, а её долю от радиуса.
            float bestRatio = 1f;
            int best = -1;
            for (int i = 0; i < _hits.Count; i++)
            {
                float r = _hits[i].PickRadius * _style.PickRadiusScale;
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
            if (!_shown || _style == null) return;

            _hoverIndex = (_travelling || _input == null || _input.PointerOverUI) ? -1 : HitTest();
            if (_hoverIndex != _lastHoverSounded)
            {
                _lastHoverSounded = _hoverIndex;
                if (_hoverIndex >= 0) _audio?.Play("map.node_hover.ui");
            }
            float now = Time.unscaledTime;

            AnimateNodes(now);
            AnimateDots(now);

            if (_travelling) TickTravel();
            UpdateFogReveal(); // дымка расходится вслед за отрядом
        }

        // Где узел на экране, в долях кадра. Это точка, К КОТОРОЙ схлопываются чернила: переход должен
        // начаться там, куда игрок ткнул, а не в геометрическом центре экрана.
        private Vector2 ScreenUvOf(Vector2 world)
        {
            Camera cam = Camera.main;
            if (cam == null || Screen.width <= 0 || Screen.height <= 0) return new Vector2(0.5f, 0.5f);

            Vector3 screen = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            return new Vector2(Mathf.Clamp01(screen.x / Screen.width), Mathf.Clamp01(screen.y / Screen.height));
        }

        private void AnimateNodes(float now)
        {
            // Доступные узлы дышат ОДНИМ движением: яркость и размер берут одну и ту же огибающую одной
            // доли. Прежде у размера была своя, вдвое более медленная доля — и два движения сходились в фазе
            // лишь через такт, отчего узел выглядел не дышащим, а дёргающимся вразнобой (play-QA Макса).
            float swell  = _tempo?.Swell(_style.BeatDivision) ?? 0.5f;
            float wave   = (swell - 0.5f) * 2f;                      // -1..1, общая фаза дыхания
            float breath = 1f + wave * _style.AvailableBreath;
            float grow   = 1f + (_pulseOn ? wave * _style.PulseAmount : 0f);

            for (int i = 0; i < _hits.Count; i++)
            {
                if (_hits[i].View == null) continue;

                // Под курсором узел ЗАМИРАЕТ: дыхание зовёт взгляд, но звать уже некуда — игрок и так
                // здесь. Продолжать моргать под рукой значит спорить с собственным откликом на наведение.
                bool hovered = i == _hoverIndex;

                float scale = 1f;
                if (_hits[i].Selectable && !hovered) scale *= grow;
                if (hovered) scale *= _pressed ? _style.PressScale : _style.HoverScale;

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

                // Яркость замирает вместе с размером — но на ПОЛНОЙ, а не на случайной точке дыхания:
                // узел под курсором должен быть самым ярким на карте, иначе наведение читается как затухание.
                if (_hits[i].Selectable) _hits[i].View.SetBrightness(hovered ? 1f + _style.AvailableBreath : breath);
            }
        }

        // Волна вдоль дорожки к доступному узлу: точка ярче, когда волна проходит через неё. Это и есть
        // «сюда можно» — направление читается само, без стрелок.
        private void AnimateDots(float now)
        {
            float length = Mathf.Max(0.01f, _style.DotFlowLength);
            float cycle  = length * 2.5f;

            // Волна привязана к ДОЛЕ, а не к секундам: за свою долю она проходит ровно один цикл, поэтому
            // приход волны к узлу совпадает с его ударом. Смена темпа меняет обе анимации разом.
            float head = _tempo != null
                ? _tempo.Phase(_style.FlowDivision) * cycle
                : now * _style.DotFlowSpeed;

            for (int i = 0; i < _dots.Count; i++)
            {
                PathDot dot = _dots[i];
                if (!dot.Flowing || dot.Shape == null) continue;

                if (!_pathFlowOn) { dot.Shape.Color = _style.PathAvailable; continue; }

                float phase = Mathf.Repeat(head - dot.Along, cycle);
                float glow  = Mathf.Clamp01(1f - phase / length);
                dot.Shape.Color = Color.Lerp(_style.PathIdle, _style.PathAvailable, 0.35f + glow * 0.65f);
            }
        }

        private void TickTravel()
        {
            float dur = Mathf.Max(0.01f, _style.PawnTravelSeconds);
            _travelT += Time.unscaledDeltaTime / dur * _travelSpeedScale;

            if (_travelT >= 1f)
            {
                PlacePawn(_travelTo);
                _travelling = false;
                _audio?.Play("map.travel_arrive.ui");
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
            if (_table != null) _table.gameObject.SetActive(active);
            if (_backdrop != null) _backdrop.gameObject.SetActive(active);
            if (_fog != null) _fog.gameObject.SetActive(active);

            // Шторку при скрытии карты НЕ трогаем (QA #53). Раньше здесь стоял её сброс — и он же убивал
            // переход: карта уходит в узел ровно на закрытом кадре, сброс срабатывал на пике, и от моргания
            // игрок видел одно закрытие. Шторка не наша: её ведёт владелец, переживающий уход карты.
            if (!active) _stepping = false;
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

            if (_dotPrefab == null) return null;

            Disc dot = Instantiate(_dotPrefab, _edgeRoot);
            dot.SortingLayerID = SortingLayerId();
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
