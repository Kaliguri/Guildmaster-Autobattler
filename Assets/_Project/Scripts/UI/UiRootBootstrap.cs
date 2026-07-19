using System;
using System.Collections.Generic;
using Guildmaster.Core.Input;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
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

        [Tooltip("UXML экрана карты акта (граф узлов слева→направо, клик по доступному узлу).")]
        [SerializeField] private VisualTreeAsset _mapScreen;

        [Tooltip("UXML единой кнопки «Продолжить» (правый нижний угол, бит между узлом и картой).")]
        [SerializeField] private VisualTreeAsset _continueScreen;

        [Tooltip("UXML экрана магазина (витрина 4 слота, реролл, продажа).")]
        [SerializeField] private VisualTreeAsset _shopScreen;

        [Tooltip("UXML экрана сундука (фасад с кликабельной крышкой → награда 1-из-3).")]
        [SerializeField] private VisualTreeAsset _chestScreen;

        [Tooltip("UXML экрана исхода забега (Победа/Поражение → В меню).")]
        [SerializeField] private VisualTreeAsset _outcomeScreen;

        [Tooltip("UXML главного меню (Начать/Продолжить/Настройки/Выход).")]
        [SerializeField] private VisualTreeAsset _mainMenuScreen;

        [Tooltip("UXML верхней панели забега (StS-style: хаб/опции/золото, центр «Начать»↔таймер, акт/время). LEGACY — заменён на _runModeBar.")]
        [SerializeField] private VisualTreeAsset _runTopBar;

        [Tooltip("UXML глобальной панели забега (app-shell редизайн): режимы-навигация + HP/золото/акт/таймер/меню. Заменяет _runTopBar.")]
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
        private ISubscriber<OpenMapRequest> _openMapSub;
        private ISubscriber<OpenContinueRequest> _openContinueSub;
        private ISubscriber<OpenShopRequest> _openShopSub;
        private ISubscriber<OpenChestRequest> _openChestSub;
        private ISubscriber<OpenOutcomeRequest> _openOutcomeSub;
        private ISubscriber<OpenMainMenuRequest> _openMainMenuSub;
        private IDisposable _openLoadoutSubscription;
        private IDisposable _openRewardSubscription;
        private IDisposable _openEventSubscription;
        private IDisposable _openMapSubscription;
        private IDisposable _openContinueSubscription;
        private IDisposable _openShopSubscription;
        private IDisposable _openChestSubscription;
        private IDisposable _openOutcomeSubscription;
        private IDisposable _openMainMenuSubscription;
        private UIDocument _doc;
        private IRunTopBar _topBar;
        private VisualElement _backdrop; // постоянный задний фон под не-боевыми экранами (выкл в бою/инвентаре)
        private bool _inventoryOpen; // инвентарь открыт → подсветить режим «Инвентарь» + тумблер
        private bool _mapOpen;       // read-only карта (кнопка «Карта») открыта → подсветить режим «Карта»
        private float _runElapsed;   // «рабочий» таймер забега (аккумулятор, RunState его не хранит)

        [Inject]
        public void Construct(MenuRouter router, IInputService input,
            IBattleClock clock, RunStateService runStates, GameConfig config, ILocalizationService loc,
            ISubscriber<OpenLoadoutRequest> openLoadoutSub, ISubscriber<OpenRewardRequest> openRewardSub,
            ISubscriber<OpenTextEventRequest> openEventSub, ISubscriber<OpenMapRequest> openMapSub,
            ISubscriber<OpenContinueRequest> openContinueSub, ISubscriber<OpenShopRequest> openShopSub,
            ISubscriber<OpenChestRequest> openChestSub, ISubscriber<OpenOutcomeRequest> openOutcomeSub,
            ISubscriber<OpenMainMenuRequest> openMainMenuSub)
        {
            _router = router;
            _input = input;
            _clock = clock;
            _runStates = runStates;
            _config = config;
            _loc = loc;
            _openLoadoutSub = openLoadoutSub;
            _openRewardSub = openRewardSub;
            _openEventSub = openEventSub;
            _openMapSub = openMapSub;
            _openContinueSub = openContinueSub;
            _openShopSub = openShopSub;
            _openChestSub = openChestSub;
            _openOutcomeSub = openOutcomeSub;
            _openMainMenuSub = openMainMenuSub;
        }

        private void Awake() => _doc = GetComponent<UIDocument>();

        private void Start()
        {
            if (_router == null || _input == null)
            {
                Debug.LogWarning("[UiRootBootstrap] Нет инъекции (MenuRouter/IInputService) — в этой сцене отсутствует " +
                                 "RootLifetimeScope? Рантайм-меню отключено для этого объекта.");
                return;
            }
            _router.Initialize(_doc.rootVisualElement, _pauseScreen, _settingsScreen, _loadoutScreen, _rewardScreen, _eventScreen, _mapScreen, _continueScreen, _shopScreen, _chestScreen, _outcomeScreen, _mainMenuScreen, _loadoutHubScreen, _loadoutInventoryScreen, _arcanaCard);
            _input.MenuToggleRequested += OnMenuToggle;
            // Открытие loadout по запросу из фазы расстановки (MessagePipe-событие с Data-пейлоадом).
            _openLoadoutSubscription = _openLoadoutSub?.Subscribe(req => _router.OpenLoadout(req));
            // Открытие экрана награды после боя (A3) — запрос из GameFlow.
            _openRewardSubscription = _openRewardSub?.Subscribe(req => _router.OpenReward(req));
            // Открытие текстового ивента (StS-style) — запрос из GameFlow.
            _openEventSubscription = _openEventSub?.Subscribe(req => _router.OpenTextEvent(req));
            // Открытие карты акта — запрос из петли акта (MapScreenNodeChooser).
            _openMapSubscription = _openMapSub?.Subscribe(req => _router.OpenMap(req));
            // Единая кнопка «Продолжить» — запрос из петли акта (ContinuePresenter).
            _openContinueSubscription = _openContinueSub?.Subscribe(req => _router.ShowContinue(req));
            // Магазин — запрос из узла магазина (ShopFlow).
            _openShopSubscription = _openShopSub?.Subscribe(req => _router.OpenShop(req));
            // Сундук — запрос из узла сундука (ChestFlow).
            _openChestSubscription = _openChestSub?.Subscribe(req => _router.OpenChest(req));
            // Исход забега — запрос из GameFlow после акта.
            _openOutcomeSubscription = _openOutcomeSub?.Subscribe(req => _router.ShowOutcome(req));
            // Главное меню — запрос из GameFlow (верхний цикл).
            _openMainMenuSubscription = _openMainMenuSub?.Subscribe(req => _router.OpenMainMenu(req));

            InitTopBar();
        }

        // Глобальная панель забега (app-shell) — постоянный НЕ-модальный слой сверху (в обход стека
        // MenuRouter, чтобы не глушить ввод). Режимы-навигация + HP/золото/акт/таймер/меню. Тело экранов
        // сдвинуто под неё (padding-top). Видимость и центр (Начать↔таймер) — по фазе боя в Update.
        private void InitTopBar()
        {
            // Постоянный задний фон забега: лежит ПОД всем UI (SendToBack), виден на не-боевых экранах.
            // Видимостью управляет Update (выкл в бою и в инвентаре). pickingMode Ignore — ввод не перехватывает.
            _backdrop = new VisualElement { name = "run-backdrop", pickingMode = PickingMode.Ignore };
            _backdrop.AddToClassList("gm-screen-backdrop");
            _backdrop.style.display = DisplayStyle.None;
            _doc.rootVisualElement.Add(_backdrop);
            _backdrop.SendToBack();

            if (_runModeBar != null)
            {
                _topBar = new RunModeBarView(
                    _runModeBar,
                    key => _loc?.GetString(key),
                    onMap: OpenMapView,
                    // «Бой» = вернуться в боевой вид, ТОЛЬКО когда бой реально идёт. Вне боя (Phase None)
                    // на стеке висит карта петли акта — её CloseOverlays снёс бы, а DetachFromPanelEvent
                    // резолвил бы выбор узла как null → петля акта падает (вылет из play). No-op вне боя.
                    onBattle: () => { if (_clock != null && _clock.Phase != BattlePhase.None) _router.CloseOverlays(); },
                    onInventory: ToggleInventory,
                    onTactics: () => { },       // задел под будущий экран AI-тактики
                    onCompendium: () => { },    // задел под компендиум
                    onMenu: () => _router.ToggleSystemMenu(),
                    onStart: () => _clock?.RequestStart());
            }
            else if (_runTopBar != null)
            {
                // Фолбэк, пока _runModeBar не назначен в CoreScene (координация с параллельной работой по сцене):
                // старая панель работает как раньше, «Гильдия» открывает новый инвентарь.
                _topBar = new RunTopBarView(
                    _runTopBar,
                    key => _loc?.GetString(key),
                    onHub: ToggleInventory,
                    onSettings: () => _router.OpenSettings(),
                    onStart: () => _clock?.RequestStart());
            }
            else return;

            _topBar.Root.style.display = DisplayStyle.None; // скрыта, пока нет активного забега
            _doc.rootVisualElement.Add(_topBar.Root);
        }

        private void Update()
        {
            if (_topBar == null || _clock == null) return;

            // Задний фон: виден на не-боевых экранах (меню/карта/ивент/сундук). Выключается в бою
            // (Phase != None — видна арена с юнитами) и в инвентаре (прозрачный оверлей поверх арены).
            if (_backdrop != null)
            {
                bool showBackdrop = _clock.Phase == BattlePhase.None && !_inventoryOpen;
                _backdrop.style.display = showBackdrop ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Глобальный топбар виден ВЕСЬ забег (реш. №65, STS-style); тело экранов под ним (padding-top).
            RunState run = _runStates?.Current;
            bool runActive = run != null;
            _topBar.Root.style.display = runActive ? DisplayStyle.Flex : DisplayStyle.None;
            if (!runActive) { _runElapsed = 0f; return; }

            _runElapsed += UnityEngine.Time.unscaledDeltaTime; // «рабочий» таймер забега
            _topBar.Root.BringToFront(); // держим топбар поверх оверлеев узлов (карта/магазин/награда/инвентарь)
            _topBar.SetGold(run.Gold);
            _topBar.SetAct(run.CurrentActIndex + 1);
            _topBar.SetRestarts(run.RestartsRemaining, _config != null ? _config.RestartsPerAct : run.RestartsRemaining);
            _topBar.SetRunTime(FormatTime(_runElapsed));

            BattlePhase phase = _clock.Phase;
            // QA #11: подсвечивать активный режим по факту (не только инвентарь). Инвентарь поверх боя →
            // приоритетнее; иначе идёт бой/расстановка → «Бой»; иначе открыта read-only карта → «Карта».
            if (_topBar is RunModeBarView modeBar) modeBar.SetActiveMode(ActiveMode(phase));

            if (phase == BattlePhase.None) _topBar.HideBattleCenter();       // карта/магазин — центр пуст
            else _topBar.SetFighting(phase == BattlePhase.Fighting, FormatTime(_clock.ElapsedSeconds));
        }

        // Режим «Инвентарь» — тумблер: открыт → закрыть, закрыт → открыть новый инвентарь-экран (тело под топбаром).
        private void ToggleInventory()
        {
            if (_inventoryOpen) { _router.CloseOverlays(); return; }
            if (_loadoutInventoryScreen == null) { _router.OpenHub(); return; } // фолбэк на старый хаб, если ассет не назначен
            _inventoryOpen = true;
            int gold = _runStates?.Current != null ? _runStates.Current.Gold : 0;
            _router.OpenInventory(gold, () => _inventoryOpen = false);
        }

        // Режим «Карта» — открыть карту акта read-only (просмотр текущей карты; клик по узлу закрывает просмотр).
        private void OpenMapView()
        {
            RunState run = _runStates?.Current;
            if (run?.Map == null || run.Map.Nodes == null || run.Map.Nodes.Length == 0) return;
            var ids = new List<string>();
            foreach (var n in Guildmaster.Guild.MapTraversal.AvailableNext(run.Map)) ids.Add(n.Id);
            _mapOpen = true; // QA #11: подсветить режим «Карта», снять на закрытии (клик узла/ESC)
            _router.OpenMap(new Guildmaster.Guild.OpenMapRequest(run.Map, ids, _ => { _mapOpen = false; _router.CloseOverlays(); }));
        }

        // Активный режим для подсветки таба (QA #11). Инвентарь поверх → приоритет; иначе бой/расстановка;
        // иначе read-only карта. NB: карта петли акта (обход узлов) идёт через флоу, не через этот бутстрап —
        // её подсветка потребует отдельного шва флоу→топбар (не покрыто здесь).
        private string ActiveMode(BattlePhase phase)
        {
            if (_inventoryOpen)            return "inventory";
            if (phase != BattlePhase.None) return "battle";
            if (_mapOpen)                  return "map";
            return null;
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.FloorToInt(seconds);
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        private void OnDestroy()
        {
            if (_input != null) _input.MenuToggleRequested -= OnMenuToggle;
            _openLoadoutSubscription?.Dispose();
            _openRewardSubscription?.Dispose();
            _openEventSubscription?.Dispose();
            _openMapSubscription?.Dispose();
            _openContinueSubscription?.Dispose();
            _openShopSubscription?.Dispose();
            _openChestSubscription?.Dispose();
            _openOutcomeSubscription?.Dispose();
            _openMainMenuSubscription?.Dispose();
        }

        private void OnMenuToggle() => _router.ToggleSystemMenu();
    }
}
