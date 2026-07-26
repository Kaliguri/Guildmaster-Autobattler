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


        private CombatSimulation            _simulation;

        // Команда «смотрящего» приходит от ILocalPlayer — единственного владельца этого факта
        // (GameConfig.LocalPlayerTeam → SoloLocalPlayer). Своего поля презентер не держит: пока оно
        // было, за одну и ту же команду отвечали двое, и цвет полосок мог разойтись со стингером
        // победы, который всегда спрашивал ILocalPlayer.
        private Core.Players.ILocalPlayer   _localPlayer;

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
        private CombatVfx                   _vfx;               // пул боевых VFX-префабов
        private RuntimeUnit                 _finisherCandidate; // автор последнего добивающего мили-удара

        private IPublisher<UnitSpawnedEvent> _unitSpawnedPublisher;
        private IPublisher<UnitDiedEvent>    _unitDiedPublisher;
        private IPublisher<DamageDealtEvent> _damageDealtPublisher;
        private IPublisher<BattleEndedEvent> _battleEndedPublisher;

        // Все feel-параметры (hitstop, финишер, вспышка/сплющивание вью) — из design-конфига (единый источник).
        private Design.CombatFeelConfig _feel;
        private Core.Audio.IAudioService _audio;   // раздаётся видам: разлёт на осколки звучит из UnitView

        [Inject]
        public void Construct(
            CombatSimulation simulation,
            IPublisher<UnitSpawnedEvent> unitSpawnedPublisher,
            IPublisher<UnitDiedEvent>    unitDiedPublisher,
            IPublisher<DamageDealtEvent> damageDealtPublisher,
            IPublisher<BattleEndedEvent> battleEndedPublisher,
            Design.CombatFeelConfig feel,
            Core.Audio.IAudioService audio,
            Core.Players.ILocalPlayer localPlayer)
        {
            _localPlayer          = localPlayer;
            _audio                = audio;
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

            // Летящие VFX-префабы — погасить и вернуть в пул.
            if (_vfx != null) _vfx.DespawnAll();
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

        /// <summary>Создать пул боевых VFX-префабов в рантайме (без правок сцены).</summary>
        private void EnsureVfx()
        {
            if (_vfx != null) return;
            var go = new GameObject("CombatVfx");
            go.transform.SetParent(transform, worldPositionStays: false);
            _vfx = go.AddComponent<CombatVfx>();
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
                    _vfx.Spawn(_feel.VfxMuzzle, srcView.ShotPoint, ang);
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
                    view.ApplyAudio(_audio);     // хруст разлёта: вид сам знает, когда начинается shatter
                    view.SetContactDustHandler(OnUnitContactDust);

                    // Тинт тела по персонажу (dev-различение, пока placeholder-спрайт) + подпись над HP-баром.
                    view.SetTint(TintFor(unit));
                    view.SetLabel(NameFor(unit));

                    // Цвет HP-бара по принадлежности к смотрящему (дизайн-система).
                    bool isAllyOfViewer = IsAllyOfViewer(unit);
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
            Vector2 nudgeDir = Vector2.zero;
            if (source != null)
            {
                Vector2 delta = target.Position - source.Position;
                if (delta.sqrMagnitude > 1e-8f) nudgeDir = delta.normalized;
            }

            if (_views.TryGetValue(target.Id, out var view) && view != null)
            {
                Color flash = _feel != null
                    ? _feel.ResolveHitFlashColor(result.School, result.Affinity)
                    : Color.white;
                view.OnDamageReceived(flash, nudgeDir);
            }

            // Доля HP-урона от MaxHP цели — общий «вес удара» для hitstop и размера цифры.
            float maxHp = target.Stats.Get(Data.Stats.StatType.MaxHP);
            float frac  = maxHp > 0f ? result.HpDamage / maxHp : 0f;

            // Локальный hitstop пары «источник + цель» по значимости удара (кривая в feel-конфиге).
            if (view != null && _feel != null)
            {
                float stop = _feel.EvaluateHitstopSeconds(frac);
                view.OnHitstop(stop);
                if (source != null && _views.TryGetValue(source.Id, out var sourceView) && sourceView != null)
                {
                    sourceView.OnHitstop(stop);
                    if (nudgeDir.sqrMagnitude > 1e-8f)
                        sourceView.OnAttackLunge(nudgeDir);
                }
            }

            // Кандидат в финишеры: автор добивающего удара, если он мили (снаряд/яд позу удара не держат).
            if (result.KilledTarget)
                _finisherCandidate = (source?.Unit != null && source.Unit.AttackType == AttackType.Melee) ? source : null;

            int shield = Mathf.RoundToInt(result.ShieldDamage);
            int hp     = Mathf.RoundToInt(result.HpDamage);

            // Цифры — в точку попадания (грудь) цели. Размер HP-цифры растёт с весом удара (тяжёлый = крупнее).
            Vector3 anchor  = AnchorFor(target);
            float   hpScale = Mathf.Lerp(1f, _feel.NumberMaxScale, Mathf.Clamp01(frac / Mathf.Max(1e-4f, _feel.NumberFullFrac)));

            // VFX-префабы: искры в точку попадания + пыль у ног на мили-ударе.
            if (_vfx != null && _feel != null && view != null)
            {
                _vfx.Spawn(_feel.VfxHitSpark, anchor, intensity: _feel.EvaluateHitVfxIntensity(frac));

                bool melee = source?.Unit != null && source.Unit.AttackType == AttackType.Melee;
                if (melee)
                    _vfx.Spawn(_feel.VfxImpactDust, view.FeetPoint);
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

            // VFX лечения в точку попадания.
            if (_vfx != null && _feel != null && _views.TryGetValue(target.Id, out var tView) && tView != null)
                _vfx.Spawn(_feel.VfxHeal, tView.HitPoint);
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
            float arcG = _feel != null && _feel.EnableFloatingTextArc ? _feel.NumberArcGravity : 0f;
            ft.Play(text, color, sizeScale, arcG, _releaseText);
        }

        /// <summary>Мировая точка для боевой цифры: HitPoint вида цели (грудь); фолбэк — над позицией сима.</summary>
        private Vector3 AnchorFor(RuntimeUnit target)
        {
            if (_views.TryGetValue(target.Id, out var v) && v != null)
                return v.HitPoint;
            return (Vector3)(Vector2)target.Position + Vector3.up * 0.4f;
        }

        /// <summary>Contact-dust: пыль у ног при старте/стопе бега (VfxData → префаб, тумблер в feel-конфиге).</summary>
        private void OnUnitContactDust(UnitView view)
        {
            if (_vfx == null || _feel == null || view == null) return;
            if (!_feel.EnableContactDust) return;
            _vfx.Spawn(_feel.VfxContactDust, view.FeetPoint);
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
        /// (тот же цвет, что рендерит карточка инвентаря); у болванчиков без данных — по стороне смотрящего.
        /// </summary>
        private Color TintFor(RuntimeUnit unit) =>
            unit.Unit != null
                ? unit.Unit.ResolveBodyTint()
                : (IsAllyOfViewer(unit) ? new Color(0.7f, 0.8f, 1f) : new Color(1f, 0.7f, 0.7f));

        /// <summary>
        /// Юнит на стороне смотрящего? Единственное место, где в презентере решается «свой/чужой».
        /// Без <see cref="Core.Players.ILocalPlayer"/> (сцена без DI, дев-запуск) считаем команду 0 своей.
        /// </summary>
        private bool IsAllyOfViewer(RuntimeUnit unit) =>
            unit.Team == (_localPlayer != null ? _localPlayer.Team : 0);

        /// <summary>Фолбэк-цвет HP-бара, если палитра дизайн-системы не назначена (совпадает с дефолтами SO).</summary>
        private static Color DefaultHealthColor(bool isAllyOfViewer) => isAllyOfViewer
            ? new Color(0.30f, 0.85f, 0.35f)
            : new Color(0.90f, 0.25f, 0.25f);

        /// <summary>Подпись персонажа: имя реликвии (SO) либо «Ally/Enemy N» для болванчиков.</summary>
        private string NameFor(RuntimeUnit unit)
        {
            if (unit.Unit != null) return unit.Unit.name;
            return (IsAllyOfViewer(unit) ? "Ally " : "Enemy ") + unit.Id;
        }

        private void HandleAttackStarted(RuntimeUnit source, RuntimeUnit target)
        {
            // Вход в замах: запускаем анимацию свинга у источника (вики «14»).
            if (source == null || !_views.TryGetValue(source.Id, out var sourceView) || sourceView == null)
                return;

            Vector2 away = Vector2.zero;
            if (target != null)
            {
                Vector2 delta = source.Position - target.Position; // от цели = назад
                if (delta.sqrMagnitude > 1e-8f) away = delta.normalized;
            }
            sourceView.OnAttackStarted(away);
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
