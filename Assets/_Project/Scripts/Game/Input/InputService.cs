using System;
using Guildmaster.Core.Input;
using UnityEngine;
using UnityEngine.InputSystem;

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
        private readonly InputActionMap _uiMap; // seam под меню/навигацию (реализация — будущая фаза)

        private readonly InputAction _pan;
        private readonly InputAction _zoom;
        private readonly InputAction _cycleView;
        private readonly InputAction _pauseToggle;

        private InputContext _context = InputContext.None;

        public InputContext Context => _context;

        public event Action CycleViewRequested;
        public event Action PauseToggleRequested;

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
            _cycleView = _cameraMap.AddAction("CycleView", InputActionType.Button, "<Keyboard>/tab");

            // --- Карта «Combat»: пауза (Space). Рестарт боя/сцены (R/F5) — dev-инструменты (DevTools). ---
            _combatMap = new InputActionMap("Combat");
            _pauseToggle = _combatMap.AddAction("PauseToggle", InputActionType.Button, "<Keyboard>/space");

            // --- Карта «UI»: пока пустой seam ---
            _uiMap = new InputActionMap("UI");

            _cycleView.performed   += OnCycleView;
            _pauseToggle.performed += OnPauseToggle;
        }

        public void SetContext(InputContext context)
        {
            if (_context == context) return;
            _context = context;

            _cameraMap.Disable();
            _combatMap.Disable();
            _uiMap.Disable();

            switch (context)
            {
                case InputContext.Menu:
                    _uiMap.Enable();
                    break;
                case InputContext.Deployment:
                    _cameraMap.Enable();
                    break;
                case InputContext.Combat:
                    _cameraMap.Enable();
                    _combatMap.Enable();
                    break;
                case InputContext.None:
                default:
                    break;
            }

            UnityEngine.Debug.Log($"[Input] SetContext -> {context}; camMap={_cameraMap.enabled}, combatMap={_combatMap.enabled}");
        }

        public Vector2 CameraPan     => _pan.ReadValue<Vector2>();
        public float   CameraZoomDelta => _zoom.ReadValue<float>();

        private void OnCycleView(InputAction.CallbackContext _)
        {
            int subs = CycleViewRequested?.GetInvocationList().Length ?? 0;
            UnityEngine.Debug.Log($"[Input] CycleView fired (subscribers={subs})");
            CycleViewRequested?.Invoke();
        }

        private void OnPauseToggle(InputAction.CallbackContext _) => PauseToggleRequested?.Invoke();

        public void Dispose()
        {
            _cycleView.performed   -= OnCycleView;
            _pauseToggle.performed -= OnPauseToggle;

            _cameraMap.Dispose();
            _combatMap.Dispose();
            _uiMap.Dispose();
        }
    }
}
