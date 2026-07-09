using Guildmaster.Core.Arena;
using Guildmaster.Core.Input;
using Unity.Cinemachine;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>Режим камеры (вики «16» §5).</summary>
    public enum CameraMode
    {
        /// <summary>Динамично держит драку: следует за центроидом боя, зум подгоняется под разброс.</summary>
        Action,
        /// <summary>Обзор всей арены целиком; ручной пан/зум в пределах зоны.</summary>
        Overview,
        /// <summary>Свободная dev-камера: пан/зум без клампа (доступ выдаётся отдельно).</summary>
        Dev,
    }

    /// <summary>
    /// Управляет режимами камеры и потребляет <see cref="IInputService"/> (вики «16» §5).
    /// Переключение вида — сменой приоритета Cinemachine (Brain плавно блендит). Ручной пан/зум
    /// в Overview/Dev; кламп видимой области границами арены — из данных (<see cref="ArenaLayoutData"/>),
    /// без коллайдера. Dev-камера клампу не подчиняется и в цикл попадает только при выданном доступе.
    /// </summary>
    public sealed class CameraModeController : MonoBehaviour
    {
        [Header("Виртуальные камеры (Cinemachine)")]
        [SerializeField] private CinemachineCamera _actionCam;
        [SerializeField] private CinemachineCamera _overviewCam;
        [SerializeField] private CinemachineCamera _devCam;

        [Header("Зона камеры")]
        [Tooltip("Отступ вокруг границ арены — насколько видно за краем поля.")]
        [SerializeField] private float _boundsPadding = 2f;

        [Header("Зум")]
        [Tooltip("Ближний предел орто-размера (максимальное приближение).")]
        [SerializeField] private float _minZoom = 3f;
        [Tooltip("Шаг зума за одно деление колеса (в орто-единицах).")]
        [SerializeField] private float _zoomStep = 1.5f;
        [Tooltip("Верхний предел зума для dev-камеры (не привязан к зоне).")]
        [SerializeField] private float _devMaxZoom = 40f;

        [Header("Панорамирование (ед./сек при полном отклонении)")]
        [SerializeField] private float _panSpeed = 12f;
        [SerializeField] private float _devPanSpeed = 24f;

        [Header("Экшн-камера (динамический зум)")]
        [Tooltip("Запас вокруг разброса боя при подгоне зума.")]
        [SerializeField] private float _actionZoomPadding = 4f;
        [Tooltip("Скорость подгона орто-размера экшн-камеры.")]
        [SerializeField] private float _actionZoomDamping = 3f;

        [Header("Приоритеты")]
        [SerializeField] private int _activePriority = 20;
        [SerializeField] private int _inactivePriority = 0;

        private IInputService     _input;
        private ArenaLayoutData   _layout;
        private CombatFocusTarget _focus;

        private CameraMode _mode = CameraMode.Action;
        private bool _devAccess;

        /// <summary>Разблокирован ли dev-режим камеры (доступ выдаётся отдельно, вики «16» §6).</summary>
        public bool DevAccess => _devAccess;

        /// <summary>Текущий режим камеры.</summary>
        public CameraMode Mode => _mode;

        [Inject]
        public void Construct(IInputService input, ArenaLayoutData layout, CombatFocusTarget focus)
        {
            _input  = input;
            _layout = layout;
            _focus  = focus;
        }

        private void OnEnable()
        {
            if (_input != null) _input.CycleViewRequested += OnCycleView;
            SnapOverviewToArena();
            ApplyMode();
        }

        private void OnDisable()
        {
            if (_input != null) _input.CycleViewRequested -= OnCycleView;
        }

        /// <summary>Выдать/забрать доступ к dev-камере (QFSW: gm_cam_dev). Забирая — уводим из Dev.</summary>
        public void SetDevAccess(bool granted)
        {
            _devAccess = granted;
            if (!granted && _mode == CameraMode.Dev)
            {
                _mode = CameraMode.Overview;
                ApplyMode();
            }
        }

        private void OnCycleView()
        {
            _mode = NextMode(_mode, _devAccess);
            ApplyMode();
        }

        private static CameraMode NextMode(CameraMode mode, bool devAccess)
        {
            switch (mode)
            {
                case CameraMode.Action:   return CameraMode.Overview;
                case CameraMode.Overview: return devAccess ? CameraMode.Dev : CameraMode.Action;
                case CameraMode.Dev:      return CameraMode.Action;
                default:                  return CameraMode.Action;
            }
        }

        private void ApplyMode()
        {
            SetPriority(_actionCam,   _mode == CameraMode.Action);
            SetPriority(_overviewCam, _mode == CameraMode.Overview);
            SetPriority(_devCam,      _mode == CameraMode.Dev);
        }

        private void SetPriority(CinemachineCamera cam, bool active)
        {
            if (cam != null) cam.Priority = active ? _activePriority : _inactivePriority;
        }

        private void Update()
        {
            if (_input == null) return;
            switch (_mode)
            {
                case CameraMode.Overview: DriveManual(_overviewCam, _panSpeed, clampToZone: true);  break;
                case CameraMode.Dev:      DriveManual(_devCam, _devPanSpeed, clampToZone: false);    break;
                case CameraMode.Action:   DriveActionZoom();                                         break;
            }
        }

        // Ручной пан + зум (Overview/Dev). Клампит видимую область границами зоны (если clampToZone).
        private void DriveManual(CinemachineCamera cam, float panSpeed, bool clampToZone)
        {
            if (cam == null) return;

            LensSettings lens = cam.Lens;
            float size = lens.OrthographicSize;

            float zoom = _input.CameraZoomDelta;
            if (zoom > 0f)      size -= _zoomStep; // колесо вперёд — приблизить (меньше орто-размер)
            else if (zoom < 0f) size += _zoomStep; // колесо назад — отдалить

            float maxZoom = clampToZone ? MaxZoomForZone() : _devMaxZoom;
            size = Mathf.Clamp(size, _minZoom, maxZoom);
            lens.OrthographicSize = size;
            cam.Lens = lens;

            Vector2 pan = _input.CameraPan;
            Vector3 pos = cam.transform.position;
            pos.x += pan.x * panSpeed * Time.deltaTime;
            pos.y += pan.y * panSpeed * Time.deltaTime;

            if (clampToZone) pos = ClampVisibleCenter(pos, size);
            cam.transform.position = pos;
        }

        // Экшн-камера: позицию ведёт Follow (focus target), здесь подгоняем орто-размер под разброс боя.
        private void DriveActionZoom()
        {
            if (_actionCam == null || _focus == null || !_focus.HasUnits) return;

            LensSettings lens = _actionCam.Lens;
            float desired = Mathf.Max(_minZoom, _focus.Spread + _actionZoomPadding);
            desired = Mathf.Min(desired, MaxZoomForZone());
            float t = 1f - Mathf.Exp(-_actionZoomDamping * Time.deltaTime);
            lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, desired, t);
            _actionCam.Lens = lens;
        }

        // Максимальный орто-размер, при котором видимая область не превышает зону (по обеим осям).
        private float MaxZoomForZone()
        {
            Vector2 zone = ZoneSize();
            float aspect = ScreenAspect();
            float halfH = zone.y * 0.5f;
            float halfW = (zone.x * 0.5f) / Mathf.Max(aspect, 0.0001f);
            return Mathf.Max(_minZoom, Mathf.Min(halfH, halfW));
        }

        // Кламп центра так, чтобы видимый прямоугольник (полу-высота = size) не вышел за зону.
        private Vector3 ClampVisibleCenter(Vector3 pos, float size)
        {
            Vector2 c = _layout.Bounds.Center;
            Vector2 zone = ZoneSize();
            float aspect = ScreenAspect();

            float slackX = Mathf.Max(0f, zone.x * 0.5f - size * aspect);
            float slackY = Mathf.Max(0f, zone.y * 0.5f - size);

            pos.x = Mathf.Clamp(pos.x, c.x - slackX, c.x + slackX);
            pos.y = Mathf.Clamp(pos.y, c.y - slackY, c.y + slackY);
            return pos;
        }

        // Ставит Overview-камеру в центр арены на весь размер зоны (вызов при включении).
        private void SnapOverviewToArena()
        {
            if (_overviewCam == null || _layout == null) return;

            Vector2 c = _layout.Bounds.Center;
            Vector3 pos = _overviewCam.transform.position;
            _overviewCam.transform.position = new Vector3(c.x, c.y, pos.z);

            LensSettings lens = _overviewCam.Lens;
            lens.OrthographicSize = MaxZoomForZone();
            _overviewCam.Lens = lens;
        }

        private Vector2 ZoneSize()
        {
            Vector2 s = _layout.Bounds.Size;
            return new Vector2(Mathf.Abs(s.x) + _boundsPadding * 2f, Mathf.Abs(s.y) + _boundsPadding * 2f);
        }

        private static float ScreenAspect()
        {
            return Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        }
    }
}
