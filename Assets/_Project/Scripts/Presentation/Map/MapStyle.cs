using UnityEngine;
using UnityEngine.Serialization;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// ЕДИНОЕ место настройки карты акта: раскладка, цвета, пути, отклик, фишка. Всё в одном ассете.
    /// </summary>
    /// <remarks>
    /// Почему один ассет, а не поля компонента: настройки, лежащие прямо на <c>WorldMapView</c>, уходят
    /// в сериализацию СЦЕНЫ, и тогда дефолты в C# перестают что-либо значить — в сцене живёт своя копия.
    /// Дважды подряд это стоило раунда play-QA (профиль ширины в `ActConfig.asset`, разброс раскладки в
    /// WorldScene). Здесь настройка ровно одна на всю игру, и «поправить в коде, но не в игре» невозможно.
    /// <para>Вид САМОГО узла (круг, обод, размер иконки) настраивается в префабе <c>MapNode.prefab</c> —
    /// это отдельное место намеренно: там форма, здесь числа и цвета.</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "MapStyle", menuName = "Guildmaster/Map/Style")]
    public sealed class MapStyle : ScriptableObject
    {
        [Header("Раскладка")]
        [Tooltip("Шаги сетки и правила дистанции. Разброс выводится из сида забега — в данных карты его нет.")]
        [SerializeField] private MapLayout _layout = MapLayout.Default;

        [Tooltip("Сколько этажей показывать в кадре при входе на карту.")]
        [SerializeField] private float _floorsInView = 4f;

        [Tooltip("Множитель зоны хвата относительно радиуса из префаба: попасть по узлу должно быть легче, " +
                 "чем он выглядит.")]
        [SerializeField] private float _pickRadiusScale = 1.4f;

        [Header("Задник карты")]
        [Tooltip("Материал ЛИСТА карты (шейдер Guildmaster/Map/Backdrop). Пусто — листа нет. " +
                 "Тон, фактуру и рваность края крутить В МАТЕРИАЛЕ: это шейдерные параметры, дублировать " +
                 "их здесь значило бы завести второй источник правды.")]
        [SerializeField] private Material _backdropMaterial;

        [Tooltip("Поля листа: во сколько раз он больше графа. Лист ОБТЯГИВАЕТ карту, а не заливает экран — " +
                 "за его рваными краями начинается фон. 1.0 = впритык, 1.1 = небольшие поля.")]
        [SerializeField] private float _backdropPadding = 1.08f;

        [Header("Стол под картой (то, что видно за краем листа)")]
        [Tooltip("Материал поверхности под листом (шейдер Guildmaster/Map/Table). Пусто — за листом пустота, " +
                 "которую камера зальёт своим цветом очистки. Тайл и маску света крутить В МАТЕРИАЛЕ.")]
        [SerializeField] private Material _tableMaterial;

        [Tooltip("Во сколько раз стол больше ЛИСТА. Должен с запасом перекрывать кадр на любом отъезде " +
                 "камеры: край стола в кадре читается как ошибка, а не как край стола.")]
        [SerializeField] private float _tablePadding = 3.5f;

        [Header("Цвета узла")]
        [Tooltip("Подложка узла — одна на все типы (--gm-ink-600). Тип читается ИКОНКОЙ, не цветом.")]
        [SerializeField] private Color _nodeBacking = new Color(0.141f, 0.102f, 0.071f);

        [Tooltip("Обод узла (--gm-brass-600).")]
        [SerializeField] private Color _nodeRim = new Color(0.627f, 0.435f, 0.188f);

        [Tooltip("Метка узла, на котором стоит отряд (--gm-brass-200).")]
        [SerializeField] private Color _currentMarker = new Color(0.902f, 0.788f, 0.561f);

        [Header("Рампа иконок")]
        [Tooltip("Тёмный конец рампы перекраски иконки (--gm-ink-700).")]
        [SerializeField] private Color _iconShadow = new Color(0.110f, 0.078f, 0.051f);

        [Tooltip("Светлый конец рампы (--gm-parchment-100).")]
        [SerializeField] private Color _iconLight = new Color(0.937f, 0.886f, 0.769f);

        [Header("Состояния (множители яркости)")]
        [SerializeField] private Color _available = Color.white;
        [SerializeField] private Color _current = Color.white;

        [Tooltip("Пройденный узел — притушен, но не выключен: маршрут должен читаться.")]
        [SerializeField] private Color _cleared = new Color(0.62f, 0.58f, 0.5f);

        [Tooltip("Ещё не открытый узел — самый тусклый.")]
        [SerializeField] private Color _locked = new Color(0.42f, 0.4f, 0.36f);

        [Header("Пути")]
        [SerializeField] private float _dotRadius = 0.07f;

        [Tooltip("Расстояние между точками пути. Шаг считается по длине дуги, поэтому ритм одинаков " +
                 "на путях любой длины.")]
        [SerializeField] private float _dotSpacing = 0.32f;

        [Tooltip("Зазор дорожки от КРАЯ узла, в долях его видимого радиуса. Точки начинаются не от центра, " +
                 "а от внешнего радиуса — иначе дорожка вползает под иконку. Учитывается и увеличение узла " +
                 "под курсором: на hover он не должен наезжать на свою же дорожку.")]
        [SerializeField] private float _dotClearance = 1.25f;

        [Tooltip("Изгиб пути в долях его длины. 0 = прямые линии — изгиб случайной стороны читался кашей.")]
        [SerializeField] private float _edgeCurve;

        [Tooltip("Обычный путь — бледные точки (--gm-parchment-300, приглушённый).")]
        [SerializeField] private Color _pathIdle = new Color(0.42f, 0.37f, 0.28f);

        [Tooltip("Пройденный маршрут — прочерченный чернилами по карте (--gm-brass-500).")]
        [SerializeField] private Color _pathTravelled = new Color(0.722f, 0.525f, 0.231f);

        [Tooltip("Путь к доступному узлу — самый яркий (--gm-brass-200).")]
        [SerializeField] private Color _pathAvailable = new Color(0.902f, 0.788f, 0.561f);

        [Tooltip("Скорость бега волны по пути к доступному узлу (метров в секунду).")]
        [SerializeField] private float _dotFlowSpeed = 2.6f;

        [Tooltip("Длина светящегося участка бегущей волны (метры).")]
        [SerializeField] private float _dotFlowLength = 1.4f;

        [Header("Отклик")]
        [Tooltip("Насколько подрастает узел под курсором. На этот же множитель отодвигается дорожка.")]
        [SerializeField] private float _hoverScale = 1.18f;

        [SerializeField] private float _pressScale = 0.9f;

        [Tooltip("Глубина дыхания доступных узлов по ЯРКОСТИ.")]
        [SerializeField, Range(0f, 0.6f)] private float _availableBreath = 0.22f;

        [Header("Такт (доли ЕДИНОГО метронома, а не секунды)")]
        [Tooltip("На какой доле дышат ЯРКОСТЬЮ доступные узлы. 1 = каждый такт, 2 = через такт.")]
        [SerializeField] private float _beatDivision = 1f;

        [Tooltip("На какой доле доступные узлы моргают РАЗМЕРОМ. Отдельно от яркости и намеренно медленнее: " +
                 "2 = полный цикл «больше-меньше» за два такта. Пульс должен быть спокойным, но заметным — " +
                 "ритм сердца (двойной толчок) здесь пробовали, он читался как суета.")]
        [SerializeField] private float _pulseDivision = 2f;

        [Tooltip("Насколько узел раздаётся на пульсе. Это размер, поверх дыхания яркостью.")]
        [FormerlySerializedAs("_heartbeatAmount")]
        [SerializeField, Range(0f, 0.5f)] private float _pulseAmount = 0.14f;

        [Tooltip("На какой доле бежит волна по дорожке к доступному узлу. 0.5 = вдвое чаще биения — " +
                 "дорожка успевает добежать за половину такта, и удар узла читается как её приход.")]
        [SerializeField] private float _flowDivision = 0.5f;

        [Header("Туман (атмосфера, НЕ механика)")]
        [Tooltip("Материал слоя тумана (шейдер Guildmaster/Map/Fog). Пусто — тумана нет. " +
                 "ВАЖНО: туман ничего не скрывает и ни на что не влияет — узлы видны и кликаются сквозь него. " +
                 "Это только атмосфера: дымка лежит над непройденной частью акта и развеивается за отрядом.")]
        [SerializeField] private Material _fogMaterial;

        [Tooltip("За сколько мировых единиц перед отрядом туман успевает разойтись. Больше — мягче граница.")]
        [SerializeField] private float _fogFalloff = 14f;

        [Tooltip("Насколько туман заходит ЗА отряд (мировые единицы): позади должно остаться немного дымки, " +
                 "иначе граница читается как ровная линейка.")]
        [SerializeField] private float _fogTrail = 6f;

        [Header("Фишка отряда")]
        [Tooltip("Радиус точки отряда. Фишка — та же точка, что на пути, только крупнее и ярче.")]
        [SerializeField] private float _pawnRadius = 0.2f;

        [SerializeField] private Color _pawn = new Color(0.973f, 0.925f, 0.796f);

        [Tooltip("Сколько едет фишка между узлами (секунды).")]
        [SerializeField] private float _pawnTravelSeconds = 1.5f;

        [Tooltip("Во сколько раз ускоряется поездка по повторному клику (дабл-клик).")]
        [SerializeField] private float _pawnSkipSpeed = 6f;

        /// <inheritdoc cref="_layout"/>
        public MapLayout Layout => _layout;
        /// <inheritdoc cref="_floorsInView"/>
        public float FloorsInView => _floorsInView;
        /// <inheritdoc cref="_pickRadiusScale"/>
        public float PickRadiusScale => _pickRadiusScale;

        /// <inheritdoc cref="_beatDivision"/>
        public float BeatDivision => _beatDivision;
        /// <inheritdoc cref="_pulseDivision"/>
        public float PulseDivision => _pulseDivision;
        /// <inheritdoc cref="_pulseAmount"/>
        public float PulseAmount => _pulseAmount;
        /// <inheritdoc cref="_flowDivision"/>
        public float FlowDivision => _flowDivision;

        /// <inheritdoc cref="_fogMaterial"/>
        public Material FogMaterial => _fogMaterial;
        /// <inheritdoc cref="_fogFalloff"/>
        public float FogFalloff => _fogFalloff;
        /// <inheritdoc cref="_fogTrail"/>
        public float FogTrail => _fogTrail;

        /// <inheritdoc cref="_backdropMaterial"/>
        public Material BackdropMaterial => _backdropMaterial;
        /// <inheritdoc cref="_backdropPadding"/>
        public float BackdropPadding => _backdropPadding;
        /// <inheritdoc cref="_tableMaterial"/>
        public Material TableMaterial => _tableMaterial;
        /// <inheritdoc cref="_tablePadding"/>
        public float TablePadding => _tablePadding;

        /// <inheritdoc cref="_nodeBacking"/>
        public Color NodeBacking => _nodeBacking;
        /// <inheritdoc cref="_nodeRim"/>
        public Color NodeRim => _nodeRim;
        /// <inheritdoc cref="_currentMarker"/>
        public Color CurrentMarker => _currentMarker;
        /// <inheritdoc cref="_iconShadow"/>
        public Color IconShadow => _iconShadow;
        /// <inheritdoc cref="_iconLight"/>
        public Color IconLight => _iconLight;

        /// <inheritdoc cref="_dotRadius"/>
        public float DotRadius => _dotRadius;
        /// <inheritdoc cref="_dotSpacing"/>
        public float DotSpacing => _dotSpacing;
        /// <inheritdoc cref="_dotClearance"/>
        public float DotClearance => _dotClearance;
        /// <inheritdoc cref="_edgeCurve"/>
        public float EdgeCurve => _edgeCurve;
        /// <inheritdoc cref="_pathIdle"/>
        public Color PathIdle => _pathIdle;
        /// <inheritdoc cref="_pathTravelled"/>
        public Color PathTravelled => _pathTravelled;
        /// <inheritdoc cref="_pathAvailable"/>
        public Color PathAvailable => _pathAvailable;
        /// <inheritdoc cref="_dotFlowSpeed"/>
        public float DotFlowSpeed => _dotFlowSpeed;
        /// <inheritdoc cref="_dotFlowLength"/>
        public float DotFlowLength => _dotFlowLength;

        /// <inheritdoc cref="_hoverScale"/>
        public float HoverScale => _hoverScale;
        /// <inheritdoc cref="_pressScale"/>
        public float PressScale => _pressScale;
        /// <inheritdoc cref="_availableBreath"/>
        public float AvailableBreath => _availableBreath;

        /// <inheritdoc cref="_pawnRadius"/>
        public float PawnRadius => _pawnRadius;
        /// <inheritdoc cref="_pawn"/>
        public Color Pawn => _pawn;
        /// <inheritdoc cref="_pawnTravelSeconds"/>
        public float PawnTravelSeconds => _pawnTravelSeconds;
        /// <inheritdoc cref="_pawnSkipSpeed"/>
        public float PawnSkipSpeed => _pawnSkipSpeed;

        /// <summary>Множитель яркости по состоянию узла.</summary>
        public Color StateTint(MapNodeVisualState state) => state switch
        {
            MapNodeVisualState.Available => _available,
            MapNodeVisualState.Current   => _current,
            MapNodeVisualState.Cleared   => _cleared,
            _                            => _locked,
        };
    }
}
