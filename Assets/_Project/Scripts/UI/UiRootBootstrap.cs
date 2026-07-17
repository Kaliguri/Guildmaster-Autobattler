using System;
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

        [Tooltip("UXML верхней панели забега (StS-style: хаб/опции/золото, центр «Начать»↔таймер, акт/время).")]
        [SerializeField] private VisualTreeAsset _runTopBar;

        private MenuRouter _router;
        private IInputService _input;
        private IBattleClock _clock;
        private RunStateService _runStates;
        private ILocalizationService _loc;
        private ISubscriber<OpenLoadoutRequest> _openLoadoutSub;
        private ISubscriber<OpenRewardRequest> _openRewardSub;
        private ISubscriber<OpenTextEventRequest> _openEventSub;
        private ISubscriber<OpenMapRequest> _openMapSub;
        private ISubscriber<OpenContinueRequest> _openContinueSub;
        private ISubscriber<OpenShopRequest> _openShopSub;
        private ISubscriber<OpenChestRequest> _openChestSub;
        private IDisposable _openLoadoutSubscription;
        private IDisposable _openRewardSubscription;
        private IDisposable _openEventSubscription;
        private IDisposable _openMapSubscription;
        private IDisposable _openContinueSubscription;
        private IDisposable _openShopSubscription;
        private IDisposable _openChestSubscription;
        private UIDocument _doc;
        private RunTopBarView _topBar;

        [Inject]
        public void Construct(MenuRouter router, IInputService input,
            IBattleClock clock, RunStateService runStates, ILocalizationService loc,
            ISubscriber<OpenLoadoutRequest> openLoadoutSub, ISubscriber<OpenRewardRequest> openRewardSub,
            ISubscriber<OpenTextEventRequest> openEventSub, ISubscriber<OpenMapRequest> openMapSub,
            ISubscriber<OpenContinueRequest> openContinueSub, ISubscriber<OpenShopRequest> openShopSub,
            ISubscriber<OpenChestRequest> openChestSub)
        {
            _router = router;
            _input = input;
            _clock = clock;
            _runStates = runStates;
            _loc = loc;
            _openLoadoutSub = openLoadoutSub;
            _openRewardSub = openRewardSub;
            _openEventSub = openEventSub;
            _openMapSub = openMapSub;
            _openContinueSub = openContinueSub;
            _openShopSub = openShopSub;
            _openChestSub = openChestSub;
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
            _router.Initialize(_doc.rootVisualElement, _pauseScreen, _settingsScreen, _loadoutScreen, _rewardScreen, _eventScreen, _mapScreen, _continueScreen, _shopScreen, _chestScreen);
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

            InitTopBar();
        }

        // Верхняя панель забега (план 12 Фаза 2) — постоянный НЕ-модальный слой в корне панели (в обход
        // стека MenuRouter, чтобы не глушить ввод). Видимость и центр (Начать↔таймер) — по фазе боя в Update.
        private void InitTopBar()
        {
            if (_runTopBar == null) return;
            _topBar = new RunTopBarView(
                _runTopBar,
                key => _loc?.GetString(key),
                onHub: OnHubClicked,
                onSettings: () => _router.ToggleSystemMenu(),
                onStart: () => _clock?.RequestStart());
            _topBar.Root.style.display = DisplayStyle.None; // скрыта, пока нет активного боя
            _doc.rootVisualElement.Add(_topBar.Root);
        }

        private void Update()
        {
            if (_topBar == null || _clock == null) return;

            BattlePhase phase = _clock.Phase;
            bool visible = phase != BattlePhase.None;
            _topBar.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;

            RunState run = _runStates?.Current;
            _topBar.SetGold(run?.Gold ?? 0);
            _topBar.SetAct((run?.CurrentActIndex ?? 0) + 1);
            _topBar.SetFighting(phase == BattlePhase.Fighting, FormatTime(_clock.ElapsedSeconds));
        }

        // Заглушка: хаб оживёт в под-шаге 2 (переключение сосудов + вкладки). Пока — лог.
        private void OnHubClicked() => Debug.Log("[UiRootBootstrap] Хаб — заглушка (под-шаг 2).");

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
        }

        private void OnMenuToggle() => _router.ToggleSystemMenu();
    }
}
