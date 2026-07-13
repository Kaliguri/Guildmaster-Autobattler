using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using LitMotion;
using UnityEngine;
using UnityEngine.Events;

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
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        private const float MoveEpsilonSq = 1e-6f;

        [Header("Components")]
        [SerializeField] private SpriteRenderer _sprite;
        [Tooltip("Animator на теле (той же GO, что SpriteRenderer). Пусто/без визуала = статичный спрайт.")]
        [SerializeField] private Animator _animator;
        [SerializeField] private HealthBarView  _healthBar;
        [Tooltip("Бар ресурса (мана/ярость). Пусто = без бара; скрывается сам для безресурсных юнитов.")]
        [SerializeField] private ManaBarView    _manaBar;

        [Header("Animation")]
        [SerializeField] private UnitVisual _visual;

        [Tooltip("Бег «прибит к земле»: сколько мировых юнитов проходит юнит на ОДИН кадр бега. " +
                 "Меньше = ноги быстрее (бодрее), больше = медленнее. Темп бега привязан к скорости — не скользит.")]
        [SerializeField] private float _runUnitsPerFrame = 0.15f;

        [Header("Feel Hooks (пустой шов под точечный MMF_Player в Inspector)")]
        [SerializeField] private UnityEvent _onHitFeedback;
        [SerializeField] private UnityEvent _onDeathFeedback;

        [Header("Feel — реакция на попадание (LitMotion, код)")]
        [Tooltip("Материал спрайта с параметром _FlashAmount (Guildmaster/Sprite/HitFlash). " +
                 "Ставится на спрайт в Bind; пусто = вспышки не будет (обычный .color так не осветлить).")]
        [SerializeField] private Material _flashMaterial;
        // Длительности/сила/цвет вспышки и сплющивания — из CombatFeelConfig (ApplyFeelConfig), не с префаба.

        [Header("Identity label — подпись персонажа над HP-баром (TMP-ребёнок префаба)")]
        [Tooltip("TMP-текст подписи. Позиция/размер/шрифт настраиваются на нём в префабе.")]
        [SerializeField] private TMPro.TMP_Text _nameLabel;

        [Header("Attach points (сокеты) — GO под 'Sprite Visual', ставятся по арту")]
        [Tooltip("Ноги: точка касания земли (совпадает с позицией юнита в симе).")]
        [SerializeField] private Transform _feetPoint;
        [Tooltip("Голова: макушка — якорь для баров/статус-текста.")]
        [SerializeField] private Transform _headPoint;
        [Tooltip("Точка выстрела: откуда визуально вылетает снаряд/каст (у команды 1 отзеркалить по X — см. ShotPoint).")]
        [SerializeField] private Transform _shotPoint;
        [Tooltip("Точка попадания: куда прилетают снаряды/цифры урона/вспышка (обычно грудь).")]
        [SerializeField] private Transform _hitPoint;

        [Header("Gizmo — круг коллизии сима (только редактор)")]
        [Tooltip("Превью Size для гизмо круга коллизии, когда юнит ещё не заспавнен (рантайм берёт настоящий Size).")]
        [SerializeField] private float _gizmoPreviewSize = 1f;
        [Tooltip("Показывать оранжевый круг коллизии симуляции (радиус = Size × SimTuning.BodyRadiusPerSize). Выключи, если мешает.")]
        [SerializeField] private bool _showCollisionGizmo = true;

        private static readonly int IdleHash   = Animator.StringToHash("Idle");
        private static readonly int RunHash     = Animator.StringToHash("Run");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int DeathHash   = Animator.StringToHash("Death");

        private RuntimeUnit _unit;
        private Vector2     _renderPosition;

        // --- Feel (реакция на удар, LitMotion) — только презентация, сим не трогает ---
        private Design.CombatFeelConfig _feel;          // параметры вспышки/сплющивания (из design-конфига)
        private Color        _baseTint = Color.white;   // цвет-тинт тела (умножается на текстуру в шейдере)
        private float        _flashAmount;               // 0..1 — сила вспышки (параметр _FlashAmount шейдера)
        private bool         _flashApplied;              // держим ли сейчас MPB на спрайте (чтобы вернуть в 0 один раз)
        private Vector3      _baseSpriteScale = Vector3.one; // масштаб узла сплющивания до эффекта
        private Transform    _squashTarget;              // узел, который сплющиваем (выше Animator, чтобы не затирался)
        private float        _hitstopRemaining;          // unscaled-окно заморозки анимации участников удара
        private MaterialPropertyBlock _mpb;              // per-instance _FlashAmount без клонирования материала
        private MotionHandle _flashHandle;
        private MotionHandle _squashHandle;

        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
        private static readonly int FlashColorId  = Shader.PropertyToID("_FlashColor");

        // --- Состояние анимации (рендер-сторона, не влияет на сим) ---
        // Своя фаза анимации атаки (НЕ путать с сим-AttackPhase на RuntimeUnit): охватывает ВЕСЬ цикл
        // атаки — замах до кадра контакта + хвост-возврат, растянутый на «окно» до следующего замаха.
        // За счёт этого у непрерывно атакующего клип атаки лупится бесшовно в темпе скорости атаки, а
        // не мигает Attack↔Run в паузе между ударами.
        private enum AttackAnimPhase { None, Windup, Recovery }

        private UnitAnimationState _state = UnitAnimationState.Idle;
        private AttackAnimPhase _attackPhase;
        private bool  _isDead;
        private float _deathRemaining;

        private bool _freeRun;        // бой окончен → доигрываем анимации натурально, не скрабим по замершему симу
        private bool _freeRunSettled; // уже осели в Idle после доигрыша
        private bool  _holdHitFrame;  // финишер: держим кадр контакта весь финальный slowmo
        private float _holdRemaining; // unscaled-остаток удержания

        private bool  _animActive;              // визуал с клипами подан → Animator рулит спрайтом
        private float _attackMarkerNormalized;  // 0..1 — доля клипа атаки до маркера контакта
        private int   _recoveryGapTicks = 1;    // тиков от кадра контакта до следующего замаха (снап на конце замаха) — темп хвоста
        private float _runFrameRate = 10f;

        /// <summary>Связать вид с рантайм-юнитом.</summary>
        public void Bind(RuntimeUnit unit)
        {
            _unit           = unit;
            _renderPosition = unit.Position;
            transform.position = (Vector3)_renderPosition;

            // Спрайты нарисованы лицом вправо: команда 0 (слева) так и смотрит, враги (справа) — влево.
            // Дальше разворот динамический (ApplyFacing по цели/движению), но стартовый — по стороне, иначе
            // стоящий без цели (напр. ассасин в инвизе) смотрит «от противника».
            if (_sprite != null)
            {
                _sprite.flipX = unit.Team != 0;

                // Материал с flash-параметром: осветлить спрайт в белый через SpriteRenderer.color нельзя
                // (это множитель), поэтому ставим шейдер с _FlashAmount поверх текстуры.
                if (_flashMaterial != null) _sprite.sharedMaterial = _flashMaterial;

                // Сплющиваем узел ВЫШЕ Animator (родитель спрайта), иначе кадровая анимация тела его затирает.
                _squashTarget    = _sprite.transform.parent != null ? _sprite.transform.parent : _sprite.transform;
                _baseSpriteScale = _squashTarget.localScale;
            }

            if (_healthBar != null) _healthBar.Bind(unit);
            if (_manaBar != null)   _manaBar.Bind(unit);

            InitVisual(); // визуал/анимация — из самого префаба (см. InitVisual), без рантайм-подмены
        }

        /// <summary>Тинт тела юнита: один общий спрайт, разный цвет на персонажа (dev-харнесс, «пока один спрайт»).</summary>
        public void SetTint(Color color)
        {
            _baseTint = color;
            ApplyColor(); // итоговый цвет = база + вспышка + альфа инвиза (единый писатель _sprite.color)
        }

        /// <summary>Подать design-конфиг сочности (длительности/сила/цвет вспышки и сплющивания). CombatPresenter — при спавне.</summary>
        public void ApplyFeelConfig(Design.CombatFeelConfig feel) => _feel = feel;

        /// <summary>Цвет HP-бара по принадлежности к смотрящему (из <c>CombatColorPalette</c>).</summary>
        public void SetHealthColor(Color color)
        {
            if (_healthBar != null) _healthBar.SetMainColor(color);
        }

        /// <summary>Подпись «что за персонаж» над HP-баром. Задаёт лишь текст — вид настраивается на TMP в префабе.</summary>
        public void SetLabel(string text)
        {
            if (_nameLabel != null) _nameLabel.text = text;
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

            bool ready = _visual != null && _visual.HasClips
                         && _animator != null && _animator.runtimeAnimatorController != null;
            _animActive = ready;

            if (!ready)
            {
                if (_animator != null) _animator.enabled = false;
                return;
            }

            _animator.fireEvents = false;
            _animator.enabled = true;

            _attackMarkerNormalized = ClipMarkers.MarkerNormalized(_visual.AttackClip);
            AnimationClip run = _visual.Clip(UnitAnimationState.Run);
            _runFrameRate = run != null && run.frameRate > 0f ? run.frameRate : 10f;

            _animator.Play(IdleHash, 0, 0f);
            _animator.speed = 1f;
        }

        /// <summary>
        /// Обновить интерполированную позицию. Вызывается из <see cref="CombatPresenter.Update"/>.
        /// </summary>
        /// <param name="alpha">Степень интерполяции [0, 1] между PreviousPosition и Position.</param>
        public void UpdateInterpolation(float alpha)
        {
            if (_unit == null) return;

            _renderPosition = Vector2.Lerp(_unit.PreviousPosition, _unit.Position, alpha);
            transform.position = new Vector3(_renderPosition.x, _renderPosition.y, 0f);

            if (_healthBar != null)
                _healthBar.UpdateBar(_unit.CurrentHP, _unit.Stats.Get(Data.Stats.StatType.MaxHP));

            if (_manaBar != null)
                _manaBar.UpdateBar(_unit.CurrentResource, _unit.Stats.Get(Data.Stats.StatType.MaxResource));
        }

        // --- Attach points (сокеты) ----------------------------------------------------------------
        // Размер вида задаётся СТАТИКОЙ (масштаб узла тела или PPU арта), не рантаймом. Сокеты — пустые GO,
        // презентация читает их мировые позиции для спавна снаряда/цифр/вспышек. Фолбэк — позиция юнита.

        /// <summary>Мировая точка ног (касание земли). Фолбэк — позиция юнита.</summary>
        public Vector3 FeetPoint => _feetPoint != null ? _feetPoint.position : transform.position;

        /// <summary>Мировая точка головы (макушка) — якорь баров/статус-текста. Фолбэк — позиция юнита.</summary>
        public Vector3 HeadPoint => _headPoint != null ? _headPoint.position : transform.position;

        /// <summary>Мировая точка выстрела (откуда визуально стартует снаряд/каст). Фолбэк — позиция юнита.</summary>
        public Vector3 ShotPoint => _shotPoint != null ? _shotPoint.position : transform.position;

        /// <summary>Мировая точка попадания (куда прилетают снаряды/цифры урона). Фолбэк — позиция юнита.</summary>
        public Vector3 HitPoint => _hitPoint != null ? _hitPoint.position : transform.position;

        private void Update()
        {
            ApplyColor(); // вспышка + альфа инвиза видны даже в hitstop/паузе (единый писатель _sprite.color)

            // Локальный hitstop: удар «весит» — участники замирают на unscaled-окно, толпа вокруг не стынет.
            // Морозим только анимацию: позиция продолжает интерполироваться (≈50 мс дрейфа незаметны, зато
            // нет снапа при выходе). unscaled — работает и во время global slowmo (2b), и на паузе.
            if (_hitstopRemaining > 0f)
            {
                _hitstopRemaining -= Time.unscaledDeltaTime;
                if (_animActive) _animator.speed = 0f;
                return;
            }

            ApplyFacing(); // разворот по цели — до guard'а анимации (нужен и статичным спрайтам)

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

            bool isMoving = _unit != null &&
                            (_unit.Position - _unit.PreviousPosition).sqrMagnitude > MoveEpsilonSq;

            // Attack показываем поверх Run, ПОКА идёт цикл атаки. Ключ — что «в атаке» решает СИМ-фаза,
            // а не сырое смещение: во время замаха/хвоста мили зарутован (MovementSystem), и толчок
            // сепарации НЕ должен рвать свинг в Run (иначе виден бег вместо замаха и пропадают замах/хвост
            // у преследователя — остаётся будто только удар). isMoving разделяет стойку и погоню лишь в
            // ПАУЗЕ между ударами (сим Idle, рендер ещё тянет хвост-цикл).
            bool attackWhileMoving = _unit?.Unit != null && _unit.Unit.CanAttackWhileMoving;
            bool simInSwing = _unit != null &&
                              (_unit.Phase == AttackPhase.Windup || _unit.Phase == AttackPhase.Recovery);
            bool attackPlaying = UnitAnimationSelector.AttackClipPlaying(
                _attackPhase != AttackAnimPhase.None, simInSwing, attackWhileMoving, isMoving);

            UnitAnimationState next = UnitAnimationSelector.Select(_isDead, attackPlaying, isMoving);
            if (next != _state)
            {
                _state = next;
                _animator.Play(HashFor(next), 0, 0f);
                _animator.speed = 1f;
            }

            DriveAnimation(dt);

            if (_isDead && _deathRemaining > 0f)
            {
                _deathRemaining -= dt;
                if (_deathRemaining <= 0f) gameObject.SetActive(false);
            }
        }

        private static int HashFor(UnitAnimationState state) => state switch
        {
            UnitAnimationState.Run    => RunHash,
            UnitAnimationState.Attack => AttackHash,
            UnitAnimationState.Death  => DeathHash,
            _                         => IdleHash,
        };

        // Управление фазой анимации атаки от состояния сима (вики «14»): замах → кадр контакта → хвост.
        // Замах скрабится по windup-тикам (маркер на тик урона); хвост — по остатку интервала до
        // следующего замаха, поэтому у непрерывно бьющего клип лупится бесшовно в темпе скорости атаки.
        private void UpdateAttackPhase(float dt)
        {
            if (_isDead) { _attackPhase = AttackAnimPhase.None; return; }
            if (_unit == null) return;

            // Идёт сим-замах → фаза замаха (покрывает и реконструкцию после load/resync без события старта).
            if (_unit.IsWindingUp) { _attackPhase = AttackAnimPhase.Windup; return; }

            switch (_attackPhase)
            {
                case AttackAnimPhase.Windup:
                    // Замах кончился (кадр контакта) → хвост. Окно до следующего удара = текущий кулдаун:
                    // на старте замаха он равнялся интервалу, за замах убыл до «интервал − замах».
                    _recoveryGapTicks = Mathf.Max(1, _unit.AttackCooldownTicks);
                    _attackPhase = AttackAnimPhase.Recovery;
                    break;

                case AttackAnimPhase.Recovery:
                    // Пока тикает кулдаун — цикл атаки жив, держим хвост (в непрерывной атаке следующий
                    // замах придёт ровно на кулдаун 0 → бесшовный луп, без провала в Run). Кулдаун истёк
                    // без нового замаха (цель ушла/вне радиуса) → атака кончилась, возврат к локомоции.
                    if (_unit.AttackCooldownTicks <= 0) _attackPhase = AttackAnimPhase.None;
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
                    if (_attackPhase == AttackAnimPhase.Windup && _unit != null && _unit.WindupTicks > 0)
                    {
                        // Замах: скрабим [0..маркер] по прогрессу windup — контакт (маркер) приходится
                        // ровно на конец замаха = сим-тик урона.
                        float progress = 1f - (float)_unit.WindupRemaining / _unit.WindupTicks;
                        progress = Mathf.Clamp01(progress);
                        _animator.speed = 0f;
                        _animator.Play(AttackHash, 0, progress * _attackMarkerNormalized);
                    }
                    else if (_attackPhase == AttackAnimPhase.Recovery && _unit != null)
                    {
                        // Хвост: скрабим [маркер..1] по прогрессу окна до следующего замаха — клип
                        // доигрывает ровно к старту следующего удара, цикл лупится в темпе скорости атаки.
                        float gapProgress = 1f - (float)_unit.AttackCooldownTicks / _recoveryGapTicks;
                        gapProgress = Mathf.Clamp01(gapProgress);
                        float clipT = _attackMarkerNormalized + gapProgress * (1f - _attackMarkerNormalized);
                        _animator.speed = 0f;
                        _animator.Play(AttackHash, 0, clipT);
                    }
                    else
                    {
                        // Мгновенный удар без данных тайминга — доигрываем натуральным ходом.
                        _animator.speed = 1f;
                    }
                    break;

                case UnitAnimationState.Run:
                    // Кадры бега листаются по пройденной дистанции: скорость клипа = (ед/с) / (ед/кадр) / (кадр/с).
                    float speed = _unit != null
                        ? (_unit.Position - _unit.PreviousPosition).magnitude / SimConstants.TickDelta
                        : 0f;
                    float step = Mathf.Max(0.01f, _runUnitsPerFrame);
                    _animator.speed = speed / (step * _runFrameRate);
                    break;

                default:
                    _animator.speed = 1f;
                    break;
            }
        }

        /// <summary>Юнит вошёл в замах авто-атаки (событие сима OnAttackStarted) — запускаем свинг.</summary>
        public void OnAttackStarted()
        {
            if (!_animActive) return;

            if (_unit != null && _unit.IsWindingUp && _unit.WindupTicks > 0)
            {
                _attackPhase = AttackAnimPhase.Windup;
            }
            else
            {
                // Мгновенный удар (windup 0): сразу хвост, окно = весь интервал (кулдаун только что взведён).
                _recoveryGapTicks = Mathf.Max(1, _unit != null ? _unit.AttackCooldownTicks : 1);
                _attackPhase = AttackAnimPhase.Recovery;
            }
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
            if (!_animActive || _state != UnitAnimationState.Attack) return;
            _holdHitFrame  = true;
            _holdRemaining = seconds;
        }

        // Застываем на кадре контакта (маркер атаки), пока идёт финальный slowmo; на unscaled-времени —
        // держим ровно столько же, сколько slowmo. По истечении «момента» — доигрываем и оседаем в Idle.
        private void DriveHoldHitFrame()
        {
            _animator.speed = 0f;
            _animator.Play(AttackHash, 0, _attackMarkerNormalized);
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
            if (_state == UnitAnimationState.Attack)
            {
                AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == AttackHash && info.normalizedTime < 1f) return;
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
            if (_sprite == null || _unit == null) return;

            // Пока идёт цикл атаки — целимся в цель (приоритет над движением): стрелок смотрит на врага,
            // даже отступая. Нет цели — падаем на разворот по движению ниже.
            if (_attackPhase != AttackAnimPhase.None && FaceTarget(_unit.CurrentTarget)) return;

            float moveDx = _unit.Position.x - _unit.PreviousPosition.x;
            _facingVelX = Mathf.Lerp(_facingVelX, moveDx, 0.2f);

            if (Mathf.Abs(_facingVelX) > FacingMoveEpsilonX)
            {
                _sprite.flipX = _facingVelX < 0f; // бежим влево → отражаем
                return;
            }

            FaceTarget(_unit.CurrentTarget); // стоим — смотрим на цель
        }

        // Развернуть спрайт к цели по X. false = цели нет/мертва (разворот не сделан → фолбэк на движение).
        // Почти вертикаль (в мёртвой зоне) считаем «уже повёрнут» — не дёргаем и не проваливаемся в движение.
        private bool FaceTarget(RuntimeUnit target)
        {
            if (target == null || target.IsDead) return false;
            float dx = target.Position.x - _unit.Position.x;
            if (Mathf.Abs(dx) >= FacingTargetDeadzoneX) _sprite.flipX = dx < 0f;
            return true;
        }

        // Тинт тела (умножается на текстуру в шейдере) + альфа инвиза (dev, §10.5: тег Stealth → полупрозрачность).
        // Вспышка попадания идёт НЕ здесь, а через _FlashAmount материала (ApplyFlash) — .color осветлить не может.
        private void ApplyColor()
        {
            if (_sprite == null) return;
            Color c = _baseTint;
            bool stealthed = _unit != null && (_unit.EffectTagMask & EffectTag.Stealth) != 0;
            c.a = stealthed ? 0.4f : 1f;
            _sprite.color = c;
            ApplyFlash();
        }

        // Вспышка через MaterialPropertyBlock (per-instance _FlashAmount, без клонирования материала).
        // Пишем блок, только пока вспышка активна, + один раз чтобы вернуть в 0 (не ломаем SRP-батчинг зря).
        private void ApplyFlash()
        {
            if (_sprite == null) return;
            bool active = _flashAmount > 0.0001f;
            if (!active && !_flashApplied) return;

            _mpb ??= new MaterialPropertyBlock();
            _sprite.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FlashAmountId, _flashAmount);
            _mpb.SetColor(FlashColorId, _feel != null ? _feel.FlashColor : Color.white);
            _sprite.SetPropertyBlock(_mpb);
            _flashApplied = active;
        }

        /// <summary>Вызывается при получении урона: локальная реакция цели (вспышка + сплющивание).</summary>
        public void OnDamageReceived(float damage)
        {
            _onHitFeedback?.Invoke();
            PlayHitFlash();
            PlayHitSquash();
        }

        /// <summary>Заморозить анимацию этого вида на unscaled-окно (локальный hitstop участника удара).</summary>
        public void OnHitstop(float unscaledSeconds)
        {
            if (unscaledSeconds <= 0f) return;
            _hitstopRemaining = Mathf.Max(_hitstopRemaining, unscaledSeconds);
        }

        // Вспышка белым: подмешиваем _flashAmount 1→0, ApplyColor рисует. Bind со state (this) — zero-alloc.
        private void PlayHitFlash()
        {
            if (_sprite == null) return;
            if (_flashHandle.IsActive()) _flashHandle.Cancel();
            _flashAmount = 1f;
            float dur = _feel != null ? _feel.FlashDuration : 0.25f;
            // Линейный спад: вспышка держится и ровно гаснет (OutQuad сваливал её в первые 1-2 кадра → «миг»).
            _flashHandle = LMotion.Create(1f, 0f, dur)
                .WithEase(Ease.Linear)
                .Bind(this, static (v, self) => self._flashAmount = v)
                .AddTo(gameObject);
        }

        // Сплющивание: на пике (v=1) X растягивается, Y сжимается на _hitSquashAmount; линейно возвращается
        // к базе (v→0). Linear, а не Out* — тот сваливал весь эффект в первые 1-2 кадра («слабо/мгновенно»).
        // Крутим _squashTarget (узел выше Animator), иначе кадровая анимация тела затирает scale.
        private void PlayHitSquash()
        {
            if (_squashTarget == null) return;
            if (_squashHandle.IsActive()) _squashHandle.Cancel();
            float dur = _feel != null ? _feel.SquashDuration : 0.25f;
            _squashHandle = LMotion.Create(1f, 0f, dur)
                .WithEase(Ease.Linear)
                .Bind(this, static (v, self) =>
                {
                    float amount = self._feel != null ? self._feel.SquashAmount : 0.4f;
                    float a = amount * v;
                    self._squashTarget.localScale = new Vector3(
                        self._baseSpriteScale.x * (1f + a),
                        self._baseSpriteScale.y * (1f - a),
                        self._baseSpriteScale.z);
                })
                .AddTo(gameObject);
        }

        /// <summary>Вызывается при гибели юнита.</summary>
        public void OnDeath()
        {
            _onDeathFeedback?.Invoke();

            if (_animActive)
            {
                _isDead = true;
                AnimationClip death = _visual.Clip(UnitAnimationState.Death);
                _deathRemaining = death != null ? death.length : 0f;
                if (_deathRemaining <= 0f) gameObject.SetActive(false); // нет death-клипа → прячемся сразу
            }
            else
            {
                gameObject.SetActive(false); // статичный фолбэк — прежнее поведение
            }
        }

#if UNITY_EDITOR
        // Гизмо (редактор): показываем, когда выбран сам UnitView ИЛИ любой его дочерний GO
        // (Body, точки, бары) — чтобы при настройке видеть всё сразу. Ранний выход, если выбрана
        // чужая иерархия, — в бою не мусорит. Линейка роста стоит от КОРНЯ (точки земли/ног в симе,
        // y=0): сюда ставь ноги спрайта, и макушка должна доставать до верхней засечки.
        private void OnDrawGizmos()
        {
            var sel = UnityEditor.Selection.activeGameObject;
            bool related = sel != null && sel.transform.IsChildOf(transform);
            bool inPrefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null;
            if (!related && !inPrefabStage) return;

            Vector3 root   = transform.position;                                 // сим-позиция юнита
            Vector3 feet   = _feetPoint != null ? _feetPoint.position : root;     // низ фигуры (ноги)

            // --- Круг коллизии сима (радиус = Size × SimTuning.Default.BodyRadiusPerSize) в НОГАХ. Тумблер. ---
            // В рантайме сим считает коллизию в unit.Position; презентация ставит юнита так, чтобы Feet Point
            // попал в неё (офсет спавна — фаза коллизии), поэтому центр здесь = feet.
            if (_showCollisionGizmo)
            {
                float size = Application.isPlaying && _unit != null
                    ? Mathf.Max(0.01f, _unit.Stats.Get(Data.Stats.StatType.Size))
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
