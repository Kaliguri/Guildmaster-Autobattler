using System;
using Guildmaster.Core.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Guildmaster.Game.Input
{
    /// <summary>
    /// Реализация <see cref="IInputService"/> поверх Unity Input System (вики «16» §3).
    /// Карты действий строятся в коде — единый источник правды; переход на GUI-ассет
    /// <c>.inputactions</c> + UI-ребиндинг возможен поверх той же таблицы биндов (будущее).
    /// <para>Живёт в корневом DI-скоупе (ввод глобален, переживает перезагрузку боевой сцены).
    /// Активные карты задаёт <see cref="SetContext"/>; Input System сам тикает включённые
    /// действия в player loop — отдельный Update не нужен.</para>
    /// </summary>
    public sealed class InputService : IInputService, IDisposable
    {
        private readonly InputActionMap _cameraMap;
        private readonly InputActionMap _combatMap;
        private readonly InputActionMap _deploymentMap; // действия фазы расстановки (шаг 4)
        private readonly InputActionMap _pointerMap; // указатель (позиция + ЛКМ) — общий для расстановки и карты
        private readonly InputActionMap _uiMap; // seam под меню/навигацию (реализация — будущая фаза)

        private readonly InputAction _pan;
        private readonly InputAction _zoom;
        private readonly InputAction _middlePan;   // <Mouse>/middleButton — зажата = пан драгом
        private readonly InputAction _pointerDelta; // <Mouse>/delta — дельта мыши за кадр (для MMB-пана)
        private readonly InputAction _cycleView;
        private readonly InputAction _pauseToggle;
        private readonly InputAction _gameSpeedCycle;
        private readonly InputAction _pointerPos;    // <Mouse>/position — screen→world при пикинге/drag
        private readonly InputAction _pointerPress;  // <Mouse>/leftButton — начало/конец протяжки
        private readonly InputAction _menuToggle; // Escape — оверлей системного меню, живёт вне контекст-карт (always-on)
        private readonly InputAction _detailsHold; // Shift — подробности в подсказках, тоже always-on

        private InputContext _context = InputContext.None;

        public InputContext Context => _context;

        /// <inheritdoc/>
        public bool GameplaySuppressed { get; set; }

        public event Action CycleViewRequested;
        public event Action PauseToggleRequested;
        public event Action GameSpeedCycleRequested;
        public event Action MenuToggleRequested;
        public event Action PointerPressed;
        public event Action PointerReleased;

        /// <inheritdoc/>
        public bool DetailsHeld { get; private set; }

        public event Action<bool> DetailsHeldChanged;

        public InputService()
        {
            // --- Карта «Camera»: пан (WASD + стрелки), зум (колесо), цикл вида (Tab) ---
            _cameraMap = new InputActionMap("Camera");

            _pan = _cameraMap.AddAction("Pan", InputActionType.Value);
            _pan.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _pan.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            _zoom = _cameraMap.AddAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            _middlePan    = _cameraMap.AddAction("MiddlePan", InputActionType.Button, "<Mouse>/middleButton");
            _pointerDelta = _cameraMap.AddAction("PointerDelta", InputActionType.Value, "<Mouse>/delta");
            _cycleView = _cameraMap.AddAction("CycleView", InputActionType.Button, "<Keyboard>/tab");

            // --- Карта «Combat»: пауза (Space), смена скорости (.). Рестарт боя/сцены (R/F5) — dev (DevTools). ---
            _combatMap = new InputActionMap("Combat");
            _pauseToggle = _combatMap.AddAction("PauseToggle", InputActionType.Button, "<Keyboard>/space");
            _gameSpeedCycle = _combatMap.AddAction("GameSpeedCycle", InputActionType.Button, "<Keyboard>/period");

            // --- Карта «Deployment»: действия фазы расстановки (шаг 4). Указатель вынесен в «Pointer». ---
            _deploymentMap = new InputActionMap("Deployment");

            // --- Карта «Pointer»: указатель мыши (позиция + ЛКМ). Общая для расстановки (перетаскивание
            // юнитов) и карты акта (клик по узлу) — оба контекста тыкают в мир одной и той же мышью,
            // и включать ради этого чужую карту «Deployment» было бы враньём по смыслу. ---
            _pointerMap = new InputActionMap("Pointer");
            _pointerPos   = _pointerMap.AddAction("PointerPosition", InputActionType.Value, "<Mouse>/position");
            _pointerPress = _pointerMap.AddAction("PointerPress", InputActionType.Button, "<Mouse>/leftButton");
            _pointerPress.performed += OnPointerPressed;
            _pointerPress.canceled  += OnPointerReleased;

            // --- Карта «UI»: пока пустой seam ---
            _uiMap = new InputActionMap("UI");

            // Системное меню (Escape): оверлей, НЕ пауза (кооп). Вне контекст-карт — доступно из любого
            // контекста; НЕ глушится GameplaySuppressed, иначе открытое меню нельзя было бы закрыть.
            _menuToggle = new InputAction("MenuToggle", InputActionType.Button, "<Keyboard>/escape");
            _menuToggle.performed += OnMenuToggle;
            _menuToggle.Enable();

            // Подробности в подсказках (Shift): как и меню — вне контекст-карт и без глушения. Тултип
            // может висеть над модальным экраном, и там Shift обязан работать так же, как в бою.
            _detailsHold = new InputAction("DetailsHold", InputActionType.Button, "<Keyboard>/shift");
            _detailsHold.performed += OnDetailsHeld;
            _detailsHold.canceled  += OnDetailsReleased;
            _detailsHold.Enable();

            _cycleView.performed     += OnCycleView;
            _pauseToggle.performed   += OnPauseToggle;
            _gameSpeedCycle.performed += OnGameSpeedCycle;
        }

        public void SetContext(InputContext context)
        {
            if (_context == context) return;
            _context = context;

            _cameraMap.Disable();
            _combatMap.Disable();
            _deploymentMap.Disable();
            _pointerMap.Disable();
            _uiMap.Disable();

            switch (context)
            {
                case InputContext.Menu:
                    _uiMap.Enable();
                    break;
                case InputContext.Deployment:
                    _cameraMap.Enable();
                    _deploymentMap.Enable();
                    _pointerMap.Enable();
                    break;
                // Карта акта: своя world-камера (пан/зум как в бою) + указатель для клика по узлу.
                // Боевых действий (пауза, скорость) здесь нет — боя не идёт.
                case InputContext.Map:
                    _cameraMap.Enable();
                    _pointerMap.Enable();
                    break;
                case InputContext.Combat:
                    _cameraMap.Enable();
                    _combatMap.Enable();
                    break;
                case InputContext.None:
                default:
                    break;
            }
        }

        // Пока ввод заглушен модальным слоем (консоль/меню) — отдаём нейтраль, чтобы WASD/колесо,
        // которыми набирают текст в консоли, не таскали и не зумили камеру.
        public Vector2 CameraPan      => GameplaySuppressed ? Vector2.zero : _pan.ReadValue<Vector2>();
        public float   CameraZoomDelta => GameplaySuppressed ? 0f : _zoom.ReadValue<float>();

        // Пан драгом средней кнопки: дельта мыши, пока MMB зажата (иначе ноль). Гейтится модальным слоем.
        public Vector2 CameraPanDrag =>
            (GameplaySuppressed || !_middlePan.IsPressed()) ? Vector2.zero : _pointerDelta.ReadValue<Vector2>();

        // Позиция указателя не гейтится (это просто «где мышь»); нажатие/зажатие — гейтится (модальный слой).
        public Vector2 PointerScreenPosition => _pointerPos.ReadValue<Vector2>();
        public bool    PointerHeld           => !GameplaySuppressed && _pointerPress.IsPressed();

        // --- Шов развязки UI↔мир: hit-тест panel.Pick над курсором ---
        // Ленивый кеш UIDocument (один в сцене, живёт весь сеанс). rootVisualElement.panel — рантайм-панель.
        private UIDocument _uiDoc;
        private IPanel UiPanel
        {
            get
            {
                if (_uiDoc == null) _uiDoc = UnityEngine.Object.FindFirstObjectByType<UIDocument>();
                return _uiDoc != null ? _uiDoc.rootVisualElement?.panel : null;
            }
        }

        public bool PointerOverUI
        {
            get
            {
                IPanel panel = UiPanel;
                if (panel == null) return false;
                Vector2 screen = _pointerPos.ReadValue<Vector2>();
                Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, screen);
                // Pick пропускает pickingMode=Ignore (прозрачная боевая «дырка») → null над миром, панель над UI.
                return panel.Pick(panelPos) != null;
            }
        }

        private void OnCycleView(InputAction.CallbackContext _)      { if (!GameplaySuppressed) CycleViewRequested?.Invoke(); }
        private void OnPauseToggle(InputAction.CallbackContext _)    { if (!GameplaySuppressed) PauseToggleRequested?.Invoke(); }
        private void OnGameSpeedCycle(InputAction.CallbackContext _) { if (!GameplaySuppressed) GameSpeedCycleRequested?.Invoke(); }
        // Клик над непрозрачной UITK-панелью не начинает деплой-пик (уходит в UI). Drag, начатый над миром,
        // продолжается и над панелью (PointerHeld этот флаг не гейтит — иначе протяжка рвалась бы у края панели).
        private void OnPointerPressed(InputAction.CallbackContext _)  { if (!GameplaySuppressed && !PointerOverUI) PointerPressed?.Invoke(); }
        private void OnPointerReleased(InputAction.CallbackContext _) { if (!GameplaySuppressed) PointerReleased?.Invoke(); }

        // Escape НЕ гейтится GameplaySuppressed: меню должно закрываться, даже когда геймплейный ввод заглушён.
        private void OnMenuToggle(InputAction.CallbackContext _) => MenuToggleRequested?.Invoke();

        private void OnDetailsHeld(InputAction.CallbackContext _)     => SetDetailsHeld(true);
        private void OnDetailsReleased(InputAction.CallbackContext _) => SetDetailsHeld(false);

        private void SetDetailsHeld(bool held)
        {
            if (DetailsHeld == held) return;
            DetailsHeld = held;
            DetailsHeldChanged?.Invoke(held);
        }

        public void Dispose()
        {
            _cycleView.performed     -= OnCycleView;
            _pauseToggle.performed   -= OnPauseToggle;
            _gameSpeedCycle.performed -= OnGameSpeedCycle;
            _pointerPress.performed  -= OnPointerPressed;
            _pointerPress.canceled   -= OnPointerReleased;
            _menuToggle.performed    -= OnMenuToggle;
            _detailsHold.performed   -= OnDetailsHeld;
            _detailsHold.canceled    -= OnDetailsReleased;

            _cameraMap.Dispose();
            _combatMap.Dispose();
            _deploymentMap.Dispose();
            _pointerMap.Dispose();
            _uiMap.Dispose();
            _menuToggle.Dispose();
            _detailsHold.Dispose();
        }
    }
}
