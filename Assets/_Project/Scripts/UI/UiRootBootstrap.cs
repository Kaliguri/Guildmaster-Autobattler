using System;
using Guildmaster.Core.Input;
using Guildmaster.Data.Definitions;
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

        private MenuRouter _router;
        private IInputService _input;
        private ISubscriber<OpenLoadoutRequest> _openLoadoutSub;
        private ISubscriber<OpenRewardRequest> _openRewardSub;
        private IDisposable _openLoadoutSubscription;
        private IDisposable _openRewardSubscription;
        private UIDocument _doc;

        [Inject]
        public void Construct(MenuRouter router, IInputService input,
            ISubscriber<OpenLoadoutRequest> openLoadoutSub, ISubscriber<OpenRewardRequest> openRewardSub)
        {
            _router = router;
            _input = input;
            _openLoadoutSub = openLoadoutSub;
            _openRewardSub = openRewardSub;
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
            _router.Initialize(_doc.rootVisualElement, _pauseScreen, _settingsScreen, _loadoutScreen);
            _input.MenuToggleRequested += OnMenuToggle;
            // Открытие loadout по запросу из фазы расстановки (MessagePipe-событие с Data-пейлоадом).
            _openLoadoutSubscription = _openLoadoutSub?.Subscribe(req => _router.OpenLoadout(req));
            // Открытие экрана награды после боя (A3) — запрос из GameFlow.
            _openRewardSubscription = _openRewardSub?.Subscribe(req => _router.OpenReward(req));
        }

        private void OnDestroy()
        {
            if (_input != null) _input.MenuToggleRequested -= OnMenuToggle;
            _openLoadoutSubscription?.Dispose();
            _openRewardSubscription?.Dispose();
        }

        private void OnMenuToggle() => _router.ToggleSystemMenu();
    }
}
