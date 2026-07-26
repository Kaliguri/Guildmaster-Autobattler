using System;
using System.Collections.Generic;
using Guildmaster.Core.Input;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Diagnostics;
using Guildmaster.Guild;
using MessagePipe;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Guildmaster.UI
{
    /// <summary>
    /// Точка входа рантайм-UI: держит постоянный <see cref="UIDocument"/> (в CoreScene, живёт всю сессию),
    /// отдаёт его корень роутеру и заводит ESC-открытие меню (<see cref="IInputService.MenuToggleRequested"/>).
    /// UXML-шаблоны экранов — сериализованные ссылки (из сцены, не DI). Инъекция — методом (VContainer
    /// <see cref="RegisterComponentInHierarchy"/> в RootLifetimeScope).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UiRootBootstrap : MonoBehaviour
    {
        [Tooltip("UXML системного меню (кнопки Return/Settings).")]
        [SerializeField] private VisualTreeAsset _pauseScreen;

        [Tooltip("UXML экрана настроек (3 слайдера + Save/Cancel/Defaults).")]
        [SerializeField] private VisualTreeAsset _settingsScreen;

        [Tooltip("UXML loadout-экрана (грид реликов + Accept/Save/Close). Открывается дабл-кликом по сосуду в расстановке.")]
        [SerializeField] private VisualTreeAsset _loadoutScreen;

        [Tooltip("UXML экрана награды после боя (витрина реликвий + Взять/Пропустить).")]
        [SerializeField] private VisualTreeAsset _rewardScreen;

        [Tooltip("UXML экрана текстового ивента (StS-style: заголовок, тело, варианты ответа).")]
        [SerializeField] private VisualTreeAsset _eventScreen;

        [Tooltip("UXML единой кнопки «Продолжить» (правый нижний угол, бит между узлом и картой).")]
        [SerializeField] private VisualTreeAsset _continueScreen;

        [Tooltip("UXML экрана магазина (витрина 4 слота, реролл, продажа).")]
        [SerializeField] private VisualTreeAsset _shopScreen;

        [Tooltip("UXML экрана сундука (фасад с кликабельной крышкой → награда 1-из-3).")]
        [SerializeField] private VisualTreeAsset _chestScreen;

        [Tooltip("UXML экрана привала (бюджет действий отряда + список трат).")]
        [SerializeField] private VisualTreeAsset _campScreen;

        [Tooltip("UXML экрана исхода забега (Победа/Поражение → В меню).")]
        [SerializeField] private VisualTreeAsset _outcomeScreen;

        [Tooltip("UXML главного меню (Начать/Продолжить/Настройки/Выход).")]
        [SerializeField] private VisualTreeAsset _mainMenuScreen;

        [Tooltip("UXML boot title card (Happy Guildmasters) до главного меню.")]
        [SerializeField] private VisualTreeAsset _titleCardScreen;

        [Tooltip("Печать/seal для boot title card (PixelLab AppIcon).")]
        [SerializeField] private Sprite _titleCardSeal;

        [Tooltip("UXML глобальной панели забега (app-shell): режимы-навигация + HP/золото/акт/таймер/меню.")]
        [SerializeField] private VisualTreeAsset _runModeBar;

        [Tooltip("UXML лоадаут/инвентарь-экрана (редизайн, Ф3a: трёхколоночник с таро-карточками). Открывается табом «Инвентарь».")]
        [SerializeField] private VisualTreeAsset _loadoutInventoryScreen;

        [Tooltip("UXML таро-карточки реликвии (клонируется в грид нового инвентаря).")]
        [SerializeField] private VisualTreeAsset _arcanaCard;

        [Tooltip("Материал чернильной шторки перехода (SH_Map_Transition). Рисуется в текстуру и кладётся " +
                 "фоном верхнего слоя. Пусто = ровное затемнение без узора.")]
        [SerializeField] private Material _transitionMaterial;

        private MenuRouter _router;
        private IInputService _input;
        private IBattleClock _clock;
        private RunStateService _runStates;
        private GameConfig _config;
        private ILocalizationService _loc;
        private ISubscriber<OpenLoadoutRequest> _openLoadoutSub;
        private ISubscriber<OpenRewardRequest> _openRewardSub;
        private ISubscriber<OpenTextEventRequest> _openEventSub;
        private ISubscriber<OpenContinueRequest> _openContinueSub;
        private ISubscriber<OpenShopRequest> _openShopSub;
        private ISubscriber<OpenChestRequest> _openChestSub;
        private ISubscriber<OpenCampRequest> _openCampSub;
        private ISubscriber<OpenNodeFarewellRequest> _openFarewellSub; // единый ритм конца узла (QA #48/#49)
        private IDisposable _openFarewellSubscription;
        private ISubscriber<OpenOutcomeRequest> _openOutcomeSub;
        private ISubscriber<OpenMainMenuRequest> _openMainMenuSub;
        private ISubscriber<OpenTitleCardRequest> _openTitleCardSub;
        private IDisposable _openLoadoutSubscription;
        private IDisposable _openRewardSubscription;
        private IDisposable _openEventSubscription;
        private IDisposable _openContinueSubscription;
        private IDisposable _openShopSubscription;
        private IDisposable _openChestSubscription;
        private IDisposable _openCampSubscription;
        private IDisposable _openOutcomeSubscription;
        private IDisposable _openMainMenuSubscription;
        private IDisposable _openTitleCardSubscription;
        private UIDocument _doc;
        private RunModeBarView _topBar;

        // Слои-контейнеры (Ф4, план II.4): фиксированный z-порядок = порядок добавления в корень панели.
        // Заменяют императивный BringToFront/SendToBack (снос K1/K2). Персистентные (backdrop/battle-center/
        // топбар) наполняет бутстрап; экраны навигатора кладутся в screens/modal им самим.
        private VisualElement _layerBackdrop;     // [0] задний фон забега
        private VisualElement _layerBattleCenter; // [1] «Начать»/таймер боя (ЗА экранами — QA #19/#23)
        private VisualElement _layerScreens;      // [2] Page/Sheet навигатора (под топбаром)
        private VisualElement _layerTopbar;       // [3] RunModeBar (над обычными экранами)
        private VisualElement _layerModal;        // [4] Modal навигатора (над топбаром, scrim накрывает его)
        private VisualElement _layerTooltip;      // [6] окно тултипа (Трек Т) — над топбаром и модалками
        private Tooltips.TooltipSystem _tooltips; // Трек Т: показыватель тултипов, привязан к слою в Start
        private UiSoundSystem _uiSound;           // звук интерфейса: один слушатель на корне панели
        private float _runElapsed;   // «рабочий» таймер забега (аккумулятор, RunState его не хранит)
        private BattlePhase _lastPhase = BattlePhase.None; // ребро смены фазы для RefreshShell (Ф4, K3)
        private bool _lastInventoryOpen; // ребро смены инвентаря для RefreshShell (Ф4; источник — _router.IsInventoryOpen)
        private IPublisher<RelicDragEvent> _relicDragPub; // QA #5: drag реликвии из грида → фаза расстановки
        private IPublisher<SetTestZoneRequest> _testZonePub; // радио-табы: целевое состояние тест-зоны (бой/не-бой)
        private ISubscriber<TestZoneChangedEvent> _testZoneChangedSub; // Ф5: СОСТОЯНИЕ тест-зоны → Sheet-экран
        private IDisposable _testZoneChangedSubscription;
        private ISubscriber<WorldMapSpaceChangedEvent> _mapSpaceSub; // фаза D: СОСТОЯНИЕ world-карты → Sheet-экран
        private IDisposable _mapSpaceSubscription;
        private IPublisher<SetWorldMapRequest> _worldMapPub; // фаза D: радио-табы → показать/скрыть карту в мире
        private ISubscriber<Core.Flow.MainMenuVisibilityChangedEvent> _mainMenuVisSub; // за меню виден мировой стол
        private IDisposable _mainMenuVisSubscription;
        private bool _mainMenuOpen;
        private IPublisher<Core.Flow.ScreenBackdropChangedEvent> _screenBackdropPub; // QA #50: единый задник экранов
        private bool _backdropShown; // последнее сказанное презентации — публикуем только по ребру
        private ISubscriber<Core.Flow.ScreenFadeChangedEvent> _screenFadeSub; // QA #47: шторка перехода поверх всего
        private IDisposable _screenFadeSubscription;
        private VisualElement _screenFade;
        private RenderTexture _fadeRt;
        private Material _fadeMat; // рабочая КОПИЯ материала перехода (ассет не трогаем — см. EnsureFadeMaterial)

        private const int FadeTextureHeight = 360; // высота картинки шторки; ширина считается по аспекту экрана
        private static readonly int FadeProgressId = Shader.PropertyToID("_Progress");
        private static readonly int FadeCenterId   = Shader.PropertyToID("_Center");
        private static readonly int FadeAspectId   = Shader.PropertyToID("_Aspect");
        private static readonly int FadeSeedId     = Shader.PropertyToID("_Seed");
        private static readonly int FadeShapeTexId = Shader.PropertyToID("_ShapeTex");
        private static readonly int FadeUseShapeId = Shader.PropertyToID("_UseShape");

        [Inject]
        public void Construct(MenuRouter router, IInputService input,
            IBattleClock clock, RunStateService runStates, GameConfig config, ILocalizationService loc,
            ISubscriber<OpenLoadoutRequest> openLoadoutSub, ISubscriber<OpenRewardRequest> openRewardSub,
            ISubscriber<OpenTextEventRequest> openEventSub,
            ISubscriber<OpenContinueRequest> openContinueSub, ISubscriber<OpenShopRequest> openShopSub,
            ISubscriber<OpenChestRequest> openChestSub, ISubscriber<OpenOutcomeRequest> openOutcomeSub,
            ISubscriber<OpenMainMenuRequest> openMainMenuSub, IPublisher<RelicDragEvent> relicDragPub,
            IPublisher<SetTestZoneRequest> testZonePub, ISubscriber<TestZoneChangedEvent> testZoneChangedSub,
            ISubscriber<WorldMapSpaceChangedEvent> mapSpaceSub, IPublisher<SetWorldMapRequest> worldMapPub,
            ISubscriber<Core.Flow.MainMenuVisibilityChangedEvent> mainMenuVisSub,
            IPublisher<Core.Flow.ScreenBackdropChangedEvent> screenBackdropPub,
            ISubscriber<Core.Flow.ScreenFadeChangedEvent> screenFadeSub,
            ISubscriber<OpenCampRequest> openCampSub,
            ISubscriber<OpenNodeFarewellRequest> openFarewellSub,
            ISubscriber<OpenTitleCardRequest> openTitleCardSub,
            Tooltips.TooltipSystem tooltips,
            UiSoundSystem uiSound)
        {
            _uiSound = uiSound;
            _tooltips = tooltips;
            _screenBackdropPub = screenBackdropPub;
            _screenFadeSub     = screenFadeSub;
            _openCampSub = openCampSub;
            _openFarewellSub = openFarewellSub;
            _openTitleCardSub = openTitleCardSub;
            _mainMenuVisSub = mainMenuVisSub;
            _router = router;
            _mapSpaceSub = mapSpaceSub;
            _worldMapPub = worldMapPub;
            _relicDragPub = relicDragPub;
            _testZonePub = testZonePub;
            _testZoneChangedSub = testZoneChangedSub;
            _input = input;
            _clock = clock;
            _runStates = runStates;
            _config = config;
            _loc = loc;
            _openLoadoutSub = openLoadoutSub;
            _openRewardSub = openRewardSub;
            _openEventSub = openEventSub;
            _openContinueSub = openContinueSub;
            _openShopSub = openShopSub;
            _openChestSub = openChestSub;
            _openOutcomeSub = openOutcomeSub;
            _openMainMenuSub = openMainMenuSub;
        }

        private void Awake() => _doc = GetComponent<UIDocument>();

        private void Start()
        {
            ApplyDeviceProfile(); // II.12.9: device-профиль на корне панели до прочей инициализации
            if (_router == null || _input == null)
            {
                Debug.LogWarning("[UiRootBootstrap] Нет инъекции (MenuRouter/IInputService) — в этой сцене отсутствует " +
                                 "RootLifetimeScope? Рантайм-меню отключено для этого объекта.");
                return;
            }
            BuildLayers(); // Ф4: скелет слоёв-контейнеров ДО инициализации роутера (навигатор кладёт экраны в них)
            // Трек Т: система тултипов слушает всплывающие запросы на КОРНЕ панели, а окно держит в своём
            // слое — поэтому привязка идёт сразу после слоёв и до построения экранов.
            _tooltips?.Attach(_doc.rootVisualElement, _layerTooltip);
            // Звук интерфейса ловится там же, на корне панели: клики и наведения всплывают до него со
            // всех экранов сразу, поэтому ни один экран не обязан знать про IAudioService.
            _uiSound?.Attach(_doc.rootVisualElement);
            _router.Initialize(_layerScreens, _layerModal, _pauseScreen, _settingsScreen, _loadoutScreen, _rewardScreen, _eventScreen, _continueScreen, _shopScreen, _chestScreen, _outcomeScreen, _mainMenuScreen, _loadoutInventoryScreen, _arcanaCard, _campScreen, _titleCardScreen, _titleCardSeal);
            _input.MenuToggleRequested += OnMenuToggle;
            // Открытие loadout по запросу из фазы расстановки (MessagePipe-событие с Data-пейлоадом).
            _openLoadoutSubscription = _openLoadoutSub?.Subscribe(req => _router.OpenLoadout(req));
            // Открытие экрана награды после боя (A3) — запрос из GameFlow.
            _openRewardSubscription = _openRewardSub?.Subscribe(req => _router.OpenReward(req));
            // Открытие текстового ивента (StS-style) — запрос из GameFlow.
            _openEventSubscription = _openEventSub?.Subscribe(req => _router.OpenTextEvent(req));
            // Единая кнопка «Продолжить» — запрос из петли акта (ContinuePresenter).
            _openContinueSubscription = _openContinueSub?.Subscribe(req => _router.ShowContinue(req));
            // Магазин — запрос из узла магазина (ShopFlow).
            _openShopSubscription = _openShopSub?.Subscribe(req => _router.OpenShop(req));
            // Сундук — запрос из узла сундука (ChestFlow).
            _openChestSubscription = _openChestSub?.Subscribe(req => _router.OpenChest(req));
            // Привал — запрос из узла привала (CampFlow).
            _openCampSubscription = _openCampSub?.Subscribe(req => _router.OpenCamp(req));
            _openFarewellSubscription = _openFarewellSub?.Subscribe(req => _router.ShowNodeFarewell(req));
            // Исход забега — запрос из GameFlow после акта.
            _openOutcomeSubscription = _openOutcomeSub?.Subscribe(req => _router.ShowOutcome(req));
            // Главное меню — запрос из GameFlow (верхний цикл).
            _openMainMenuSubscription = _openMainMenuSub?.Subscribe(req => _router.OpenMainMenu(req));
            // Boot title card — один раз до главного меню.
            _openTitleCardSubscription = _openTitleCardSub?.Subscribe(req => _router.ShowTitleCard(req));

            InitTopBar();

            // Ф4: подсветка табов и backdrop — по подписке на изменение стека навигатора (снос поллинга
            // структуры в Update, K3/K4). Смену фазы боя ВВОД пересчитывает через IBattleClock.PhaseChanged
            // (навигатор, K8); визуал shell тут ловит её дешёвым ребром в Update — кадр задержки визуалу не вреден.
            _router.Changed += RefreshShell;
            // Шов II.9.2: смена языка на лету перестраивает персистентный топбар (стек-экраны пересоздаются сами).
            if (_loc != null) _loc.LocaleChanged += RebuildTopBar;
            // Ф5: СОСТОЯНИЕ тест-зоны (владелец — DeploymentController) → показать/снять Sheet-экран «Бой».
            _testZoneChangedSubscription = _testZoneChangedSub?.Subscribe(e =>
            {
                UiTrace.Log($"bootstrap: TestZoneChanged(Active={e.Active}) → {(e.Active ? "ShowTestZone" : "HideTestZone")}");
                if (e.Active) _router.ShowTestZone();
                else          _router.HideTestZone();
            });
            // Фаза D: СОСТОЯНИЕ world-карты (владелец — WorldMapNodeChooser) → прозрачный Sheet «карта».
            // Сама карта рисуется в мире; Sheet нужен ради тега режима и контекста ввода InputContext.Map.
            _mapSpaceSubscription = _mapSpaceSub?.Subscribe(e =>
            {
                UiTrace.Log($"bootstrap: WorldMapSpaceChanged(Active={e.Active}) → {(e.Active ? "ShowMapSpace" : "HideMapSpace")}");
                if (e.Active) _router.ShowMapSpace();
                else          _router.HideMapSpace();
            });
            // Шторка перехода (QA #47): плотность считает тот, кто ведёт переход (карта акта), UI её рисует.
            _screenFadeSubscription = _screenFadeSub?.Subscribe(e => ApplyScreenFade(e.Progress, e.Center, e.Seed));

            // Главное меню открыто → гасим непрозрачную подложку, иначе она закроет собой мировой стол.
            _mainMenuVisSubscription = _mainMenuVisSub?.Subscribe(e =>
            {
                UiTrace.Log($"bootstrap: MainMenuVisibilityChanged(Visible={e.Visible}) → backdrop {(e.Visible ? "off" : "on")}");
                _mainMenuOpen = e.Visible;
                RefreshShell();
            });
            RefreshShell();
        }

        // --- Слои-контейнеры (Ф4, план II.4): фиксированный z-порядок вместо BringToFront/SendToBack ---
        // Порядок Add = порядок отрисовки. cursors/tooltip/system — заделы под будущие треки (курсоры/тултипы/
        // dev-консоль-тосты): пустые слои поверх, pickingMode Ignore, наполняются позже без ретрофита.
        private void BuildLayers()
        {
            VisualElement root = _doc.rootVisualElement;
            _layerBackdrop     = AddLayer(root, "layer-backdrop");
            _layerBattleCenter = AddLayer(root, "layer-battle-center");
            _layerScreens      = AddLayer(root, "layer-screens");
            _layerTopbar       = AddLayer(root, "layer-topbar");
            _layerModal        = AddLayer(root, "layer-modal");
            AddLayer(root, "layer-cursors");  // задел II.14 (live-курсоры)
            _layerTooltip = AddLayer(root, "layer-tooltip"); // Трек Т: окно тултипа над топбаром и модалками
            AddLayer(root, "layer-system");   // задел II.13/Трек К (тосты/фид/dev-консоль)

            // Шторка перехода — САМЫЙ верх (QA #47): она обязана накрывать и топбар, и модалки. Всё, что
            // ниже, гасится ею целиком; никаких исключений у перехода между сценами узла быть не должно.
            VisualElement fadeLayer = AddLayer(root, "layer-transition");
            _screenFade = new VisualElement { name = "screen-fade", pickingMode = PickingMode.Ignore };
            _screenFade.AddToClassList("gm-screen-fade");
            _screenFade.style.display = DisplayStyle.None;
            fadeLayer.Add(_screenFade);
        }

        // Слой = fullscreen-контейнер, растянутый по корню панели. pickingMode Ignore: сам контейнер не крадёт
        // клики (интерактивные ДЕТИ пикаются как обычно) — «дырки» между экранами остаются кликабельны в мир.
        private static VisualElement AddLayer(VisualElement root, string layerName)
        {
            var layer = new VisualElement { name = layerName, pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = 0; layer.style.top = 0; layer.style.right = 0; layer.style.bottom = 0;
            root.Add(layer);
            return layer;
        }

        // Глобальная панель забега (app-shell) — постоянный НЕ-модальный слой сверху (в обход стека
        // MenuRouter, чтобы не глушить ввод). Режимы-навигация + HP/золото/акт/таймер/меню. Тело экранов
        // сдвинуто под неё (padding-top). Видимость и центр (Начать↔таймер) — по фазе боя в Update.
        // Слой backdrop остаётся пустым: задник экранов рисует презентация (стол из MapStyle), а UI лишь
        // говорит, когда он нужен — ScreenBackdropChangedEvent из RefreshShell (QA #50). Слой держим, чтобы
        // z-порядок остальных не поехал и было куда положить будущие фоновые элементы UI.
        private void InitTopBar()
        {
            CreateAndPlaceTopBar();
        }

        // Создать топбар и разместить его в слоях (Ф4). Вынесено из InitTopBar ради hot-swap локали (II.9.2):
        // при смене языка топбар пересоздаётся из UXML с актуальными строками. «Начать»/таймер (battle-center)
        // живут в ОТДЕЛЬНОМ слое ПОД экранами (QA #19/#23) — порядок слоёв даёт z без SendToBack (снос K2).
        private void CreateAndPlaceTopBar()
        {
            if (_runModeBar == null) return;
            _topBar = new RunModeBarView(
                _runModeBar,
                key => _loc?.GetString(key),
                onMap: GoToMap,             // радио-режимы: таб = перейти в режим (не тумблер)
                onBattle: GoToBattle,
                onInventory: GoToInventory,
                onTactics: () => { },       // задел под будущий экран AI-тактики
                onCompendium: () => { },    // задел под компендиум
                onMenu: () => _router.ToggleSystemMenu(),
                onStart: () => _clock?.RequestStart());

            _topBar.Root.style.display = DisplayStyle.None; // скрыта, пока нет активного забега
            _layerTopbar.Add(_topBar.Root);

            // battle-center — узел RunModeBar.uxml; переносим в свой слой-контейнер (ссылки RunModeBarView на
            // btn-start/battle-timer закешированы в конструкторе → переживают перемещение). z даёт порядок слоёв.
            var battleCenter = _topBar.Root.Q<VisualElement>("battle-center");
            if (battleCenter != null)
            {
                battleCenter.RemoveFromHierarchy();
                _layerBattleCenter.Add(battleCenter);
            }
        }

        // Шов II.9.2: пересобрать персистентный топбар при смене локали (стек-экраны локаль подхватят сами —
        // они пересоздаются на каждый показ). Снять старый из слоёв, создать новый, вернуть shell в актуальный вид.
        private void RebuildTopBar()
        {
            if (_topBar == null) return;
            _topBar.Root.RemoveFromHierarchy();
            _layerBattleCenter.Clear(); // старый battle-center жил здесь
            _topBar = null;
            CreateAndPlaceTopBar();
            RefreshShell();
        }

        private void Update()
        {
            ApplyDeviceProfile(); // II.12.9: переоценка профиля при смене разрешения (дёшево — сравнение int)
            if (_topBar == null || _clock == null) return;

            // Глобальный топбар виден ВЕСЬ забег (реш. №65, STS-style); тело экранов под ним (padding-top).
            // НО не под главным меню: RunState там ещё жив (сейв не сбрасывается, забег можно продолжить),
            // и по одному лишь runActive панель с «Начать» оставалась висеть поверх меню (наход. Макса, п.9).
            RunState run = _runStates?.Current;
            bool runActive = run != null && !_mainMenuOpen;
            _topBar.Root.style.display = runActive ? DisplayStyle.Flex : DisplayStyle.None;

            BattlePhase phase = _clock.Phase;

            // «Начать»/таймер боя (battle-center) в своём слое ПОД экранами (Ф4). Управляем ЯВНО: виден только
            // когда идёт забег И фаза боя/расстановки (Deployment→«Начать», Fighting→таймер). Иначе (главное
            // меню/карта/магазин/нет забега) — скрыт. Данные боя (таймер тикает) — законный поллинг каждый кадр.
            // Центр панели живёт только у боя: расстановка → «Начать», бой → таймер. В передышке (Interlude)
            // начинать нечего и считать нечего — центр пуст, чтобы «Начать» не звало в несуществующий бой.
            if (runActive && (phase == BattlePhase.Deployment || phase == BattlePhase.Fighting))
                _topBar.SetFighting(phase == BattlePhase.Fighting, FormatTime(_clock.ElapsedSeconds));
            else
                _topBar.HideBattleCenter();

            // Ф4: структуру shell (backdrop + подсветка таба) пересчитываем по РЕБРУ фазы/инвентаря; стек ловится
            // подпиской nav.Changed → RefreshShell. Ввод на смену фазы идёт через IBattleClock.PhaseChanged (K8);
            // визуалу тут дешёвого ребра достаточно — кадр задержки не вреден, а подписка добавила бы lifecycle-риск.
            bool inventoryOpen = _router.IsInventoryOpen;
            if (phase != _lastPhase || inventoryOpen != _lastInventoryOpen)
            {
                _lastPhase = phase;
                _lastInventoryOpen = inventoryOpen;
                RefreshShell();
            }

            if (!runActive) { _runElapsed = 0f; return; }

            _runElapsed += UnityEngine.Time.unscaledDeltaTime; // «рабочий» таймер забега
            _topBar.SetGold(run.Gold);
            _topBar.SetAct(run.CurrentActIndex + 1);
            _topBar.SetRestarts(run.RestartsRemaining, _config != null ? _config.RestartsPerAct : run.RestartsRemaining);
            _topBar.SetRunTime(FormatTime(_runElapsed));
            UpdateFloor(run);
        }

        // «Веха» в топбаре: глубина текущего узла по карте акта + сколько их всего. Считается из графа,
        // а не из отдельного счётчика — иначе после перегенерации карты счётчик разъедется с реальностью.
        private void UpdateFloor(RunState run)
        {
            Guildmaster.Guild.MapState map = run.Map;
            if (map == null || map.Nodes == null || map.Nodes.Length == 0) return;

            int current = 0, last = 0;
            for (int i = 0; i < map.Nodes.Length; i++)
            {
                Guildmaster.Guild.MapNode node = map.Nodes[i];
                if (node == null) continue;
                if (node.Floor > last) last = node.Floor;
                if (node.Id == map.CurrentNodeId) current = node.Floor;
            }

            // Floor нумеруется с нуля (0 = Start), игроку показываем по-человечески с единицы.
            _topBar.SetFloor(current + 1, last + 1);
        }

        // Ф4: структурный вид shell — backdrop и подсветка таба. Дёргается по подписке nav.Changed (изменение
        // стека) и по ребру фазы/инвентаря из Update. Заменяет поллинг структуры каждый кадр (снос K3/K4).
        private void RefreshShell()
        {
            if (_clock == null) return;
            BattlePhase phase = _clock.Phase;

            // Задний фон экранов — ОДИН на всю игру: стол, который презентация рисует под главным меню
            // (MenuBackdropView, материал из MapStyle). Своей непрозрачной заливки у UI больше нет: рядом
            // с настоящим столом она читалась как чёрный экран (QA #50, «единый источник правды»).
            //
            // Нужен он ровно там, где мир закрыт непрозрачной страницей — главное меню, ивент, магазин,
            // сундук, награда, исход. Гасим, когда за UI живой мир: карта (уехала в мир, фон закрыл бы её),
            // инвентарь (прозрачный оверлей поверх арены), бой и передышка (Interlude — ЖИВАЯ арена: досмотр
            // добивания и всё, что игрок делает между узлами, подложка накрывала бы собой).
            //
            // Фазу тут больше не спрашиваем: правду про «что сейчас на экране» знает стек, а не бой. Экран
            // пройденного ивента живёт и в Interlude (QA #49) — по фазе фон под ним мигал бы на арену.
            bool needBackdrop = (_mainMenuOpen || _router.HasVisiblePage)
                                && !_router.IsInventoryOpen && !_router.IsMapSpaceOpen;
            if (needBackdrop != _backdropShown)
            {
                _backdropShown = needBackdrop;
                _screenBackdropPub?.Publish(new Core.Flow.ScreenBackdropChangedEvent(needBackdrop));
            }

            // QA #11/#21/#35: подсветка активного таба из единого источника — верхний НЕ-Modal экран навигатора
            // (ActiveScreenMode = nav.ActiveModeTag, игнорит Modal) либо «Бой» по фазе. Modal-меню не сбивает таб.
            _topBar.SetActiveMode(ActiveMode(phase));
            // Настройки — не режим, а модалка: у их таба своё состояние «нажат, пока меню открыто» (раунд 2, п.6).
            _topBar.SetMenuActive(_router.IsSystemMenuOpen);
        }

        // Шторка перехода (QA #47). Живёт в самом верхнем слое и накрывает ВСЁ, включая топбар и модалки.
        // Полностью прозрачную снимаем из отрисовки (display:None), чтобы не держать лишний слой в лэйауте.
        //
        // Рисует её НАСТОЯЩИЙ шейдер чернил (QA #53): UI Toolkit чужих шейдеров не знает, но картинку
        // показать умеет — поэтому материал рисуем в небольшую текстуру и отдаём её элементу фоном. Так
        // вернулся узор растекающихся чернил, потерянный, когда шторка переехала из мира в UI. Ровная
        // заливка остаётся фолбэком на случай, если материал не назначен.
        private void ApplyScreenFade(float progress, Vector2 center, Vector4 seed)
        {
            if (_screenFade == null) return;

            float p = Mathf.Clamp01(progress);
            bool visible = p > 0.001f;
            _screenFade.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;

            if (_transitionMaterial == null)
            {
                // Фолбэк без узора: плотность держим в альфе заливки, а не в opacity элемента — цвета
                // у класса нет, его источник тут же, рядом с материалом.
                _screenFade.style.opacity = 1f;
                _screenFade.style.backgroundColor = new Color(0.055f, 0.043f, 0.031f, p);
                return;
            }

            RenderTexture rt = EnsureFadeTexture();
            Material mat = EnsureFadeMaterial();
            mat.SetFloat(FadeProgressId, p);
            mat.SetVector(FadeCenterId, center);
            mat.SetVector(FadeSeedId, seed);
            mat.SetFloat(FadeAspectId, (float)rt.width / Mathf.Max(1, rt.height));

            // Форму смыкания берём из рисунка ТОЛЬКО когда он есть: пустой слот в шейдере читается как
            // чёрная текстура, и без этой проверки кадр закрывался бы разом, а не сходился к точке.
            mat.SetFloat(FadeUseShapeId, mat.GetTexture(FadeShapeTexId) != null ? 1f : 0f);

            // Чистим цель перед отрисовкой: у шейдера прозрачный блендинг, и без очистки кадры копились бы
            // друг на друге, а шторка чернела бы сама по себе.
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;

            Graphics.Blit(Texture2D.whiteTexture, rt, mat);

            _screenFade.style.opacity = 1f; // плотность внутри картинки, а не в прозрачности элемента
            _screenFade.style.backgroundImage = Background.FromRenderTexture(rt);
        }

        // Рисуем КОПИЕЙ материала, а не самим ассетом. Ход перехода пишется в параметры каждый кадр, и на
        // общем ассете это грязнило бы проект: после каждого play-теста в .mat оседали чужие прогресс,
        // центр и жребий, и они уезжали в git как «изменение».
        private Material EnsureFadeMaterial()
        {
            if (_fadeMat == null)
                _fadeMat = new Material(_transitionMaterial) { name = _transitionMaterial.name + " (runtime)" };
            return _fadeMat;
        }

        // Текстура шторки НАМЕРЕННО мельче экрана: дизеринг чернил рисуется её пикселями, и на полном
        // разрешении растр стал бы невидимой рябью вместо крупного зерна, к которому привязан наш пиксель-арт.
        private RenderTexture EnsureFadeTexture()
        {
            int height = FadeTextureHeight;
            int width  = Mathf.Max(1, Mathf.RoundToInt(height * Screen.width / (float)Mathf.Max(1, Screen.height)));

            if (_fadeRt != null && _fadeRt.width == width && _fadeRt.height == height) return _fadeRt;

            if (_fadeRt != null) _fadeRt.Release();
            _fadeRt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name       = "gm-screen-fade",
                filterMode = FilterMode.Point, // растягиваем без сглаживания — зерно остаётся зерном
                wrapMode   = TextureWrapMode.Clamp,
            };
            _fadeRt.Create();
            return _fadeRt;
        }

        // ── Радио-режимы табов (Карта/Бой/Инвентарь — включён РОВНО один; таб = перейти в режим, НЕ тумблер) ──
        // Каждый метод приводит стек+мир к целевому режиму идемпотентными действиями (повтор = no-op).

        // «Инвентарь» = бой + инвентарь над ним: войти в бой (скрыть карту) + показать инвентарь поверх мира.
        private void GoToInventory()
        {
            UiTrace.Log($"topbar «Инвентарь» → GoToInventory (invOpen={_router.IsInventoryOpen}, phase={_clock?.Phase}, hasMap={_router.HasMapInStack})");
            // Неназначенный UXML — баг разводки, а не режим работы: роутер скажет об этом громко
            // (CannotShow), а до билда его ловит SceneWiringTests. Прежний фолбэк открывал вместо
            // инвентаря старый хаб — и этим держал живой целую мёртвую ветку (аудит 2026-07-26, R1-21).
            RequestWorldMap(false); // инвентарь смотрит на мир, а не на карту — идемпотентно
            RequestTestZone(true);  // сначала бой — идемпотентно
            int gold = _runStates?.Current != null ? _runStates.Current.Gold : 0;
            // QA #5: drag карточки реликвии → публикуем RelicDragEvent, фаза расстановки рисует призрак и надевает.
            _router.ShowInventory(gold, PublishRelicDrag); // инвентарь над боем — идемпотентно
        }

        private void PublishRelicDrag(Guildmaster.Data.Definitions.RelicData relic, RelicDragPhase phase)
            => _relicDragPub?.Publish(new RelicDragEvent(relic, phase));

        // «Бой» = чистый бой: закрыть инвентарь + войти в бой (скрыть карту). Повтор «Бой» = уже в бою = no-op
        // (никакого выхода на карту — это был баг тумблера). Выход из боя на карту = таб «Карта».
        private void GoToBattle()
        {
            if (_clock == null) return;
            UiTrace.Log($"topbar «Бой» → GoToBattle (invOpen={_router.IsInventoryOpen}, phase={_clock.Phase}, hasMap={_router.HasMapInStack})");
            _router.HideInventory(); // идемпотентно
            RequestWorldMap(false);  // убрать карту и вернуть камеру в бой — идемпотентно
            RequestTestZone(true);   // войти в бой — идемпотентно
        }

        // «Карта» = показать карту: закрыть инвентарь + выйти из боя (карта петли под геймплеем вернётся). Если
        // карты петли в стеке нет (реальный бой/меню) — read-only просмотр текущей карты поверх мира.
        private void GoToMap()
        {
            UiTrace.Log($"topbar «Карта» → GoToMap (invOpen={_router.IsInventoryOpen}, phase={_clock?.Phase}, hasMap={_router.HasMapInStack})");
            _router.HideInventory();  // идемпотентно
            RequestTestZone(false);   // выйти из тест-зоны — идемпотентно
            // Фаза D: карта живёт в мире. Показываем её ВСЕГДА, в том числе посреди идущего боя — бой
            // продолжается за кадром, камера просто уезжает в зону карты. Узлы при этом горят, лишь если
            // петля реально ждёт выбор (после «Продолжить»); иначе это чистый просмотр.
            RequestWorldMap(true);
        }

        // Publish целевого состояния тест-зоны (радио). Владелец (DeploymentController) приводит мир к бою/не-бою
        // идемпотентно; результат — TestZoneChangedEvent → Sheet-экран навигатора.
        private void RequestTestZone(bool active) => _testZonePub?.Publish(new SetTestZoneRequest(active));

        // Publish целевого состояния world-карты (радио, как тест-зона). Владелец (WorldMapController)
        // приводит мир к цели идемпотентно; результат — WorldMapSpaceChangedEvent → Sheet-экран.
        private void RequestWorldMap(bool visible) => _worldMapPub?.Publish(new SetWorldMapRequest(visible));

        // UITK-карта снесена целиком: и read-only просмотр, и выбор узла идут одним путём через
        // WorldMapController в мире. Второй UI-путь к той же карте плодил расхождения, а держать его
        // «на всякий случай» значило чинить каждый баг дважды.

        // Активный режим для подсветки таба (QA #11/#21) — ЕДИНЫЙ источник: верхний оверлей роутера несёт
        // mode-тег (inventory/map, ставится при Push). У карты этот тег несёт её прозрачное Sheet-пространство.
        // Нет оверлея → активен «Бой», если идёт бой/расстановка (Phase != None).
        private string ActiveMode(BattlePhase phase)
        {
            string overlay = _router?.ActiveScreenMode;
            if (overlay != null)           return overlay;
            if (phase != BattlePhase.None) return "battle";
            return null;
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.FloorToInt(seconds);
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        // --- Device-профили (II.12.9): USS-класс профиля на корне панели под адаптивные правки.
        // В UITK нет media queries → профиль выражаем классом, а Deck/ultrawide-стили вешаются
        // ПОЗЖЕ точечно правилами под этими классами (сейчас правил ноль — это только шов).
        // HARD: никаких C#-ветвлений по разрешению в экранах — только эти классы.
        private const string DeviceDesktop = "gm-device--desktop";
        private const string DeviceDeck = "gm-device--deck";
        private const string DeviceUltrawide = "gm-device--ultrawide";
        private int _lastDeviceW = -1;
        private int _lastDeviceH = -1;

        private void ApplyDeviceProfile()
        {
            if (_doc == null) return;
            int w = Screen.width, h = Screen.height;
            if (w == _lastDeviceW && h == _lastDeviceH) return;
            _lastDeviceW = w;
            _lastDeviceH = h;

            VisualElement root = _doc.rootVisualElement;
            if (root == null) return;
            root.RemoveFromClassList(DeviceDesktop);
            root.RemoveFromClassList(DeviceDeck);
            root.RemoveFromClassList(DeviceUltrawide);

            float aspect = h > 0 ? (float)w / h : 0f;
            string profile;
            // Steam Deck — нативная панель 1280×800 (aspect 1.6). Полноценный детект через
            // Facepunch SteamUtils отложен до появления Steam-фасада (без него API не дёргаем).
            if (w == 1280 && h == 800) profile = DeviceDeck;
            else if (aspect >= 2.2f)   profile = DeviceUltrawide; // ultrawide 21:9+ (II.12.9)
            else                       profile = DeviceDesktop;
            root.AddToClassList(profile);
        }

        private void OnDestroy()
        {
            if (_input != null) _input.MenuToggleRequested -= OnMenuToggle;
            if (_router != null) _router.Changed -= RefreshShell;     // Ф4
            if (_loc != null) _loc.LocaleChanged -= RebuildTopBar;    // шов II.9.2
            _testZoneChangedSubscription?.Dispose();                  // Ф5
            _mapSpaceSubscription?.Dispose();                         // фаза D
            _mainMenuVisSubscription?.Dispose();                      // фон за главным меню
            _screenFadeSubscription?.Dispose();                       // QA #47: шторка перехода
            _openFarewellSubscription?.Dispose();                     // QA #48/#49: прощание узла
            _openLoadoutSubscription?.Dispose();
            _openRewardSubscription?.Dispose();
            _openEventSubscription?.Dispose();
            _openContinueSubscription?.Dispose();
            _openShopSubscription?.Dispose();
            _openChestSubscription?.Dispose();
            _openCampSubscription?.Dispose();
            _openOutcomeSubscription?.Dispose();
            _openMainMenuSubscription?.Dispose();
            _openTitleCardSubscription?.Dispose();

            _tooltips?.Detach();                                      // Трек Т: снять окно и подписки с панели

            if (_fadeRt != null) { _fadeRt.Release(); _fadeRt = null; } // цель шторки живёт вне GC — освобождаем руками
            if (_fadeMat != null) { Destroy(_fadeMat); _fadeMat = null; }
        }

        // Семантика ESC (план II.4, КОНСТИТУЦИЯ): показан тултип → ESC гасит ЕГО и меню не трогает.
        // QA #32: сам ESC-вызов меню работает ТОЛЬКО в активном забеге (в главном меню/вне забега — no-op).
        // Внутри забега ToggleSystemMenu сам решает открыть/шаг-назад.
        private void OnMenuToggle()
        {
            if (_tooltips != null && _tooltips.HideAll()) return;
            if (_runStates?.Current != null) _router.ToggleSystemMenu();
        }
    }
}
