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

        [Tooltip("UXML глобальной панели забега (app-shell): режимы-навигация + HP/золото/акт/таймер/меню.")]
        [SerializeField] private VisualTreeAsset _runModeBar;

        [Tooltip("UXML лоадаут-хаба (гильдия: 4 сосуда + навешивание реликвий из запаса). Открывается кнопкой «Хаб».")]
        [SerializeField] private VisualTreeAsset _loadoutHubScreen;

        [Tooltip("UXML нового лоадаут/инвентарь-экрана (редизайн, Ф3a: трёхколоночник с таро-карточками). Открывается кнопкой «Хаб».")]
        [SerializeField] private VisualTreeAsset _loadoutInventoryScreen;

        [Tooltip("UXML таро-карточки реликвии (клонируется в грид нового инвентаря).")]
        [SerializeField] private VisualTreeAsset _arcanaCard;

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
        private ISubscriber<OpenOutcomeRequest> _openOutcomeSub;
        private ISubscriber<OpenMainMenuRequest> _openMainMenuSub;
        private IDisposable _openLoadoutSubscription;
        private IDisposable _openRewardSubscription;
        private IDisposable _openEventSubscription;
        private IDisposable _openContinueSubscription;
        private IDisposable _openShopSubscription;
        private IDisposable _openChestSubscription;
        private IDisposable _openCampSubscription;
        private IDisposable _openOutcomeSubscription;
        private IDisposable _openMainMenuSubscription;
        private UIDocument _doc;
        private RunModeBarView _topBar;
        private VisualElement _backdrop; // постоянный задний фон под не-боевыми экранами (выкл в бою/инвентаре)

        // Слои-контейнеры (Ф4, план II.4): фиксированный z-порядок = порядок добавления в корень панели.
        // Заменяют императивный BringToFront/SendToBack (снос K1/K2). Персистентные (backdrop/battle-center/
        // топбар) наполняет бутстрап; экраны навигатора кладутся в screens/modal им самим.
        private VisualElement _layerBackdrop;     // [0] задний фон забега
        private VisualElement _layerBattleCenter; // [1] «Начать»/таймер боя (ЗА экранами — QA #19/#23)
        private VisualElement _layerScreens;      // [2] Page/Sheet навигатора (под топбаром)
        private VisualElement _layerTopbar;       // [3] RunModeBar (над обычными экранами)
        private VisualElement _layerModal;        // [4] Modal навигатора (над топбаром, scrim накрывает его)
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
            ISubscriber<OpenCampRequest> openCampSub)
        {
            _openCampSub = openCampSub;
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
            _router.Initialize(_layerScreens, _layerModal, _pauseScreen, _settingsScreen, _loadoutScreen, _rewardScreen, _eventScreen, _continueScreen, _shopScreen, _chestScreen, _outcomeScreen, _mainMenuScreen, _loadoutHubScreen, _loadoutInventoryScreen, _arcanaCard, _campScreen);
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
            // Исход забега — запрос из GameFlow после акта.
            _openOutcomeSubscription = _openOutcomeSub?.Subscribe(req => _router.ShowOutcome(req));
            // Главное меню — запрос из GameFlow (верхний цикл).
            _openMainMenuSubscription = _openMainMenuSub?.Subscribe(req => _router.OpenMainMenu(req));

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
            AddLayer(root, "layer-tooltip");  // задел Трек Т (тултипы)
            AddLayer(root, "layer-system");   // задел II.13/Трек К (тосты/фид/dev-консоль)
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
        private void InitTopBar()
        {
            // Постоянный задний фон забега в СВОЁМ слое (Ф4, самый низ z). Виден на не-боевых экранах,
            // выключается в бою/инвентаре (RefreshShell). pickingMode Ignore — ввод не перехватывает.
            _backdrop = new VisualElement { name = "run-backdrop", pickingMode = PickingMode.Ignore };
            _backdrop.AddToClassList("gm-screen-backdrop");
            _backdrop.style.display = DisplayStyle.None;
            _layerBackdrop.Add(_backdrop);

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
            if (runActive && phase != BattlePhase.None)
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

            // Задний фон: виден на не-боевых экранах (меню/карта/ивент/сундук). Выкл в бою (видна арена)
            // и в инвентаре (прозрачный оверлей поверх арены).
            if (_backdrop != null)
            {
                // Фаза D: при world-карте фон ТОЖЕ гасим — карта уехала в мир, и непрозрачный backdrop
                // закрывал бы её собой (раньше он служил задником именно UITK-карты).
                // По той же причине гасим его под главным меню: за меню лежит мировой стол, и подложка
                // закрывала бы его собой — снаружи это выглядело как «фон не работает».
                bool showBackdrop = phase == BattlePhase.None && !_router.IsInventoryOpen
                                    && !_router.IsMapSpaceOpen && !_mainMenuOpen;
                _backdrop.style.display = showBackdrop ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // QA #11/#21/#35: подсветка активного таба из единого источника — верхний НЕ-Modal экран навигатора
            // (ActiveScreenMode = nav.ActiveModeTag, игнорит Modal) либо «Бой» по фазе. Modal-меню не сбивает таб.
            _topBar.SetActiveMode(ActiveMode(phase));
            // Настройки — не режим, а модалка: у их таба своё состояние «нажат, пока меню открыто» (раунд 2, п.6).
            _topBar.SetMenuActive(_router.IsSystemMenuOpen);
        }

        // ── Радио-режимы табов (Карта/Бой/Инвентарь — включён РОВНО один; таб = перейти в режим, НЕ тумблер) ──
        // Каждый метод приводит стек+мир к целевому режиму идемпотентными действиями (повтор = no-op).

        // «Инвентарь» = бой + инвентарь над ним: войти в бой (скрыть карту) + показать инвентарь поверх мира.
        private void GoToInventory()
        {
            UiTrace.Log($"topbar «Инвентарь» → GoToInventory (invOpen={_router.IsInventoryOpen}, phase={_clock?.Phase}, hasMap={_router.HasMapInStack})");
            if (_loadoutInventoryScreen == null) { _router.OpenHub(); return; } // фолбэк на старый хаб, если ассет не назначен
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
            _openLoadoutSubscription?.Dispose();
            _openRewardSubscription?.Dispose();
            _openEventSubscription?.Dispose();
            _openContinueSubscription?.Dispose();
            _openShopSubscription?.Dispose();
            _openChestSubscription?.Dispose();
            _openCampSubscription?.Dispose();
            _openOutcomeSubscription?.Dispose();
            _openMainMenuSubscription?.Dispose();
        }

        // QA #32: ESC открывает системное меню ТОЛЬКО в активном забеге (в главном меню/вне забега — no-op).
        // Внутри забега ToggleSystemMenu сам решает открыть/шаг-назад (семантика ESC, план II.4).
        private void OnMenuToggle()
        {
            if (_runStates?.Current != null) _router.ToggleSystemMenu();
        }
    }
}
