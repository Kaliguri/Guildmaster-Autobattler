using System.Collections.Generic;
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
        /// <summary>Карта акта: ручной пан/зум в пределах зоны КАРТЫ (не боевой арены).</summary>
        Map,
    }

    /// <summary>
    /// Управляет режимами камеры и потребляет <see cref="IInputService"/> (вики «16» §5).
    /// Переключение вида — сменой приоритета Cinemachine (Brain плавно блендит). Ручной пан/зум
    /// в Overview/Dev; кламп видимой области границами арены — из данных (<see cref="ArenaLayoutData"/>),
    /// без коллайдера. Dev-камера клампу не подчиняется и в цикл попадает только при выданном доступе.
    /// </summary>
    public sealed class CameraModeController : MonoBehaviour, IScreenShake
    {
        [Header("Виртуальные камеры (Cinemachine)")]
        [SerializeField] private CinemachineCamera _actionCam;
        [SerializeField] private CinemachineCamera _overviewCam;
        [SerializeField] private CinemachineCamera _devCam;
        [Tooltip("Камера карты акта. Отдельная vcam — чтобы позиция и зум карты жили НЕЗАВИСИМО от боевых " +
                 "(Cinemachine хранит transform и Lens на каждой vcam; неактивная стоит где стояла). " +
                 "ГОТЧА: зона карты разнесена в мире от арены, поэтому переходы бой↔карта в Custom Blends " +
                 "должны быть Cut — иначе Brain полетит между зонами через пустоту.")]
        [SerializeField] private CinemachineCamera _mapCam;

        [Header("Камера (глубина)")]
        [Tooltip("Z-позиция камеры (2D: отрицательная, чтобы плоскость поля z=0 попадала в кадр). " +
                 "Саму зону-ограничитель задаёт арена (ArenaLayoutAuthoring, жёлтая рамка).")]
        [SerializeField] private float _cameraZ = -10f;

        [Header("Зум")]
        [Tooltip("Ближний предел орто-размера (максимальное приближение).")]
        [SerializeField] private float _minZoom = 3f;
        [Tooltip("Шаг зума за одно деление колеса (в орто-единицах).")]
        [SerializeField] private float _zoomStep = 1.5f;
        [Tooltip("Верхний предел зума для dev-камеры (не привязан к зоне).")]
        [SerializeField] private float _devMaxZoom = 40f;
        [Tooltip("Свобода панорамирования на карте: насколько можно увести карту за край экрана, в долях " +
                 "видимой области. 0.5 = полэкрана в каждую сторону — угол карты можно рассмотреть вблизи, " +
                 "но совсем в пустоту не уедешь.")]
        [SerializeField] private float _mapFreedom = 0.5f;

        [Header("Панорамирование (ед./сек при полном отклонении)")]
        [SerializeField] private float _panSpeed = 12f;
        [SerializeField] private float _devPanSpeed = 24f;

        [Header("Экшн-камера (динамический зум)")]
        [Tooltip("Запас вокруг разброса боя при подгоне зума.")]
        [SerializeField] private float _actionZoomPadding = 4f;
        [Tooltip("Скорость подгона орто-размера экшн-камеры.")]
        [SerializeField] private float _actionZoomDamping = 3f;
        [Tooltip("Дедзона зума (орто-ед.): цель зума обновляем, лишь когда разброс ушёл дальше — гасит микро-подстройку " +
                 "(«дыхание» зума). При орто ~3–20 порог ~1.5 = зум реагирует на реальный разлёт, не на дрожь.")]
        [SerializeField] private float _actionZoomDeadzone = 1.5f;

        [Header("Приоритеты")]
        [SerializeField] private int _activePriority = 20;
        [SerializeField] private int _inactivePriority = 0;

        private IInputService     _input;
        private ArenaLayoutData   _layout;
        private CombatFocusTarget _focus;
        private Design.CombatFeelConfig _feel; // конфиг тряски → раздаётся ScreenShake-ам

        private CameraMode _mode = CameraMode.Action;
        private bool _devAccess;
        private readonly List<ScreenShake> _shakers = new List<ScreenShake>(3); // тряска на каждой vcam
        // Удерживаемая цель зума экшн-камеры (обновляется через дедзону, см. DriveActionZoom). ≤0 = ещё не задана.
        private float _actionZoomTarget = -1f;

        // Зона карты: границы клампа для CameraMode.Map. Приходит снаружи (EnterMap) — карта живёт в
        // СВОЁЙ области мира, боевая _layout.CameraZone к ней отношения не имеет.
        private Rect2D _mapZone;
        // Кадрируем карту целиком только при ПЕРВОМ входе. Дальше не трогаем позицию/зум: игрок мог
        // отъехать и приблизиться, и это должно пережить поход в бой и обратно.
        private bool _mapFramed;
        // Режим, из которого ушли в карту: карту можно открыть посреди боя, и выход обязан вернуть
        // ровно тот вид, что был (боевые vcam при этом всё время стоят где стояли).
        private CameraMode _modeBeforeMap = CameraMode.Action;

        /// <summary>Разблокирован ли dev-режим камеры (доступ выдаётся отдельно, вики «16» §6).</summary>
        public bool DevAccess => _devAccess;

        /// <summary>Текущий режим камеры.</summary>
        public CameraMode Mode => _mode;

        [Inject]
        public void Construct(IInputService input, ArenaLayoutData layout, CombatFocusTarget focus, Design.CombatFeelConfig feel)
        {
            _input  = input;
            _layout = layout;
            _focus  = focus;
            _feel   = feel;
        }

        // Подписку и стартовую настройку делаем в Start, а НЕ в OnEnable: компонент инъектится
        // VContainer'ом во время Build (в Awake скоупа боя), а [Camera] стоит выше [Combat] в
        // иерархии — его OnEnable успел бы отработать до инъекции (_input == null) и подписка на
        // Tab потерялась бы. Start гарантированно после всех Awake, т.е. после инъекции.
        private void Start()
        {
            if (_input != null) _input.CycleViewRequested += OnCycleView;

            // В редакторе dev-камера доступна сразу (удобно тестить); в билде — гейтед, выдаётся
            // через gm_cam_dev (вики «16» §6). Обычный игрок в релизе циклит только Action↔Overview.
            _devAccess = Application.isEditor;

            ApplyCameraDepth();
            SnapOverviewToArena();
            ApplyMode();

            // Тряска — extension на каждой vcam (ставим кодом, префаб камеры не трогаем).
            CollectShaker(_actionCam);
            CollectShaker(_overviewCam);
            CollectShaker(_devCam);
            CollectShaker(_mapCam);
        }

        private void CollectShaker(CinemachineCamera cam)
        {
            if (cam == null) return;
            var shaker = cam.GetComponent<ScreenShake>();
            if (shaker == null) shaker = cam.gameObject.AddComponent<ScreenShake>();
            shaker.ApplyConfig(_feel);
            _shakers.Add(shaker);
        }

        /// <summary>Тряхнуть камеру (IScreenShake): рассылаем на все vcam — активная тряхнётся, прочие вхолостую.</summary>
        public void Shake(float intensity)
        {
            for (int i = 0; i < _shakers.Count; i++) _shakers[i].Shake(intensity);
        }

        /// <summary>Снять остаточную тряску со всех vcam-расширений (перезапуск боя).</summary>
        public void ResetShake()
        {
            for (int i = 0; i < _shakers.Count; i++) if (_shakers[i] != null) _shakers[i].ResetShake();
        }

        private void OnDestroy()
        {
            if (_input != null) _input.CycleViewRequested -= OnCycleView;
        }

        // 2D-глубина: камеры смотрят на плоскость z=0 из _cameraZ. Overview/Dev — прямой z;
        // Action ведёт CinemachineFollow, поэтому правим z его смещения (x/y offset сохраняем).
        private void ApplyCameraDepth()
        {
            SetZ(_overviewCam);
            SetZ(_devCam);
            SetZ(_mapCam);

            if (_actionCam != null)
            {
                var follow = _actionCam.GetComponent<CinemachineFollow>();
                if (follow != null)
                {
                    Vector3 off = follow.FollowOffset;
                    off.z = _cameraZ;
                    follow.FollowOffset = off;
                }
            }
        }

        private void SetZ(CinemachineCamera cam)
        {
            if (cam == null) return;
            Vector3 p = cam.transform.position;
            p.z = _cameraZ;
            cam.transform.position = p;
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

        /// <summary>
        /// Войти в свободную камеру фазы расстановки (QA #4): режим Overview (ручной пан/зум в пределах зоны),
        /// но СТАРТОВЫЙ кадр — как у экшн-камеры (центр боя + зум под разброс), а НЕ отзум на всю зону.
        /// Кадр приходит явно из <c>DeploymentController</c> (позиции живых юнитов) — без гонки с focus-таймингом.
        /// </summary>
        public void EnterDeployment(Vector2 center, float spread)
        {
            _mode = CameraMode.Overview;
            ApplyMode();
            if (_overviewCam == null) return;

            float size = Mathf.Clamp(spread + _actionZoomPadding, _minZoom, MaxZoomForZone());
            Vector3 pos = ClampVisibleCenter(new Vector3(center.x, center.y, _cameraZ), size);
            _overviewCam.transform.position = pos;

            LensSettings lens = _overviewCam.Lens;
            lens.OrthographicSize = size;
            _overviewCam.Lens = lens;
        }

        /// <summary>
        /// Войти в вид карты акта: своя vcam, свои границы клампа (<paramref name="bounds"/> — область карты
        /// в мире, разнесённая от арены). Кадрируем карту целиком только при ПЕРВОМ входе: дальше позиция
        /// и зум карты — то, что игрок оставил, и поход в бой их не сбивает (боевые vcam живут отдельно).
        /// </summary>
        public void EnterMap(Rect2D bounds)
        {
            if (_mode != CameraMode.Map) _modeBeforeMap = _mode; // повторный вход не затирает точку возврата
            _mapZone = bounds;
            _mode    = CameraMode.Map;
            ApplyMode();
            if (_mapCam == null || _mapFramed) return;

            _mapFramed = true;
            float size = MaxZoomForZone(); // вся карта в кадре — стартовый вид
            Vector2 c  = bounds.Center;
            _mapCam.transform.position = new Vector3(c.x, c.y, _cameraZ);

            LensSettings lens = _mapCam.Lens;
            lens.OrthographicSize = size;
            _mapCam.Lens = lens;
        }

        /// <summary>
        /// Выйти из вида карты в тот режим, из которого в неё вошли. Карту можно открыть посреди боя —
        /// закрытие обязано вернуть взгляд ровно туда, где игрок его оставил, а не в дефолтный вид.
        /// Вне режима карты — no-op.
        /// </summary>
        public void ExitMap()
        {
            if (_mode != CameraMode.Map) return;
            _mode = _modeBeforeMap;
            ApplyMode();
        }

        /// <summary>Вернуть боевой вид (экшн-камера, слежение за дракой) — на старте боя из расстановки.</summary>
        public void ExitToActionView()
        {
            _mode = CameraMode.Action;
            ApplyMode();
        }

        private void OnCycleView()
        {
            // На карте Tab не циклит: боевые виды смотрят в другую область мира — переключение
            // увело бы камеру с карты в пустую арену. Выход из карты — только через вход в узел.
            if (_mode == CameraMode.Map) return;

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
            SetPriority(_mapCam,      _mode == CameraMode.Map);
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
                case CameraMode.Map:      DriveManual(_mapCam, _panSpeed, clampToZone: true);        break;
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

            // unscaled: чтобы можно было двигать камеру даже на паузе боя (Time.timeScale = 0).
            Vector2 pan = _input.CameraPan;
            float dt = Time.unscaledDeltaTime;
            Vector3 pos = cam.transform.position;
            pos.x += pan.x * panSpeed * dt;
            pos.y += pan.y * panSpeed * dt;

            // Пан средней кнопкой мыши (MMB-drag): дельта в пикселях → мир, инверсия (тянем мир под курсором).
            // Масштаб по орто-размеру, чтобы скорость пана совпадала с движением курсора на любом зуме.
            Vector2 drag = _input.CameraPanDrag;
            if (drag.sqrMagnitude > 0f)
            {
                float worldPerPixel = size * 2f / Mathf.Max(1, Screen.height);
                pos.x -= drag.x * worldPerPixel;
                pos.y -= drag.y * worldPerPixel;
            }

            if (clampToZone) pos = ClampVisibleCenter(pos, size);
            pos.z = _cameraZ; // держим 2D-глубину (иначе спрайты на z=0 отсекаются)
            cam.transform.position = pos;
        }

        // Экшн-камера: позицию ведёт Follow (focus target), здесь подгоняем орто-размер под разброс боя.
        private void DriveActionZoom()
        {
            if (_actionCam == null || _focus == null || !_focus.HasUnits) return;

            LensSettings lens = _actionCam.Lens;
            float desired = Mathf.Clamp(_focus.Spread + _actionZoomPadding, _minZoom, MaxZoomForZone());

            // Дедзона: удерживаемую цель зума двигаем, лишь когда разброс отошёл от неё дальше порога —
            // камера не гоняется за микро-колебаниями (юниты «дышат» на месте), но реагирует на реальный
            // разлёт/сжатие боя. Затем плавно тянемся к удерживаемой цели.
            if (_actionZoomTarget <= 0f || Mathf.Abs(desired - _actionZoomTarget) > _actionZoomDeadzone)
                _actionZoomTarget = desired;

            float t = 1f - Mathf.Exp(-_actionZoomDamping * Time.deltaTime);
            lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, _actionZoomTarget, t);
            _actionCam.Lens = lens;
        }

        // Активная зона клампа = зона ТЕКУЩЕГО режима. Боевые режимы клампятся ареной, карта — своей
        // областью мира (она разнесена от арены и в боевую рамку не влезает: 14 колонок).
        private Rect2D ActiveZone()
        {
            if (_mode == CameraMode.Map) return _mapZone;
            return _layout != null ? _layout.CameraZone : ArenaLayoutData.Unbounded.CameraZone;
        }

        // Максимальный орто-размер, при котором видимая область не превышает зону (по обеим осям).
        // Карта — исключение: она широкая и низкая, и ограничение по МЕНЬШЕЙ стороне зажимало бы отдаление
        // высотой (всю карту разом было не увидеть). Там берём бОльшую сторону — карта влезает целиком.
        private float MaxZoomForZone()
        {
            Vector2 zone = ZoneSize();
            float aspect = ScreenAspect();
            float halfH = zone.y * 0.5f;
            float halfW = (zone.x * 0.5f) / Mathf.Max(aspect, 0.0001f);
            float limit = _mode == CameraMode.Map ? Mathf.Max(halfH, halfW) : Mathf.Min(halfH, halfW);
            return Mathf.Max(_minZoom, limit);
        }

        // Кламп центра так, чтобы видимый прямоугольник (полу-высота = size) не вышел за зону.
        private Vector3 ClampVisibleCenter(Vector3 pos, float size)
        {
            Vector2 c = ActiveZone().Center;
            Vector2 zone = ZoneSize();
            float aspect = ScreenAspect();

            float slackX = Mathf.Max(0f, zone.x * 0.5f - size * aspect);
            float slackY = Mathf.Max(0f, zone.y * 0.5f - size);

            // На карте кламп мягкий: разрешаем увести её к краю экрана (доля видимой области во все
            // стороны), чтобы можно было рассмотреть угол карты вблизи, а не упираться в жёсткую рамку.
            if (_mode == CameraMode.Map)
            {
                slackX += size * aspect * _mapFreedom;
                slackY += size * _mapFreedom;
            }

            pos.x = Mathf.Clamp(pos.x, c.x - slackX, c.x + slackX);
            pos.y = Mathf.Clamp(pos.y, c.y - slackY, c.y + slackY);
            return pos;
        }

        // Ставит Overview-камеру в центр арены на весь размер зоны (вызов при включении).
        private void SnapOverviewToArena()
        {
            if (_overviewCam == null || _layout == null) return;

            Vector2 c = _layout.CameraZone.Center;
            _overviewCam.transform.position = new Vector3(c.x, c.y, _cameraZ);

            LensSettings lens = _overviewCam.Lens;
            lens.OrthographicSize = MaxZoomForZone();
            _overviewCam.Lens = lens;
        }

        private Vector2 ZoneSize()
        {
            Vector2 s = ActiveZone().Size;
            return new Vector2(Mathf.Abs(s.x), Mathf.Abs(s.y));
        }

        private static float ScreenAspect()
        {
            return Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        }
    }
}
