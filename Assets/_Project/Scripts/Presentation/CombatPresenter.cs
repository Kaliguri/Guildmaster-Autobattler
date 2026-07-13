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

        [Tooltip("Пер-юнит визуалы по реликвии (вики «13» шаг 4): если у юнита эта реликвия — её набор кадров вместо дефолтного на префабе.")]
        [SerializeField] private VisualOverride[] _visualOverrides = System.Array.Empty<VisualOverride>();

        [Header("Дизайн-система (цвета боевого UI)")]
        [Tooltip("Палитра цветов боя (первый SO дизайн-системы). Задаёт цвет HP-бара по принадлежности к " +
                 "смотрящему. Пусто = фолбэк-цвета по умолчанию (см. DefaultHealthColor).")]
        [SerializeField] private Design.CombatColorPalette _colorPalette;

        [Tooltip("Команда «смотрящего» (локального игрока): его юниты — союзные (ally-цвет), прочие — enemy. " +
                 "Шов под кооп (там смотрящий может быть в любой команде); пока 0 = команда игрока.")]
        [SerializeField] private int _localViewerTeam;

        [System.Serializable]
        private struct VisualOverride
        {
            public RelicData Relic;
            public UnitVisual Visual;
        }

        private CombatSimulation            _simulation;
        private readonly Dictionary<int, UnitView>       _views     = new Dictionary<int, UnitView>();
        private readonly Dictionary<int, ProjectileView> _projViews = new Dictionary<int, ProjectileView>();
        private readonly List<int>                       _deadProj  = new List<int>();

        private ObjectPool<FloatingText>    _textPool;
        private System.Action<FloatingText> _releaseText;
        private CombatStatusOverlay         _statusOverlay;
        private RuntimeUnit                 _finisherCandidate; // автор последнего добивающего мили-удара

        private IPublisher<UnitSpawnedEvent> _unitSpawnedPublisher;
        private IPublisher<UnitDiedEvent>    _unitDiedPublisher;
        private IPublisher<DamageDealtEvent> _damageDealtPublisher;
        private IPublisher<BattleEndedEvent> _battleEndedPublisher;

        // Локальный hitstop (2a): окно заморозки участников удара, масштабируется долей нанесённого HP-урона.
        // Локально (на пару вью), поэтому допустимо на каждом ударе — толпа вокруг не стынет. Global-эффекты
        // (slowmo/шейк) — отдельный слой по порогам значимости (2b).
        private const float HitstopMinSeconds = 0.02f; // слабый удар — ~1 кадр при 60 fps
        private const float HitstopMaxSeconds = 0.09f; // тяжёлый удар
        private const float HitstopFullFrac   = 0.25f; // урон ≥25% MaxHP цели → максимальный стоп

        // Финишер держит кадр контакта столько же, сколько длится финальный slowmo (см. CombatFeelDirector).
        private const float FinisherHoldSeconds = 5f;

        [Inject]
        public void Construct(
            CombatSimulation simulation,
            IPublisher<UnitSpawnedEvent> unitSpawnedPublisher,
            IPublisher<UnitDiedEvent>    unitDiedPublisher,
            IPublisher<DamageDealtEvent> damageDealtPublisher,
            IPublisher<BattleEndedEvent> battleEndedPublisher)
        {
            _simulation           = simulation;
            _unitSpawnedPublisher = unitSpawnedPublisher;
            _unitDiedPublisher    = unitDiedPublisher;
            _damageDealtPublisher = damageDealtPublisher;
            _battleEndedPublisher = battleEndedPublisher;
        }

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

        // Перезапуск боя на месте (dev-R): снимаем все виды юнитов и снарядов. Сцена/камера не трогаются;
        // новый сетап заспавнит юнитов заново через OnUnitSpawned.
        private void HandleBattleReset()
        {
            foreach (var kvp in _views)
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            _views.Clear();

            foreach (var kvp in _projViews)
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            _projViews.Clear();

            _finisherCandidate = null;
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

            var view = Instantiate(_bulletPrefab, (Vector3)(Vector2)projectile.Position, Quaternion.identity, transform);
            // Тинт снаряда = цвет юнита-источника (тот же метод, что и тело юнита).
            Color tint = projectile.Source != null ? TintFor(projectile.Source) : Color.white;
            view.Bind(projectile, tint);
            _projViews[projectile.Id] = view;
        }

        private void HandleUnitSpawned(RuntimeUnit unit)
        {
            if (_unitViewPrefab != null)
            {
                var view = Instantiate(_unitViewPrefab, (Vector3)(Vector2)unit.Position, Quaternion.identity, transform);
                view.Bind(unit);

                UnitVisual ov = ResolveVisual(unit.Unit);
                if (ov != null) view.SetVisual(ov);

                // «Пока один спрайт»: тинтуем тело на персонажа + подпись над HP-баром (dev-харнесс).
                view.SetTint(TintFor(unit));
                view.SetLabel(NameFor(unit));

                // Цвет HP-бара по принадлежности к смотрящему (дизайн-система, задача 1).
                bool isAllyOfViewer = unit.Team == _localViewerTeam;
                view.SetHealthColor(_colorPalette != null
                    ? _colorPalette.HealthBarColor(isAllyOfViewer)
                    : DefaultHealthColor(isAllyOfViewer));

                _views[unit.Id] = view;
            }

            _unitSpawnedPublisher.Publish(new UnitSpawnedEvent(unit));
        }

        // Источник визуала — данные юнита (UnitData.Visual): тот же ассет, что читает сим для windup
        // (AutoAttackSystem). Scene-_visualOverrides остаётся лишь dev-фолбэком для юнитов без своего
        // визуала (сравнение по reference-равенству RelicData(.Relic) == UnitData(data) через общую базу).
        private UnitVisual ResolveVisual(UnitData data)
        {
            if (data == null) return null;
            if (data.Visual != null) return data.Visual;
            for (int i = 0; i < _visualOverrides.Length; i++)
                if (_visualOverrides[i].Relic == data) return _visualOverrides[i].Visual;
            return null;
        }

        private void HandleUnitDied(RuntimeUnit unit)
        {
            if (_views.TryGetValue(unit.Id, out var view))
            {
                view.OnDeath();
                _views.Remove(unit.Id);
            }

            _unitDiedPublisher.Publish(new UnitDiedEvent(unit));
        }

        private void HandleDamageDealt(RuntimeUnit source, RuntimeUnit target, DamageResult result)
        {
            // Урон совпадает с кадром контакта (конец замаха): здесь — импакт-фидбэк цели.
            // Свинг источника запускается раньше, на OnAttackStarted (вики «14»).
            if (_views.TryGetValue(target.Id, out var view))
                view.OnDamageReceived(result.TotalDamage);

            // Локальный hitstop пары «источник + цель» по значимости удара (доля HP-урона от MaxHP цели).
            if (view != null)
            {
                float maxHp = target.Stats.Get(Data.Stats.StatType.MaxHP);
                float frac  = maxHp > 0f ? result.HpDamage / maxHp : 0f;
                float stop  = Mathf.Lerp(HitstopMinSeconds, HitstopMaxSeconds, Mathf.Clamp01(frac / HitstopFullFrac));
                view.OnHitstop(stop);
                if (source != null && _views.TryGetValue(source.Id, out var sourceView))
                    sourceView.OnHitstop(stop);
            }

            // Кандидат в финишеры: автор добивающего удара, если он мили (снаряд/яд позу удара не держат).
            if (result.KilledTarget)
                _finisherCandidate = (source?.Unit != null && source.Unit.AttackType == AttackType.Melee) ? source : null;

            int shield = Mathf.RoundToInt(result.ShieldDamage);
            int hp     = Mathf.RoundToInt(result.HpDamage);

            // Урон по щиту — синим «-N»; по HP — «-N» цветом урона. Если задет и щит, и HP —
            // цифра щита сразу, цифра HP через очень маленькую задержку (обе читаемы).
            if (shield > 0) SpawnNumber(target.Position, "-" + shield, _shieldColor);
            if (hp > 0)
            {
                if (shield > 0) StartCoroutine(DelayedNumber(target.Position, "-" + hp, _damageColor, _splitDelay));
                else            SpawnNumber(target.Position, "-" + hp, _damageColor);
            }

            _damageDealtPublisher.Publish(new DamageDealtEvent(source, target, result));
        }

        private void HandleHealed(RuntimeUnit source, RuntimeUnit target, float amount)
        {
            // Хил-цифра над целью (+N). Мелкие тики регена округляются в 0 и не спамят.
            int healed = Mathf.RoundToInt(amount);
            if (healed > 0) SpawnNumber(target.Position, "+" + healed, _healColor);
        }

        private void HandleAttackEvaded(RuntimeUnit target)
        {
            // Полный негейт удара («Изворотливость») — урона нет, показываем «evade».
            SpawnNumber(target.Position, "evade", _evadeColor);
        }

        private IEnumerator DelayedNumber(Vector2 worldPosition, string text, Color color, float delay)
        {
            yield return new WaitForSeconds(delay);
            SpawnNumber(worldPosition, text, color);
        }

        /// <summary>Заспавнить свою всплывающую боевую цифру над мировой точкой (через пул).</summary>
        private void SpawnNumber(Vector2 worldPosition, string text, Color color)
        {
            EnsureTextPool();
            if (_textPool == null) return;

            FloatingText ft = _textPool.Get();
            ft.transform.position = (Vector3)worldPosition + Vector3.up * 0.4f;
            ft.Play(text, color, _releaseText);
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
                actionOnDestroy: ft => Destroy(ft.gameObject),
                collectionCheck: false,
                defaultCapacity: 16,
                maxSize: 64);
        }

        /// <summary>Тинт тела по персонажу: у реликвии — стабильный оттенок от имени; у болванчиков — по команде.</summary>
        private static Color TintFor(RuntimeUnit unit)
        {
            if (unit.Unit != null)
            {
                float hue = (Mathf.Abs(unit.Unit.name.GetHashCode()) % 360) / 360f;
                return Color.HSVToRGB(hue, 0.5f, 1f);
            }
            return unit.Team == 0 ? new Color(0.7f, 0.8f, 1f) : new Color(1f, 0.7f, 0.7f);
        }

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
                finisher.HoldHitFrame(FinisherHoldSeconds);

            // Живые (в _views мёртвые уже удалены) доигрывают анимации натурально, а не виснут на замершем симе.
            foreach (var kvp in _views)
                if (kvp.Value != null) kvp.Value.OnBattleEnded();

            Debug.Log($"[CombatPresenter] - Бой завершён: {outcome}");

            _battleEndedPublisher.Publish(new BattleEndedEvent(outcome));
        }
    }
}
