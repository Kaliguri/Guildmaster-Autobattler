using System.Collections;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using MessagePipe;
using UnityEngine;
using UnityEngine.Pool;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Мост «симуляция → презентация». Подписывается на C#-события <see cref="CombatSimulation"/>,
    /// спавнит/деспавнит <see cref="UnitView"/> и ретранслирует события в MessagePipe, чтобы
    /// Audio/VFX/UI подписывались независимо от симуляции (вики «10» §7, стек — CLAUDE.md).
    /// Только читает состояние симуляции — никогда не мутирует.
    /// </summary>
    public sealed class CombatPresenter : MonoBehaviour
    {
        [Tooltip("Префаб вида юнита.")]
        [SerializeField] private UnitView _unitViewPrefab;

        [Tooltip("Префаб вида снаряда (Bullet). Пусто = снаряды не визуализируются (сим не меняется).")]
        [SerializeField] private ProjectileView _bulletPrefab;

        [Header("Свои боевые цифры (урон/хил) — один префаб, цвет задаётся здесь")]
        [Tooltip("Общий префаб всплывающей цифры (несёт FloatingText; размер/шрифт/тайминг — на префабе).")]
        [SerializeField] private GameObject _floatingTextPrefab;
        [Tooltip("Цвет цифры урона по HP (-N).")]
        [SerializeField] private Color _damageColor = new Color(1f, 0.75f, 0.2f);
        [Tooltip("Цвет цифры лечения (+N).")]
        [SerializeField] private Color _healColor = new Color(0.5f, 1f, 0.6f);
        [Tooltip("Цвет цифры урона по щиту (-N).")]
        [SerializeField] private Color _shieldColor = new Color(0.4f, 0.7f, 1f);
        [Tooltip("Цвет надписи «evade» при полном негейте удара.")]
        [SerializeField] private Color _evadeColor = new Color(0.85f, 0.9f, 0.95f);
        [Tooltip("Задержка между цифрой щита и цифрой HP при сплите (сек).")]
        [SerializeField] private float _splitDelay = 0.06f;

        [Header("Дизайн-система (цвета боевого UI)")]
        [Tooltip("Палитра цветов боя (первый SO дизайн-системы). Задаёт цвет HP-бара по принадлежности к " +
                 "смотрящему. Пусто = фолбэк-цвета по умолчанию (см. DefaultHealthColor).")]
        [SerializeField] private Design.CombatColorPalette _colorPalette;

        [Tooltip("Команда «смотрящего» (локального игрока): его юниты — союзные (ally-цвет), прочие — enemy. " +
                 "Шов под кооп (там смотрящий может быть в любой команде); пока 0 = команда игрока.")]
        [SerializeField] private int _localViewerTeam;

        private CombatSimulation            _simulation;
        private readonly Dictionary<int, UnitView>       _views     = new Dictionary<int, UnitView>();
        private readonly Dictionary<int, ProjectileView> _projViews = new Dictionary<int, ProjectileView>();
        private readonly List<int>                       _deadProj  = new List<int>();
        // Виды погибших юнитов: сняты из _views (перестают следовать за симом), но GameObject живёт, пока идёт
        // секвенс смерти (death-клип → разлёт). Держим отдельно, чтобы гарантированно снести их при рестарте —
        // иначе трупы прошлого боя остаются висеть в новом («телепортируются»).
        private readonly List<UnitView>                  _corpses   = new List<UnitView>();

        private ObjectPool<FloatingText>    _textPool;
        private System.Action<FloatingText> _releaseText;
        private CombatStatusOverlay         _statusOverlay;
        private CombatVfx                   _vfx;               // пул пиксельных VFX-брызгов
        private RuntimeUnit                 _finisherCandidate; // автор последнего добивающего мили-удара

        private IPublisher<UnitSpawnedEvent> _unitSpawnedPublisher;
        private IPublisher<UnitDiedEvent>    _unitDiedPublisher;
        private IPublisher<DamageDealtEvent> _damageDealtPublisher;
        private IPublisher<BattleEndedEvent> _battleEndedPublisher;

        // Все feel-параметры (hitstop, финишер, вспышка/сплющивание вью) — из design-конфига (единый источник).
        private Design.CombatFeelConfig _feel;

        [Inject]
        public void Construct(
            CombatSimulation simulation,
            IPublisher<UnitSpawnedEvent> unitSpawnedPublisher,
            IPublisher<UnitDiedEvent>    unitDiedPublisher,
            IPublisher<DamageDealtEvent> damageDealtPublisher,
            IPublisher<BattleEndedEvent> battleEndedPublisher,
            Design.CombatFeelConfig feel)
        {
            _simulation           = simulation;
            _unitSpawnedPublisher = unitSpawnedPublisher;
            _unitDiedPublisher    = unitDiedPublisher;
            _damageDealtPublisher = damageDealtPublisher;
            _battleEndedPublisher = battleEndedPublisher;
            _feel                 = feel;
        }

        /// <summary>
        /// Вид живого юнита по его Id (единственная карта Id→вид). Нужен фазе расстановки (шаг 4), чтобы
        /// нарисовать outline на наведённом/выбранном юните по его мировой позиции.
        /// </summary>
        public bool TryGetView(int unitId, out UnitView view) => _views.TryGetValue(unitId, out view);

        private void OnEnable()
        {
            if (_simulation == null) return;
            _simulation.OnUnitSpawned       += HandleUnitSpawned;
            _simulation.OnUnitDied          += HandleUnitDied;
            _simulation.OnDamageDealt       += HandleDamageDealt;
            _simulation.OnHealed            += HandleHealed;
            _simulation.OnAttackEvaded      += HandleAttackEvaded;
            _simulation.OnBattleEnded       += HandleBattleEnded;
            _simulation.OnAttackStarted     += HandleAttackStarted;
            _simulation.OnAttackInterrupted += HandleAttackInterrupted;
            _simulation.OnProjectileSpawned += HandleProjectileSpawned;
            _simulation.OnBattleReset       += HandleBattleReset;

            EnsureStatusOverlay();
            EnsureVfx();
        }

        private void OnDisable()
        {
            if (_simulation == null) return;
            _simulation.OnUnitSpawned       -= HandleUnitSpawned;
            _simulation.OnUnitDied          -= HandleUnitDied;
            _simulation.OnDamageDealt       -= HandleDamageDealt;
            _simulation.OnHealed            -= HandleHealed;
            _simulation.OnAttackEvaded      -= HandleAttackEvaded;
            _simulation.OnBattleEnded       -= HandleBattleEnded;
            _simulation.OnAttackStarted     -= HandleAttackStarted;
            _simulation.OnAttackInterrupted -= HandleAttackInterrupted;
            _simulation.OnProjectileSpawned -= HandleProjectileSpawned;
            _simulation.OnBattleReset       -= HandleBattleReset;
        }

        // Перезапуск боя на месте (dev-R): снимаем все виды юнитов и снарядов и чистим летящие цифры.
        // Slowmo/тряску сбрасывает CombatFeelDirector (тоже по OnBattleReset — у него есть TimeScaleService/шейк;
        // презентер в другой сборке и до них не дотянется без цикла asmdef). Статус-кольца (CombatStatusOverlay)
        // само-гаснут, когда юнитов нет. Сцена/камера не трогаются; новый сетап заспавнит юнитов через OnUnitSpawned.
        private void HandleBattleReset()
        {
            foreach (var kvp in _views)
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            _views.Clear();

            foreach (var kvp in _projViews)
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            _projViews.Clear();

            // Трупы прошлого боя (виды в секвенсе смерти, снятые из _views) — иначе висят в новом бою.
            for (int i = 0; i < _corpses.Count; i++)
                if (_corpses[i] != null) Destroy(_corpses[i].gameObject);
            _corpses.Clear();

            _finisherCandidate = null;

            // Летящие боевые цифры (урон/хил) — прервать и вернуть в пул, иначе висят после рестарта.
            if (_textPool != null)
            {
                var texts = GetComponentsInChildren<FloatingText>(includeInactive: false);
                for (int i = 0; i < texts.Length; i++) texts[i].Cancel();
            }
        }

        /// <summary>Создать dev-слой статус-колец в рантайме (без правок сцены/префабов) и подать симуляцию.</summary>
        private void EnsureStatusOverlay()
        {
            if (_statusOverlay != null) return;
            var go = new GameObject("CombatStatusOverlay");
            go.transform.SetParent(transform, worldPositionStays: false);
            _statusOverlay = go.AddComponent<CombatStatusOverlay>();
            _statusOverlay.Initialize(_simulation);
        }

        /// <summary>Создать пул пиксельных VFX-брызгов в рантайме (без правок сцены/префабов).</summary>
        private void EnsureVfx()
        {
            if (_vfx != null) return;
            var go = new GameObject("CombatVfx");
            go.transform.SetParent(transform, worldPositionStays: false);
            _vfx = go.AddComponent<CombatVfx>();
            _vfx.Initialize();
        }

        private void Update()
        {
            float alpha = Time.deltaTime / Guildmaster.Core.Simulation.SimConstants.TickDelta;
            alpha = UnityEngine.Mathf.Clamp01(alpha);

            foreach (var kvp in _views)
            {
                kvp.Value.UpdateInterpolation(alpha);
            }

            // Снаряды: следуем за ссылкой на симовый Projectile; когда он исчез (попал/вышел за поле) —
            // вид снапнулся в точку удара и вернул false → уничтожаем (импакт совпал с цифрой урона).
            if (_projViews.Count > 0)
            {
                _deadProj.Clear();
                foreach (var kvp in _projViews)
                    if (!kvp.Value.Tick(alpha)) _deadProj.Add(kvp.Key);

                for (int i = 0; i < _deadProj.Count; i++)
                {
                    if (_projViews.TryGetValue(_deadProj[i], out var pv) && pv != null) Destroy(pv.gameObject);
                    _projViews.Remove(_deadProj[i]);
                }
            }
        }

        private void HandleProjectileSpawned(Projectile projectile)
        {
            if (_bulletPrefab == null || projectile == null) return;

            // Визуальный старт — из ShotPoint (дула) источника, если его вид есть; иначе из позиции сима.
            Vector3 origin = (Vector3)(Vector2)projectile.Position;
            if (projectile.Source != null && _views.TryGetValue(projectile.Source.Id, out var srcView) && srcView != null)
            {
                origin = srcView.ShotPoint;

                // Muzzle-вспышка из дула по направлению полёта снаряда.
                if (_vfx != null && _feel != null)
                {
                    Vector2 vel = projectile.Velocity;
                    float ang = vel.sqrMagnitude > 1e-6f ? Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg : 0f;
                    _vfx.SpawnBurst(srcView.ShotPoint, ang, _feel.Muzzle, 1f, srcView.BodySortingLayerId, srcView.BodySortingOrder + 5);
                }
            }

            var view = Instantiate(_bulletPrefab, origin, Quaternion.identity, transform);
            // Тинт снаряда = цвет юнита-источника (тот же метод, что и тело юнита).
            Color tint = projectile.Source != null ? TintFor(projectile.Source) : Color.white;
            view.Bind(projectile, tint, origin);
            _projViews[projectile.Id] = view;
        }

        private void HandleUnitSpawned(RuntimeUnit unit)
        {
            // Свой префаб персонажа (визуал/анимация/размер настроены ПРЯМО в нём); фолбэк — дефолтный
            // из презентера. Никакой рантайм-подмены визуала — префаб самодостаточен.
            GameObject prefabGo = unit.Unit != null && unit.Unit.ViewPrefab != null
                ? unit.Unit.ViewPrefab
                : (_unitViewPrefab != null ? _unitViewPrefab.gameObject : null);

            if (prefabGo != null)
            {
                var go = Instantiate(prefabGo, (Vector3)(Vector2)unit.Position, Quaternion.identity, transform);
                if (go.TryGetComponent(out UnitView view))
                {
                    view.Bind(unit);
                    view.ApplyFeelConfig(_feel); // параметры вспышки/сплющивания — из design-конфига

                    // Тинт тела по персонажу (dev-различение, пока placeholder-спрайт) + подпись над HP-баром.
                    view.SetTint(TintFor(unit));
                    view.SetLabel(NameFor(unit));

                    // Цвет HP-бара по принадлежности к смотрящему (дизайн-система).
                    bool isAllyOfViewer = unit.Team == _localViewerTeam;
                    view.SetHealthColor(_colorPalette != null
                        ? _colorPalette.HealthBarColor(isAllyOfViewer)
                        : DefaultHealthColor(isAllyOfViewer));

                    // Цвет щита — общий из палитры (не зависит от принадлежности).
                    if (_colorPalette != null)
                        view.SetShieldColor(_colorPalette.Shield);

                    _views[unit.Id] = view;
                }
                else Destroy(go);
            }

            _unitSpawnedPublisher.Publish(new UnitSpawnedEvent(unit));
        }

        private void HandleUnitDied(RuntimeUnit unit)
        {
            if (_views.TryGetValue(unit.Id, out var view))
            {
                view.OnDeath();
                _views.Remove(unit.Id);
                _corpses.Add(view); // труп доигрывает секвенс смерти сам; сносим гарантированно при рестарте
            }

            _unitDiedPublisher.Publish(new UnitDiedEvent(unit));
        }

        private void HandleDamageDealt(RuntimeUnit source, RuntimeUnit target, DamageResult result)
        {
            // Урон совпадает с кадром контакта (конец замаха): здесь — импакт-фидбэк цели.
            // Свинг источника запускается раньше, на OnAttackStarted (вики «14»).
            if (_views.TryGetValue(target.Id, out var view))
                view.OnDamageReceived(result.TotalDamage);

            // Доля HP-урона от MaxHP цели — общий «вес удара» для hitstop и размера цифры.
            float maxHp = target.Stats.Get(Data.Stats.StatType.MaxHP);
            float frac  = maxHp > 0f ? result.HpDamage / maxHp : 0f;

            // Локальный hitstop пары «источник + цель» по значимости удара.
            if (view != null)
            {
                float stop = Mathf.Lerp(_feel.HitstopMin, _feel.HitstopMax, Mathf.Clamp01(frac / _feel.HitstopFullFrac));
                view.OnHitstop(stop);
                if (source != null && _views.TryGetValue(source.Id, out var sourceView))
                    sourceView.OnHitstop(stop);
            }

            // Кандидат в финишеры: автор добивающего удара, если он мили (снаряд/яд позу удара не держат).
            if (result.KilledTarget)
                _finisherCandidate = (source?.Unit != null && source.Unit.AttackType == AttackType.Melee) ? source : null;

            int shield = Mathf.RoundToInt(result.ShieldDamage);
            int hp     = Mathf.RoundToInt(result.HpDamage);

            // Цифры — в точку попадания (грудь) цели. Размер HP-цифры растёт с весом удара (тяжёлый = крупнее).
            Vector3 anchor  = AnchorFor(target);
            float   hpScale = Mathf.Lerp(1f, _feel.NumberMaxScale, Mathf.Clamp01(frac / Mathf.Max(1e-4f, _feel.NumberFullFrac)));

            // Пиксельные VFX: искры в точку попадания (кол-во по весу удара) + пыль у ног на мили-ударе.
            if (_vfx != null && _feel != null && view != null)
            {
                float intensity = 0.35f + 0.65f * Mathf.Clamp01(frac / Mathf.Max(1e-4f, _feel.HeavyHitFrac));
                _vfx.SpawnBurst(anchor, 0f, _feel.HitSpark, intensity, view.BodySortingLayerId, view.BodySortingOrder + 5);

                bool melee = source?.Unit != null && source.Unit.AttackType == AttackType.Melee;
                if (melee)
                    _vfx.SpawnBurst(view.FeetPoint, 90f, _feel.ImpactDust, 1f, view.BodySortingLayerId, view.BodySortingOrder - 1);
            }

            // Урон по щиту — синим «-N»; по HP — «-N» цветом урона. Если задет и щит, и HP —
            // цифра щита сразу, цифра HP через очень маленькую задержку (обе читаемы).
            if (shield > 0) SpawnNumber(anchor, "-" + shield, _shieldColor);
            if (hp > 0)
            {
                if (shield > 0) StartCoroutine(DelayedNumber(anchor, "-" + hp, _damageColor, _splitDelay, hpScale));
                else            SpawnNumber(anchor, "-" + hp, _damageColor, hpScale);
            }

            _damageDealtPublisher.Publish(new DamageDealtEvent(source, target, result));
        }

        private void HandleHealed(RuntimeUnit source, RuntimeUnit target, float amount)
        {
            // Хил-цифра в точку попадания цели (+N). Мелкие тики регена округляются в 0 и не спамят.
            int healed = Mathf.RoundToInt(amount);
            if (healed <= 0) return;

            SpawnNumber(AnchorFor(target), "+" + healed, _healColor);

            // Пиксельные хил-искры (восходящие, зелёные) в точку попадания.
            if (_vfx != null && _feel != null && _views.TryGetValue(target.Id, out var tView) && tView != null)
                _vfx.SpawnBurst(tView.HitPoint, 90f, _feel.Heal, 1f, tView.BodySortingLayerId, tView.BodySortingOrder + 5);
        }

        private void HandleAttackEvaded(RuntimeUnit target)
        {
            // Полный негейт удара («Изворотливость») — урона нет, показываем «evade».
            SpawnNumber(AnchorFor(target), "evade", _evadeColor);
        }

        private IEnumerator DelayedNumber(Vector3 worldPosition, string text, Color color, float delay, float sizeScale = 1f)
        {
            yield return new WaitForSeconds(delay);
            SpawnNumber(worldPosition, text, color, sizeScale);
        }

        /// <summary>Заспавнить свою всплывающую боевую цифру в мировой точке (через пул). sizeScale — размер по величине удара.</summary>
        private void SpawnNumber(Vector3 worldPosition, string text, Color color, float sizeScale = 1f)
        {
            EnsureTextPool();
            if (_textPool == null) return;

            FloatingText ft = _textPool.Get();
            ft.transform.position = worldPosition;
            ft.Play(text, color, sizeScale, _releaseText);
        }

        /// <summary>Мировая точка для боевой цифры: HitPoint вида цели (грудь); фолбэк — над позицией сима.</summary>
        private Vector3 AnchorFor(RuntimeUnit target)
        {
            if (_views.TryGetValue(target.Id, out var v) && v != null)
                return v.HitPoint;
            return (Vector3)(Vector2)target.Position + Vector3.up * 0.4f;
        }

        /// <summary>Лениво собрать пул всплывающих цифр из префаба (zero-alloc в бою, пункт QA #5).</summary>
        private void EnsureTextPool()
        {
            if (_textPool != null || _floatingTextPrefab == null) return;
            if (!_floatingTextPrefab.TryGetComponent(out FloatingText _)) return;

            _releaseText = ft => _textPool.Release(ft);
            _textPool = new ObjectPool<FloatingText>(
                createFunc: () => Instantiate(_floatingTextPrefab, transform).GetComponent<FloatingText>(),
                actionOnGet: ft => ft.gameObject.SetActive(true),
                actionOnRelease: ft => ft.gameObject.SetActive(false),
                actionOnDestroy: ft => { if (ft != null) Destroy(ft.gameObject); }, // ft может быть уже уничтожен при teardown play
                collectionCheck: false,
                defaultCapacity: 16,
                maxSize: 64);
        }

        /// <summary>
        /// Тинт тела по персонажу. У юнита с данными — ЕДИНЫЙ резолвер <see cref="UnitData.ResolveBodyTint"/>
        /// (тот же цвет, что рендерит карточка инвентаря); у болванчиков без данных — по команде.
        /// </summary>
        private static Color TintFor(RuntimeUnit unit) =>
            unit.Unit != null
                ? unit.Unit.ResolveBodyTint()
                : (unit.Team == 0 ? new Color(0.7f, 0.8f, 1f) : new Color(1f, 0.7f, 0.7f));

        /// <summary>Фолбэк-цвет HP-бара, если палитра дизайн-системы не назначена (совпадает с дефолтами SO).</summary>
        private static Color DefaultHealthColor(bool isAllyOfViewer) => isAllyOfViewer
            ? new Color(0.30f, 0.85f, 0.35f)
            : new Color(0.90f, 0.25f, 0.25f);

        /// <summary>Подпись персонажа: имя реликвии (SO) либо «Ally/Enemy N» для болванчиков.</summary>
        private static string NameFor(RuntimeUnit unit)
        {
            if (unit.Unit != null) return unit.Unit.name;
            return (unit.Team == 0 ? "Ally " : "Enemy ") + unit.Id;
        }

        private void HandleAttackStarted(RuntimeUnit source, RuntimeUnit target)
        {
            // Вход в замах: запускаем анимацию свинга у источника (вики «14»).
            if (source != null && _views.TryGetValue(source.Id, out var sourceView))
                sourceView.OnAttackStarted();
        }

        private void HandleAttackInterrupted(RuntimeUnit unit)
        {
            if (unit != null && _views.TryGetValue(unit.Id, out var view))
                view.OnAttackInterrupted();
        }

        private void HandleBattleEnded(BattleOutcome outcome)
        {
            // Финишер-мили держит кадр контакта весь финальный slowmo (перекрывает free-run у него).
            if (_finisherCandidate != null && _views.TryGetValue(_finisherCandidate.Id, out var finisher) && finisher != null)
                finisher.HoldHitFrame(_feel.FinisherHoldSeconds);

            // Живые (в _views мёртвые уже удалены) доигрывают анимации натурально, а не виснут на замершем симе.
            foreach (var kvp in _views)
                if (kvp.Value != null) kvp.Value.OnBattleEnded();

            Debug.Log($"[CombatPresenter] - Бой завершён: {outcome}");

            _battleEndedPublisher.Publish(new BattleEndedEvent(outcome));
        }
    }
}
