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
        [Tooltip("Бар ресурса (мана/ярость). Пусто = без бара; скрывается сам для безресурсных юнитов.")]
        [SerializeField] private ManaBarView    _manaBar;

        [Header("Animation (S4) — опционально; пусто = статичный спрайт")]
        [SerializeField] private UnitVisual _visual;

        [Header("Feel Hooks (подключить MMF_Player в Inspector)")]
        [SerializeField] private UnityEvent _onHitFeedback;
        [SerializeField] private UnityEvent _onDeathFeedback;

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

        [Header("Gizmo — рекомендованный рост (только редактор, ни на что не влияет)")]
        [Tooltip("Эталонная высота юнита в мировых юнитах (1 юнит = 1 метр). Рисуется линейкой с подписью — " +
                 "ставь размер спрайта вручную под неё.")]
        [SerializeField] private float _recommendedHeight = 1.7f;
        [Tooltip("Превью Size для гизмо круга коллизии, когда юнит ещё не заспавнен (рантайм берёт настоящий Size).")]
        [SerializeField] private float _gizmoPreviewSize = 1f;
        [Tooltip("Показывать оранжевый круг коллизии симуляции (радиус Size×0.25). Выключи, если мешает.")]
        [SerializeField] private bool _showCollisionGizmo = true;

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

            if (_manaBar != null)
                _manaBar.Bind(unit);
        }

        /// <summary>Тинт тела юнита: один общий спрайт, разный цвет на персонажа (dev-харнесс, «пока один спрайт»).</summary>
        public void SetTint(Color color)
        {
            if (_sprite != null) _sprite.color = color;
        }

        /// <summary>Подпись «что за персонаж» над HP-баром. Задаёт лишь текст — вид настраивается на TMP в префабе.</summary>
        public void SetLabel(string text)
        {
            if (_nameLabel != null) _nameLabel.text = text;
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

            if (_manaBar != null)
                _manaBar.UpdateBar(_unit.CurrentResource, _unit.Stats.Get(Data.Stats.StatType.MaxResource));
        }

        // --- Attach points (сокеты) ----------------------------------------------------------------
        // Размер вида задаётся СТАТИКОЙ (масштаб узла 'Sprite Visual' или PPU арта), не рантаймом.
        // Сокеты — пустые GO под 'Sprite Visual', презентация читает их мировые позиции для спавна
        // снаряда/цифр/вспышек. Фолбэк на позицию юнита, если сокет не назначен.

        /// <summary>Мировая точка ног (касание земли). Фолбэк — позиция юнита.</summary>
        public Vector3 FeetPoint => _feetPoint != null ? _feetPoint.position : transform.position;

        /// <summary>Мировая точка головы (макушка) — якорь баров/статус-текста. Фолбэк — позиция юнита.</summary>
        public Vector3 HeadPoint => _headPoint != null ? _headPoint.position : transform.position;

        /// <summary>Мировая точка выстрела (откуда визуально стартует снаряд/каст). Фолбэк — позиция юнита.</summary>
        public Vector3 ShotPoint => _shotPoint != null ? _shotPoint.position : transform.position;

        /// <summary>Мировая точка попадания (куда прилетают снаряды/цифры урона). Фолбэк — позиция юнита.</summary>
        public Vector3 HitPoint => _hitPoint != null ? _hitPoint.position : transform.position;

        // Кадровая анимация листается по рендер-времени — независимо от 30-Гц сим-тика.
        private void Update()
        {
            ApplyStealthAlpha(); // dev-подсветка инвиза — до guard'а, работает и без набора кадров

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

        // Инвиз (dev, §10.5): пока висит тег Stealth — тело полупрозрачно; иначе непрозрачно.
        // Перекрывает альфу из SetTint (RGB сохраняется), поллится каждый кадр.
        private void ApplyStealthAlpha()
        {
            if (_sprite == null || _unit == null) return;
            bool stealthed = (_unit.EffectTagMask & EffectTag.Stealth) != 0;
            Color c = _sprite.color;
            c.a = stealthed ? 0.4f : 1f;
            _sprite.color = c;
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
            Vector3 rightN = transform.right;

            // --- Линейка рекомендованного роста: от НОГ вверх на _recommendedHeight ---
            float rec = Mathf.Max(0.01f, _recommendedHeight);
            var green = new Color(0.35f, 0.95f, 0.55f, 0.95f);
            Vector3 rBot = feet - rightN * 0.7f;
            Vector3 rTop = rBot + Vector3.up * rec;
            Gizmos.color = green;
            Gizmos.DrawLine(rBot, rTop);
            Gizmos.DrawLine(rBot - rightN * 0.1f, rBot + rightN * 0.1f); // засечка низа (= ноги)
            Gizmos.DrawLine(rTop - rightN * 0.1f, rTop + rightN * 0.1f); // засечка верха (= рек. рост)
            // Тонкие горизонтальные ориентиры ног и макушки — сквозь фигуру, чтобы видеть где 0 и где рост.
            Gizmos.color = new Color(green.r, green.g, green.b, 0.22f);
            Gizmos.DrawLine(feet - rightN * 0.7f, feet + rightN * 0.7f);
            Gizmos.DrawLine((feet + Vector3.up * rec) - rightN * 0.7f, (feet + Vector3.up * rec) + rightN * 0.7f);
            UnityEditor.Handles.color = green;
            UnityEditor.Handles.Label(rTop + Vector3.up * 0.06f, "рек. рост " + rec.ToString("0.##") + " м");

            // --- Круг коллизии сима (радиус = Size × 0.25) в НОГАХ (Feet Point), как и линейка. Тумблер. ---
            // В рантайме сим считает коллизию в unit.Position; презентация ставит юнита так, чтобы Feet Point
            // попал в неё (офсет спавна — фаза коллизии), поэтому центр здесь = feet.
            if (_showCollisionGizmo)
            {
                float size = Application.isPlaying && _unit != null
                    ? Mathf.Max(0.01f, _unit.Stats.Get(Data.Stats.StatType.Size))
                    : Mathf.Max(0.01f, _gizmoPreviewSize);
                float cr = size * 0.25f;
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
