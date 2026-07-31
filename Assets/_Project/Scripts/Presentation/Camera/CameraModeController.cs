using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Input;
using Guildmaster.Core.Settings;
using Guildmaster.Data.Definitions;
using Unity.Cinemachine;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>Режим камеры (вики «16» §5).</summary>
    public enum CameraMode
    {
        /// <summary>Сценарная: динамично держит драку — следует за центроидом боя, зум подгоняется под разброс.</summary>
        Action,
        /// <summary>Свободная: ручной пан/зум в пределах зоны. Вид по умолчанию везде, кроме идущего боя.</summary>
        Overview,
        /// <summary>Свободная dev-камера: пан/зум без клампа (в цикл попадает только в редакторе).</summary>
        Dev,
        /// <summary>Карта акта: ручной пан/зум в пределах зоны КАРТЫ (не боевой арены).</summary>
        Map,
    }

    /// <summary>
    /// Управляет режимами камеры и потребляет <see cref="IInputService"/> (вики «16» §5).
    /// Переключение вида — сменой приоритета Cinemachine (Brain блендит). Ручной пан/зум
    /// в Overview/Dev/Map; кламп видимой области границами арены — из данных (<see cref="ArenaLayoutData"/>),
    /// без коллайдера. Dev-камера клампу не подчиняется.
    /// <para><b>Камера логически ОДНА, режимов у неё два:</b> сценарный (слежение за боем) и свободный (руль
    /// у игрока). Отсюда два правила перехода, и оба несимметричны намеренно. В свободный вид камера входит
    /// <b>подхватывая живой кадр</b> (<see cref="AdoptLiveFrame"/>) — игрок продолжает смотреть ровно туда,
    /// куда смотрел, поэтому в блендах переход в свободную стоит Cut: блендить нечего. В сценарный —
    /// наоборот, Brain делает подводку своим бленд-временем, и зум сценарной камеры взводится сразу
    /// (<see cref="PrimeActionZoom"/>), чтобы кадр приехал одним движением, а не двумя подряд.</para>
    /// <para><b>Кто выбирает вид.</b> Сценарной камере вне боя следить не за кем, поэтому владелец «какой вид
    /// сейчас» — фаза боя (<see cref="IBattleClock"/>): <see cref="BattlePhase.Fighting"/> → сценарный,
    /// всё остальное → свободный. Внутри боя выбор за игроком (Tab), и этот выбор <b>переживает бой, забег
    /// и запуск игры</b>: он лежит в настройках (<see cref="ISettingsService"/>, ключ <c>prefs</c>), а не
    /// сбрасывается на входе в каждый бой.</para>
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

        [Tooltip("Brain на Main Camera. Нужен как источник ЖИВОГО кадра: вход в свободную камеру подхватывает " +
                 "то, что игрок видит прямо сейчас, а это результат бленда — он есть только у Brain, а не у " +
                 "какой-то одной vcam. Пусто = переходы в свободный вид будут прыгать.")]
        [SerializeField] private CinemachineBrain _brain;

        [Header("Камера (глубина)")]
        [Tooltip("Z-позиция камеры (2D: отрицательная, чтобы плоскость поля z=0 попадала в кадр). " +
                 "Саму зону-ограничитель задаёт арена (ArenaLayoutAuthoring, жёлтая рамка).")]
        [SerializeField] private float _cameraZ = -10f;

        [Header("Зум")]
        [Tooltip("Ближний предел орто-размера (максимальное приближение).")]
        [SerializeField] private float _minZoom = 3f;
        [Tooltip("Шаг зума за одно деление колеса, В РАЗАХ: 1.15 = деление меняет кадр на 15%. Множитель, а не " +
                 "фиксированные единицы: у аддитивного шага вблизи одно деление прыгало через полкадра, а вдали " +
                 "почти не двигало — глаз воспринимает зум кратно.")]
        [SerializeField] private float _zoomFactor = 1.15f;
        [Tooltip("Скорость подхода зума к накрученной колесом цели. Колесо задаёт ЦЕЛЬ, кадр идёт к ней плавно — " +
                 "иначе каждое деление это скачок. Больше = резче; 0 и меньше = мгновенно.")]
        [SerializeField] private float _zoomDamping = 16f;
        [Tooltip("Верхний предел зума для dev-камеры (не привязан к зоне).")]
        [SerializeField] private float _devMaxZoom = 40f;
        [Tooltip("Свобода панорамирования на карте: насколько можно увести карту за край экрана, в долях " +
                 "видимой области. 0.5 = полэкрана в каждую сторону — угол карты можно рассмотреть вблизи, " +
                 "но совсем в пустоту не уедешь.")]
        [SerializeField] private float _mapFreedom = 0.5f;
        [Tooltip("Насколько камера приближается к узлу за время закрытия кадра (доля исходного орто-размера). " +
                 "0.45 = кадр вдвое с лишним крупнее к концу нырка; меньше — резче.")]
        [SerializeField] private float _mapDiveZoom = 0.45f;

        [Header("Панорамирование (высот кадра в секунду при полном отклонении)")]
        [Tooltip("Скорость пана в ДОЛЯХ ВИДИМОЙ ВЫСОТЫ, а не в мировых единицах: иначе на дальнем зуме камера " +
                 "ползёт через полкарты минуту, а на ближнем проносит мимо цели. 0.9 = кадр за секунду.")]
        [SerializeField] private float _panScreens = 0.9f;
        [SerializeField] private float _devPanScreens = 1.8f;

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
        private IBattleClock      _clock;      // владелец факта «идёт ли бой» — от него зависит доступность сценарной
        private ISettingsService  _settings;   // дом предпочтения вида (prefs), переживает забег и запуск игры

        // Вид в МИРЕ (Action/Overview/Dev). Карта поверх него — отдельным флагом: она временная, и выход
        // из неё обязан вернуть ровно тот мировой вид, что был. Второго поля «откуда пришли» поэтому нет.
        private CameraMode _worldMode = CameraMode.Overview;
        private bool _onMap;
        private CameraMode Mode => _onMap ? CameraMode.Map : _worldMode;

        private bool _devAccess;
        private bool _inCombat;        // фаза Fighting: только в ней сценарной камере есть за кем следить
        private bool _prefersFreeView; // выбор игрока внутри боя (Tab) — из настроек, туда же и пишется

        private readonly List<ScreenShake> _shakers = new List<ScreenShake>(3); // тряска на каждой vcam
        // Удерживаемая цель зума экшн-камеры (обновляется через дедзону, см. DriveActionZoom). ≤0 = ещё не задана.
        private float _actionZoomTarget = -1f;
        // Цель зума РУЧНОЙ камеры: колесо крутит её, кадр идёт следом (см. DriveManual). ≤0 = перечитать с камеры.
        private float _freeZoomTarget = -1f;

        // Зона карты: границы клампа для CameraMode.Map. Приходит снаружи (EnterMap) — карта живёт в
        // СВОЕЙ области мира, боевая _layout.CameraZone к ней отношения не имеет.
        private Rect2D _mapZone;
        // Кадрируем карту целиком только при ПЕРВОМ входе. Дальше не трогаем позицию/зум: игрок мог
        // отъехать и приблизиться, и это должно пережить поход в бой и обратно.
        private bool _mapFramed;

        // Нырок в узел: кадр, из которого нырнули, чтобы вернуть его целиком. Пока ныряем, ручной пан/зум
        // молчит — иначе колесо посреди перехода спорит с наездом.
        private bool _mapDiving;
        private Vector3 _mapFrameBeforeDive;
        private float _mapSizeBeforeDive;

        // Интро карты: отъезд на весь акт и возвращение к рабочему кадру. В отличие от нырка ПРЕРЫВАЕТСЯ
        // игроком — как только он взялся за колесо или потянул карту, камера остаётся там, где застал
        // ввод, и слушается его. Доводить кадр после этого значило бы спорить с рукой на руле.
        private bool _mapIntro;
        private Vector3 _mapIntroFrom, _mapIntroTo;
        private float _mapIntroSizeFrom, _mapIntroSizeTo;
        private float _mapIntroT, _mapIntroDuration;

        [Inject]
        public void Construct(IInputService input, ArenaLayoutData layout, CombatFocusTarget focus,
                              Design.CombatFeelConfig feel, IBattleClock clock, ISettingsService settings)
        {
            _input    = input;
            _layout   = layout;
            _focus    = focus;
            _feel     = feel;
            _clock    = clock;
            _settings = settings;
        }

        // Подписку и стартовую настройку делаем в Start, а НЕ в OnEnable: компонент инъектится
        // VContainer'ом во время Build (в Awake скоупа боя), а [Camera] стоит выше [Combat] в
        // иерархии — его OnEnable успел бы отработать до инъекции (_input == null) и подписка на
        // Tab потерялась бы. Start гарантированно после всех Awake, т.е. после инъекции.
        private void Start()
        {
            if (_input != null) _input.CycleViewRequested += OnCycleView;
            if (_clock != null) _clock.PhaseChanged += OnPhaseChanged;

            // В редакторе dev-камера доступна сразу (удобно тестить), в билде — нет: обычный игрок
            // циклит только Action↔Overview. Прежде доступ можно было выдать и в рантайме
            // (SetDevAccess), но команды, ради которой ручка существовала, в проекте нет — снята.
            _devAccess = Application.isEditor;

            _prefersFreeView = _settings != null && _settings.Gameplay.FreeCombatCamera;
            _inCombat        = _clock != null && _clock.Phase == BattlePhase.Fighting;
            _worldMode       = WantedWorldMode();

            ApplyCameraDepth();
            SnapOverviewToArena();
            ApplyPriorities();

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
            if (_clock != null) _clock.PhaseChanged -= OnPhaseChanged;
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

        // ── Кто решает, какой вид сейчас ──────────────────────────────────────

        // Сценарная камера существует ТОЛЬКО для идущего боя: вне его следить не за кем, и вид там всегда
        // свободный. Dev-камера липкая — из неё фаза не выкидывает: её включают, чтобы смотреть, и смена
        // фазы посреди осмотра была бы враньём по смыслу.
        private CameraMode WantedWorldMode()
        {
            if (_worldMode == CameraMode.Dev && _devAccess) return CameraMode.Dev;
            return _inCombat && !_prefersFreeView ? CameraMode.Action : CameraMode.Overview;
        }

        private void OnPhaseChanged()
        {
            bool fighting = _clock != null && _clock.Phase == BattlePhase.Fighting;
            if (_inCombat == fighting) return;
            _inCombat = fighting;
            SetWorldMode(WantedWorldMode());
        }

        private void OnCycleView()
        {
            // На карте Tab не циклит: боевые виды смотрят в другую область мира — переключение
            // увело бы камеру с карты в пустую арену. Выход из карты — только через вход в узел.
            if (_onMap) return;

            CameraMode next = NextMode(_worldMode, _devAccess, _inCombat);
            if (next == _worldMode) return;
            SetWorldMode(next);

            // Tab между слежением и свободой — это выбор игрока «как я хочу смотреть бой», и он должен
            // пережить бой, забег и перезапуск игры. Дев-камера предпочтения не трогает: она инструмент.
            if (next == CameraMode.Action || next == CameraMode.Overview)
                SetPreference(next == CameraMode.Overview);
        }

        /// <summary>
        /// Следующий вид в цикле Tab. Вне боя <see cref="CameraMode.Action"/> из цикла ВЫПАДАЕТ — сценарной
        /// камере там не за кем следить, и вид «слежение за пустой ареной» игроку показывать нечестно.
        /// Без dev-доступа вне боя цикл вырождается в один свободный вид, и Tab становится no-op.
        /// </summary>
        internal static CameraMode NextMode(CameraMode mode, bool devAccess, bool inCombat)
        {
            switch (mode)
            {
                case CameraMode.Action:   return CameraMode.Overview;
                case CameraMode.Overview: return devAccess ? CameraMode.Dev
                                               : (inCombat ? CameraMode.Action : CameraMode.Overview);
                case CameraMode.Dev:      return inCombat ? CameraMode.Action : CameraMode.Overview;
                default:                  return CameraMode.Overview;
            }
        }

        // Пишем предпочтение сразу на диск, а не по кнопке «Применить»: экрана настроек у этого выбора нет,
        // он делается Tab'ом посреди боя — и вылет игры не должен его съесть. Файл prefs крошечный, запись
        // атомарная (ISaveService), а Tab жмут единицы раз за бой.
        private void SetPreference(bool free)
        {
            if (_prefersFreeView == free) return;
            _prefersFreeView = free;
            if (_settings == null) return;
            _settings.SetFreeCombatCamera(free);
            _settings.Save();
        }

        private void SetWorldMode(CameraMode mode)
        {
            if (_worldMode == mode) return;
            _worldMode = mode;
            if (_onMap) return; // карта поверх: новый мировой вид ждёт выхода с неё
            EnterMode(mode);
        }

        // Активация вида. Свободная камера ПОДХВАТЫВАЕТ живой кадр (переход выходит бесшовным — в блендах
        // вход в свободную стоит Cut), сценарная взводит свой зум и отдаётся Brain'у на подводку.
        private void EnterMode(CameraMode mode)
        {
            switch (mode)
            {
                case CameraMode.Overview: AdoptLiveFrame(_overviewCam, clampToZone: true);  break;
                case CameraMode.Dev:      AdoptLiveFrame(_devCam, clampToZone: false);      break;
                case CameraMode.Action:   PrimeActionZoom();                                break;
            }

            _freeZoomTarget = -1f; // цель колеса перечитается с новой активной камеры
            ApplyPriorities();
        }

        /// <summary>
        /// Перенести на <paramref name="cam"/> кадр, который игрок видит ПРЯМО СЕЙЧАС (позиция и орто-размер
        /// с выхода Brain — то есть результат бленда, а не состояние какой-то одной vcam).
        /// <para>Это и есть «камера одна»: выход в свободный вид не переносит взгляд, он просто отдаёт руль.
        /// Без подхвата свободная камера стояла бы там, где её оставили полчаса назад, и Tab читался бы как
        /// телепорт.</para>
        /// </summary>
        private void AdoptLiveFrame(CinemachineCamera cam, bool clampToZone)
        {
            if (cam == null) return;
            if (_brain == null || _brain.OutputCamera == null)
            {
                Debug.LogError("[CameraModeController] - не разведён Brain → свободная камера не подхватит " +
                               "живой кадр, переход в неё будет прыгать.");
                return;
            }

            Camera live = _brain.OutputCamera;
            float maxZoom = clampToZone ? MaxZoomForZone() : _devMaxZoom;
            float size = Mathf.Clamp(live.orthographicSize, _minZoom, maxZoom);

            var pos = new Vector3(live.transform.position.x, live.transform.position.y, _cameraZ);
            if (clampToZone) pos = ClampVisibleCenter(pos, size);
            cam.transform.position = pos;

            LensSettings lens = cam.Lens;
            lens.OrthographicSize = size;
            cam.Lens = lens;
        }

        // Вход в слежение: цель зума считаем от текущего боя и ставим её на камеру СРАЗУ, до бленда. Иначе
        // подводка Brain и собственный лерп зума (DriveActionZoom) накладываются друг на друга, и кадр
        // читается как два разных движения подряд вместо одного.
        private void PrimeActionZoom()
        {
            _actionZoomTarget = -1f;
            if (_actionCam == null || _focus == null || !_focus.HasUnits) return;

            _actionZoomTarget = Mathf.Clamp(_focus.Spread + _actionZoomPadding, _minZoom, MaxZoomForZone());
            LensSettings lens = _actionCam.Lens;
            lens.OrthographicSize = _actionZoomTarget;
            _actionCam.Lens = lens;
        }

        // ── Карта акта ────────────────────────────────────────────────────────

        /// <summary>
        /// Войти в вид карты акта: своя vcam, свои границы клампа (<paramref name="bounds"/> — область карты
        /// в мире, разнесённая от арены). Кадрируем карту целиком только при ПЕРВОМ входе: дальше позиция
        /// и зум карты — то, что игрок оставил, и поход в бой их не сбивает (боевые vcam живут отдельно).
        /// </summary>
        /// <param name="bounds">Область карты в мире — границы клампа.</param>
        /// <param name="focus">Куда смотреть при первом входе: узел, где игрок стоит сейчас.</param>
        /// <param name="visibleHeight">Желаемая высота кадра в мировых единицах (крупный вид, не вся карта).</param>
        /// <returns>
        /// Правда, если кадр ставился ВПЕРВЫЕ. По этому же признаку карта решает, играть ли интро:
        /// «первый показ» должен быть одним фактом на двоих, иначе камера и рост дорожек однажды
        /// разойдутся во мнении, первый он или нет.
        /// </returns>
        public bool EnterMap(Rect2D bounds, Vector2 focus, float visibleHeight)
        {
            _mapZone = bounds;
            _onMap   = true;
            _freeZoomTarget = -1f;
            ApplyPriorities();
            if (_mapCam == null || _mapFramed) return false;

            // Стартовый кадр — КРУПНО у текущего узла, а не вся карта разом: игрок должен видеть, где он
            // и куда шагнуть, а обзор целиком берётся колесом.
            _mapFramed = true;
            float size = Mathf.Clamp(visibleHeight * 0.5f, _minZoom, MaxZoomForZone());
            Vector3 pos = ClampVisibleCenter(new Vector3(focus.x, focus.y, _cameraZ), size);
            _mapCam.transform.position = pos;

            LensSettings lens = _mapCam.Lens;
            lens.OrthographicSize = size;
            _mapCam.Lens = lens;
            return true;
        }

        /// <summary>
        /// Интро карты: показать акт ЦЕЛИКОМ и съехать к рабочему кадру, который уже поставил
        /// <see cref="EnterMap"/>. Целью берётся текущее положение vcam — «куда приехать» остаётся одним
        /// решением на всю карту, а интро только добавляет к нему дорогу.
        /// <para>ПРЕРЫВАЕТСЯ вводом игрока (см. <see cref="ManualCameraInput"/>): камера замирает там, где
        /// её застали, и тем же кадром отдаётся в руки. Ни доводки, ни возврата — перехват руля значит,
        /// что смотреть игрок будет сам.</para>
        /// </summary>
        /// <param name="seconds">Длительность проезда. Ноль или меньше — интро не играется вовсе.</param>
        public void PlayMapIntro(float seconds)
        {
            if (_mapCam == null || !_onMap || seconds <= 0f) return;

            _mapIntroTo       = _mapCam.transform.position;
            _mapIntroSizeTo   = _mapCam.Lens.OrthographicSize;

            // Стартовый кадр — весь акт: центр зоны карты и предельное отдаление, которое ей разрешено.
            _mapIntroSizeFrom = MaxZoomForZone();
            Rect2D zone = ActiveZone();
            _mapIntroFrom     = new Vector3(zone.Center.x, zone.Center.y, _cameraZ);

            _mapIntroDuration = seconds;
            _mapIntroT        = 0f;
            _mapIntro         = true;

            ApplyMapIntro(0f);
        }

        /// <summary>Идёт ли проезд интро — карта по нему решает, показывать ли себя целиком.</summary>
        public bool MapIntroPlaying => _mapIntro;

        private void TickMapIntro()
        {
            _mapIntroT += Time.unscaledDeltaTime;
            float p = _mapIntroDuration > 0f ? Mathf.Clamp01(_mapIntroT / _mapIntroDuration) : 1f;

            // Торможение к концу: бросок случается сразу, а у цели камера подходит мягко.
            float e = 1f - Mathf.Pow(1f - p, 3f);
            ApplyMapIntro(e);

            if (p >= 1f) _mapIntro = false;
        }

        private void ApplyMapIntro(float e)
        {
            // Зум интерполируем в логарифме: орто-размер меняется в разы, и линейный ход читался бы как
            // рывок в начале и ползание в конце — глаз воспринимает зум кратно, а не в единицах.
            float size = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(0.01f, _mapIntroSizeFrom)),
                                              Mathf.Log(Mathf.Max(0.01f, _mapIntroSizeTo)), e));
            Vector3 pos = ClampVisibleCenter(Vector3.Lerp(_mapIntroFrom, _mapIntroTo, e), size);
            pos.z = _cameraZ;

            _mapCam.transform.position = pos;

            LensSettings lens = _mapCam.Lens;
            lens.OrthographicSize = size;
            _mapCam.Lens = lens;

            _freeZoomTarget = -1f; // кадр вели мы, а не колесо — цель зума перечитать с камеры
        }

        // Игрок сам двигает камеру? Любой из трёх каналов ручного управления — колесо, WASD, MMB-drag.
        // Это единственное условие прерывания подачи: клики и наведение камеру не трогают.
        private bool ManualCameraInput() =>
            _input != null &&
            (Mathf.Abs(_input.CameraZoomDelta) > 0.0001f ||
             _input.CameraPan.sqrMagnitude > 0.0001f ||
             _input.CameraPanDrag.sqrMagnitude > 0.0001f);

        /// <summary>
        /// Нырок в узел на время закрытия кадра: камера подъезжает к выбранной точке и приближается к ней.
        /// Кадр, из которого нырнули, запоминается — вернёт его <see cref="SurfaceMap"/>.
        /// </summary>
        /// <param name="focus">Узел, в который входим.</param>
        /// <param name="progress">Ход закрытия шторки, 0..1. Нырок идёт с ней в ногу, а не своим темпом.</param>
        public void DiveMapTo(Vector2 focus, float progress)
        {
            if (_mapCam == null || !_onMap) return;

            _mapIntro = false; // выбрали узел, не досмотрев интро — подача уступает переходу

            if (!_mapDiving)
            {
                _mapDiving          = true;
                _mapFrameBeforeDive = _mapCam.transform.position;
                _mapSizeBeforeDive  = _mapCam.Lens.OrthographicSize;
            }

            float t = Mathf.Clamp01(progress);

            // Целимся НЕ в сам узел, а чуть-чуть не доезжая: полный доезд к концу закрытия выглядит как
            // рывок в упор, а нам нужно ощущение начатого движения, которое кадр обрывает на середине.
            float size = Mathf.Max(_minZoom, Mathf.Lerp(_mapSizeBeforeDive, _mapSizeBeforeDive * _mapDiveZoom, t));
            var target = new Vector3(focus.x, focus.y, _cameraZ);
            Vector3 pos = Vector3.Lerp(_mapFrameBeforeDive, target, t);

            _mapCam.transform.position = ClampVisibleCenter(pos, size);

            LensSettings lens = _mapCam.Lens;
            lens.OrthographicSize = size;
            _mapCam.Lens = lens;
        }

        /// <summary>
        /// Вернуть кадр карты, из которого ныряли. Зовётся на ЗАКРЫТОМ кадре: возврат не виден, а следующий
        /// показ карты начинается с того вида, который игрок оставил, а не изнутри узла.
        /// </summary>
        public void SurfaceMap()
        {
            if (!_mapDiving) return;
            _mapDiving = false;

            if (_mapCam == null) return;
            _mapCam.transform.position = _mapFrameBeforeDive;

            LensSettings lens = _mapCam.Lens;
            lens.OrthographicSize = _mapSizeBeforeDive;
            _mapCam.Lens = lens;

            _freeZoomTarget = -1f;
        }

        /// <summary>
        /// Выйти из вида карты в мировой вид. Какой именно — решает фаза и предпочтение игрока
        /// (<see cref="WantedWorldMode"/>), а не то, что было на экране до карты: карту можно открыть в
        /// передышке и закрыть уже в бою. Вне режима карты — no-op.
        /// </summary>
        public void ExitMap()
        {
            if (!_onMap) return;
            _mapIntro = false;
            SurfaceMap(); // карту могли закрыть посреди нырка — кадр обязан вернуться, а не остаться в узле
            _onMap = false;
            _worldMode = WantedWorldMode();
            EnterMode(_worldMode);
        }

        // ── Покадровое управление ─────────────────────────────────────────────

        private void ApplyPriorities()
        {
            CameraMode mode = Mode;
            SetPriority(_actionCam,   mode == CameraMode.Action);
            SetPriority(_overviewCam, mode == CameraMode.Overview);
            SetPriority(_devCam,      mode == CameraMode.Dev);
            SetPriority(_mapCam,      mode == CameraMode.Map);
        }

        private void SetPriority(CinemachineCamera cam, bool active)
        {
            if (cam != null) cam.Priority = active ? _activePriority : _inactivePriority;
        }

        private void Update()
        {
            if (_input == null) return;
            switch (Mode)
            {
                case CameraMode.Overview: DriveManual(_overviewCam, _panScreens, clampToZone: true);  break;
                case CameraMode.Dev:      DriveManual(_devCam, _devPanScreens, clampToZone: false);   break;
                // Пока ныряем в узел, ручной пан/зум молчит: колесо посреди перехода спорило бы с наездом.
                // Интро — наоборот: ввод его отменяет, и руль отдаётся ТЕМ ЖЕ кадром, чтобы движение,
                // которым игрок прервал подачу, не пропало впустую.
                case CameraMode.Map:
                    if (_mapDiving) break;
                    if (_mapIntro)
                    {
                        if (!ManualCameraInput()) { TickMapIntro(); break; }
                        _mapIntro = false;
                    }
                    DriveManual(_mapCam, _panScreens, clampToZone: true);
                    break;
                case CameraMode.Action:   DriveActionZoom();                                          break;
            }
        }

        // Ручной пан + зум (Overview/Dev/Map). Клампит видимую область границами зоны (если clampToZone).
        private void DriveManual(CinemachineCamera cam, float panScreens, bool clampToZone)
        {
            if (cam == null) return;

            LensSettings lens = cam.Lens;
            float size = lens.OrthographicSize;
            float maxZoom = clampToZone ? MaxZoomForZone() : _devMaxZoom;

            // Колесо крутит ЦЕЛЬ, а кадр идёт к ней следом: у мгновенного шага каждое деление читалось
            // как рывок, а серия делений — как дёрганая лестница.
            if (_freeZoomTarget <= 0f) _freeZoomTarget = size;
            float wheel = _input.CameraZoomDelta;
            if (wheel > 0f)      _freeZoomTarget /= _zoomFactor; // колесо вперёд — приблизить
            else if (wheel < 0f) _freeZoomTarget *= _zoomFactor; // колесо назад — отдалить
            _freeZoomTarget = Mathf.Clamp(_freeZoomTarget, _minZoom, maxZoom);

            float dt = Time.unscaledDeltaTime; // unscaled: камера живёт и на паузе боя (Time.timeScale = 0)
            float newSize = _zoomDamping > 0f
                ? Mathf.Lerp(size, _freeZoomTarget, 1f - Mathf.Exp(-_zoomDamping * dt))
                : _freeZoomTarget;
            newSize = Mathf.Clamp(newSize, _minZoom, maxZoom);

            lens.OrthographicSize = newSize;
            cam.Lens = lens;

            Vector3 pos = cam.transform.position;

            // Зум держит точку под курсором на месте: иначе колесо всегда тянет кадр к центру экрана, и до
            // угла арены приходится доезжать паном. Над UI-панелью якоря нет — мира под курсором не видно.
            if (!Mathf.Approximately(newSize, size) && !_input.PointerOverUI)
                pos += ZoomAnchorShift(_input.PointerScreenPosition, size, newSize);

            // Пан в долях видимой высоты: на дальнем зуме те же WASD проходят тот же кусок КАДРА, а не
            // ползут по нему втрое дольше.
            Vector2 pan = _input.CameraPan;
            float panSpeed = panScreens * newSize * 2f;
            pos.x += pan.x * panSpeed * dt;
            pos.y += pan.y * panSpeed * dt;

            // Пан средней кнопкой мыши (MMB-drag): дельта в пикселях → мир, инверсия (тянем мир под курсором).
            // Масштаб по орто-размеру, чтобы скорость пана совпадала с движением курсора на любом зуме.
            Vector2 drag = _input.CameraPanDrag;
            if (drag.sqrMagnitude > 0f)
            {
                float worldPerPixel = WorldPerPixel(newSize);
                pos.x -= drag.x * worldPerPixel;
                pos.y -= drag.y * worldPerPixel;
            }

            if (clampToZone) pos = ClampVisibleCenter(pos, newSize);
            pos.z = _cameraZ; // держим 2D-глубину (иначе спрайты на z=0 отсекаются)
            cam.transform.position = pos;
        }

        // Сдвиг центра, при котором мировая точка под курсором остаётся под курсором после смены зума.
        // Курсор вне экрана (окно неактивно, ноль до первого движения мыши) — якоря нет, зум идёт в центр.
        private static Vector3 ZoomAnchorShift(Vector2 cursor, float sizeBefore, float sizeAfter)
        {
            if (Screen.height <= 0) return Vector3.zero;
            if (cursor.x < 0f || cursor.y < 0f || cursor.x > Screen.width || cursor.y > Screen.height)
                return Vector3.zero;

            Vector2 fromCenter = cursor - new Vector2(Screen.width, Screen.height) * 0.5f;
            float delta = (sizeBefore - sizeAfter) * 2f / Screen.height;
            return new Vector3(fromCenter.x * delta, fromCenter.y * delta, 0f);
        }

        private static float WorldPerPixel(float orthoSize) => orthoSize * 2f / Mathf.Max(1, Screen.height);

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

        // ── Зона и кламп ──────────────────────────────────────────────────────

        // Активная зона клампа = зона ТЕКУЩЕГО режима. Боевые режимы клампятся ареной, карта — своей
        // областью мира (она разнесена от арены и в боевую рамку не влезает: 14 колонок).
        private Rect2D ActiveZone()
        {
            if (_onMap) return _mapZone;
            return _layout != null ? _layout.CameraZone : ArenaLayoutData.Unbounded.CameraZone;
        }

        // Максимальный орто-размер: столько, чтобы зона влезала в кадр ЦЕЛИКОМ (по большей стороне).
        // Раньше боевые режимы резались по меньшей — и всю арену разом увидеть было нельзя, а стартовый
        // кадр «вся арена» первое же колесо схлопывало. За краем зоны не чернота, а тот же мир, поэтому
        // цена такого отдаления — полоска двора по бокам, и она дешевле, чем невозможность увидеть бой целиком.
        private float MaxZoomForZone()
        {
            Vector2 zone = ZoneSize();
            float aspect = ScreenAspect();
            float halfH = zone.y * 0.5f;
            float halfW = (zone.x * 0.5f) / Mathf.Max(aspect, 0.0001f);
            return Mathf.Max(_minZoom, Mathf.Max(halfH, halfW));
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
            if (_onMap)
            {
                slackX += size * aspect * _mapFreedom;
                slackY += size * _mapFreedom;
            }

            pos.x = Mathf.Clamp(pos.x, c.x - slackX, c.x + slackX);
            pos.y = Mathf.Clamp(pos.y, c.y - slackY, c.y + slackY);
            return pos;
        }

        // Стартовый кадр свободной камеры: вся арена в отдалении, из центра зоны. Ставится ОДИН раз, на
        // включении: дальше позиция и зум — то, что оставил игрок, и ни бой, ни расстановка их не сбивают.
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
