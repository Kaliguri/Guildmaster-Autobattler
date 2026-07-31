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
        private readonly InputActionMap _pointerMap; // клик по миру (ЛКМ) — общий для расстановки и карты
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
        private readonly InputAction _devConsoleToggle; // F1 — командная dev-консоль; always-on по той же причине, что и меню
        private readonly InputAction _devLogToggle;     // F2 — лог-консоль
        private readonly InputAction _devBattlesToggle; // F3 — витрина боёв
        private readonly InputAction _detailsHold; // Shift — подробности в подсказках, тоже always-on
        private readonly InputAction _skipTransition; // Space — пропустить подачу; always-on, см. комментарий у создания

        private InputContext _context = InputContext.None;

        public InputContext Context => _context;

        // Кто сейчас держит клавиатуру. Единственный владелец факта «геймплей заглушён»: источники
        // заявляют свою причину, итог считается здесь. Прежде каждый писал в общее булево напрямую и
        // снимал чужое глушение — см. InputSuppressSource.
        private InputSuppressSource _suppressors = InputSuppressSource.None;

        /// <inheritdoc/>
        public bool GameplaySuppressed => _suppressors != InputSuppressSource.None;

        /// <inheritdoc/>
        public void SetSuppressed(InputSuppressSource source, bool suppressed)
        {
            if (suppressed) _suppressors |=  source;
            else            _suppressors &= ~source;
        }

        public event Action CycleViewRequested;
        public event Action PauseToggleRequested;
        public event Action SkipRequested;
        public event Action GameSpeedCycleRequested;
        public event Action MenuToggleRequested;
        public event Action DevConsoleToggleRequested;
        public event Action DevLogToggleRequested;
        public event Action DevBattlesToggleRequested;
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

            // Позиция указателя живёт в карте КАМЕРЫ, а не «Pointer», хотя клик по миру — там. Причина:
            // зум колесом держит точку под курсором на месте, и позиция нужна везде, где есть камера, — в
            // том числе в бою, где карта «Pointer» выключена (кликать по миру в бою нечем). Пока она лежала
            // рядом с кликом, в бою ReadValue отдавал ноль, и колесо утаскивало кадр в левый нижний угол.
            _pointerPos = _cameraMap.AddAction("PointerPosition", InputActionType.Value, "<Mouse>/position");

            // --- Карта «Combat»: пауза (Space), смена скорости (.). Рестарт боя/сцены (R/F5) — dev (DevTools). ---
            _combatMap = new InputActionMap("Combat");
            _pauseToggle = _combatMap.AddAction("PauseToggle", InputActionType.Button, "<Keyboard>/space");
            _gameSpeedCycle = _combatMap.AddAction("GameSpeedCycle", InputActionType.Button, "<Keyboard>/period");

            // --- Карта «Deployment»: действия фазы расстановки (шаг 4). Указатель вынесен в «Pointer». ---
            _deploymentMap = new InputActionMap("Deployment");

            // --- Карта «Pointer»: КЛИК по миру (ЛКМ). Общая для расстановки (перетаскивание юнитов) и
            // карты акта (клик по узлу) — оба контекста тыкают в мир одной и той же мышью, и включать ради
            // этого чужую карту «Deployment» было бы враньём по смыслу. Позиция указателя лежит не здесь,
            // а в карте камеры (см. выше): она нужна и там, где кликать нечем. ---
            _pointerMap = new InputActionMap("Pointer");
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

            // Dev-консоли: вне контекст-карт и БЕЗ проверки глушения. Открытая консоль сама держит
            // InputSuppressSource.DevConsole, и гейт по GameplaySuppressed запер бы её изнутри.
            // F1 — командная, F2 — лог. Тильда снята: ОС печатает её литеру в поле первым же символом,
            // и на функциональных клавишах этой болезни нет вовсе.
            _devConsoleToggle = new InputAction("DevConsoleToggle", InputActionType.Button, "<Keyboard>/f1");
            _devConsoleToggle.performed += OnDevConsoleToggle;
            _devConsoleToggle.Enable();

            _devLogToggle = new InputAction("DevLogToggle", InputActionType.Button, "<Keyboard>/f2");
            _devLogToggle.performed += OnDevLogToggle;
            _devLogToggle.Enable();

            _devBattlesToggle = new InputAction("DevBattlesToggle", InputActionType.Button, "<Keyboard>/f3");
            _devBattlesToggle.performed += OnDevBattlesToggle;
            _devBattlesToggle.Enable();

            // Подробности в подсказках (Shift): как и меню — вне контекст-карт и без глушения. Тултип
            // может висеть над модальным экраном, и там Shift обязан работать так же, как в бою.
            // Скип подачи (Space): как меню и Shift — вне контекст-карт. Переход арены играет в расстановке
            // и на полигоне, где карта «Combat» выключена, а пауза живёт именно в ней — на общей клавише
            // скип просто не доходил до слушателя. В бою Space по-прежнему пауза: подача там не идёт.
            _skipTransition = new InputAction("SkipTransition", InputActionType.Button, "<Keyboard>/space");
            _skipTransition.performed += OnSkipRequested;
            _skipTransition.Enable();

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
                if (_uiDoc == null) _uiDoc = UnityEngine.Object.FindAnyObjectByType<UIDocument>();
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
        private void OnSkipRequested(InputAction.CallbackContext _)  { if (!GameplaySuppressed) SkipRequested?.Invoke(); }
        private void OnGameSpeedCycle(InputAction.CallbackContext _) { if (!GameplaySuppressed) GameSpeedCycleRequested?.Invoke(); }
        // Клик над непрозрачной UITK-панелью не начинает деплой-пик (уходит в UI). Drag, начатый над миром,
        // продолжается и над панелью (PointerHeld этот флаг не гейтит — иначе протяжка рвалась бы у края панели).
        private void OnPointerPressed(InputAction.CallbackContext _)  { if (!GameplaySuppressed && !PointerOverUI) PointerPressed?.Invoke(); }
        private void OnPointerReleased(InputAction.CallbackContext _) { if (!GameplaySuppressed) PointerReleased?.Invoke(); }

        // Escape НЕ гейтится GameplaySuppressed: меню должно закрываться, даже когда геймплейный ввод заглушён.
        private void OnMenuToggle(InputAction.CallbackContext _) => MenuToggleRequested?.Invoke();

        private void OnDevConsoleToggle(InputAction.CallbackContext _) => DevConsoleToggleRequested?.Invoke();

        private void OnDevLogToggle(InputAction.CallbackContext _) => DevLogToggleRequested?.Invoke();

        private void OnDevBattlesToggle(InputAction.CallbackContext _) => DevBattlesToggleRequested?.Invoke();

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
            _devConsoleToggle.performed -= OnDevConsoleToggle;
            _devLogToggle.performed  -= OnDevLogToggle;
            _devBattlesToggle.performed -= OnDevBattlesToggle;
            _detailsHold.performed   -= OnDetailsHeld;
            _detailsHold.canceled    -= OnDetailsReleased;
            _skipTransition.performed -= OnSkipRequested;

            _cameraMap.Dispose();
            _combatMap.Dispose();
            _deploymentMap.Dispose();
            _pointerMap.Dispose();
            _uiMap.Dispose();
            _menuToggle.Dispose();
            _devConsoleToggle.Dispose();
            _devLogToggle.Dispose();
            _devBattlesToggle.Dispose();
            _detailsHold.Dispose();
            _skipTransition.Dispose();
        }
    }
}
