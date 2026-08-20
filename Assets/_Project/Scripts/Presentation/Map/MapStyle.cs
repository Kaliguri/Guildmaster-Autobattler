using System.Collections.Generic;
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

        [Tooltip("Поля листа ПО ШИРИНЕ: во сколько раз он длиннее графа. Лист ОБТЯГИВАЕТ карту, а не " +
                 "заливает экран — за его рваными краями начинается фон. 1.0 = впритык, 1.1 = небольшие поля.")]
        [SerializeField] private float _backdropPadding = 1.08f;

        [Tooltip("Поля листа ПО ВЫСОТЕ — отдельно от ширины намеренно: сверху и снизу нужно место под " +
                 "название акта и подписи, а по длине лист и так тянется на весь акт. Разводить эти два " +
                 "числа приходится потому, что карта сильно вытянута: единый множитель дал бы сверху " +
                 "полоску, а по краям — пустые вёрсты бумаги.")]
        [SerializeField] private float _backdropPaddingY = 1.35f;

        [Header("Стол — общая поверхность под картой И за главным меню")]
        [Tooltip("Материал стола (шейдер Guildmaster/Map/Table). Пусто — за листом пустота, которую камера " +
                 "зальёт своим цветом очистки. ЦВЕТ и ТЕКСТУРЫ (тайл, маска света) крутить в материале — " +
                 "они общие для обоих мест. Числа ниже задают, как эта поверхность подаётся в каждом.")]
        [SerializeField] private Material _tableMaterial;

        [Tooltip("Во сколько раз стол больше ЛИСТА. Должен с запасом перекрывать кадр на любом отъезде " +
                 "камеры: край стола в кадре читается как ошибка, а не как край стола.")]
        [SerializeField] private float _tablePadding = 3.5f;

        [Space(4)]
        [Tooltip("ПОД КАРТОЙ: сила лампы. Здесь стол работает в контрасте со светлым листом и может быть тёмным.")]
        [SerializeField] private float _tableLight = 1.6f;

        [Tooltip("ПОД КАРТОЙ: подсвет вне пятна света.")]
        [SerializeField, Range(0f, 1f)] private float _tableAmbient = 0.25f;

        [Tooltip("ПОД КАРТОЙ: тайлинг рисунка. Считает повторы на РАЗМЕР КВАДА, а квад стола много больше " +
                 "экрана — потому число здесь и в меню разное при одинаковой крупности ромбов на глаз.")]
        [SerializeField] private float _tableTiling = 10f;

        [Header("Задник экранов меты — своя поверхность, НЕ стол")]
        [Tooltip("Материал задника за настройками, паузой и главным меню (шейдер Guildmaster/UI/MenuBackdrop). " +
                 "Пусто — задника не будет, как и у стола.\n\n" +
                 "Почему отдельно от стола: у меты свой регистр — «тёмное стекло», а стол принадлежит " +
                 "гроссбуху (мир и забег). До 05.08.2026 за меню лежал тот же стол с тремя своими числами " +
                 "яркости; числа ушли вместе с ним — новый задник красится рампой прямо в материале.")]
        [SerializeField] private Material _menuBackdropMaterial;

        [Header("Цвета — из палитры проекта")]
        [Tooltip("Снимок токенов дизайн-системы. Все цвета карты берутся ОТСЮДА по имени роли " +
                 "(--gm-color-map-*), своих у карты больше нет: раньше они трижды разошлись с токенами, " +
                 "которые сами же называли в подсказках. Пересобрать снимок — Alebardium → Дизайн-система.")]
        [SerializeField] private Guildmaster.Data.Definitions.GuildmasterPalette _palette;

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
        [Tooltip("На какой доле дышат доступные узлы. 1 = каждый такт, 2 = через такт. " +
                 "ЯРКОСТЬ и РАЗМЕР идут на ЭТОЙ ЖЕ доле — одним движением, не двумя.")]
        [SerializeField] private float _beatDivision = 1f;

        [Tooltip("Насколько узел раздаётся на вдохе. Это размер, поверх дыхания яркостью — но в одной фазе с ним.")]
        [FormerlySerializedAs("_heartbeatAmount")]
        [SerializeField, Range(0f, 0.5f)] private float _pulseAmount = 0.14f;

        [Tooltip("На какой доле бежит волна по дорожке к доступному узлу. 1 = один проход за такт, " +
                 "в ритме дыхания узлов; 0.5 было вдвое быстрее и читалось как суета.")]
        [SerializeField] private float _flowDivision = 1f;

        [Header("Интро карты (первый показ в забеге)")]
        [Tooltip("Сколько камера едет от вида «вся карта целиком» к рабочему кадру у текущего узла " +
                 "(секунды). ПРЕРЫВАЕТСЯ любым движением камеры игроком — колесо, WASD, перетаскивание.")]
        [SerializeField] private float _introCameraSeconds = 1.7f;

        [Tooltip("Пауза перед началом роста (секунды): игрок должен успеть увидеть пустой лист, прежде " +
                 "чем по нему побегут дорожки.")]
        [SerializeField] private float _introStartDelay = 0.2f;

        [Tooltip("Базовая скорость роста дорожек (мировых единиц в секунду). Фронт идёт ПО ПУТЯМ от " +
                 "стартового узла и ветвится на развилках сам.")]
        [SerializeField] private float _introGrowSpeed = 62f;

        [Tooltip("Разброс скоростей веток (0 — все растут ровно, 0.6 — вдвое быстрее/медленнее друг друга). " +
                 "Разброс детерминирован парой id узлов: карта прорастает одинаково при каждом показе " +
                 "одного и того же акта, но ветки обгоняют друг друга — рост читается живым, а не циркулем.")]
        [SerializeField, Range(0f, 0.9f)] private float _introSpeedScatter = 0.45f;

        [Tooltip("За сколько узел выскакивает, когда до него дорос путь (секунды).")]
        [SerializeField] private float _introNodePop = 0.22f;

        [Tooltip("За сколько раздаётся до полного радиуса точка дорожки (секунды). Коротко: точка должна " +
                 "прорастать, а не всплывать.")]
        [SerializeField] private float _introDotPop = 0.12f;

        [Tooltip("Не чаще чем раз в столько секунд звучит появление узла. Узлов на акте четыре десятка, " +
                 "и часть их проступает в один кадр — без этого порога проявление карты звучит как треск, " +
                 "а не как проступающие метки. Ноль — звучит каждый узел.")]
        [SerializeField] private float _introNodeSoundGap = 0.07f;

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


        [Tooltip("Сколько едет фишка между узлами (секунды). Работает, только если включён тумблер " +
                 "map.travel — по умолчанию переход идёт затемнением, поездка оставлена про запас.")]
        [SerializeField] private float _pawnTravelSeconds = 1.5f;

        [Tooltip("Во сколько раз ускоряется поездка по повторному клику (дабл-клик).")]
        [SerializeField] private float _pawnSkipSpeed = 6f;

        // Материала перехода здесь БОЛЬШЕ НЕТ (QA #53): шторку рисует UI-слой, и материал живёт у него —
        // у одной вещи один владелец. Карта задаёт только ритм: она инициатор шага, но не рисовальщик.
        [Header("Переход при выборе узла (шторка вместо поездки)")]
        [Tooltip("Сколько кадр затягивается чернилами (секунды). Это ощущаемая цена шага по карте — " +
                 "заметно, но без ожидания.")]
        [SerializeField] private float _transitionInSeconds = 0.85f;

        [Tooltip("Насколько поздно вступают чернила, в долях закрытия. 0 = вместе с наездом камеры, " +
                 "0.4 = кадр темнеет только на последних 60% нырка. Больше — дольше видно, куда ныряем.")]
        [SerializeField] private float _transitionInkDelay = 0.35f;

        [Tooltip("Сколько кадр держится закрытым, прежде чем начать открываться.")]
        [SerializeField] private float _transitionHoldSeconds = 0.3f;

        [Tooltip("Сколько кадр раскрывается обратно (секунды). Чуть дольше закрытия: уходить резко приятно, " +
                 "а появляться — мягко.")]
        [SerializeField] private float _transitionOutSeconds = 0.55f;

        /// <inheritdoc cref="_layout"/>
        public MapLayout Layout => _layout;
        /// <inheritdoc cref="_floorsInView"/>
        public float FloorsInView => _floorsInView;
        /// <inheritdoc cref="_pickRadiusScale"/>
        public float PickRadiusScale => _pickRadiusScale;

        /// <inheritdoc cref="_beatDivision"/>
        public float BeatDivision => _beatDivision;
        /// <inheritdoc cref="_pulseAmount"/>
        public float PulseAmount => _pulseAmount;
        /// <inheritdoc cref="_flowDivision"/>
        public float FlowDivision => _flowDivision;

        /// <inheritdoc cref="_introCameraSeconds"/>
        public float IntroCameraSeconds => _introCameraSeconds;
        /// <inheritdoc cref="_introStartDelay"/>
        public float IntroStartDelay => _introStartDelay;
        /// <inheritdoc cref="_introGrowSpeed"/>
        public float IntroGrowSpeed => _introGrowSpeed;
        /// <inheritdoc cref="_introSpeedScatter"/>
        public float IntroSpeedScatter => _introSpeedScatter;
        /// <inheritdoc cref="_introNodePop"/>
        public float IntroNodePop => _introNodePop;
        /// <inheritdoc cref="_introDotPop"/>
        public float IntroDotPop => _introDotPop;
        /// <inheritdoc cref="_introNodeSoundGap"/>
        public float IntroNodeSoundGap => _introNodeSoundGap;

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
        /// <inheritdoc cref="_backdropPaddingY"/>
        public float BackdropPaddingY => _backdropPaddingY;
        /// <inheritdoc cref="_tableMaterial"/>
        public Material TableMaterial => _tableMaterial;
        /// <inheritdoc cref="_tablePadding"/>
        public float TablePadding => _tablePadding;
        /// <inheritdoc cref="_tableLight"/>
        public float TableLight => _tableLight;
        /// <inheritdoc cref="_tableAmbient"/>
        public float TableAmbient => _tableAmbient;
        /// <inheritdoc cref="_tableTiling"/>
        public float TableTiling => _tableTiling;
        /// <inheritdoc cref="_menuBackdropMaterial"/>
        public Material MenuBackdropMaterial => _menuBackdropMaterial;

        // ── Цвета: единственный владелец — палитра (UI/Theme/tokens.*.uss → GuildmasterPalette) ──

        /// <summary>Подложка узла — одна на все типы. Тип читается ИКОНКОЙ, не цветом.</summary>
        public Color NodeBacking   => Role("--gm-color-map-node-backing");
        /// <summary>Обод узла.</summary>
        public Color NodeRim       => Role("--gm-color-map-node-rim");
        /// <summary>Метка узла, на котором стоит отряд.</summary>
        public Color CurrentMarker => Role("--gm-color-map-current-marker");
        /// <summary>Тёмный конец рампы перекраски иконки.</summary>
        public Color IconShadow    => Role("--gm-color-map-icon-shadow");
        /// <summary>Светлый конец рампы.</summary>
        public Color IconLight     => Role("--gm-color-map-icon-light");

        /// <inheritdoc cref="_dotRadius"/>
        public float DotRadius => _dotRadius;
        /// <inheritdoc cref="_dotSpacing"/>
        public float DotSpacing => _dotSpacing;
        /// <inheritdoc cref="_dotClearance"/>
        public float DotClearance => _dotClearance;
        /// <inheritdoc cref="_edgeCurve"/>
        public float EdgeCurve => _edgeCurve;
        /// <summary>Обычный путь — самые бледные точки.</summary>
        public Color PathIdle      => Role("--gm-color-map-path-idle");
        /// <summary>Пройденный маршрут — прочерченный по карте.</summary>
        public Color PathTravelled => Role("--gm-color-map-path-travelled");
        /// <summary>Путь к доступному узлу — самый яркий.</summary>
        public Color PathAvailable => Role("--gm-color-map-path-available");
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
        /// <summary>Цвет фишки отряда.</summary>
        public Color Pawn => Role("--gm-color-map-pawn");
        /// <inheritdoc cref="_pawnTravelSeconds"/>
        public float PawnTravelSeconds => _pawnTravelSeconds;
        /// <inheritdoc cref="_pawnSkipSpeed"/>
        public float PawnSkipSpeed => _pawnSkipSpeed;

        /// <inheritdoc cref="_transitionInSeconds"/>
        public float TransitionInSeconds => _transitionInSeconds;
        /// <inheritdoc cref="_transitionInkDelay"/>
        public float TransitionInkDelay => _transitionInkDelay;
        /// <inheritdoc cref="_transitionHoldSeconds"/>
        public float TransitionHoldSeconds => _transitionHoldSeconds;
        /// <inheritdoc cref="_transitionOutSeconds"/>
        public float TransitionOutSeconds => _transitionOutSeconds;

        /// <summary>Множитель яркости по состоянию узла.</summary>
        public Color StateTint(MapNodeVisualState state) => state switch
        {
            MapNodeVisualState.Available => _available,
            MapNodeVisualState.Current   => _current,
            MapNodeVisualState.Cleared   => _cleared,
            _                            => _locked,
        };

        // ── Резолв цвета из палитры ─────────────────────────────────────────

        // Кэш: карта спрашивает цвет на каждую точку каждой дорожки, а поиск в снимке — линейный.
        // Живёт до перезагрузки домена; пересборка палитры её и вызывает, так что устареть не успевает.
        private readonly Dictionary<string, Color> _resolved = new Dictionary<string, Color>(12);

        /// <summary>
        /// Цвет роли из палитры. Пустая ссылка или неизвестное имя — это баг разводки, а не повод
        /// рисовать чем попало: говорим вслух один раз на роль и отдаём пурпур, который нельзя не
        /// заметить. Молчаливый фолбэк здесь стоил бы ровно того расхождения, ради которого карта
        /// и переехала на палитру.
        /// </summary>
        private Color Role(string token)
        {
            if (_resolved.TryGetValue(token, out Color cached)) return cached;

            Color color = Color.magenta;
            if (_palette == null)
                Debug.LogError($"[MapStyle] - палитра не назначена, цвет '{token}' взять неоткуда " +
                               $"(ассет {name}).");
            else if (!_palette.TryGet(token, out color))
                Debug.LogError($"[MapStyle] - в палитре нет роли '{token}'. Пересобери снимок: " +
                               "Alebardium → Дизайн-система → Пересобрать палитру.");

            _resolved[token] = color;
            return color;
        }

        private void OnValidate() => _resolved.Clear();   // правка ассета в инспекторе не должна залипать в кэше
    }
}
