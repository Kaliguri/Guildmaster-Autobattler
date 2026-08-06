using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using LitMotion;
using UnityEngine;
using UnityEngine.Events;
// Псевдонимы: `Body` — ещё и свойство вида (IUnitBodyVisual Body), поэтому `Body.PartMask` в выражении
// резолвится в свойство, а не в неймспейс. Алиасы снимают конфликт — пишем короткие имена.
using PartMask = Guildmaster.Presentation.Body.PartMask;
using UnitPart = Guildmaster.Presentation.Body.UnitPart;
using HandSlot = Guildmaster.Presentation.Body.HandSlot;
using CastGlowMask = Guildmaster.Presentation.Body.CastGlowMask;
using UnitPartGeometry = Guildmaster.Presentation.Body.UnitPartGeometry;
using BodySide = Guildmaster.Presentation.Body.BodySide;
using RigNaming = Guildmaster.Presentation.Body.RigNaming;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// World-space визуальное представление <see cref="RuntimeUnit"/>. Интерполирует позицию между
    /// тиками (сим 30 Hz, рендер 60+ fps) и проигрывает анимацию через <see cref="Animator"/> —
    /// клипы берутся из <see cref="UnitVisual"/> сборкой <see cref="AnimatorOverrideController"/> поверх
    /// базового контроллера. Состояние выбирает чистый <see cref="UnitAnimationSelector"/> по наблюдаемому
    /// состоянию сима; тайминг Attack привязан к сим-windup (маркер клипа садится на тик удара), Run
    /// «прибит к земле». <c>animator.fireEvents=false</c> — маркеры это данные, а не колбэки. Анимация
    /// НИКОГДА не пишет в сим. Базовая реакция на удар (вспышка/сплющивание/локальный hitstop) — кодом
    /// на LitMotion (zero-alloc); <see cref="UnityEvent"/>-хуки оставлены пустым швом под точечный MMF.
    /// <para>
    /// Само ТЕЛО вид держит за швом <see cref="Body.IUnitBodyVisual"/> и не знает, один это спрайт или
    /// полтора десятка частей: тинт, вспышку, голограмму, контур, сортировку, силуэт и осколки он адресует
    /// телу целиком. До шва всё это писалось в один <see cref="SpriteRenderer"/>, и у составного юнита
    /// доставалось одной части — вспыхивала грудь, разлетался торс.
    /// </para>
    /// </summary>
    public sealed class UnitView : MonoBehaviour, Effects.ISwingArcSource
    {
        private const float MoveEpsilonSq = 1e-6f;
        private const int   YSortPrecision = 100; // ордеров на 1 мировую ед. Y (0.01 Y = 1 ордер) — Y-сортировка тел

        [Header("Components")]
        [Tooltip("Animator, играющий клипы рига.")]
        [SerializeField] private Animator _animator;
        [Tooltip("Тело юнита: компонент на корне визуала, владеющий всеми частями. Вспышка, тинт и осколки " +
                 "идут по ВСЕМ частям через него.")]
        [SerializeField] private Body.SkeletalBodyVisual _skeletalBody;
        [SerializeField] private HealthBarView  _healthBar;
        [Tooltip("Бар ресурса (мана/ярость). Пусто = без бара; скрывается сам для безресурсных юнитов.")]
        [SerializeField] private ManaBarView    _manaBar;
        [Tooltip("Общий контейнер world-UI (бары + подпись) — нода 'UI'. Гасится целиком при смерти. " +
                 "Пусто = фолбэк на поштучное скрытие баров/подписи.")]
        [SerializeField] private GameObject     _worldUi;

        [Header("Animation")]
        // Клипы играются из контроллера Animator по именам стейтов (Idle/Run/Attack/Death/Hit/Skill1-4) —
        // на префабе вручную указывать НЕ надо. _visual берётся из данных юнита (UnitData.Visual) авто и нужен
        // ТОЛЬКО для маркера контакта/темпа бега (те же данные, что читает сим для windup).
        private UnitVisual _visual;

        [Tooltip("Бег «прибит к земле»: сколько мировых юнитов проходит клип бега за СЕКУНДУ на скорости 1. " +
                 "Меньше = ноги быстрее (бодрее), больше = медленнее. Темп бега привязан к скорости — не скользит.\n" +
                 "Покадровый юнит: (ед. на кадр) × (кадров в секунду), у нас 0.15 × 10 = 1.5.\n" +
                 "Скелетный: (ед. за цикл шагов) ÷ (длина клипа), кадров у него нет.")]
        [SerializeField] private float _runUnitsPerSecond = 1.5f;

        [Tooltip("То же самое для клипа РАЗБЕГА: сколько мировых юнитов проходит клип спринта за секунду. " +
                 "У спринта свой шаг и своя длина цикла, поэтому число у него ОТДЕЛЬНОЕ — общее с бегом " +
                 "листало клип вдвое быстрее земли, и ноги мельтешили.\n" +
                 "0 = клипа разбега у юнита нет (покадровый бестиарий), темп берётся от бега.")]
        [SerializeField] private float _sprintUnitsPerSecond;

        [Tooltip("Секунды блендинга между ПОЗНЫМИ стейтами (Idle / CombatIdle / Stun и переходы в/из них): " +
                 "без него выход из стана и наведение цели щёлкали позу за кадр. Локомоция Run↔Sprint " +
                 "блендится своим окном с переносом фазы шага; вход в свинг на базе покадровых — снапом, там " +
                 "кадр сразу перехватывает скраб. 0 = мгновенный снап, как было.")]
        [SerializeField] private float _poseBlendSeconds = 0.13f;

        [Header("Feel Hooks (пустой шов под точечный MMF_Player в Inspector)")]
        [SerializeField] private UnityEvent _onHitFeedback;
        [SerializeField] private UnityEvent _onDeathFeedback;

        [Header("Feel — реакция на попадание (LitMotion, код)")]
        // Материал вспышки (Guildmaster/Sprite/HitFlash с параметром _FlashAmount) стоит ПРЯМО на частях тела
        // в префабе — без рантайм-свапа. Свап базового материала ломал per-instance путь спрайта (тинт/флип
        // не подхватывались до первого MPB). Часть без этого материала физически не умеет вспыхивать — за
        // раскладку по всем частям отвечает SkeletalBodyVisual и его валидатор.
        // Длительности/сила/цвет вспышки — из CombatFeelConfig (ApplyFeelConfig).

        // Подписи-имени над юнитом НЕТ и не планируется (решение 2026-07-31/67): ни один автобаттлер жанра
        // не пишет имя над головой — опознание идёт силуэтом, цветом бара и плашкой инфы по наведению.
        // Поле _nameLabel и метод SetLabel сняты здесь же; не возвращать без пересмотра решения.

        [Header("Attach points (сокеты) — GO под 'Sprite Visual', ставятся по арту")]
        [Tooltip("Ноги: точка касания земли (совпадает с позицией юнита в симе).")]
        [SerializeField] private Transform _feetPoint;
        [Tooltip("Голова: макушка — якорь для баров/статус-текста.")]
        [SerializeField] private Transform _headPoint;
        [Tooltip("Точка выстрела: откуда визуально вылетает снаряд/каст (у команды 1 отзеркалить по X — см. ShotPoint).")]
        [SerializeField] private Transform _shotPoint;
        [Tooltip("Точка попадания: куда прилетают снаряды/цифры урона/вспышка (обычно грудь).")]
        [SerializeField] private Transform _hitPoint;

        [Header("Gizmo — эталонный габарит + коллизия (только редактор)")]
        [Tooltip("Эталонная ВЫСОТА юнита, мировые ед. (1 = 1 метр). Зелёная рамка — подгоняй размер спрайта под неё.")]
        [SerializeField] private float _recommendedHeight = 1.7f;
        [Tooltip("Эталонная ШИРИНА юнита, мировые ед. Горизонталь у ног + рамка.")]
        [SerializeField] private float _recommendedWidth = 0.7f;
        [Tooltip("Превью Size для гизмо круга коллизии, когда юнит ещё не заспавнен (рантайм берёт настоящий Size).")]
        [SerializeField] private float _gizmoPreviewSize = 1f;
        [Tooltip("Показывать оранжевый круг коллизии симуляции (радиус = Size × SimTuning.BodyRadiusPerSize). Выключи, если мешает.")]
        [SerializeField] private bool _showCollisionGizmo = true;

        // Пик подсветки телеграфа: заметно, но слабее удара — подводка не должна читаться как попадание.
        private const float TelegraphPeak = 0.55f;

        private static readonly int IdleHash   = Animator.StringToHash("Idle");
        private static readonly int RunHash     = Animator.StringToHash("Run");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int DeathHash   = Animator.StringToHash("Death");
        // Состояния скелетного бестиария. Имена совпадают со стейтами контроллера: у покадровых юнитов
        // таких стейтов нет, и Animator.Play по несуществующему имени — тихий no-op, поэтому они просто
        // продолжают играть то, что играли (см. HashFor: разбег у них падает обратно в бег).
        private static readonly int SprintHash       = Animator.StringToHash("Sprint");
        private static readonly int AttackChargeHash = Animator.StringToHash("AttackCharge");
        private static readonly int StunHash         = Animator.StringToHash("Stun");
        private static readonly int CombatIdleHash   = Animator.StringToHash("CombatIdle");

        // Свинг тоже живёт СЛОЕМ — по той же причине, что и гвардия: удар это работа рук и корпуса, а не
        // всего тела целиком. Пока он занимал базовый слой, «бей на бегу» упиралось в выбор одного из двух:
        // либо клип атаки (и ноги замирали посреди разбега), либо клип бега (и удара не было видно).
        // Маска слоя — торс, голова, обе руки; ноги остаются за базой и продолжают тот шаг, что шли.
        private const string ActionLayerName     = "Action";
        // Таз — отдельным АДДИТИВНЫМ слоем, а не частью маски действия: override поставил бы таз в позу
        // клипа атаки и стёр качание бега, аддитив же кладёт дельту удара ПОВЕРХ этого качания. Замер
        // 30.07: override давал −0.022 (bob бега исчезал), аддитив — 0.038 − 0.037 = 0.001.
        private const string ActionHipsLayerName = "ActionHips";

        /// <summary>
        /// За сколько рука уходит со слоя действия обратно в локомоцию. Вход в свинг мгновенный и это не
        /// симметрично намеренно: кадр контакта привязан к сим-тику, и плавный въезд смазал бы начало
        /// замаха, тогда как уходить руке некуда торопиться.
        /// </summary>
        private const float ActionDropSeconds = 0.12f;

        // Гвардия живёт СЛОЕМ, а не стейтом базы: щит поднимается поверх того, что юнит делает — бежит,
        // бьёт, стоит, — и потому не имеет права занимать собой всё тело. Маска слоя — только рука со щитом.
        private const string GuardLayerName    = "Block";
        private static readonly int GuardHash  = Animator.StringToHash("Block");
        private const float GuardRaiseSeconds  = 0.10f;   // въезд веса: гвардия, которая едет вверх, приезжает поздно
        private const float GuardDropSeconds   = 0.22f;   // уход мягче въезда — рука опускается, а не отщёлкивает

        /// <summary>
        /// За сколько до события щит обязан УЖЕ СТОЯТЬ (требование Макса 29.07). Телеграф — это не «успеть к
        /// кадру», а «встать и подождать»: подъём кончается на столько раньше, и остаток подводки поза держится
        /// стоп-кадром. Без этой паузы щит приезжал в финал одновременно с барьером и читался как реакция на
        /// него, а не как предупреждение о нём.
        /// </summary>
        private const float GuardSettleSeconds = 0.15f;

        /// <summary>
        /// Куда скрабится клип гвардии, если он НЕ размечен маркерами: считаем, что весь клип — подъём, а
        /// возврат делает вес слоя. Это честная деградация «клип как одна поза», а не подобранное число:
        /// доля подъёма живёт в клипе (<see cref="ClipMarkers.GuardWindowNormalized"/>), и подменять её
        /// константой нельзя — именно так показ и разъехался с клипом 2026-08-04.
        /// </summary>
        private const float GuardUnmarkedUp = 1f;

        // Состояние берётся из ленты боя, а НЕ из живого RuntimeUnit: сим уходит вперёд на окно
        // опережения, и живой юнит для показа — «будущее». Определение (UnitData) при этом статично,
        // поэтому живёт отдельной ссылкой и на тик не копируется.
        private Combat.Tape.UnitSnapshot _snapshot;
        private bool                     _hasState;
        private Data.Definitions.UnitData _definition;

        /// <summary>
        /// Ключ, под которым о дефекте визуала говорится один раз (<see cref="VisualDefects"/>). Это
        /// ОПРЕДЕЛЕНИЕ, а не экземпляр: вид переиспользуется пулом, и «дефект вида» на деле дефект кита.
        /// </summary>
        private string DefectKey => _definition != null ? _definition.Id.ToString() : name;

        // Снимок цели того же тика — нужен только развороту и вздрогу при смене цели.
        private Combat.Tape.UnitSnapshot _targetState;
        private bool                     _hasTargetState;

        private Vector2     _renderPosition;

        // Доля внутри показываемого тика [0..1] — та же, по которой интерполируется позиция. Поза атаки
        // скрабится ЕЮ, а не целыми счётчиками снимка: иначе клип получал бы ровно TickRate положений в
        // секунду (при замахе в 6 тиков — 6 поз на весь замах), и рубленность выглядела бы «визуалом в
        // 30 Гц». С дробной долей поза течёт непрерывно и синхронно с движением при любом TickRate.
        private float _frameAlpha;

        // Тело юнита за швом: один спрайт или полтора десятка частей — виду разницы не видно (см. IUnitBodyVisual).
        private Body.IUnitBodyVisual _body;
        private bool                 _bodyResolved;

        // --- Feel (реакция на удар, LitMotion) — только презентация, сим не трогает ---
        private Design.CombatFeelConfig _feel;          // параметры вспышки/сплющивания (из design-конфига)
        private Core.Audio.IAudioService _audio;        // голос вида: хруст разлёта на осколки
        private Color        _baseTint = Color.white;   // цвет-тинт тела (умножается на текстуру в шейдере)
        private Gradient     _vfxSpread;                // «свой» разброс цвета: подаёт презентер, читают осколки
        private Color        _activeFlashColor = Color.white; // цвет текущей вспышки (school flash или фолбэк)
        private float        _flashAmount;               // 0..1 — сила вспышки (параметр _FlashAmount шейдера)
        private float        _flashPeak = 1f;            // пик текущей вспышки: удар бьёт в полную, лечение мягче
        private Vector3      _baseSpriteScale = Vector3.one; // масштаб узла сплющивания до эффекта
        private Transform    _squashTarget;              // узел, который сплющиваем (выше Animator, чтобы не затирался)
        private float        _hitSquashWeight;           // 0..1 — вес hit-squash (твин)
        private float        _flipSquashWeight;          // 0..1 — вес facing-flip squash
        private float        _acquireSquashWeight;       // 0..1 — вес target-acquire twitch
        private float        _hitstopRemaining;          // unscaled-окно заморозки анимации участников удара
        private MotionHandle _flashHandle;
        private MotionHandle _squashHandle;
        private MotionHandle _nudgeHandle;
        private MotionHandle _flipHandle;
        private MotionHandle _acquireHandle;
        private MotionHandle _attackMotionHandle;        // anticipation / lunge
        private Vector2      _nudgeOffset;               // презентационный сдвиг поверх интерполяции
        private Vector2      _nudgePeak;                 // пик hit-nudge (направление × дистанция)
        private Vector2      _attackOffset;              // anticipation/lunge поверх интерполяции
        private Vector2      _attackPeak;
        private bool         _flipAnimActive;            // идёт разворот-сплющивание — ApplyFacing не дергает flipX
        private bool         _desiredFlipX;
        private bool         _wasMoving;                 // предыдущий кадр: юнит бежал (для contact-dust)
        private float        _contactDustCooldownLeft;
        private int          _lastTargetId = int.MinValue;
        private float        _deathAnticipateLeft;       // remaining unscaled death-anticipation
        private System.Action<UnitView> _onContactDust;  // презентер спавнит VfxContactDust; null = нет VFX
        private System.Action<UnitView> _onSwingStarted; // презентер спавнит дугу за клинком; null = нет VFX

        // --- Состояние анимации (рендер-сторона, не влияет на сим) ---
        // Своя фаза анимации атаки (НЕ путать с сим-AttackPhase на RuntimeUnit): охватывает ВЕСЬ цикл
        // атаки — замах до кадра контакта + хвост-возврат, растянутый на «окно» до следующего замаха.
        // За счёт этого у непрерывно атакующего клип атаки лупится бесшовно в темпе скорости атаки, а
        // не мигает Attack↔Run в паузе между ударами.
        private enum AttackAnimPhase { None, Windup, Channel, Recovery }

        private UnitAnimationState _state = UnitAnimationState.Idle;
        private AttackAnimPhase _attackPhase;
        private bool  _isDead;
        private float _deathRemaining;

        // Секвенс смерти: ждём конца hit-flash → death-клип → anticipation → разлёт на осколки.
        private enum DeathPhase { None, WaitFlash, Dying, Hologram, Anticipate, Shattering }
        private DeathPhase _deathPhase;

        private float _holoAmount;   // 0..1 — сила голограммы (параметр _Holo шейдера тела)
        private float _holoLeft;     // остаток стадии голограммы, сек

        private float _outlineAmount;      // 0..1 — контур каста (параметр _Outline шейдера тела)
        private float _outlineLeft;        // остаток окна контура, сек
        private float _outlineTotal;
        private Color _outlineColor = Color.white;  // главный цвет кастера
        private bool  _outlineCharge;      // это ПОДВОДКА к касту (растёт к концу), а не вспышка «состоялось»

        // Свечение части-источника на касте (реф SAO): заряд за cast-time → пик на выпуске → спад.
        private float _glowAmount;         // 0..1 — сила свечения (параметр _GlowAmount шейдера тела)
        private float _glowLeft;           // остаток текущей фазы, сек
        private float _glowChargeTotal;    // длительность заряда, сек (0 = сразу пик)
        private float _glowReleaseTotal;   // длительность спада с пика, сек
        private bool  _glowInCharge;       // идёт заряд (растём к пику) vs спад
        private Color _glowColor = Color.white;              // HDR-цвет: цвет юнита × bloom-множитель
        private PartMask _glowParts;       // какие ИМЕННО части светятся; пусто = не светимся

        private CanvasGroup _uiFadeGroup;  // контейнер world-UI: гаснет вместе с телом, а не щелчком
        private float _uiFadeLeft;
        private float _uiFadeTotal;

        private bool _freeRun;        // бой окончен → доигрываем анимации натурально, не скрабим по замершему симу
        private bool _freeRunSettled; // уже осели в Idle после доигрыша
        private bool  _holdHitFrame;  // финишер: держим кадр весь финальный slowmo
        private bool  _holdKeepsAttackFrame; // добивающему — именно кадр контакта; остальным — где застало
        private float _holdRemaining; // unscaled-остаток удержания

        // --- Слой действия: свинг поверх ног ---
        private int   _actionLayer     = -1;  // индекс слоя свинга; -1 = у юнита его нет (покадровый бестиарий)
        private int   _actionHipsLayer = -1;  // аддитивный таз, синхронизированный с ним; -1 = нет
        private float _actionWeight;          // текущий вес обоих 0..1
        private bool  _swingCharged;          // текущий свинг — с разбега (фиксируется на входе в цикл)

        // --- Гвардия (телеграф барьера): поза щита, поднятая ДО того, как барьер появится ---
        private int   _guardLayer = -1;  // индекс слоя щита; -1 = у юнита его нет
        private float _guardWeight;      // текущий вес слоя 0..1
        private bool  _guardActive;      // идёт окно гвардии (подводка + жизнь барьера)
        private float _guardElapsed;     // сколько окна прошло
        private float _guardTotal;       // всё окно: подводка + жизнь барьера
        private float _guardRise = 0.1f; // за сколько щит встаёт; дальше стоп-кадр до конца окна
        private float _guardRelease;     // 0..1 — насколько прошло опускание после конца окна
        private float _guardPose;        // 0..1 — куда в клипе гвардии поставлен скраб этим кадром

        // Фазы клипа гвардии — из его же маркеров: 0..Up подъём, Up..Down держание, Down..1 возврат.
        private bool  _hasGuardWindow;
        private float _guardUpN   = GuardUnmarkedUp;
        private float _guardDownN = GuardUnmarkedUp;

        private bool  _animActive;              // визуал с клипами подан → Animator рулит спрайтом
        private float _attackHitNormalized;  // 0..1 — доля клипа атаки до маркера контакта (Hit)

        // --- Взмах: окно между маркерами StrikeStart и StrikeEnd ------------------------------------
        // На нём живёт дуга за клинком, и с его начала берётся точка A формы удара. Клип без разметки
        // взмаха просто не даёт ни того, ни другого: остальной удар (вспышка, искры, цифра) работает.
        private Vector2 _strikeDirLocal;     // направление удара на кадре контакта, в координатах корня тела
        private bool  _hasStrikeDir;         // замер удался
        private bool  _hasStrikeWindow;      // клип атаки размечен обоими маркерами
        private float _strikeFrom;           // 0..1 — нормированное начало взмаха
        private float _strikeTo;             // 0..1 — нормированный конец взмаха
        private float _swingClipTime = -1f;  // 0..1 — где скраб поставил клип свинга в этом кадре; -1 = свинг не играет
        /// <summary>
        /// Номер текущего взмаха. Растёт на КАЖДОМ входе в атаку — там, где о нём сообщает сим
        /// (<see cref="OnAttackStarted"/>), и больше нигде.
        /// </summary>
        /// <remarks>
        /// Показ не выводит «начался новый взмах» из своего же состояния. Фаза для этого не годится: у
        /// кита, чей следующий замах стартует в тот же тик, где кончился прошлый удар, фаза замаха не
        /// прерывается ни на кадр. Скраб клипа не годится тоже — сравнивать его с прошлым кадром значит
        /// угадывать по порогу. Событие ленты знает ответ точно, и оно уже приходит.
        /// </remarks>
        private int _swingSerial;

        /// <summary>Взмах, с которого снята точка A и заказана дуга. <c>-1</c> = ещё ни одного.</summary>
        private int _originSwingSerial = -1;

        /// <summary>
        /// Тело юнита за швом. Резолвится лениво, а не в <c>Awake</c>, потому что силуэт для drag-призрака
        /// снимается ПРЯМО С АССЕТА префаба (юнита на поле ещё нет) — там ни один жизненный метод не звучал.
        /// </summary>
        private Body.IUnitBodyVisual Body
        {
            get
            {
                if (_bodyResolved) return _body;
                _bodyResolved = true;
                // Вторая реализация тела — из ОДНОГО спрайта, для покадровых юнитов — удалена 06.08.2026
                // вместе с самим покадровым путём: последний такой префаб ушёл из дерева 05.08, и ветка
                // стала недостижимой. Тело теперь всегда скелетное либо его нет вовсе.
                _body = _skeletalBody;
                return _body;
            }
        }

        /// <summary>
        /// Связать вид с юнитом ленты: <paramref name="state"/> — его состояние на показываемом тике,
        /// <paramref name="definition"/> — его определение (арт, палитра, флаги), которое за бой не меняется.
        /// </summary>
        public void Bind(in Combat.Tape.UnitSnapshot state, Data.Definitions.UnitData definition)
        {
            _snapshot       = state;
            _hasState       = true;
            _definition     = definition;
            _hasTargetState = false;
            _renderPosition = state.Position;
            transform.position = (Vector3)_renderPosition;

            // Вид могли переиспользовать после чьей-то смерти — возвращаем полосу из погашенного состояния.
            _uiFadeLeft = 0f;
            ClearBodyCuts();   // и раны прошлого жильца: они принадлежали его телу, а не этому виду
            if (_worldUi != null)
            {
                _worldUi.SetActive(true);
                CanvasGroup group = _worldUi.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1f;
            }

            // Спрайты/кости нарисованы лицом вправо: команда 0 (слева) так и смотрит, враги (справа) — влево.
            // Дальше разворот динамический (ApplyFacing по цели/движению), но стартовый — по стороне, иначе
            // стоящий без цели (напр. ассасин в инвизе) смотрит «от противника».
            if (Body != null)
            {
                // Вид переиспользуется после чьей-то смерти, а на разлёте осколков тело было спрятано —
                // возвращаем его, иначе второй жилец этого вида выходит на арену невидимым.
                Body.SetVisible(true);

                bool startFlip = state.Team != 0;
                _desiredFlipX = startFlip;
                ApplyFacingVisual(startFlip);

                // Сплющиваем узел ВЫШЕ Animator (корень тела), иначе клип затирает scale каждым кадром.
                _squashTarget = Body.Root != null ? Body.Root : transform;
                _baseSpriteScale = _squashTarget.localScale;

                // Праймим property block (flash=0) с первого кадра: спрайт с кастомным SRP-batcher-шейдером
                // включает per-instance путь именно выставленным MPB — иначе тинт/флип не подхватывались до
                // первого удара (наблюдалось как «юнит без своего цвета/прозрачности, пока не получит урон»).
                // _feel в Bind ещё может быть null — цвет вспышки тут не важен (сила 0).
                Body.Prime(_feel != null ? _feel.FlashColor : Color.white);
                _holoAmount = 0f;
            }

            if (_healthBar != null) _healthBar.Bind(in state);
            if (_manaBar != null)   _manaBar.Bind(in state);

            InitVisual(); // визуал/анимация — из самого префаба (см. InitVisual), без рантайм-подмены
        }

        /// <summary>Тинт тела юнита: один общий спрайт, разный цвет на персонажа (dev-харнесс, «пока один спрайт»).</summary>
        public void SetTint(Color color)
        {
            _baseTint = color;
            ApplyColor(); // итоговый цвет = база + вспышка + альфа инвиза (единый писатель цвета тела)
        }

        /// <summary>
        /// Подать диапазон разброса «своего» цвета: осколки павшего берут его концы. Вид цвет не
        /// РЕЗОЛВИТ — роль юнита превращает в цвет палитра проекта, и делает это презентер; иначе показ
        /// снова начал бы знать про токены и стал бы вторым входом за цветом.
        /// </summary>
        public void SetVfxSpread(Gradient spread) => _vfxSpread = spread;

        /// <summary>Подать design-конфиг сочности (длительности/сила/цвет вспышки и сплющивания). CombatPresenter — при спавне.</summary>
        public void ApplyFeelConfig(Design.CombatFeelConfig feel)
        {
            _feel = feel;

            // Полоса не должна знать чисел — она их получает, как и цвета (её единственный источник — конфиг).
            if (_healthBar != null && feel != null)
                _healthBar.SetLowHpPulse(feel.LowHpThreshold, feel.LowHpPulsePeriod, feel.LowHpPulseAmount);
        }

        /// <summary>
        /// Дать виду голос. Нужен ровно для одного момента — разлёта на осколки: он случается сильно позже
        /// сим-смерти (после вспышки и anticipation), и звук смерти его уже не покрывает.
        /// </summary>
        public void ApplyAudio(Core.Audio.IAudioService audio) => _audio = audio;

        /// <summary>
        /// Хук contact-dust: презентер спавнит <c>VfxContactDust</c> в <see cref="FeetPoint"/>.
        /// Вызывается на старте/стопе бега, если тумблер в feel-конфиге включён.
        /// </summary>
        public void SetContactDustHandler(System.Action<UnitView> handler) => _onContactDust = handler;

        /// <summary>
        /// Хук начала взмаха: презентер берёт из пула дугу за клинком и отдаёт ей этот вид как источник.
        /// Зовётся на кадре <c>StrikeStart</c> — том же, с которого снимается точка A.
        /// </summary>
        public void SetSwingStartHandler(System.Action<UnitView> handler) => _onSwingStarted = handler;

        /// <summary>Цвет HP-бара по принадлежности к смотрящему (из <c>CombatColorPalette</c>).</summary>
        public void SetHealthColor(Color color)
        {
            if (_healthBar != null) _healthBar.SetMainColor(color);
        }

        /// <summary>Цвет полоски щита (из <c>CombatColorPalette</c>, общий для всех).</summary>
        public void SetShieldColor(Color color)
        {
            if (_healthBar != null) _healthBar.SetShieldColor(color);
        }

        /// <summary>
        /// Инициализировать визуал из САМОГО префаба (вызывается из <see cref="Bind"/>): Animator уже несёт
        /// контроллер с клипами персонажа — рантайм-подмены больше нет. Из <see cref="_visual"/> (задан на
        /// префабе) берём только маркер контакта авто-атаки и темп бега для скраба анимации по симу. Нет
        /// клипов/контроллера → статичный спрайт (Animator выключается).
        /// </summary>
        private void InitVisual()
        {
            _state = UnitAnimationState.Idle;
            _attackPhase = AttackAnimPhase.None;

            // Данные юнита — только для маркера контакта/темпа бега (скраб по симу). Клипы играет контроллер.
            _visual = _definition != null ? _definition.Visual : null;

            // Анимация активна, если у Animator есть контроллер (клипы — в его стейтах). UnitVisual не обязателен.
            _animActive = _animator != null && _animator.runtimeAnimatorController != null;

            // Индексы слоёв сбрасываются ДО выхода: вид переиспользуется после чьей-то смерти, и индекс
            // прошлого жильца достался бы новому — вместе с попыткой писать вес в чужой Animator.
            _guardLayer   = -1;
            _guardActive  = false;
            _guardWeight  = 0f;
            _guardRelease = 0f;
            _guardPose    = 0f;

            _actionLayer     = -1;
            _actionHipsLayer = -1;
            _actionWeight    = 0f;
            _swingCharged    = false;

            // Вид переиспользуется пулом: взмахи прошлого жильца новому не принадлежат.
            _swingSerial       = 0;
            _originSwingSerial = -1;
            _swingClipTime     = -1f;

            if (!_animActive)
            {
                if (_animator != null) _animator.enabled = false;
                return;
            }

            _animator.fireEvents = false;
            _animator.enabled = true;

            ResolveAttackMarker();
            ResolveGuardMarkers();
            RequireSockets();
            MeasureStrikeDirection();

            // Слой-надстройка щита. Его нет у покадрового бестиария — тогда гвардия просто не играется:
            // это отсутствие контента, а не ошибка разводки (см. ResolvedHash).
            _guardLayer = _animator.GetLayerIndex(GuardLayerName);
            if (_guardLayer >= 0) _animator.SetLayerWeight(_guardLayer, 0f);

            // Слой-надстройка свинга. Его отсутствие тоже законно: у семнадцати покадровых юнитов клип
            // атаки — это весь кадр целиком, разложить его по маске нечем. Тогда свинг остаётся на базе
            // ровно как раньше (см. SwingIsOverlay), и «бей на бегу» у них по-прежнему выбирает одно из двух.
            _actionLayer = _animator.GetLayerIndex(ActionLayerName);
            if (_actionLayer >= 0)
            {
                _animator.SetLayerWeight(_actionLayer, 0f);
                _actionHipsLayer = _animator.GetLayerIndex(ActionHipsLayerName);
                if (_actionHipsLayer >= 0) _animator.SetLayerWeight(_actionHipsLayer, 0f);
            }

            _animator.Play(IdleHash, 0, 0f);
            _animator.speed = 1f;
        }

        /// <summary>
        /// Все четыре сокета обязаны быть разведены у КАЖДОГО вида — включая те, что киту как будто не
        /// нужны: точка выстрела у ближнего бойца тоже обязательна, пусть даже в нуле (требование Макса,
        /// 30.07). Молчать нельзя: незаданный сокет тихо падает на позицию юнита, и цифры урона, искры,
        /// зона щита и будущий доворот прицела начинают считаться от ног — а ищется это глазами по всему бою.
        /// </summary>
        /// <remarks>
        /// Это не фолбэк на внешний отказ, а дефект разводки внутри нашего же контента — по политике
        /// фолбэков такой случай обязан кричать, а не подставлять правдоподобное значение.
        /// </remarks>
        private void RequireSockets()
        {
            string missing = null;
            if (_feetPoint == null) missing = Join(missing, "Ноги (_feetPoint)");
            if (_headPoint == null) missing = Join(missing, "Голова (_headPoint)");
            if (_shotPoint == null) missing = Join(missing, "Выстрел (_shotPoint)");
            if (_hitPoint  == null) missing = Join(missing, "Попадание (_hitPoint)");

            if (missing != null)
                Debug.LogError($"[UnitView] {name}: не разведены точки — {missing}. Стандартные точки " +
                               "обязаны быть у ВСЕХ видов, даже если кит ими не пользуется (ближнему — " +
                               "точка выстрела хотя бы в нуле).", this);
        }

        private static string Join(string list, string item) => list == null ? item : list + ", " + item;

        /// <summary>
        /// Найти долю клипа атаки до кадра контакта. По ней скрабится замах, поэтому промах здесь двигает
        /// видимый удар мимо сим-тика урона. Источник **один** — <see cref="UnitVisual"/> из данных юнита,
        /// тот же, из которого симуляция берёт кадр контакта. Клип без маркера — не «настройка по
        /// умолчанию», а неразведённые данные: молчать нельзя, иначе удар уезжает в конец клипа и это
        /// ищется глазами по всему бою.
        /// </summary>
        /// <remarks>
        /// Прежде источников было два: у скелетных юнитов <c>UnitVisual</c> не заводили, и клип брался
        /// отдельным полем с префаба вида. Тогда позиция контакта жила в двух местах — в клипе для показа
        /// и долей <c>WindupShare</c> в данных для сима, — а второй владелец того же факта у нас считается
        /// дефектом. Скелетный риг получил свой <c>UnitVisual</c> (2026-07-31), и путь стал общим для всех.
        /// </remarks>
        private void ResolveAttackMarker()
        {
            AnimationClip attack = _visual != null ? _visual.AttackClip : null;

            if (attack == null)
            {
                Debug.LogError($"[UnitView] {name}: у юнита нет UnitVisual с клипом атаки — " +
                               "удар не привязан к тику урона.", this);
                _attackHitNormalized = 1f;
                return;
            }

            if (ClipMarkers.FirstHitTime(attack) < 0f)
            {
                Debug.LogError($"[UnitView] {name}: в клипе '{attack.name}' нет маркера контакта " +
                               $"(AnimationEvent '{ClipMarkers.HitFunction}') — удар не привязан к тику урона.", this);
                _attackHitNormalized = 1f;
                return;
            }

            _attackHitNormalized = ClipMarkers.HitNormalized(attack);

            // Разметка взмаха не ломает удар, но забирает у него ВЕСЬ язык ближнего боя: без окна нет ни
            // дуги за клинком, ни точки, откуда пришёл удар, — форма строится от ног бьющего. Раньше это
            // молчало как «осознанное состояние покадрового бестиария»; молчание и стоило того, что
            // отсутствие эффекта читалось с экрана как задумка (Макс, 03.08.2026).
            _hasStrikeWindow = ClipMarkers.StrikeWindowNormalized(attack, out _strikeFrom, out _strikeTo);
            if (!_hasStrikeWindow)
                VisualDefects.Report($"strike-window:{attack.GetInstanceID()}",
                    $"[UnitView] клип атаки '{attack.name}' не размечен взмахом (нужны AnimationEvent " +
                    $"'{ClipMarkers.StrikeStartFunction}' и '{ClipMarkers.StrikeEndFunction}') — у этого удара " +
                    "не будет дуги за клинком, а форма удара пойдёт от ног бьющего, а не от оружия.", attack);
        }

        /// <summary>
        /// Найти фазы клипа гвардии: где щит встал (<c>GuardUp</c>) и где пошёл вниз (<c>GuardDown</c>).
        /// Показ играет три куска этого клипа по трём разным часам — подъём за время подводки, держание
        /// пока живёт барьер, возврат за своё, — и знать границы обязан из клипа, а не из числа в коде.
        /// </summary>
        /// <remarks>
        /// Клип без разметки — не ошибка контента: у кита может не быть щита вовсе, а покадровый бестиарий
        /// и слоя-то не имеет. Поэтому здесь молчание, а не <see cref="VisualDefects"/>: гвардия
        /// деградирует на «клип как одна поза», и это видно ровно там, где она играется.
        /// </remarks>
        private void ResolveGuardMarkers()
        {
            AnimationClip guard = _visual != null ? _visual.GuardClip : null;

            _hasGuardWindow = ClipMarkers.GuardWindowNormalized(guard, out _guardUpN, out _guardDownN);
            if (_hasGuardWindow) return;

            _guardUpN   = GuardUnmarkedUp;
            _guardDownN = GuardUnmarkedUp;
        }

        // --- Взмах ------------------------------------------------------------------------------------

        /// <summary>
        /// Идёт ли сейчас взмах и насколько он прошёл (0..1) — окно между маркерами <c>StrikeStart</c> и
        /// <c>StrikeEnd</c>. На нём живёт дуга за клинком: до начала клинок только собирается, после конца
        /// возвращается, и рисовать там нечего.
        /// </summary>
        public bool TryGetSwingProgress(out float progress)
        {
            progress = 0f;
            if (!_hasStrikeWindow || _swingClipTime < 0f) return false;

            // ЗАМЕРШИЙ КАДР ВЗМАХА НЕ СОДЕРЖИТ. Финишер держит кадр контакта весь свой таймлайн (пауза +
            // смерть + разлёт + возврат — семь с половиной секунд по нынешнему конфигу), и скраб на нём
            // стоит: окно взмаха остаётся «текущим», хотя клинок не двигается. Дуга принадлежит ДВИЖЕНИЮ,
            // поэтому в удержанном кадре взмах считается кончившимся и след догорает своим fade-out.
            // Без этого он висел над добитым телом до самой страховки пула (ArcSafetyLifetime, 4 сек).
            //
            // Hitstop сюда НЕ попадает намеренно: он живёт своим полем, длится доли секунды, и дуга обязана
            // замирать вместе с кадром удара — залипание клинка в воздухе и есть его вес.
            if (_holdHitFrame) return false;

            // ПОТОК — один непрерывный взмах, а не череда отдельных. Клип канала крутится оборотами, и
            // окно взмаха внутри него кончается на каждом обороте; судить по нему значило бы гасить дугу
            // между ударами вращения, хотя клинок не останавливался ни на кадр. Границы здесь ставит фаза
            // сима, а не разметка клипа: пока идёт поток, взмах идёт.
            if (_hasState && _snapshot.Phase == AttackPhase.Channel)
            {
                progress = _swingClipTime;
                return true;
            }

            if (_swingClipTime < _strikeFrom || _swingClipTime > _strikeTo) return false;

            progress = Mathf.InverseLerp(_strikeFrom, _strikeTo, _swingClipTime);
            return true;
        }

        /// <summary>
        /// КУДА ДВИГАЛСЯ КОНЧИК ОРУЖИЯ на кадре контакта — направление, вдоль которого ложится форма.
        /// </summary>
        /// <param name="dir">Единичное направление движения кончика в мире.</param>
        /// <returns><c>false</c> — вести форму не от чего; причина уже названа в консоли.</returns>
        /// <remarks>
        /// Отвечает ЗАМЕР КЛИПА, снятый один раз при инициализации вида, а не поза в момент вызова.
        /// Причина в порядке кадра: событие урона приходит внутри <c>Update</c> презентера, у которого
        /// <c>[DefaultExecutionOrder(-100)]</c>, — то есть до <c>Update</c> самих видов и заведомо до
        /// того, как Animator применит позу. Спросив кости в этот момент, мы читаем ПРОШЛЫЙ кадр: на
        /// медленной атаке это 15–20° ошибки, на быстрой — все 60–100°, потому что взмах в 200° там
        /// укладывается в два-три кадра показа. Ровно от этого дуга за клинком читает геометрию в
        /// <c>LateUpdate</c>, а форме и такой возможности нет — она рождается по событию.
        /// <para>
        /// Замер хранится в координатах корня тела, поэтому поворот и зеркало приходят даром:
        /// <c>TransformDirection</c> учитывает знак масштаба, которым разворачивается юнит.
        /// </para>
        /// </remarks>
        public bool TryGetStrikeDirection(out Vector2 dir)
        {
            dir = default;

            if (!_hasStrikeDir)
            {
                VisualDefects.Report($"strike-dir:{DefectKey}",
                    $"[UnitView] {name}: направление удара не замерено по клипу — знака удара не будет. " +
                    "Причина названа выше при инициализации вида.", this);
                return false;
            }

            Transform root = Body?.Root;
            if (root == null) return false;

            // Масштабом корня приходит и разворот юнита, и сплющивание. Первое нужно, второе слегка
            // искажает угол — но squash живёт доли секунды и не превышает десятка процентов.
            Vector3 world = root.TransformDirection(new Vector3(_strikeDirLocal.x, _strikeDirLocal.y, 0f));
            Vector2 d = new Vector2(world.x, world.y);
            if (d.sqrMagnitude < 1e-8f) return false;

            dir = d.normalized;
            return true;
        }

        /// <summary>
        /// Замерить направление удара по клипу — один раз за жизнь вида, до первого боя.
        /// </summary>
        /// <remarks>
        /// Считается двумя сэмплами клипа вокруг кадра контакта: где кончик оружия был чуть раньше и
        /// чуть позже. Разница и есть движение. Всё в координатах корня тела, поэтому замер не зависит
        /// от того, куда юнит смотрел в момент замера.
        /// <para>
        /// <b>Почему не геометрия «перпендикуляр к рычагу плечо → кончик».</b> Так было полдня
        /// 06.08.2026 и оказалось приближением вдвойне: рука не совсем жёсткий рычаг (замер живого
        /// клипа даёт разброс длины 8% между началом взмаха и контактом), а главное — позу всё равно
        /// приходилось читать в момент события, то есть кадром позже. Замер клипа не зависит ни от
        /// порядка <c>Update</c>, ни от скорости атаки, ни от числа кадров показа на взмах.
        /// </para>
        /// </remarks>
        private void MeasureStrikeDirection()
        {
            _hasStrikeDir = false;

            AnimationClip attack = _visual != null ? _visual.AttackClip : null;
            if (attack == null || attack.length <= 0f) return;   // об отсутствии клипа уже крикнул резолв маркера

            var body = Body;
            if (body?.Parts == null || body.Root == null) return;
            if (!body.Parts.TryGetStrikeSource(HandSlot.None, out UnitPart source))
            {
                VisualDefects.Report($"strike-source:{DefectKey}",
                    $"[UnitView] {name}: тело не отдаёт, ЧЕМ бьют — нет ни предмета в хвате " +
                    "(UnitHeldItem на кости под 'Rotation Point (Grip)'), ни кисти. Направление удара " +
                    "замерить не по чему, знака удара не будет.", this);
                return;
            }

            if (_animator == null) return;

            // Полное имя намеренно: `Body` здесь ещё и свойство вида, и короткая форма разрешается в него.
            if (!Guildmaster.Presentation.Body.StrikeDirectionMeasure.TryMeasure(
                    attack, source, body.Root, _attackHitNormalized, out Vector2 local))
            {
                VisualDefects.Report($"strike-measure:{DefectKey}",
                    $"[UnitView] {name}: направление удара не замерилось по клипу '{attack.name}' — " +
                    "кончик оружия на кадре контакта стоит на месте либо у него пуст спрайт. Проверь " +
                    "маркер контакта и то, что оружие анимировано этим клипом. Знака удара не будет.", this);
                return;
            }

            _strikeDirLocal = local;
            _hasStrikeDir   = true;
        }

        /// <summary>
        /// Отследить кадр начала взмаха и объявить его начавшимся. Зовётся из скраба свинга — там, где
        /// известно, куда именно поставлен клип.
        /// </summary>
        /// <remarks>
        /// «Этот взмах уже обслужен» проверяется НОМЕРОМ взмаха (<see cref="_swingSerial"/>), а не флагом,
        /// который кто-то обязан вовремя сбросить. Флаг и был причиной того, что дуга появлялась один раз
        /// за бой: сбрасывать его поручили переходу фазы, а фаза у быстрых китов не прерывается.
        /// </remarks>
        private void TrackStrikeWindow(float clipTime)
        {
            _swingClipTime = clipTime;
            if (!_hasStrikeWindow || _originSwingSerial == _swingSerial || clipTime < _strikeFrom) return;

            OpenSwing();
        }

        /// <summary>
        /// Объявить взмах начавшимся — один раз за свинг. Отсюда уходит заказ дуги за клинком.
        /// </summary>
        /// <remarks>
        /// До 06.08.2026 здесь же снималась точка A — кончик оружия на кадре <c>StrikeStart</c>, — и
        /// форма удара строилась по хорде от неё к цели. Хорда врала о направлении (замер: ~20° против
        /// честных ~75° вниз) и задавала длину знака расстоянием до цели, поэтому от неё отказались:
        /// направление удара теперь выводится из позы в момент касания, см.
        /// <see cref="TryGetStrikeDirection"/>. Проверки тела и оружия остались — без них некому вести
        /// дугу, и молчать об этом нельзя.
        /// </remarks>
        private void OpenSwing()
        {
            if (!_hasStrikeWindow) return;

            // Бьющей частью считаем то, чем юнит машет: предмет в руке, а у безоружного — саму кисть.
            // Клип ЗАЯВИЛ взмах своей разметкой, поэтому нехватка тела здесь — не «честное отсутствие
            // контента», а расхождение разметки с телом: о нём говорим вслух.
            var body = Body;
            if (body?.Parts == null)
            {
                VisualDefects.Report($"strike-body:{DefectKey}",
                    $"[UnitView] {name}: клип атаки размечен взмахом, но тела с частями у вида нет — " +
                    "дуги за клинком не будет.", this);
                return;
            }
            if (!body.Parts.TryGetStrikeSource(HandSlot.None, out UnitPart source))
            {
                VisualDefects.Report($"strike-source:{DefectKey}",
                    $"[UnitView] {name}: взмах начался, но тело не отдаёт, ЧЕМ бьют — нет ни предмета в " +
                    "хвате (UnitHeldItem на кости под 'Rotation Point (Grip)'), ни кисти. Удар останется " +
                    "без дуги.", this);
                return;
            }
            if (!UnitPartGeometry.TryGetTip(source, out Vector3 _))
            {
                VisualDefects.Report($"strike-tip:{DefectKey}",
                    $"[UnitView] {name}: ударная часть '{source.Bone}' есть, но кончика у неё нет — " +
                    "у рендерера пуст спрайт. Удар останется без дуги.", this);
                return;
            }

            _originSwingSerial = _swingSerial;
            _onSwingStarted?.Invoke(this);
        }

        // --- Порезы: тело помнит бой ----------------------------------------------------------------

        private readonly Body.BodyCutLedger _cuts = new Body.BodyCutLedger();

        /// <summary>
        /// Записать порез от состоявшегося удара: место в теле, направление вдоль удара, длина по весу.
        /// </summary>
        /// <param name="world">Точка попадания в мире.</param>
        /// <param name="worldDir">Вектор удара — тот же, по которому построена форма.</param>
        /// <param name="hpDamageFrac">Доля максимального HP, снятая ударом.</param>
        /// <param name="hpDamage">Сколько HP снял удар — запас, по которому порез заживает.</param>
        public void AddBodyCut(Vector3 world, Vector2 worldDir, float hpDamageFrac, float hpDamage)
        {
            if (_feel == null || !_feel.EnableBodyCuts || hpDamage <= 0f) return;

            var body = Body;
            if (body == null) return;

            float length = _feel.EvaluateCutLength(hpDamageFrac);
            if (!body.TryBuildCut(world, worldDir, length, hpDamage, out Body.BodyCut cut)) return;

            _cuts.Add(in cut);
            body.ApplyCuts(_cuts.Cuts, _feel.CutColor, _feel.CutWidthUnits);
        }

        /// <summary>
        /// Залечить раны на <paramref name="amount"/> HP. Гаснут САМЫЕ СТАРЫЕ и пропорционально своему
        /// запасу — тело чинится в том порядке, в каком его ломали, и это читается как процесс.
        /// </summary>
        public void HealBodyCuts(float amount)
        {
            if (_feel == null || !_feel.EnableBodyCuts) return;
            if (!_cuts.Heal(amount)) return;

            var body = Body;
            if (body != null) body.ApplyCuts(_cuts.Cuts, _feel.CutColor, _feel.CutWidthUnits);
        }

        /// <summary>Стереть раны: вид переиспользуется под нового юнита, чужие порезы ему не положены.</summary>
        private void ClearBodyCuts()
        {
            if (!_cuts.HasCuts) return;
            _cuts.Clear();

            var body = Body;
            if (body != null && _feel != null) body.ApplyCuts(_cuts.Cuts, _feel.CutColor, _feel.CutWidthUnits);
        }

        /// <summary>
        /// Геометрия текущего взмаха для дуги: вокруг чего он идёт (плечо бьющей руки), где сейчас
        /// кончик оружия и насколько взмах прошёл.
        /// </summary>
        /// <returns><c>false</c> — взмаха сейчас нет либо тело не даёт ни плеча, ни ударной части.</returns>
        public bool TryGetSwingArc(out Vector3 pivot, out Vector3 tip, out float progress)
        {
            pivot = default;
            tip   = default;

            if (!TryGetSwingProgress(out progress)) return false;

            var body = Body;
            if (body?.Parts == null) return false;
            if (!body.Parts.TryGetStrikeSource(HandSlot.None, out UnitPart source)) return false;
            if (!UnitPartGeometry.TryGetTip(source, out tip)) return false;

            // Дуга идёт вокруг ПЛЕЧА, а не вокруг кисти: рука — жёсткий рычаг, и вращается вся плоскость
            // удара. Взяв центром кисть, мы получили бы короткий веер вокруг запястья, которого в
            // движении нет. Сторона — та же, что у бьющей руки: у бойца с двумя клинками левый взмах
            // обязан идти от левого плеча.
            BodySide side = source.Slot == HandSlot.Left ? BodySide.Left
                          : source.Slot == HandSlot.Right ? BodySide.Right
                          : source.Side;

            string shoulderBone = RigNaming.ShoulderBone(side);
            if (!body.Parts.TryGetBone(shoulderBone, side, out UnitPart shoulder)
                || shoulder.Renderer == null)
            {
                // Дуга уже заказана презентером — значит взмах состоялся, а вести её не вокруг чего.
                // Молча вернуть false здесь значило бы погасить эффект в первом же кадре и оставить
                // впечатление, что дуги у этого кита «не бывает».
                VisualDefects.Report($"swing-pivot:{DefectKey}",
                    $"[UnitView] {name}: дуга за клинком заказана, но плеча '{shoulderBone}' " +
                    $"({side}) в теле нет — вращать сектор не вокруг чего, дуги не будет.", this);
                return false;
            }

            pivot = shoulder.Renderer.transform.position;
            return true;
        }

        /// <summary>
        /// Положить состояние показываемого тика. Зовётся раз за кадр из <see cref="CombatPresenter"/>,
        /// до <see cref="UpdateInterpolation"/>: сначала «что показываем», потом «где внутри тика».
        /// </summary>
        /// <param name="state">Снимок этого юнита на тике показа.</param>
        /// <param name="target">Снимок его цели на том же тике — для разворота и вздрога при смене цели.</param>
        /// <param name="hasTarget">Есть ли цель в кадре (её могли уже убить или её id — <c>-1</c>).</param>
        public void SetState(in Combat.Tape.UnitSnapshot state, in Combat.Tape.UnitSnapshot target, bool hasTarget)
        {
            _snapshot       = state;
            _hasState       = true;
            _targetState    = target;
            _hasTargetState = hasTarget;
        }

        /// <summary>
        /// Обновить интерполированную позицию. Вызывается из <see cref="CombatPresenter.Update"/>.
        /// </summary>
        /// <param name="alpha">Степень интерполяции [0, 1] между PreviousPosition и Position
        /// показанного тика — долю даёт момент ПОКАЗА, а не боевой луч.</param>
        public void UpdateInterpolation(float alpha)
        {
            if (!_hasState) return;

            // Доля нужна не только позиции: по ней же скрабится поза атаки (см. DriveAnimation). Порядок
            // «презентер раньше видов» держит [DefaultExecutionOrder] на CombatPresenter — иначе поза
            // отставала бы от позиции на кадр.
            _frameAlpha = alpha;

            _renderPosition = Vector2.Lerp(_snapshot.PreviousPosition, _snapshot.Position, alpha);
            transform.position = new Vector3(
                _renderPosition.x + _nudgeOffset.x + _attackOffset.x,
                _renderPosition.y + _nudgeOffset.y + _attackOffset.y,
                0f);

            // Y-sort: кто ниже по Y (ближе к зрителю) — рисуется поверх. Явный ордер стабильнее, чем
            // transparency-sort по позиции: у перекрытых спрайтов с ОДИНАКОВЫМ ордером иначе дрожит порядок.
            // У скелетного юнита частей полтора десятка, и писать ордер в одну из них — значит вырывать её
            // из собственного тела: торс уезжает по своей шкале, руки и ноги остаются на локальной и лезут
            // поверх чужих юнитов. Поэтому ордер получает ГРУППА, а внутренний порядок частей её переживает.
            int ySort = -Mathf.RoundToInt(_renderPosition.y * YSortPrecision);
            Body?.SetSortingOrder(ySort);

            if (_healthBar != null)
                _healthBar.UpdateBar(_snapshot.CurrentHP, _snapshot.MaxHP, _snapshot.CurrentShield);

            if (_manaBar != null)
                _manaBar.UpdateBar(_snapshot.CurrentResource, _snapshot.MaxResource);
        }

        // --- Attach points (сокеты) ----------------------------------------------------------------
        // Размер вида задаётся СТАТИКОЙ (масштаб узла тела или PPU арта), не рантаймом. Сокеты — пустые GO,
        // презентация читает их мировые позиции для спавна снаряда/цифр/вспышек. Фолбэк — позиция юнита.

        /// <summary>Мировая точка ног (касание земли). Фолбэк — позиция юнита.</summary>
        public Vector3 FeetPoint => _feetPoint != null ? _feetPoint.position : transform.position;

        /// <summary>Мировая точка головы (макушка) — якорь баров/статус-текста. Фолбэк — позиция юнита.</summary>
        public Vector3 HeadPoint => _headPoint != null ? _headPoint.position : transform.position;

        /// <summary>Мировая точка выстрела (откуда визуально стартует снаряд/каст), зеркалится по фейсингу. Фолбэк — позиция юнита.</summary>
        public Vector3 ShotPoint => ResolveSocketFacing(_shotPoint);

        /// <summary>Мировая точка попадания (куда прилетают снаряды/цифры урона), зеркалится по фейсингу. Фолбэк — позиция юнита.</summary>
        public Vector3 HitPoint => ResolveSocketFacing(_hitPoint);

        /// <summary>Слой сортировки тела — для размещения VFX относительно юнита.</summary>
        /// <summary>
        /// Id юнита, которого показывает этот вид, или <c>-1</c>, пока состояние не подано. Нужен хукам,
        /// которые вид дёргает сам (пыль, дуга за клинком): презентер получает вид, а палитра и паспорт
        /// у него разложены по id.
        /// </summary>
        public int UnitId => _hasState ? _snapshot.Id : -1;

        public int BodySortingLayerId => Body != null ? Body.SortingLayerId : 0;

        /// <summary>
        /// Текущий ордер сортировки тела (Y-sort) — VFX ставим со смещением от него. У составного тела это
        /// ордер ГРУППЫ: внутри неё ордер отдельной части локальный и на арене ничего не значит.
        /// </summary>
        public int BodySortingOrder => Body != null ? Body.SortingOrder : 0;

        /// <summary>
        /// Попадает ли мировая точка в ЭТАЛОННЫЙ габарит фигуры — ту самую зелёную рамку гизмо
        /// (<c>_recommendedWidth</c> × <c>_recommendedHeight</c> от ног). Это «тело юнита» в понимании дизайна:
        /// не зависит от кадра анимации и от прозрачных полей спрайта, поэтому зона хватания в расстановке
        /// одинакова у всех и совпадает с тем, что игрок видит фигурой.
        /// </summary>
        /// <param name="padding">Мировой запас во все стороны (0 = ровно по рамке).</param>
        public bool FigureContainsWorldPoint(Vector2 world, float padding = 0f)
        {
            Vector3 feet = FeetPoint;
            float halfW = Mathf.Max(0.01f, _recommendedWidth) * 0.5f + padding;
            float height = Mathf.Max(0.01f, _recommendedHeight) + padding;
            return world.x >= feet.x - halfW && world.x <= feet.x + halfW
                && world.y >= feet.y - padding && world.y <= feet.y + height;
        }

        /// <summary>
        /// Силуэт тела для drag-призрака расстановки (QA #9): части с их позами относительно точки ног
        /// <paramref name="feet"/>. Работает и на живом виде, и прямо на ассете префаба — см. <see cref="Body"/>.
        /// </summary>
        public UnitSilhouette CaptureSilhouette(Vector2 feet)
            => Body != null ? Body.CaptureSilhouette(feet) : UnitSilhouette.None;

        // Сокет с учётом разворота: flipX/facing-root НЕ зеркалят Points (они сиблинги визуала).
        // Для смотрящего влево отражаем локальную X сокета вручную.
        private Vector3 ResolveSocketFacing(Transform socket)
        {
            if (socket == null) return transform.position;
            if (!IsFacingFlipped()) return socket.position;
            Vector3 local = transform.InverseTransformPoint(socket.position);
            local.x = -local.x;
            return transform.TransformPoint(local);
        }

        private bool IsFacingFlipped() => Body != null && Body.IsFlippedX;

        /// <summary>Применить разворот — как именно, решает тело (flipX спрайта или знак масштаба корня).</summary>
        private void ApplyFacingVisual(bool flipX)
        {
            if (Body == null) return;
            Body.SetFlipX(flipX);

            // Составное тело разворачивается ЗНАКОМ МАСШТАБА того же узла, который сплющивает удар. Знак
            // держим в базовом масштабе, иначе следующая же композиция масштаба вернула бы юнита лицом назад.
            if (_squashTarget != null && _squashTarget == Body.Root)
                _baseSpriteScale.x = Mathf.Abs(_baseSpriteScale.x) * (flipX ? -1f : 1f);
        }

        private void Update()
        {
            ApplyColor(); // вспышка + альфа инвиза видны даже в hitstop/паузе (единый писатель цвета тела)
            // Гвардия тикает ДО всех ранних выходов: смерть, финишер и конец боя обходят обычный ход
            // анимации, и рука со щитом осталась бы поднятой навсегда — щит держал бы тот, кому уже нечем.
            TickGuardPose(Time.deltaTime);
            TickCastOutline();
            TickCastGlow();
            TickWorldUiFade();
            TickContactDustCooldown();
            TickIdleBreath();

            // Локальный hitstop: удар «весит» — участники замирают на unscaled-окно, толпа вокруг не стынет.
            // Морозим только анимацию: позиция продолжает интерполироваться (≈50 мс дрейфа незаметны, зато
            // нет снапа при выходе). unscaled — работает и во время global slowmo (2b), и на паузе.
            if (_hitstopRemaining > 0f)
            {
                _hitstopRemaining -= Time.unscaledDeltaTime;
                if (_animActive) _animator.speed = 0f;
                return;
            }

            // Смерть перехватывает обычную анимацию: ждём hit-flash → death-клип → разлёт (см. DriveDeath).
            if (_deathPhase != DeathPhase.None) { DriveDeath(); return; }

            ApplyFacing(); // разворот по цели — до guard'а анимации (нужен и статичным спрайтам)
            TickTargetAcquireTell();
            TickLocomotionDust();

            if (!_animActive) return;

            // Финишер: держим кадр контакта весь финальный slowmo (перекрывает free-run).
            if (_holdHitFrame)
            {
                DriveHoldHitFrame();
                return;
            }

            // Бой окончен: sim не тикает, скраб замер бы — доигрываем клип естественно (см. DriveFreeRun).
            if (_freeRun)
            {
                DriveFreeRun();
                return;
            }

            float dt = Time.deltaTime;
            UpdateAttackPhase(dt);

            bool isMoving = _hasState &&
                            (_snapshot.Position - _snapshot.PreviousPosition).sqrMagnitude > MoveEpsilonSq;

            // Attack показываем поверх Run, ПОКА идёт цикл атаки. Ключ — что «в атаке» решает СИМ-фаза,
            // а не сырое смещение: во время замаха/хвоста мили зарутован (MovementSystem), и толчок
            // сепарации НЕ должен рвать свинг в Run (иначе виден бег вместо замаха и пропадают замах/хвост
            // у преследователя — остаётся будто только удар). isMoving разделяет стойку и погоню лишь в
            // ПАУЗЕ между ударами (сим Idle, рендер ещё тянет хвост-цикл).
            bool attackWhileMoving = _definition != null && _definition.CanAttackWhileMoving;
            bool simInSwing = _hasState && _snapshot.IsSwinging;
            bool attackPlaying = UnitAnimationSelector.AttackClipPlaying(
                _attackPhase != AttackAnimPhase.None, simInSwing, attackWhileMoving, isMoving);

            // Разбег и удар с разбега — признаки СИМУЛЯЦИИ из снимка, а не догадка показа по скорости:
            // на дистанции одного тика прибавка в 30% неотличима от шума расталкивания.
            //
            // Со слоем действия база про атаку не знает ВООБЩЕ: свинг уехал наверх, и базе остаётся ровно
            // то, чем юнит занят ногами. Именно поэтому удар с разбега больше не отменяет разбег — ноги
            // продолжают тот же Sprint, пока руки бьют. Без слоя оба состояния по-прежнему делят базу, и
            // атака вытесняет локомоцию, как вытесняла.
            UnitAnimationState next = UnitAnimationSelector.Select(
                _isDead, attackPlaying && !SwingIsOverlay, isMoving,
                canAct: !_hasState || _snapshot.CanAct,
                isSprinting: _hasState && _snapshot.IsSprinting,
                chargedAttack: _hasState && _snapshot.ChargedSwing && !SwingIsOverlay,
                hasTarget: _hasState && _snapshot.TargetId >= 0);
            if (next != _state)
            {
                // Шаг и разбег — один цикл в разных амплитудах, между ними ПЕРЕХОД, а не подмена: клип,
                // начатый с нуля, ставит ногу заново посреди шага. Фазу переносим, поэтому нога продолжает
                // тот же шаг — клипы написаны одним скелетом поз и совпадают по долям цикла.
                int hash = ResolvedHash(next);
                if (IsLocomotion(_state) && IsLocomotion(next))
                    _animator.CrossFade(hash, LocomotionBlend, 0, LocomotionPhase());
                // Вход в свинг НА БАЗЕ (покадровый бестиарий) — снапом: DriveSwingScrub тем же кадром зовёт
                // Play в нужную позицию и оборвал бы кроссфейд на полдороге. У скелетных свинг живёт слоем,
                // база сюда с Attack не приходит вовсе, поэтому проверка бьёт ровно в покадровый случай.
                else if (ScrubbedOnBase(next))
                    _animator.Play(hash, 0, 0f);
                // Всё прочее — позные стейты (Idle / CombatIdle / Stun) и переходы в/из локомоции. Раньше
                // они щёлкали за кадр, и стан — самая свёрнутая поза — бросался в глаза сильнее всех.
                // Фиксированные секунды, а не нормализованное окно: стейты разной длины (Stun 1.2с против
                // Idle 2.6с), и одна доля дала бы разное реальное время перехода.
                else if (_poseBlendSeconds > 0f)
                    _animator.CrossFadeInFixedTime(hash, _poseBlendSeconds, 0, 0f);
                else
                    _animator.Play(hash, 0, 0f);

                _state = next;
                _animator.speed = 1f;
            }

            DriveAnimation(dt);
        }

        // Доля цикла разбега, за которую он вытесняет шаг. Считается от длины ЦЕЛЕВОГО клипа, поэтому
        // одинаково честна в обе стороны: примерно четверть цикла — быстрее рвётся, дольше плывёт.
        private const float LocomotionBlend = 0.35f;

        // Шаг и разбег — одно движение с разной амплитудой; всё остальное меняется подменой позы.
        private static bool IsLocomotion(UnitAnimationState state)
            => state == UnitAnimationState.Run || state == UnitAnimationState.Sprint;

        // Свинг, который DriveSwingScrub каждый кадр листает через Play в позицию по сим-тику. Кроссфейд в
        // такой стейт оборвался бы сразу — поэтому вход в него идёт снапом. У скелетных со слоем действия
        // Attack на базу не приходит (свинг уехал наверх), так что здесь остаётся ровно покадровый бестиарий.
        private bool ScrubbedOnBase(UnitAnimationState state)
            => !SwingIsOverlay &&
               (state == UnitAnimationState.Attack || state == UnitAnimationState.AttackCharge);

        // Где нога внутри текущего цикла [0..1). Клипы локомоции написаны в одной фазе, поэтому эту долю
        // можно перенести в другой из них как есть.
        private float LocomotionPhase()
        {
            float t = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            return t - Mathf.Floor(t);
        }

        private static int HashFor(UnitAnimationState state) => state switch
        {
            UnitAnimationState.Run          => RunHash,
            UnitAnimationState.Attack       => AttackHash,
            UnitAnimationState.Death        => DeathHash,
            UnitAnimationState.Sprint       => SprintHash,
            UnitAnimationState.AttackCharge => AttackChargeHash,
            UnitAnimationState.Stun         => StunHash,
            UnitAnimationState.CombatIdle   => CombatIdleHash,
            _                               => IdleHash,
        };

        /// <summary>
        /// Хэш стейта с явным фолбэком на общий, если своего у юнита НЕТ. Состояния бестиария (разбег, удар
        /// с разбега, стан) объявляет симуляция для всех, а стейты под них есть только у скелетных: у
        /// покадровых семнадцати клипа разбега не существует, и просить его — не ошибка данных, а честное
        /// отсутствие контента.
        /// <para>Фолбэк ЯВНЫЙ, а не молчание Animator: <c>Play</c> по несуществующему стейту действительно
        /// тихий no-op, но <c>CrossFade</c> на такой полагаться не даёт, и разбег покадрового юнита завис бы
        /// в том кадре, где его застало.</para>
        /// </summary>
        private int ResolvedHash(UnitAnimationState state)
        {
            int hash = HashFor(state);
            if (_animator.HasState(0, hash)) return hash;

            return state switch
            {
                UnitAnimationState.Sprint       => RunHash,      // разбег без своего клипа — обычный бег
                UnitAnimationState.AttackCharge => AttackHash,   // удар с разбега — обычный свинг
                _                               => IdleHash,     // стан, боевая стойка и прочее — покой
            };
        }

        /// <summary>Свинг живёт отдельным слоем поверх ног — есть ли он у этого юнита.</summary>
        private bool SwingIsOverlay => _actionLayer >= 0;

        /// <summary>Слой, на котором скрабится свинг: свой, если есть, иначе база (покадровый бестиарий).</summary>
        private int SwingLayer => _actionLayer >= 0 ? _actionLayer : 0;

        // Каким стейтом СКРАБИТСЯ свинг. Удар с разбега живёт своим клипом, но тайминг у него общий с
        // обычным: замах ведёт к тому же кадру контакта, просто короче. Держать для него отдельный путь
        // скраба значило бы завести второй владелец одной формулы.
        private int SwingHash()
        {
            // На слое действия базового состояния для свинга больше нет — что играть, помнит признак,
            // снятый на входе в цикл. Фолбэк тут свой: на слое рук нет стейта покоя, и общий ResolvedHash
            // увёл бы отсутствующий клип в Idle, которого на этом слое не существует.
            if (SwingIsOverlay)
            {
                int overlay = _swingCharged ? AttackChargeHash : AttackHash;
                return _animator.HasState(_actionLayer, overlay) ? overlay : AttackHash;
            }

            return ResolvedHash(_state == UnitAnimationState.AttackCharge
                ? UnitAnimationState.AttackCharge
                : UnitAnimationState.Attack);
        }

        // Управление фазой анимации атаки от состояния сима (вики «14»): замах → кадр контакта → хвост.
        // Замах скрабится по windup-тикам (маркер на тик урона); хвост — по остатку интервала до
        // следующего замаха, поэтому у непрерывно бьющего клип лупится бесшовно в темпе скорости атаки.
        private void UpdateAttackPhase(float dt)
        {
            if (_isDead) { _attackPhase = AttackAnimPhase.None; return; }
            if (!_hasState) return;

            // Идёт сим-замах → фаза замаха (покрывает и реконструкцию после load/resync без события старта).
            if (_snapshot.IsWindingUp)
            {
                // Признак разбега снимаем на ВХОДЕ и держим до конца цикла: он принадлежит одному свингу, а
                // не мгновению. Перечитывать его каждый кадр значило бы дать клипу право смениться посреди
                // замаха — удар с разбега превратился бы в обычный на полпути к контакту.
                // Точка A здесь НЕ сбрасывается: «новый взмах» объявляет событие сима, а не переход фазы.
                // У кита, чей следующий замах начинается в тот же тик, где кончился прошлый удар, этого
                // перехода не происходит вовсе — см. _swingSerial.
                if (_attackPhase != AttackAnimPhase.Windup)
                    _swingCharged = _snapshot.ChargedSwing;
                _attackPhase = AttackAnimPhase.Windup;
                return;
            }

            // Поток идёт — показ следует за симом напрямую: длина канала переменная (цель может умереть,
            // выйти из радиуса), и выводить её конец по счётчику показ не вправе.
            if (_snapshot.Phase == AttackPhase.Channel) { _attackPhase = AttackAnimPhase.Channel; return; }

            switch (_attackPhase)
            {
                case AttackAnimPhase.Windup:
                case AttackAnimPhase.Channel:
                    // Замах (или поток) кончился — дальше хвост. Его длину знает сим (RecoveryTicks): она
                    // равна доигрышу клипа, а не остатку интервала. Канал приходит сюда тем же путём:
                    // сворачивание у него общее с обычным ударом, ради чего он и сделан фазой, а не ритмом.
                    _attackPhase = AttackAnimPhase.Recovery;
                    break;

                case AttackAnimPhase.Recovery:
                    // Доигрыш кончается вместе с сим-фазой, а не с кулдауном. Дальше юнит ЖДЁТ своего окна,
                    // и показывать это должно ожидание, а не бесконечно длинный возврат меча. У быстрого
                    // кита разницы нет: там доигрыш и занимает весь интервал, поэтому серия остаётся
                    // непрерывной сама собой.
                    if (_snapshot.Phase != AttackPhase.Recovery)
                    {
                        _attackPhase   = AttackAnimPhase.None;
                        _swingClipTime = -1f;   // свинг не играет — взмаха на экране нет, и дуге не за чем идти
                    }
                    break;
            }
        }

        // Скорость/позиция клипа под текущее состояние: Attack скрабится по windup (маркер на тик удара),
        // Run «прибит к земле», остальное — натуральный темп клипа.
        private void DriveAnimation(float dt)
        {
            switch (_state)
            {
                case UnitAnimationState.Attack:
                case UnitAnimationState.AttackCharge:
                    // Базой атака бывает только у покадровых: со слоем действия сюда состояние не приходит.
                    DriveSwingScrub();
                    break;

                case UnitAnimationState.Run:
                case UnitAnimationState.Sprint:
                    // Клип локомоции листается по пройденной дистанции: во сколько раз юнит быстрее «родной»
                    // скорости клипа, во столько же крутим клип. Так ноги не скользят ни у покадрового
                    // юнита (кадры), ни у скелетного (непрерывные кривые) — единица измерения одна.
                    float speed = _hasState
                        ? (_snapshot.Position - _snapshot.PreviousPosition).magnitude / SimConstants.TickDelta
                        : 0f;
                    _animator.speed = speed / Mathf.Max(0.01f, LocomotionUnitsPerSecond());
                    break;

                default:
                    _animator.speed = 1f;
                    break;
            }

            // Надстройка идёт ПОСЛЕ базы: темп ходом владеет она (ноги привязаны к земле), а свинг своего
            // хода не имеет вовсе — он каждый кадр ставится в позицию скрабом.
            if (SwingIsOverlay) DriveActionLayer(dt);
        }

        /// <summary>
        /// Скраб позы свинга по сим-тикам — общий для обоих домов (слой действия и базовый слой покадровых).
        /// Формула одна намеренно: маркер контакта садится на тик урона, и второй её экземпляр разъехался бы
        /// с первым молча — удар уехал бы мимо цифр только у половины бестиария.
        /// </summary>
        private void DriveSwingScrub()
        {
            int layer = SwingLayer;
            bool ownsSpeed = layer == 0;   // на базе скраб ведёт ход целиком; на слое ход принадлежит ногам

            // Канал: удар не один, а поток, и кадр контакта обязан прийтись на КАЖДЫЙ его тик (см.
            // ChannelClipTime — сама проекция чистая и живёт рядом с остальными).
            if (_hasState && _snapshot.Phase == AttackPhase.Channel && _snapshot.AttackChannelTickPeriod > 0)
            {
                float cycle = UnitAnimationSelector.ChannelClipTime(
                    _attackHitNormalized,
                    _snapshot.AttackChannelTickRemaining,
                    _snapshot.AttackChannelTickPeriod,
                    _frameAlpha);
                if (ownsSpeed) _animator.speed = 0f;
                _animator.Play(SwingHash(), layer, cycle);
                TrackStrikeWindow(cycle);
                return;
            }

            if (_attackPhase == AttackAnimPhase.Windup && _hasState && _snapshot.WindupTicks > 0)
            {
                // Замах: скрабим [0..маркер] по прогрессу windup — контакт (маркер) приходится
                // ровно на конец замаха = сим-тик урона.
                float progress = TickScrubProgress(_snapshot.WindupRemaining, _snapshot.WindupTicks);
                if (ownsSpeed) _animator.speed = 0f;
                float clipTime = progress * _attackHitNormalized;
                _animator.Play(SwingHash(), layer, clipTime);
                TrackStrikeWindow(clipTime);
            }
            else if (_attackPhase == AttackAnimPhase.Recovery && _hasState)
            {
                // Хвост: скрабим [маркер..1] по прогрессу СВОЕГО доигрыша, а не по окну до
                // следующего замаха. Растягивание отменено решением Макса (30.07): быстрые киты
                // бьют непрерывной серией сами собой (у них доигрыш и занимает весь интервал), а
                // медленные обязаны отыграть удар за своё время и ВСТАТЬ — пауза и есть то, что
                // делает «редкий тяжёлый удар» видимым. Пока хвост тянулся по кулдауну, Защитник
                // 0.83 сек бесконечно медленно опускал меч, и паузы на экране не существовало.
                float tail = TickScrubProgress(_snapshot.RecoveryRemaining, _snapshot.RecoveryTicks);
                float clipT = _attackHitNormalized + tail * (1f - _attackHitNormalized);
                if (ownsSpeed) _animator.speed = 0f;
                _animator.Play(SwingHash(), layer, clipT);
                TrackStrikeWindow(clipT);
            }
            else if (ownsSpeed)
            {
                // Мгновенный удар без данных тайминга — доигрываем натуральным ходом.
                _animator.speed = 1f;
            }
        }

        /// <summary>
        /// Вес слоя действия за кадр: пока идёт цикл атаки — рука принадлежит удару, после — плавно
        /// возвращается тому, что играет база. Аддитивный таз ходит тем же весом, иначе дельта удара
        /// осталась бы на теле дольше самого удара.
        /// </summary>
        private void DriveActionLayer(float dt)
        {
            if (_attackPhase != AttackAnimPhase.None)
            {
                _actionWeight = 1f;   // вход мгновенный: замах обязан начаться там, где его начал сим
                DriveSwingScrub();
            }
            else
            {
                if (_actionWeight <= 0f) return;
                _actionWeight = Mathf.Max(0f, _actionWeight - dt / ActionDropSeconds);
            }

            ApplyActionWeight();
        }

        private void ApplyActionWeight()
        {
            _animator.SetLayerWeight(_actionLayer, _actionWeight);
            if (_actionHipsLayer >= 0) _animator.SetLayerWeight(_actionHipsLayer, _actionWeight);
        }

        /// <summary>
        /// «Родная» скорость того, что сейчас на ногах: сколько мировых единиц земли проходит клип за
        /// секунду. У шага и разбега она РАЗНАЯ — свой шаг, своя длина цикла, — и общее число листало
        /// разбег вдвое быстрее земли. Пока идёт переход, число смешивается той же долей разгона, что
        /// смешивает и сами клипы, поэтому ноги не скользят и в середине бленда.
        /// </summary>
        private float LocomotionUnitsPerSecond()
        {
            if (_sprintUnitsPerSecond <= 0f) return _runUnitsPerSecond;   // клипа разбега у юнита нет
            float ramp = _hasState ? Mathf.Clamp01(_snapshot.SprintRamp) : 0f;
            return Mathf.Lerp(_runUnitsPerSecond, _sprintUnitsPerSecond, ramp);
        }

        // Прогресс скраба по дробному тику: счётчики снимка целые, но показываемый момент лежит ВНУТРИ
        // тика — долю даёт _frameAlpha, та же, что и у позиции.
        private float TickScrubProgress(int ticksLeft, int totalTicks)
            => UnitAnimationSelector.ScrubProgress(ticksLeft, totalTicks, _frameAlpha);

        /// <summary>
        /// Гвардия: поднять щит ЗАРАНЕЕ — до того, как на юните появится барьер. Зовёт режиссура телеграфов,
        /// прочитав в ленте будущее наложение эффекта-щита; поза едет вверх за <paramref name="leadSeconds"/>
        /// и держится, пока барьер живёт (<paramref name="holdSeconds"/>).
        /// <para>Подводка ведёт к появлению БАРЬЕРА, а не к чужому удару (уточнение Макса 29.07): щит
        /// поднимает сам носитель, и читаться должно именно его действие. То, что барьер у «Оплота»
        /// встаёт в тик удара, — совпадение механики, а не смысл жеста.</para>
        /// <para>Время — ИГРОВОЕ, как у ленты: подводка обязана приехать одновременно с событием, к которому
        /// ведёт, а показ идёт по игровому времени и в slowmo растягивается вместе с ним.</para>
        /// </summary>
        public void RaiseGuard(float leadSeconds, float holdSeconds)
        {
            if (!_animActive || _guardLayer < 0) return;
            if (_feel != null && !_feel.EnableGuardPose) return;

            float lead = Mathf.Max(0f, leadSeconds);
            _guardTotal   = Mathf.Max(0.05f, lead + Mathf.Max(0f, holdSeconds));
            // Подъём кончается ЗАРАНЕЕ: остаток подводки щит стоит стоп-кадром. Если подводка короче
            // паузы, поза встаёт мгновенно — лучше резкий щит вовремя, чем плавный, но опоздавший.
            _guardRise    = Mathf.Max(0.02f, lead - GuardSettleSeconds);
            _guardRelease = 0f;
            _guardActive  = true;

            // Барьер повесили, пока щит ещё опускался с прошлого — подъём продолжается ОТ ТЕКУЩЕЙ ПОЗЫ,
            // а не с нуля. Обнуление здесь роняло руку в стойку и тут же поднимало её заново: на экране
            // это читается как сбой анимации, хотя происходит ровно то, чего просили.
            float held = _guardWeight > 0f && _guardUpN > 1e-4f
                ? Mathf.Clamp01(_guardPose / _guardUpN)
                : 0f;
            _guardElapsed = _guardRise * held;
        }

        // Поза щита за кадр. Клип гвардии скрабится СВОИМ окном, а не проигрывается: глобальный
        // animator.speed принадлежит свингу и в замахе равен нулю — щит, поднимающийся «сам», в этот момент
        // просто застыл бы. Вес слоя даёт мягкий вход и уход, скраб — саму позу.
        private void TickGuardPose(float dt)
        {
            if (_guardLayer < 0) return;

            // Кадр заморожен целиком — щит замирает вместе с ним: hitstop и финишер держат МОМЕНТ, и
            // рука, продолжающая двигаться в застывшем кадре, читается как чужая.
            if (_hitstopRemaining > 0f || _holdHitFrame) return;

            // Бой окончен: подводить больше не к чему, рука опускается (спад ниже сделает это плавно).
            if (_freeRun) _guardActive = false;

            if (!_guardActive)
            {
                if (_guardWeight <= 0f) return;

                // Возврат: рука ОПУСКАЕТСЯ хвостом клипа (после GuardDown), а не растворяется в базе. Вес
                // гаснет тем же ходом — под слоем может идти бег, и щит, доживший на полном весе до
                // последнего кадра, вернул бы руку в стойку клипа поверх бегущего тела.
                _guardRelease = Mathf.Min(1f, _guardRelease + dt / GuardDropSeconds);
                _guardPose    = Mathf.Lerp(_guardDownN, 1f, _guardRelease);
                _animator.Play(GuardHash, _guardLayer, _guardPose);

                _guardWeight = Mathf.Max(0f, _guardWeight - dt / GuardDropSeconds);
                _animator.SetLayerWeight(_guardLayer, _guardWeight);
                return;
            }

            _guardElapsed += dt;
            _guardRelease = 0f;   // окно поднялось заново — прошлое опускание больше не считается

            // Две фазы, а не одна: щит ВСТАЁТ за _guardRise, а потом ДЕРЖИТСЯ — и держится он до конца окна,
            // то есть и последние 0.15 с подводки, и всю жизнь барьера. Линейный скраб по всему окну
            // приводил позу в финал ровно к событию, и жест читался как реакция, а не как предупреждение.
            // Куда именно скрабить подъём, говорит МАРКЕР клипа: доля, стоявшая здесь числом, пережила
            // ровно одну правку клипа и оставила щит поднятым на 60% вместо 84% (замер RigSweep 04.08).
            float rise = Mathf.Clamp01(_guardElapsed / _guardRise);
            _guardPose = rise * _guardUpN;
            _animator.Play(GuardHash, _guardLayer, _guardPose);

            _guardWeight = Mathf.Min(1f, _guardWeight + dt / GuardRaiseSeconds);
            _animator.SetLayerWeight(_guardLayer, _guardWeight);

            if (_guardElapsed >= _guardTotal) _guardActive = false;   // барьер отжил — рука опускается сама
        }

        /// <summary>Юнит вошёл в замах авто-атаки — свинг + опц. anticipation-оттяг назад от цели.</summary>
        /// <param name="awayFromTarget">Нормаль «от цели» (куда оттягиваться); zero = без оттяга.</param>
        public void OnAttackStarted(Vector2 awayFromTarget)
        {
            // Новый взмах — здесь и только здесь: сим сказал, что он начался. Всё, что «раз за свинг»
            // (точка A, заказ дуги), сверяется с этим номером, а не с флагами показа.
            _swingSerial++;

            if (_animActive)
            {
                // Тот же признак и на этом входе: мгновенный удар (windup 0) в UpdateAttackPhase не заходит
                // вовсе — фаза начинается сразу с хвоста, и клип выбрать было бы нечем.
                if (_attackPhase == AttackAnimPhase.None && _hasState) _swingCharged = _snapshot.ChargedSwing;

                if (_hasState && _snapshot.IsWindingUp && _snapshot.WindupTicks > 0)
                {
                    _attackPhase = AttackAnimPhase.Windup;
                }
                else
                {
                    // Мгновенный удар (windup 0): сразу хвост — его длину даёт сим (RecoveryTicks).
                    _attackPhase = AttackAnimPhase.Recovery;
                }
            }

            PlayAttackAnticipation(awayFromTarget);
        }

        /// <summary>Микро-рывок атакующего к цели в момент импакта (только презентация).</summary>
        public void OnAttackLunge(Vector2 towardTarget)
        {
            if (_feel == null || !_feel.EnableAttackerLunge) return;
            if (towardTarget.sqrMagnitude < 1e-8f) return;
            PlayAttackOffset(towardTarget.normalized * _feel.LungeDistance, _feel.LungeDuration);
        }

        /// <summary>Замах прерван (событие сима OnAttackInterrupted) — рвём свинг в idle.</summary>
        public void OnAttackInterrupted()
        {
            _attackPhase = AttackAnimPhase.None;
        }

        /// <summary>Бой окончен: перестаём скрабить по замершему симу; даём анимации доиграть натурально.</summary>
        public void OnBattleEnded()
        {
            if (_animActive) _freeRun = true;
        }

        /// <summary>
        /// Финишер: застыть на кадре контакта на <paramref name="seconds"/> (unscaled — синхронно с финальным
        /// slowmo). Срабатывает, только если юнит СЕЙЧАС в атаке (значит удар был мили) — иначе игнор, и
        /// юнит идёт обычным free-run (снаряд/яд «финишеры» позу удара не держат).
        /// </summary>
        public void HoldHitFrame(float seconds)
        {
            // «Юнит сейчас в атаке» спрашиваем у ФАЗЫ, а не у базового состояния: со слоем действия база в
            // этот момент показывает ноги (стойку или разбег), и проверка по ней отняла бы кадр контакта
            // ровно у того, ради кого финишер и играется.
            if (!_animActive || _attackPhase == AttackAnimPhase.None) return;
            _holdHitFrame  = true;
            _holdKeepsAttackFrame = true;
            _holdRemaining = seconds;
        }

        /// <summary>
        /// Финишер для ОСТАЛЬНЫХ выживших: застыть там, где застало, на то же окно. Без этого поле вокруг
        /// добивающего удара доигрывало и оседало в стойку, пока сам момент ещё держится, — кадр распадался
        /// на «замерший герой посреди спокойных зрителей». Поза остаётся честной: кто бил — стоит в ударе,
        /// кто шёл — в шаге.
        /// </summary>
        public void HoldFrame(float seconds)
        {
            if (!_animActive) return;
            _holdHitFrame  = true;
            _holdKeepsAttackFrame = false;
            _holdRemaining = seconds;
        }

        // Застываем на кадре, пока идёт финальный slowmo; на unscaled-времени — держим ровно столько же,
        // сколько slowmo. По истечении «момента» — доигрываем и оседаем в Idle.
        private void DriveHoldHitFrame()
        {
            _animator.speed = 0f;
            // Добивающему кадр контакта возвращаем принудительно: скраб мог уже уползти с маркера. Вес
            // слоя при этом держим сами — обычный ход слоя сюда не доходит, и рука опустилась бы посреди
            // застывшего момента.
            if (_holdKeepsAttackFrame)
            {
                _animator.Play(SwingHash(), SwingLayer, _attackHitNormalized);
                if (SwingIsOverlay)
                {
                    _actionWeight = 1f;
                    ApplyActionWeight();
                }
            }
            _holdRemaining -= Time.unscaledDeltaTime;
            if (_holdRemaining <= 0f)
            {
                _holdHitFrame   = false;
                _freeRun        = true;
                _freeRunSettled = false;
            }
        }

        // После конца боя sim не тикает → скраб застыл бы на кадре. Возвращаем Animator к естественному
        // проигрышу (speed = 1): текущий замах/удар/восстановление доигрывается до конца, затем Idle.
        private void DriveFreeRun()
        {
            _animator.speed = 1f;
            if (_freeRunSettled) return;

            // Пока текущий attack-клип не доигран — ждём (юнит завершает удар и хвост естественно).
            // Удар с разбега доигрывается наравне: обрывать его на полпути к стойке — тот же провал кадра.
            if (SwingIsOverlay)
            {
                if (_actionWeight > 0f)
                {
                    // Слой доигрывает сам (speed вернули к 1), и только потом рука отпускается — тем же
                    // спадом, что и в бою: щелчок веса читался бы как обрыв удара.
                    if (_animator.GetCurrentAnimatorStateInfo(_actionLayer).normalizedTime < 1f) return;

                    _attackPhase  = AttackAnimPhase.None;
                    _actionWeight = Mathf.Max(0f, _actionWeight - Time.deltaTime / ActionDropSeconds);
                    ApplyActionWeight();
                    if (_actionWeight > 0f) return;
                }
            }
            else if (_state == UnitAnimationState.Attack || _state == UnitAnimationState.AttackCharge)
            {
                AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == SwingHash() && info.normalizedTime < 1f) return;
            }

            _state = UnitAnimationState.Idle;
            _animator.Play(IdleHash, 0, 0f);
            _freeRunSettled = true;
        }

        // Разворот тела по горизонтали (спрайты нарисованы «лицом вправо»):
        //  • ИДЁТ АТАКА (замах/хвост) — смотрим на цель: стрелок целится во врага, даже пятясь (кайт/побег);
        //  • иначе движемся — смотрим ТУДА, КУДА бежим (подход/побег без стрельбы);
        //  • стоим — смотрим на текущую цель.
        // Знак горизонтальной скорости берём со сглаживанием: знакопеременное дрожание сепарации
        // усредняется к нулю (не мельтешит разворотом), а осмысленный бег накапливает устойчивый знак.
        // Только презентация, сим не трогаем.
        private const float FacingTargetDeadzoneX = 0.05f;  // мёртвая зона по цели (почти по вертикали — не дёргаем)
        private const float FacingMoveEpsilonX    = 0.01f;  // порог «действительно бежит по X» на сглаженной скорости
        private float _facingVelX;                          // low-pass горизонтальной скорости

        private void ApplyFacing()
        {
            if (!_hasState) return;
            if (Body == null) return;
            if (_flipAnimActive) return; // идёт разворот-сплющивание — не дёргаем flip снаружи

            // Пока идёт цикл атаки — целимся в цель (приоритет над движением): стрелок смотрит на врага,
            // даже отступая. Нет цели — падаем на разворот по движению ниже.
            bool? wantFlip = null;
            if (_attackPhase != AttackAnimPhase.None && TryDesiredFlipToward(out bool faceFlip))
                wantFlip = faceFlip;
            else
            {
                float moveDx = _snapshot.Position.x - _snapshot.PreviousPosition.x;
                _facingVelX = Mathf.Lerp(_facingVelX, moveDx, 0.2f);

                if (Mathf.Abs(_facingVelX) > FacingMoveEpsilonX)
                    wantFlip = _facingVelX < 0f; // бежим влево → отражаем
                else if (TryDesiredFlipToward(out bool standFlip))
                    wantFlip = standFlip;
            }

            if (!wantFlip.HasValue || wantFlip.Value == IsFacingFlipped()) return;
            RequestFacingFlip(wantFlip.Value);
        }

        // Желаемый flip к цели. false = цели нет/мертва. Почти вертикаль — «уже повёрнут».
        private bool TryDesiredFlipToward(out bool flipX)
        {
            flipX = IsFacingFlipped();
            if (!_hasState || !_hasTargetState || _targetState.IsDead) return false;
            float dx = _targetState.Position.x - _snapshot.Position.x;
            if (Mathf.Abs(dx) < FacingTargetDeadzoneX) return true; // уже ок, не меняем
            flipX = dx < 0f;
            return true;
        }

        private void RequestFacingFlip(bool flipX)
        {
            if (_feel == null || !_feel.EnableFacingFlipSquash || _squashTarget == null)
            {
                ApplyFacingVisual(flipX);
                return;
            }

            if (_flipHandle.IsActive()) _flipHandle.Cancel();
            _desiredFlipX = flipX;
            _flipAnimActive = true;
            float dur = Mathf.Max(0.01f, _feel.FacingFlipDuration);
            // 1→0: на середине (v=0.5) меняем facing, вес squash = triangle (пик в середине).
            _flipHandle = LMotion.Create(0f, 1f, dur)
                .WithEase(Ease.Linear)
                .Bind(this, static (v, self) =>
                {
                    if (v >= 0.5f && self.IsFacingFlipped() != self._desiredFlipX)
                        self.ApplyFacingVisual(self._desiredFlipX);
                    // треугольник 0→1→0
                    self._flipSquashWeight = v < 0.5f ? v * 2f : (1f - v) * 2f;
                    if (v >= 0.999f)
                    {
                        self._flipSquashWeight = 0f;
                        self._flipAnimActive = false;
                        self.ApplyFacingVisual(self._desiredFlipX);
                    }
                    self.ApplyComposedScale();
                })
                .AddTo(gameObject);
        }

        // Материальное состояние тела за кадр — ОДНИМ куском: тинт с альфой инвиза (dev, §10.5: тег Stealth →
        // полупрозрачность), вспышка попадания, голограмма развоплощения, контур каста. Тело само решает, во
        // сколько рендереров это разложить и стоит ли вообще писать (повтор того же кадра не пишется).
        // Вспышка идёт НЕ через цвет, а через _FlashAmount шейдера — .color осветлить не может.
        private void ApplyColor()
        {
            if (Body == null) return;

            Color tint = _baseTint;
            bool stealthed = _hasState && (_snapshot.EffectTagMask & EffectTag.Stealth) != 0;
            tint.a = stealthed ? 0.4f : 1f;

            var state = new Body.BodyVisualState(
                tint,
                _flashAmount, _activeFlashColor,
                _holoAmount,
                _feel != null ? _feel.HologramColor      : Color.white,
                _feel != null ? _feel.HologramBodyAlpha  : 1f,
                _feel != null ? _feel.HologramScanScale  : 1f,
                _feel != null ? _feel.HologramScanAmount : 0f,
                _outlineAmount, _outlineColor,
                _glowAmount, _glowColor, _glowParts,
                _feel != null ? _feel.CastGlowFlatness : 1f);

            Body.Apply(state);
        }

        /// <summary>
        /// Реакция цели на урон: вспышка (цвет из feel/школы), сплющивание, опц. hit-nudge от источника.
        /// </summary>
        /// <param name="flashColor">Цвет вспышки (резолвит презентер из feel-конфига по школе/сродству).</param>
        /// <param name="nudgeDir">Мировое направление отъезда (обычно от атакующего); zero = без nudge.</param>
        /// <remarks>
        /// «Ярче обычного» задаётся ЦВЕТОМ, а не силой: <c>PlayHitFlash</c> клампит peak к единице, и
        /// обычное попадание уже бьёт в этот потолок. Сверкание блока приходит HDR-цветом из палитры
        /// (<c>CombatColorPalette.ShieldFlare</c>) — за порогом bloom он и даёт вспышку.
        /// </remarks>
        /// <param name="flashBody">
        /// Заливать ли тело вспышкой. <c>false</c> — удар принял ЩИТ и в тело не вошёл: тогда говорит
        /// только сам щит (его свечение), а тело молчит. Вспыхнувшее тело читалось бы как пробитие
        /// (решение Макса 01.08.2026, отменяет золотую вспышку тела из `2026-07-31/67`).
        /// </param>
        public void OnDamageReceived(Color flashColor, Vector2 nudgeDir, bool flashBody = true)
        {
            _onHitFeedback?.Invoke();
            if (flashBody) PlayHitFlash(flashColor);
            PlayHitSquash();
            PlayHitNudge(nudgeDir);
            if (_feel != null && _feel.EnableHpBarPunch && _healthBar != null)
                _healthBar.Punch(_feel.HpBarPunchAmount, _feel.HpBarPunchDuration);
        }

        /// <summary>
        /// Телеграф: подводка к тому, что ЕЩЁ НЕ СЛУЧИЛОСЬ. Показ узнаёт о будущем событии из ленты боя и
        /// зовёт это заранее — щит «Оплота» так поднимается до удара, а не в его кадр.
        /// <para>Держит подсветку <paramref name="seconds"/> и гасит её к моменту события: к кадру, ради
        /// которого подводка игралась, тело уже «готово», а не вспыхивает вместе с ним.</para>
        /// </summary>
        public void ShowTelegraph(Color color, float seconds)
        {
            if (Body == null || seconds <= 0f) return;
            if (_feel != null && !_feel.EnableTelegraphFlash) return;
            if (_flashHandle.IsActive()) _flashHandle.Cancel();

            _activeFlashColor = color;
            _flashPeak   = TelegraphPeak;
            _flashAmount = 0f;

            // Нарастание к пику и спад к нулю ровно за время телеграфа: время UNSCALED, иначе подводка
            // застынет в slowmo и разъедется с событием, к которому ведёт.
            _flashHandle = LMotion.Create(0f, 1f, seconds)
                .WithEase(Ease.Linear)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (v, self) =>
                {
                    float shape = v < 0.5f ? v / 0.5f : 1f - (v - 0.5f) / 0.5f;
                    self._flashAmount = self._flashPeak * shape;
                })
                .AddTo(gameObject);
        }

        /// <summary>
        /// Реакция на лечение: мягкая вспышка тела. Без неё хил читался только цифрой — цель, которую
        /// подлатали посреди свалки, ничем не отличалась от цели, по которой промахнулись.
        /// </summary>
        public void OnHealed()
        {
            if (_feel == null || !_feel.EnableHealFlash) return;
            PlayHitFlash(_feel.HealFlashColor, _feel.HealFlashPeak);
        }

        // Уклонения-как-косметики здесь НЕТ и быть не должно. Общего evade в игре нет вовсе («ноль выходного
        // рандома» — pitch, difficulty-skill), а единственное уклонение — «Изворотливость» Убийцы — принято
        // как ПОЗИЦИОННОЕ событие: кувырок с уходом с места плюс ускорение, направление по намерению юнита
        // (gdd/20-combat/positioning). Это перемещение в симуляции; вид просто поедет за ним, как за любым
        // движением. Презентационный отскок занял бы этот язык собой и разошёлся бы с ним направлением.

        // Угасание world-UI после смерти. Unscaled — как и весь секвенс смерти: финишер начинается с полной
        // паузы, и на игровом времени полоса зависла бы полупрозрачной на несколько секунд.
        private void TickWorldUiFade()
        {
            if (_uiFadeLeft <= 0f || _uiFadeGroup == null) return;

            _uiFadeLeft -= Time.unscaledDeltaTime;
            if (_uiFadeLeft > 0f)
            {
                _uiFadeGroup.alpha = Mathf.Clamp01(_uiFadeLeft / Mathf.Max(0.0001f, _uiFadeTotal));
                return;
            }

            _uiFadeLeft = 0f;
            _uiFadeGroup.alpha = 0f;
            if (_worldUi != null) _worldUi.SetActive(false);   // догорело — снимаем совсем
        }

        /// <summary>
        /// Каст способности: контур по силуэту вспыхивает и гаснет — «смотри, я сейчас выдам». Цвет один,
        /// главный цвет эффектов юнита: контуру нечего разбрасывать и некуда протягивать градиент.
        /// </summary>
        public void PlayCastOutline(Color color)
        {
            if (_feel == null || _feel.CastOutlineDuration <= 0f) return;

            _outlineColor  = color;
            _outlineTotal  = _feel.CastOutlineDuration;
            _outlineLeft   = _outlineTotal;
            _outlineCharge = false;
        }

        /// <summary>
        /// Подводка к касту с подготовкой (M3): контур держится ВСЮ подготовку и наливается к её концу —
        /// туда, где придёт удар. Отличие от <see cref="PlayCastOutline"/> принципиальное: там вспышка
        /// сообщает «уже случилось», здесь — «сейчас случится», и потому пик в конце, а не в начале.
        /// </summary>
        /// <param name="seconds">Длительность подготовки — её объявляет симуляция, не показ.</param>
        public void PlayCastCharge(Color color, float seconds)
        {
            if (_feel == null || seconds <= 0f) return;

            _outlineColor  = color;
            _outlineTotal  = seconds;
            _outlineLeft   = seconds;
            _outlineCharge = true;
        }

        /// <summary>Каст оборван: подводка гаснет, потому что обещанного удара не будет.</summary>
        public void CancelCastCharge()
        {
            if (!_outlineCharge) return;

            _outlineCharge = false;
            _outlineLeft   = 0f;
            _outlineAmount = 0f;
        }

        // Окно контура: у вспышки-«состоялось» быстрый подъём и спад (пик в первой четверти), у подводки-
        // «сейчас будет» — рост до конца подготовки. Unscaled — как и остальной фидбэк: каст часто
        // совпадает с hitstop.
        private void TickCastOutline()
        {
            if (_outlineLeft <= 0f) return;

            _outlineLeft -= Time.unscaledDeltaTime;
            float t = 1f - Mathf.Clamp01(_outlineLeft / Mathf.Max(0.0001f, _outlineTotal));

            if (_outlineCharge)
            {
                _outlineAmount = t * _feel.CastOutlineStrength;
                if (_outlineLeft <= 0f) { _outlineAmount = 0f; _outlineCharge = false; }
                return;
            }

            const float rise = 0.25f;
            _outlineAmount = t < rise ? t / rise : 1f - (t - rise) / (1f - rise);
            _outlineAmount = Mathf.Clamp01(_outlineAmount) * _feel.CastOutlineStrength;

            if (_outlineLeft <= 0f) _outlineAmount = 0f;
        }

        /// <summary>
        /// Какие части зажечь под этот приём. Навык называет РОЛЬ (<see cref="CastSource"/>), а конкретную
        /// часть находит реестр тела: <c>OffHand</c> у дуалиста это второй клинок, у щитовика — щит.
        /// </summary>
        /// <param name="source">Чем исполняется приём. Способность без определения светит как <c>Auto</c>.</param>
        public PartMask GlowMaskFor(CastSource source) =>
            Body != null ? CastGlowMask.Resolve(Body.Parts, source) : PartMask.Empty;

        /// <summary>
        /// Свечение части-источника на касте (реф SAO): часть в маске <paramref name="parts"/> наливается
        /// светом за <paramref name="chargeSeconds"/> (заряд приёма) и гаснет за спад из конфига. Для
        /// МГНОВЕННОГО приёма (пассив без каста, пульс оружия у длительного баффа) вызывающий передаёт
        /// короткий заряд <c>CastGlowPulseRise</c> — получается всполох вместо наливающегося заряда.
        /// </summary>
        /// <param name="glowColor">HDR-цвет: цвет юнита, поднятый под порог bloom (резолвит презентер).</param>
        /// <param name="parts">Какие ИМЕННО части светятся — обычно <see cref="StrikeGlowMask"/>.</param>
        /// <param name="chargeSeconds">Время заряда (cast-time из симуляции). 0 = сразу пик.</param>
        public void PlayCastGlow(Color glowColor, PartMask parts, float chargeSeconds)
        {
            if (_feel == null || !_feel.EnableCastGlow) return;
            LightParts(glowColor, parts, chargeSeconds);
        }

        /// <summary>
        /// Щит поглотил удар — вспыхивает сам щит: тот же свет, что у каста, но событие обратное
        /// (не «сейчас ударю», а «принял на себя»), поэтому заряда нет — всполох с пика.
        /// </summary>
        /// <param name="glowColor">HDR-цвет защиты (резолвит презентер), не цвет юнита.</param>
        /// <param name="parts">Части щита. Щита в руках нет — маска пуста, и ничего не светится.</param>
        public void PlayBlockGlow(Color glowColor, PartMask parts)
        {
            if (_feel == null || !_feel.EnableBlockGlow) return;
            LightParts(glowColor, parts, chargeSeconds: 0f);
        }

        // Общий механизм для обоих входов: у них одна кривая и один спад, разные только повод и цвет.
        private void LightParts(Color glowColor, PartMask parts, float chargeSeconds)
        {
            if (parts.IsEmpty) return;

            _glowColor        = glowColor;
            _glowParts        = parts;
            _glowChargeTotal  = Mathf.Max(0f, chargeSeconds);
            _glowReleaseTotal = Mathf.Max(0.0001f, _feel.CastGlowRelease);
            _glowInCharge     = _glowChargeTotal > 0.0001f;
            _glowLeft         = _glowInCharge ? _glowChargeTotal : _glowReleaseTotal;
            if (!_glowInCharge) _glowAmount = _feel.CastGlowStrength;  // без заряда — стартуем с пика и гаснем
        }

        /// <summary>Каст оборван: свечение части гаснет мгновенно — обещанного приёма не будет.</summary>
        public void CancelCastGlow()
        {
            if (_glowParts.IsEmpty) return;

            _glowParts    = PartMask.Empty;
            _glowInCharge = false;
            _glowLeft     = 0f;
            _glowAmount   = 0f;
        }

        // Профиль свечения: заряд за cast-time (форма — кривая конфига, пик к концу) → спад с пика в ноль.
        // Unscaled, как весь фидбэк каста. Погасли — маска пустеет, чтобы тело перестало писать glow.
        private void TickCastGlow()
        {
            if (_glowParts.IsEmpty || _feel == null) return;

            _glowLeft -= Time.unscaledDeltaTime;

            if (_glowInCharge)
            {
                float t = _glowChargeTotal > 0.0001f
                    ? 1f - Mathf.Clamp01(_glowLeft / _glowChargeTotal)
                    : 1f;
                float shape = _feel.CastGlowChargeCurve != null ? _feel.CastGlowChargeCurve.Evaluate(t) : t;
                _glowAmount = Mathf.Clamp01(shape) * _feel.CastGlowStrength;

                if (_glowLeft <= 0f) { _glowInCharge = false; _glowLeft = _glowReleaseTotal; }
                return;
            }

            float r = Mathf.Clamp01(_glowLeft / Mathf.Max(0.0001f, _glowReleaseTotal)); // 1→0
            _glowAmount = r * _feel.CastGlowStrength;
            if (_glowLeft <= 0f) { _glowAmount = 0f; _glowParts = PartMask.Empty; }
        }

        /// <summary>Заморозить анимацию этого вида на unscaled-окно (локальный hitstop участника удара).</summary>
        public void OnHitstop(float unscaledSeconds)
        {
            if (unscaledSeconds <= 0f) return;
            _hitstopRemaining = Mathf.Max(_hitstopRemaining, unscaledSeconds);
        }

        // Вспышка: подмешиваем _flashAmount 1→0. Цвет — из аргумента (school flash) или фолбэк feel.
        // Impact-frame: держим пик hold-секунд, затем линейный спад.
        // Время UNSCALED: финишер начинается с полной паузы и продолжается сильным slowmo, а на scaled-времени
        // вспышка в этот момент просто ЗАМИРАЕТ — юнит белеет и остаётся белым на несколько секунд, и весь
        // секвенс смерти стоит следом (он ждёт её догорания). Вспышка — презентация, ей незачем стынуть.
        private void PlayHitFlash(Color flashColor, float peak = 1f)
        {
            if (Body == null || _feel == null || !_feel.EnableHitFlash) return;
            if (_flashHandle.IsActive()) _flashHandle.Cancel();
            _activeFlashColor = flashColor;
            _flashPeak   = Mathf.Clamp01(peak);
            _flashAmount = _flashPeak;
            float hold = _feel.EnableImpactFrame ? Mathf.Max(0f, _feel.ImpactFrameHold) : 0f;
            float fade = _feel.FlashDuration;
            float total = hold + fade;
            if (total <= 0.0001f) { _flashAmount = 0f; return; }

            _flashHandle = LMotion.Create(0f, 1f, total)
                .WithEase(Ease.Linear)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (v, self) =>
                {
                    float holdLocal = (self._feel != null && self._feel.EnableImpactFrame)
                        ? Mathf.Max(0f, self._feel.ImpactFrameHold) : 0f;
                    float fadeLocal = self._feel.FlashDuration;
                    float elapsed = v * (holdLocal + fadeLocal);
                    if (elapsed <= holdLocal) self._flashAmount = self._flashPeak;
                    else
                    {
                        float ft = fadeLocal > 0f ? (elapsed - holdLocal) / fadeLocal : 1f;
                        self._flashAmount = self._flashPeak * (1f - Mathf.Clamp01(ft));
                    }
                })
                .AddTo(gameObject);
        }

        // Сплющивание: на пике (v=1) X растягивается, Y сжимается; линейно возвращается к базе.
        // Вес компонуется с flip/acquire/breath в ApplyComposedScale.
        private void PlayHitSquash()
        {
            if (_squashTarget == null || _feel == null || !_feel.EnableHitSquash) return;
            if (_squashHandle.IsActive()) _squashHandle.Cancel();
            float dur = _feel.SquashDuration;
            _squashHandle = LMotion.Create(1f, 0f, dur)
                .WithEase(Ease.Linear)
                .Bind(this, static (v, self) =>
                {
                    self._hitSquashWeight = v;
                    self.ApplyComposedScale();
                })
                .AddTo(gameObject);
        }

        private void PlayHitNudge(Vector2 dir)
        {
            if (_feel == null || !_feel.EnableHitNudge) return;
            if (dir.sqrMagnitude < 1e-8f) return;
            PlayOffsetPulse(dir.normalized * _feel.HitNudgeDistance, _feel.HitNudgeDuration);
        }

        // Отъезд тела и возврат (0→1→0): удар отбрасывает, уклонение отшатывает. Время UNSCALED — так и
        // заявлено в конфиге, и так правильно: сдвиг обязан доиграть под hitstop, который сам unscaled.
        private void PlayOffsetPulse(Vector2 peak, float duration)
        {
            if (_nudgeHandle.IsActive()) _nudgeHandle.Cancel();

            _nudgePeak = peak;
            float dur = Mathf.Max(0.01f, duration);
            _nudgeHandle = LMotion.Create(0f, 1f, dur)
                .WithEase(Ease.Linear)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (v, self) =>
                {
                    // 0→1→0: отъезд и возврат
                    float w = v < 0.5f ? v * 2f : (1f - v) * 2f;
                    self._nudgeOffset = self._nudgePeak * w;
                    if (v >= 0.999f) self._nudgeOffset = Vector2.zero;
                })
                .AddTo(gameObject);
        }

        private void PlayAttackAnticipation(Vector2 awayFromTarget)
        {
            if (_feel == null || !_feel.EnableAttackAnticipation) return;
            if (awayFromTarget.sqrMagnitude < 1e-8f) return;
            // Оттяг назад и возврат к нулю к концу duration (пик в середине).
            PlayAttackOffset(awayFromTarget.normalized * _feel.AnticipationDistance,
                _feel.AnticipationDuration);
        }

        // Сдвиг атакующего (anticipation/lunge): triangle 0→1→0.
        private void PlayAttackOffset(Vector2 peak, float duration)
        {
            if (_attackMotionHandle.IsActive()) _attackMotionHandle.Cancel();
            _attackPeak = peak;
            float dur = Mathf.Max(0.01f, duration);
            // Тоже unscaled: замах и рывок живут вокруг hitstop, а он идёт по нескалированному времени —
            // на игровом они растягивались бы вместе со slowmo и разъезжались с ударом (конфиг обещает unscaled).
            _attackMotionHandle = LMotion.Create(0f, 1f, dur)
                .WithEase(Ease.Linear)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (v, self) =>
                {
                    float w = v < 0.5f ? v * 2f : (1f - v) * 2f;
                    self._attackOffset = self._attackPeak * w;
                    if (v >= 0.999f) self._attackOffset = Vector2.zero;
                })
                .AddTo(gameObject);
        }

        // Композиция масштаба: base × hit-squash × flip-squash × acquire-twitch × idle-breath.
        private void ApplyComposedScale()
        {
            if (_squashTarget == null) return;

            float hitAmt  = _feel != null ? _feel.SquashAmount           * _hitSquashWeight     : 0f;
            float flipAmt = _feel != null ? _feel.FacingFlipSquashAmount * _flipSquashWeight    : 0f;
            float acqAmt  = _feel != null ? _feel.TargetAcquireTwitch    * _acquireSquashWeight : 0f;

            // Hit: X+ Y-; Flip: X- (edge-on); Acquire: оба чуть вниз (вздрог).
            float sx = 1f + hitAmt - flipAmt - acqAmt * 0.5f;
            float sy = 1f - hitAmt + flipAmt * 0.25f - acqAmt;

            float breath = 1f;
            if (_feel != null && _feel.EnableIdleBreath && !_isDead && _state == UnitAnimationState.Idle
                && _hitstopRemaining <= 0f && _deathPhase == DeathPhase.None)
            {
                float period = Mathf.Max(0.1f, _feel.IdleBreathPeriod);
                float phase = (Time.unscaledTime / period) * Mathf.PI * 2f;
                breath = 1f + Mathf.Sin(phase) * _feel.IdleBreathAmplitude;
            }

            _squashTarget.localScale = new Vector3(
                _baseSpriteScale.x * sx * breath,
                _baseSpriteScale.y * sy * breath,
                _baseSpriteScale.z);
        }

        private void TickIdleBreath()
        {
            if (_feel == null || !_feel.EnableIdleBreath) return;
            // Дыхание крутится в ApplyComposedScale (breath=1 вне Idle); здесь обновляем scale каждый кадр,
            // когда нет активных твинов squash — иначе при выходе из Idle останется «залипший» breath-масштаб.
            if (_hitSquashWeight > 0.001f || _flipSquashWeight > 0.001f || _acquireSquashWeight > 0.001f) return;
            if (_isDead || _deathPhase != DeathPhase.None) return;
            ApplyComposedScale();
        }

        private void TickContactDustCooldown()
        {
            if (_contactDustCooldownLeft > 0f)
                _contactDustCooldownLeft -= Time.unscaledDeltaTime;
        }

        private void TickLocomotionDust()
        {
            if (!_hasState || _onContactDust == null) return;
            if (_feel == null || !_feel.EnableContactDust) { _wasMoving = false; return; }

            bool isMoving = (_snapshot.Position - _snapshot.PreviousPosition).sqrMagnitude > MoveEpsilonSq;
            if (isMoving != _wasMoving && _contactDustCooldownLeft <= 0f)
            {
                _onContactDust.Invoke(this);
                _contactDustCooldownLeft = _feel.ContactDustCooldown;
            }
            _wasMoving = isMoving;
        }

        private void TickTargetAcquireTell()
        {
            if (!_hasState || _feel == null || !_feel.EnableTargetAcquireTell) return;

            int id = _hasTargetState && !_targetState.IsDead
                ? _targetState.Id
                : int.MinValue;
            if (id == _lastTargetId) return;

            bool hadPrevious = _lastTargetId != int.MinValue;
            _lastTargetId = id;
            if (!hadPrevious || id == int.MinValue) return; // первый лок / потеря цели — без вздрога

            if (_squashTarget == null) return;
            if (_acquireHandle.IsActive()) _acquireHandle.Cancel();
            float dur = Mathf.Max(0.01f, _feel.TargetAcquireDuration);
            _acquireHandle = LMotion.Create(1f, 0f, dur)
                .WithEase(Ease.Linear)
                .Bind(this, static (v, self) =>
                {
                    self._acquireSquashWeight = v;
                    self.ApplyComposedScale();
                })
                .AddTo(gameObject);
        }

        /// <summary>
        /// Вызывается при гибели юнита. Секвенс: сначала даём догаснуть hit-flash (не рвём моргание удара),
        /// затем проигрываем death-клип, на конце которого — вспышка в белый и разлёт спрайта на осколки
        /// (<see cref="DeathShatter"/>). Сам разлёт/тайминги ведёт <see cref="DriveDeath"/>.
        /// </summary>
        public void OnDeath()
        {
            _onDeathFeedback?.Invoke();

            // Убираем world-UI (бары) — над трупом он не нужен. Но НЕ щелчком: тело после этого
            // ещё три стадии развоплощается, и полоса, выпрыгнувшая из кадра в первый же момент, читалась
            // как сбой. Гасим её ровно за ту стадию, пока тело истончается в голограмму.
            if (_worldUi != null)
            {
                _uiFadeGroup = _worldUi.GetComponent<CanvasGroup>();
                if (_uiFadeGroup != null)
                {
                    _uiFadeTotal = _feel != null && _feel.DeathHologramDuration > 0.01f
                        ? _feel.DeathHologramDuration
                        : 0.12f;
                    _uiFadeLeft = _uiFadeTotal;
                }
                else
                {
                    _worldUi.SetActive(false);   // нет группы на контейнере — старое поведение
                }
            }
            else
            {
                // Фолбэк, если контейнер 'UI' не привязан: гасим бары поштучно.
                if (_healthBar != null) _healthBar.gameObject.SetActive(false);
                if (_manaBar   != null) _manaBar.gameObject.SetActive(false);
            }

            _isDead     = true;
            _deathPhase = DeathPhase.WaitFlash; // дальше — DriveDeath (ждём hit-flash → death → разлёт)

            // Гвардия снимается сразу и целиком: секвенс смерти обходит обычный тик анимации, и рука со
            // щитом осталась бы поднятой на всём падении — держал бы позу тот, кому уже нечем держать.
            _guardActive = false;
            _guardWeight = 0f;
            if (_guardLayer >= 0 && _animator != null) _animator.SetLayerWeight(_guardLayer, 0f);
        }

        // Секвенс смерти. WaitFlash → Dying → Hologram (опц.) → Anticipate (опц.) → StartShatter.
        private void DriveDeath()
        {
            switch (_deathPhase)
            {
                case DeathPhase.WaitFlash:
                    if (_animActive) _animator.speed = 0f; // держим кадр, пока гаснет вспышка удара
                    // Ждём конца hit-flash: и активного твина, и остаточной величины (моргание должно догореть).
                    if (_flashHandle.IsActive() || _flashAmount > 0.02f) return;

                    // Падение тела — отдельный разговор от развоплощения. По умолчанию его нет: юнит
                    // застывает на кадре, где его достали, и уходит прямо в голограмму (как в референсе).
                    if (_animActive && _feel != null && _feel.PlayDeathClip)
                    {
                        // Падение забирает тело ЦЕЛИКОМ: слой действия отпускаем сразу, иначе рука доиграет
                        // чужой удар поверх падающего трупа. Стоп-кадру ниже гасить нечего — там поза как раз
                        // обязана остаться той, в которой юнита достали.
                        if (SwingIsOverlay && _actionWeight > 0f)
                        {
                            _actionWeight = 0f;
                            ApplyActionWeight();
                        }

                        _state = UnitAnimationState.Death;
                        _animator.Play(DeathHash, 0, 0f);
                        _animator.speed = 1f;
                        AnimationClip death = _visual != null ? _visual.Clip(UnitAnimationState.Death) : null;
                        _deathRemaining = death != null && death.length > 0f ? death.length : 0.6f;
                        _deathPhase = DeathPhase.Dying;
                    }
                    else
                    {
                        BeginHologramOrAnticipate();
                    }
                    break;

                case DeathPhase.Dying:
                    _animator.speed = 1f;
                    _deathRemaining -= Time.deltaTime;
                    if (_deathRemaining <= 0f) BeginHologramOrAnticipate();
                    break;

                case DeathPhase.Hologram:
                    // Тело держит последний кадр и «расплотняется»: плоть уходит, остаются данные.
                    if (_animActive) _animator.speed = 0f;
                    _holoLeft -= Time.unscaledDeltaTime;
                    float holoTotal = _feel != null ? Mathf.Max(0.0001f, _feel.DeathHologramDuration) : 0.0001f;
                    _holoAmount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - _holoLeft / holoTotal));
                    if (_holoLeft <= 0f)
                    {
                        _holoAmount = 1f;
                        BeginDeathAnticipateOrShatter();
                    }
                    break;

                case DeathPhase.Anticipate:
                    if (_animActive) _animator.speed = 0f;
                    _deathAnticipateLeft -= Time.unscaledDeltaTime;
                    // Дрожь масштаба + белый силуэт на всё окно anticipation.
                    _flashAmount = 1f;
                    _activeFlashColor = _feel != null ? _feel.DeathFlashColor : Color.white;
            float shake = _feel != null ? _feel.DeathAnticipateShake : 0f;
            float wobble = Mathf.Sin(Time.unscaledTime * 60f) * shake;
            if (_squashTarget != null)
            {
                _squashTarget.localScale = new Vector3(
                    _baseSpriteScale.x * (1f + wobble),
                    _baseSpriteScale.y * (1f - wobble * 0.5f),
                    _baseSpriteScale.z);
            }
            if (_deathAnticipateLeft <= 0f)
            {
                if (_squashTarget != null) _squashTarget.localScale = _baseSpriteScale;
                _flashAmount = 0f;
                StartShatter();
            }
                    break;

                case DeathPhase.Shattering:
                    // Разлёт ведёт DeathShatter; по завершении он вызовет callback → gameObject.SetActive(false).
                    break;
            }
        }

        // Стадия между анимацией смерти и белой вспышкой. Выключается нулевой длительностью в feel-конфиге —
        // тогда секвенс тот же, что был до неё.
        private void BeginHologramOrAnticipate()
        {
            if (_feel != null && _feel.DeathHologramDuration > 0f)
            {
                _deathPhase = DeathPhase.Hologram;
                _holoLeft   = _feel.DeathHologramDuration;
                _holoAmount = 0f;
                return;
            }
            BeginDeathAnticipateOrShatter();
        }

        private void BeginDeathAnticipateOrShatter()
        {
            // Голограмму снимаем: следующая стадия — плотный белый пересвет, а полупрозрачное тело
            // дало бы блёклую вспышку сквозь фон.
            _holoAmount = 0f;

            if (_feel != null && _feel.EnableDeathAnticipation && _feel.DeathAnticipateDuration > 0f)
            {
                _deathPhase = DeathPhase.Anticipate;
                _deathAnticipateLeft = _feel.DeathAnticipateDuration;
                _activeFlashColor = _feel.DeathFlashColor;
                _flashAmount = 1f;
                return;
            }
            StartShatter();
        }

        // Конец death-клипа / anticipation: прячем тело и запускаем разлёт на осколки. Сколько кусков тела
        // колется и куда спавнятся осколки — знает тело; вид только даёт палитру и ждёт «догорело».
        private void StartShatter()
        {
            _deathPhase = DeathPhase.Shattering;
            _audio?.PlayAt("feel.death_shatter.death", transform.position);

            // Нечего колоть, или разлёт выключен тумблером — тело просто исчезает. Это законный выбор
            // дизайна (и способ замерить, сколько джуса даёт сам разлёт), поэтому путь молчит.
            if (Body == null || !Body.HasContent || (_feel != null && !_feel.EnableDeathShatter))
            {
                gameObject.SetActive(false);
                return;
            }

            // Разброс павшего подан презентером при спавне: осколки должны быть «его» цвета.
            Body.PlayShatter(_feel, _vfxSpread, () => gameObject.SetActive(false));
        }

#if UNITY_EDITOR
        // Гизмо (редактор): показываем, когда выбран сам UnitView ИЛИ любой его дочерний GO
        // (Body, точки, бары) — чтобы при настройке видеть всё сразу. Ранний выход, если выбрана
        // чужая иерархия, — в бою не мусорит. Линейка роста стоит от КОРНЯ (точки земли/ног в симе,
        // y=0): сюда ставь ноги спрайта, и макушка должна доставать до верхней засечки.
        private void OnDrawGizmos()
        {
            // Общий выключатель служебной разметки: при ручной правке поз и анимации она мешает целиться
            // мышью, а гасить её удалением кода нельзя — она для того и есть. Меню:
            // Alebardium/Animation/Show Unit Gizmos In Scene.
            if (!UnityEditor.EditorPrefs.GetBool("Alebardium.Gizmos.Show", true)) return;

            var sel = UnityEditor.Selection.activeGameObject;
            bool related = sel != null && sel.transform.IsChildOf(transform);
            bool inPrefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null;
            if (!related && !inPrefabStage) return;

            Vector3 root   = transform.position;                                 // сим-позиция юнита
            Vector3 feet   = _feetPoint != null ? _feetPoint.position : root;     // низ фигуры (ноги)
            Vector3 rightN = transform.right;

            // --- Эталонный габарит: рамка рост × ширина от НОГ (подгоняй спрайт под неё) ---
            var green = new Color(0.35f, 0.95f, 0.55f, 0.95f);
            float rec = Mathf.Max(0.01f, _recommendedHeight);
            float wid = Mathf.Max(0.01f, _recommendedWidth);
            Vector3 wL = feet - rightN * (wid * 0.5f);
            Vector3 wR = feet + rightN * (wid * 0.5f);
            Gizmos.color = green;
            Gizmos.DrawLine(wL, wR);                                             // ширина у ног
            Gizmos.color = new Color(green.r, green.g, green.b, 0.5f);
            Gizmos.DrawLine(wL, wL + Vector3.up * rec);                          // левая вертикаль (рост)
            Gizmos.DrawLine(wR, wR + Vector3.up * rec);                          // правая вертикаль
            Gizmos.DrawLine(wL + Vector3.up * rec, wR + Vector3.up * rec);       // верх рамки
            UnityEditor.Handles.color = green;
            UnityEditor.Handles.Label(wL + Vector3.up * (rec + 0.06f), $"рост {rec:0.##} × ширина {wid:0.##} м");

            // --- Круг коллизии сима (радиус = Size × SimTuning.Default.BodyRadiusPerSize) в НОГАХ. Тумблер. ---
            // В рантайме сим считает коллизию в unit.Position; презентация ставит юнита так, чтобы Feet Point
            // попал в неё (офсет спавна — фаза коллизии), поэтому центр здесь = feet.
            if (_showCollisionGizmo)
            {
                float size = Application.isPlaying && _hasState
                    ? Mathf.Max(0.01f, _snapshot.Size)
                    : Mathf.Max(0.01f, _gizmoPreviewSize);
                float cr = size * SimTuning.Default.BodyRadiusPerSize;
                var orange = new Color(1f, 0.6f, 0.2f, 0.85f);
                Gizmos.color = orange;
                DrawWireDisc(feet, cr);
                UnityEditor.Handles.color = orange;
                UnityEditor.Handles.Label(feet + Vector3.down * (cr + 0.06f), "коллизия r=" + cr.ToString("0.##"));
            }

            // --- Сокеты: маркер + подпись (рисуются только назначенные в компоненте) ---
            DrawSocket(_feetPoint, "Ноги",      new Color(0.6f, 0.85f, 1f));
            DrawSocket(_headPoint, "Голова",    new Color(0.4f, 0.7f,  1f));
            DrawSocket(_shotPoint, "Выстрел",   new Color(1f, 0.85f, 0.2f));
            DrawSocket(_hitPoint,  "Попадание", new Color(1f, 0.35f, 0.35f));
        }

        private static void DrawSocket(Transform t, string label, Color c)
        {
            if (t == null) return;
            const float r = 0.04f;   // маленькая точка, а не полноценный круг
            Vector3 p = t.position;
            Gizmos.color = c;
            Gizmos.DrawLine(p + Vector3.left * r, p + Vector3.right * r);
            Gizmos.DrawLine(p + Vector3.down * r, p + Vector3.up * r);
            DrawWireDisc(p, r);
            UnityEditor.Handles.color = c;
            UnityEditor.Handles.Label(p + new Vector3(r + 0.06f, r * 2f, 0f), label);
        }

        private static void DrawWireDisc(Vector3 center, float radius, int segments = 28)
        {
            if (radius <= 0f) return;
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}
