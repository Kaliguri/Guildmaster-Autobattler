using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using UnityEngine;
using UnityEngine.Events;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// World-space визуальное представление <see cref="RuntimeUnit"/>.
    /// Интерполирует позицию между тиками (сим 30 Hz, рендер 60+ fps) и проигрывает
    /// кадровую анимацию (S4, вики «13» §5), развязанную с сим-тиком: состояние выбирает
    /// чистый <see cref="UnitAnimationSelector"/> по наблюдаемому состоянию сима + событиям тика,
    /// кадры листаются по рендер-времени. Анимация НИКОГДА не пишет в сим (урон уже на тике).
    /// Feel-хуки — <see cref="UnityEvent"/> поля (подключается MMF_Player в Inspector).
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        private const float MoveEpsilonSq = 1e-6f;

        [Header("Components")]
        [SerializeField] private SpriteRenderer _sprite;
        [SerializeField] private HealthBarView  _healthBar;

        [Header("Animation (S4) — опционально; пусто = статичный спрайт")]
        [SerializeField] private UnitVisual _visual;

        [Header("Feel Hooks (подключить MMF_Player в Inspector)")]
        [SerializeField] private UnityEvent _onHitFeedback;
        [SerializeField] private UnityEvent _onDeathFeedback;

        private RuntimeUnit _unit;
        private Vector2     _renderPosition;

        // --- Состояние анимации (рендер-сторона, не влияет на сим) ---
        private enum AttackPhase { None, Windup, FollowThrough }

        private UnitAnimationState _state = UnitAnimationState.Idle;
        private int   _frameIndex;
        private float _frameTimer;
        private AttackPhase _attackPhase;
        private float _followRemaining;   // сек, фоллоу-сру кадров после контакта
        private bool  _isDead;
        private float _deathRemaining;

        /// <summary>Связать вид с рантайм-юнитом.</summary>
        public void Bind(RuntimeUnit unit)
        {
            _unit           = unit;
            _renderPosition = unit.Position;
            transform.position = (Vector3)_renderPosition;

            // Команда 0 смотрит вправо (как нарисованы спрайты), команда 1 — влево.
            if (_sprite != null) _sprite.flipX = unit.Team == 1;

            if (_healthBar != null)
                _healthBar.Bind(unit);
        }

        /// <summary>
        /// Подменить визуал в рантайме (пер-юнит визуал из реликвии, вики «13» шаг 4): все юниты
        /// инстанцируются из одного префаба, но реликвия может задать свой набор кадров.
        /// </summary>
        public void SetVisual(UnitVisual visual)
        {
            _visual     = visual;
            _state      = UnitAnimationState.Idle;
            _frameIndex = 0;
            _frameTimer = 0f;

            if (_visual != null && _sprite != null)
            {
                Sprite[] idle = _visual.Frames(UnitAnimationState.Idle);
                if (idle.Length > 0) _sprite.sprite = idle[0];
            }
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
        }

        // Кадровая анимация листается по рендер-времени — независимо от 30-Гц сим-тика.
        private void Update()
        {
            if (_visual == null || _sprite == null) return;

            float dt = Time.deltaTime;
            UpdateAttackPhase(dt);

            bool isMoving = _unit != null &&
                            (_unit.Position - _unit.PreviousPosition).sqrMagnitude > MoveEpsilonSq;

            bool attackPlaying = _attackPhase != AttackPhase.None;
            UnitAnimationState next = UnitAnimationSelector.Select(_isDead, attackPlaying, isMoving);
            if (next != _state)
            {
                _state      = next;
                _frameIndex = 0;
                _frameTimer = 0f;
            }

            StepFrames(dt);

            if (_isDead && _deathRemaining > 0f)
            {
                _deathRemaining -= dt;
                if (_deathRemaining <= 0f) gameObject.SetActive(false);
            }
        }

        // Управление фазой атаки от состояния сима (вики «14»): замах → кадр контакта → фоллоу-сру.
        private void UpdateAttackPhase(float dt)
        {
            if (_isDead) { _attackPhase = AttackPhase.None; return; }
            if (_unit == null) return;

            // Реконструкция после load/resync: сим в замахе, но события старта мы не видели.
            if (_attackPhase == AttackPhase.None && _unit.IsWindingUp)
                _attackPhase = AttackPhase.Windup;

            switch (_attackPhase)
            {
                case AttackPhase.Windup:
                    if (!_unit.IsWindingUp) BeginFollowThrough();   // замах кончился = кадр контакта
                    break;
                case AttackPhase.FollowThrough:
                    _followRemaining -= dt;
                    if (_followRemaining <= 0f) _attackPhase = AttackPhase.None;
                    break;
            }
        }

        // После кадра контакта доигрываем хвост клипа свободным ходом по _fps (косметика).
        private void BeginFollowThrough()
        {
            int count = _visual.AttackFrameCount;
            int hit   = Mathf.Clamp(_visual.AttackHitFrame, 0, Mathf.Max(0, count - 1));
            _frameIndex = hit;
            int after = Mathf.Max(0, count - 1 - hit);
            _followRemaining = after / _visual.Fps;
            _attackPhase = after > 0 ? AttackPhase.FollowThrough : AttackPhase.None;
        }

        private void StepFrames(float dt)
        {
            Sprite[] frames = _visual.Frames(_state);
            if (frames.Length == 0) return;

            if (_state == UnitAnimationState.Attack)
            {
                StepAttackFrames(frames, dt);
                return;
            }

            float frameDur = 1f / _visual.Fps;
            _frameTimer += dt;
            while (_frameTimer >= frameDur)
            {
                _frameTimer -= frameDur;
                _frameIndex++;
                if (_frameIndex >= frames.Length)
                {
                    bool looping = _state == UnitAnimationState.Idle || _state == UnitAnimationState.Run;
                    _frameIndex = looping ? 0 : frames.Length - 1;   // one-shot/death замирает на последнем кадре
                }
            }

            _sprite.sprite = frames[Mathf.Clamp(_frameIndex, 0, frames.Length - 1)];
        }

        // Кадры замаха привязаны к доле прошедшего windup → контакт (hitFrame) садится на конец замаха
        // = на сим-тик урона (OnDamageDealt). Хвост после контакта — свободный ход по _fps.
        private void StepAttackFrames(Sprite[] frames, float dt)
        {
            int hit = Mathf.Clamp(_visual.AttackHitFrame, 0, frames.Length - 1);

            if (_attackPhase == AttackPhase.Windup && _unit != null && _unit.WindupTicks > 0)
            {
                float progress = 1f - (float)_unit.WindupRemaining / _unit.WindupTicks;
                progress = Mathf.Clamp01(progress);
                _frameIndex = Mathf.Clamp(Mathf.FloorToInt(progress * hit), 0, hit);
            }
            else if (_attackPhase == AttackPhase.FollowThrough)
            {
                float frameDur = 1f / _visual.Fps;
                _frameTimer += dt;
                while (_frameTimer >= frameDur)
                {
                    _frameTimer -= frameDur;
                    if (_frameIndex < frames.Length - 1) _frameIndex++;
                }
            }

            _sprite.sprite = frames[Mathf.Clamp(_frameIndex, 0, frames.Length - 1)];
        }

        /// <summary>Юнит вошёл в замах авто-атаки (событие сима OnAttackStarted) — запускаем свинг.</summary>
        public void OnAttackStarted()
        {
            if (_visual == null) return;

            if (_unit != null && _unit.IsWindingUp && _unit.WindupTicks > 0)
            {
                _attackPhase = AttackPhase.Windup;
                _frameIndex  = 0;
                _frameTimer  = 0f;
            }
            else
            {
                BeginFollowThrough();   // мгновенный удар (windup 0) — сразу хвост
            }
        }

        /// <summary>Замах прерван (событие сима OnAttackInterrupted) — рвём свинг в idle.</summary>
        public void OnAttackInterrupted()
        {
            _attackPhase = AttackPhase.None;
            _frameIndex  = 0;
            _frameTimer  = 0f;
        }

        /// <summary>Вызывается при получении урона.</summary>
        public void OnDamageReceived(float damage)
        {
            _onHitFeedback?.Invoke();
        }

        /// <summary>Вызывается при гибели юнита.</summary>
        public void OnDeath()
        {
            _onDeathFeedback?.Invoke();

            if (_visual != null)
            {
                _isDead = true;
                _deathRemaining = _visual.Duration(UnitAnimationState.Death);
                if (_deathRemaining <= 0f) gameObject.SetActive(false);   // нет death-клипа → прячемся сразу
            }
            else
            {
                gameObject.SetActive(false);   // статичный фолбэк — прежнее поведение
            }
        }
    }
}
