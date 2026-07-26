using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
        [Tooltip("Цвет надписи «evade» при полном негейте удара.")]
        [SerializeField] private Color _evadeColor = new Color(0.85f, 0.9f, 0.95f);
        [Tooltip("Задержка между цифрой щита и цифрой HP при сплите (сек).")]
        [SerializeField] private float _splitDelay = 0.06f;

        [Header("Дизайн-система (цвета боевого UI)")]
        [Tooltip("Палитра цветов боя (первый SO дизайн-системы) — ЕДИНСТВЕННЫЙ владелец цветов HP и щита: " +
                 "и полосы, и боевых цифр. Обязательна: пусто = баг разводки сцены, а не повод рисовать " +
                 "чем попало (охраняется SceneWiringTests).")]
        [SerializeField] private Design.CombatColorPalette _colorPalette;


        private CombatSimulation            _simulation;
        private AbilitySystem               _abilities;   // сигнал каста — оттуда же, откуда его берёт аудио

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
        private ObjectPool<ProjectileView>  _projPool;
        private System.Action<FloatingText> _releaseText;
        private CombatStatusOverlay         _statusOverlay;
        private CombatVfx                   _vfx;               // пул боевых VFX-префабов
        private RuntimeUnit                 _finisherCandidate; // автор последнего добивающего мили-удара

        private IPublisher<DamageDealtEvent> _damageDealtPublisher;
        private IPublisher<BattleEndedEvent> _battleEndedPublisher;

        // Все feel-параметры (hitstop, финишер, вспышка/сплющивание вью) — из design-конфига (единый источник).
        private Design.CombatFeelConfig _feel;
        private Core.Audio.IAudioService _audio;   // раздаётся видам: разлёт на осколки звучит из UnitView
        private Core.Simulation.ISimInterpolation _interpolation;   // доля шага между тиками — считает петля

        [Inject]
        public void Construct(
            CombatSimulation simulation,
            AbilitySystem abilities,
            IPublisher<DamageDealtEvent> damageDealtPublisher,
            IPublisher<BattleEndedEvent> battleEndedPublisher,
            Design.CombatFeelConfig feel,
            Core.Audio.IAudioService audio,
            Core.Players.ILocalPlayer localPlayer,
            Core.Simulation.ISimInterpolation interpolation)
        {
            _interpolation        = interpolation;
            _localPlayer          = localPlayer;
            _audio                = audio;
            _simulation           = simulation;
            _abilities            = abilities;
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
            if (_abilities != null) _abilities.OnAbilityCast += HandleAbilityCast;

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
            if (_abilities != null) _abilities.OnAbilityCast -= HandleAbilityCast;
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
                if (kvp.Value != null) ReleaseProjectile(kvp.Value);   // в пул, а не в мусор: бой ещё будет
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
            // Тот же признак готовности, что в OnEnable: пока Construct не позвали, презентеру нечего
            // рисовать — ни симуляции, ни петли ещё нет. Это не фолбэк на пустую зависимость: с пришедшим
            // Construct приходят обе разом, поэтому отдельной проверки на _interpolation не нужно.
            if (_simulation == null) return;

            // Доля берётся у петли: она копит остаток тика. Прежний подсчёт (Time.deltaTime / TickDelta)
            // считал не долю шага, а отношение кадра к тику — при стабильном fps это константа, и тела
            // вечно стояли на одной и той же промежуточной точке вместо того, чтобы двигаться между тиками.
            float alpha = _interpolation.Alpha;

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
                    if (_projViews.TryGetValue(_deadProj[i], out var pv) && pv != null) ReleaseProjectile(pv);
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
                    _vfx.Spawn(_feel.VfxMuzzle, srcView.ShotPoint, ang, tint: VfxPaletteFor(projectile.Source));
                }
            }

            ProjectileView view = RentProjectile(origin);
            // Снаряд и его след — ЭФФЕКТ стрелка, значит его VFX-цвет, а не тинт тела.
            // Снаряд и его след — один цвет с затуханием, разбросу тут места нет.
            Color tint = projectile.Source != null ? VfxColorFor(projectile.Source) : Color.white;
            view.Bind(projectile, tint, origin);
            _projViews[projectile.Id] = view;
        }

        /// <summary>
        /// Взять вид снаряда из пула. Снаряды — единственное, что мы создавали и уничтожали на каждое
        /// событие: цифры и VFX пулятся с самого начала, а перестрелка в упор молотила по куче.
        /// </summary>
        private ProjectileView RentProjectile(Vector3 origin)
        {
            EnsureProjectilePool();
            ProjectileView view = _projPool.Get();
            view.transform.SetPositionAndRotation(origin, Quaternion.identity);
            return view;
        }

        private void ReleaseProjectile(ProjectileView view)
        {
            if (view == null) return;
            if (_projPool == null) { Destroy(view.gameObject); return; }
            _projPool.Release(view);
        }

        private void EnsureProjectilePool()
        {
            if (_projPool != null || _bulletPrefab == null) return;

            _projPool = new ObjectPool<ProjectileView>(
                createFunc: () => Instantiate(_bulletPrefab, transform),
                actionOnGet: v => v.gameObject.SetActive(true),
                actionOnRelease: v => { if (v != null) v.gameObject.SetActive(false); },
                actionOnDestroy: v => { if (v != null) Destroy(v.gameObject); },
                collectionCheck: false,
                defaultCapacity: 16,
                maxSize: 64);
        }

        /// <summary>
        /// Каст способности за ману: всплеск у самого юнита плюс контур на теле — «смотри, я сейчас
        /// выдам». Висит на КАСТЕ, а не на попадании: каст — это намерение, и объявлять его надо до того,
        /// как что-то прилетело, иначе эффект пересказывает уже случившееся.
        /// <para>Цвет — из <c>UnitData.ResolveVfxColor</c>: форма всплеска общая, а светит каждый своим.</para>
        /// </summary>
        private void HandleAbilityCast(RuntimeUnit caster)
        {
            if (caster == null || !_views.TryGetValue(caster.Id, out var view) || view == null) return;

            view.PlayCastOutline(VfxColorFor(caster));   // контур — один цвет, без разброса

            if (_vfx != null && _feel != null && _feel.VfxCastBurst != null)
                _vfx.Spawn(_feel.VfxCastBurst, view.HitPoint, tint: VfxPaletteFor(caster));
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
                    view.SetHealthColor(_colorPalette.HealthBarColor(IsAllyOfViewer(unit)));

                    // Цвет щита — общий из палитры (не зависит от принадлежности).
                    view.SetShieldColor(_colorPalette.Shield);

                    _views[unit.Id] = view;
                }
                else Destroy(go);
            }
        }

        private void HandleUnitDied(RuntimeUnit unit)
        {
            if (_views.TryGetValue(unit.Id, out var view))
            {
                view.OnDeath();
                _views.Remove(unit.Id);
                _corpses.Add(view); // труп доигрывает секвенс смерти сам; сносим гарантированно при рестарте
            }
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

                // Удар, целиком съеденный щитом, вспыхивает ЦВЕТОМ ЩИТА: иначе «пробил» и «не пробил»
                // выглядят одинаково, а разница между ними — самое интересное, что есть в этом ударе.
                if (result.HpDamage <= 0f && result.ShieldDamage > 0f && _colorPalette != null)
                    flash = _colorPalette.Shield;

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
                // Искры летят ПО направлению удара (от бьющего), и чем тяжелее удар — тем их больше.
                // Направление берём то же, что и отброс тела: один удар — один вектор, спорить им не о чем.
                float? sparkDir = nudgeDir.sqrMagnitude > 1e-8f
                    ? Mathf.Atan2(nudgeDir.y, nudgeDir.x) * Mathf.Rad2Deg
                    : (float?)null;

                _vfx.Spawn(_feel.VfxHitSpark, anchor, sparkDir,
                           _feel.EvaluateHitVfxIntensity(frac), _feel.EvaluateHitVfxCount(frac),
                           VfxPaletteFor(source));   // искры — палитры бьющего

                bool melee = source?.Unit != null && source.Unit.AttackType == AttackType.Melee;
                if (melee)
                    _vfx.Spawn(_feel.VfxImpactDust, view.FeetPoint);
            }

            // Урон по щиту — синим «-N»; по HP — «-N» цветом урона. Если задет и щит, и HP —
            // цифра щита сразу, цифра HP через очень маленькую задержку (обе читаемы).
            if (shield > 0) SpawnNumber(anchor, "-" + shield, _colorPalette.Shield);
            if (hp > 0)
            {
                if (shield > 0) DelayedNumber(anchor, "-" + hp, _damageColor, _splitDelay, hpScale).Forget();
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

            if (_views.TryGetValue(target.Id, out var tView) && tView != null)
            {
                tView.OnHealed();                                    // тело отвечает на лечение, а не только цифра
                if (_vfx != null && _feel != null)
                    _vfx.Spawn(_feel.VfxHeal, tView.HitPoint, tint: VfxPaletteFor(source));  // палитра лечащего
            }
        }

        private void HandleAttackEvaded(RuntimeUnit target)
        {
            // Полный негейт удара («Изворотливость») — урона нет, показываем «evade».
            // Движение тела сюда НЕ добавляем: по дизайну трата заряда — это кувырок с уходом с места,
            // то есть настоящее перемещение в симуляции (gdd/20-combat/positioning). Появится оно — вид
            // поедет за ним сам.
            SpawnNumber(AnchorFor(target), "evade", _evadeColor);
        }

        // Задержка на UNSCALED-времени: в паузе и в финишер-slowmo вторая цифра иначе не приходила вовсе —
        // игрок видел урон по щиту и ждал у моря погоды. Через UniTask, а не корутину: корутина здесь
        // ещё и аллоцировала WaitForSeconds на каждый расщеплённый удар.
        private async UniTaskVoid DelayedNumber(Vector3 worldPosition, string text, Color color, float delay, float sizeScale)
        {
            bool canceled = await UniTask.Delay(System.TimeSpan.FromSeconds(delay), DelayType.UnscaledDeltaTime,
                                                cancellationToken: this.GetCancellationTokenOnDestroy())
                                         .SuppressCancellationThrow();
            if (canceled) return;   // презентер умер за время задержки — спавнить цифру некуда
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
        /// Цвета ЭФФЕКТОВ юнита. Их два, и они про разное: ГЛАВНЫЙ цвет — там, где цвет один (тело снаряда,
        /// его след, контур каста), ПАЛИТРА — диапазон разброса для частиц. Держатся на <c>UnitData</c>, а не
        /// в префабах: иначе холод криоманта и свет пастыря выглядели бы одинаково просто потому, что летят
        /// из одного префаба.
        /// <para>Пыль под ногами сюда НЕ входит: она принадлежит земле, а не бойцу.</para>
        /// </summary>
        private Gradient VfxPaletteFor(RuntimeUnit unit) => unit?.Unit?.ResolveVfxPalette();

        /// <summary>Главный цвет эффектов юнита — для того, у чего нет ни длины, ни россыпи.</summary>
        private Color VfxColorFor(RuntimeUnit unit) =>
            unit?.Unit != null ? unit.Unit.ResolveVfxColor() : TintFor(unit);

        /// <summary>
        /// Юнит на стороне смотрящего? Единственное место, где в презентере решается «свой/чужой».
        /// Без <see cref="Core.Players.ILocalPlayer"/> (сцена без DI, дев-запуск) считаем команду 0 своей.
        /// </summary>
        private bool IsAllyOfViewer(RuntimeUnit unit) =>
            unit.Team == (_localPlayer != null ? _localPlayer.Team : 0);

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
            UnitView finisher = null;
            if (_finisherCandidate != null && _views.TryGetValue(_finisherCandidate.Id, out finisher) && finisher != null)
                finisher.HoldHitFrame(_feel.FinisherHoldSeconds);

            foreach (var kvp in _views)
            {
                if (kvp.Value == null) continue;

                // Весь бой замирает вместе с моментом, а не только добивающий: остальные выжившие держат
                // ту позу, в которой их застало. По окончании окна каждый доигрывает своё движение до
                // конца и оседает в стойку — это и делает free-run ниже.
                if (!ReferenceEquals(kvp.Value, finisher)) kvp.Value.HoldFrame(_feel.FinisherHoldSeconds);

                kvp.Value.OnBattleEnded();
            }

            Debug.Log($"[CombatPresenter] - Бой завершён: {outcome}");

            _battleEndedPublisher.Publish(new BattleEndedEvent(outcome));
        }
    }
}
