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
    /// <remarks>
    /// Порядок исполнения задан явно: презентер — единственный, кто двигает момент показа, и виды читают
    /// его результат (кадр ленты, доля внутри тика). Без этого <c>Update</c> вида мог бы отработать раньше
    /// и скрабить позу по доле ПРЕДЫДУЩЕГО кадра — поза постоянно отставала бы от позиции.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
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

        // Команда «смотрящего» приходит от ILocalPlayer — единственного владельца этого факта
        // (GameConfig.LocalPlayerTeam → SoloLocalPlayer). Своего поля презентер не держит: пока оно
        // было, за одну и ту же команду отвечали двое, и цвет полосок мог разойтись со стингером
        // победы, который всегда спрашивал ILocalPlayer.
        private Core.Players.ILocalPlayer   _localPlayer;

        private readonly Dictionary<int, UnitView>       _views     = new Dictionary<int, UnitView>();
        private readonly Dictionary<int, ProjectileView> _projViews = new Dictionary<int, ProjectileView>();
        private readonly List<int>                       _deadProj  = new List<int>();
        private readonly HashSet<int>                    _seenProj  = new HashSet<int>();

        // Живые снаряды и направление последнего снаряда, летевшего в конкретную цель (по её id).
        // Импакт-фидбэк дальнобойного удара должен идти ОТКУДА ПРИЛЕТЕЛО, а вектор «стрелок → цель»
        // это не то: пока снаряд летел, стрелок мог сместиться (Следопыт стреляет на ходу), а цель —
        // уйти в сторону. Направление переживает сам снаряд: урон приходит уже после его смерти.
        private readonly Dictionary<int, Projectile> _projById    = new Dictionary<int, Projectile>();
        private readonly Dictionary<int, Vector2>    _lastShotDir = new Dictionary<int, Vector2>();
        // Виды погибших юнитов: сняты из _views (перестают следовать за симом), но GameObject живёт, пока идёт
        // секвенс смерти (death-клип → разлёт). Держим отдельно, чтобы гарантированно снести их при рестарте —
        // иначе трупы прошлого боя остаются висеть в новом («телепортируются»).
        private readonly List<UnitView>                  _corpses   = new List<UnitView>();

        private ObjectPool<FloatingText>    _textPool;
        private ObjectPool<ProjectileView>  _projPool;
        private System.Action<FloatingText> _releaseText;
        private CombatStatusOverlay         _statusOverlay;
        private CombatVfx                   _vfx;               // пул боевых VFX-префабов
        private int                         _finisherCandidate = -1; // id автора последнего добивающего мили-удара

        private IPublisher<DamageDealtEvent> _damageDealtPublisher;
        private IPublisher<BattleEndedEvent> _battleEndedPublisher;

        // Все feel-параметры (hitstop, финишер, вспышка/сплющивание вью) — из design-конфига (единый источник).
        private Design.CombatFeelConfig _feel;
        private Core.Audio.IAudioService _audio;   // раздаётся видам: разлёт на осколки звучит из UnitView
        // Показ читает ЛЕНТУ, а не живой сим: сим уходит вперёд на окно опережения, поэтому живое
        // состояние для картинки — будущее. Момент показа и доля кадра — у плеера ленты.
        private Combat.Tape.BattleTapePlayback _playback;

        // События приходят отсюда — то есть тогда, когда их ПОКАЗАЛИ, а не когда посчитал сим.
        private Combat.Tape.BattleTapeDispatcher _dispatcher;

        // Режим dev-оверлеев: презентер только раздаёт его тому, что создаёт сам (статус-кольца).
        private DevOverlayMode _overlayMode;

        // Паспорт юнита: то, что за бой не меняется. Заводится по событию спавна (оно приходит заранее —
        // это регистрация, а не показ), потому что вид создаётся много позже, когда до юнита дойдёт показ,
        // и живого юнита к тому моменту может уже не быть в списке.
        private readonly Dictionary<int, UnitIdentity> _identities = new Dictionary<int, UnitIdentity>();

        // Индекс кадра показа id→снимок: собирается раз в кадр, нужен для поиска снимка ЦЕЛИ.
        private readonly Dictionary<int, Combat.Tape.UnitSnapshot> _frameIndex =
            new Dictionary<int, Combat.Tape.UnitSnapshot>();
        private readonly List<int> _viewsToBury = new List<int>();

        [Inject]
        public void Construct(
            CombatSimulation simulation,
            IPublisher<DamageDealtEvent> damageDealtPublisher,
            IPublisher<BattleEndedEvent> battleEndedPublisher,
            Design.CombatFeelConfig feel,
            Core.Audio.IAudioService audio,
            Core.Players.ILocalPlayer localPlayer,
            Combat.Tape.BattleTapePlayback playback,
            Combat.Tape.BattleTapeDispatcher dispatcher,
            DevOverlayMode overlayMode)
        {
            _playback             = playback;
            _dispatcher           = dispatcher;
            _overlayMode          = overlayMode;
            _localPlayer          = localPlayer;
            _audio                = audio;
            _simulation           = simulation;
            _damageDealtPublisher = damageDealtPublisher;
            _battleEndedPublisher = battleEndedPublisher;
            _feel                 = feel;
        }

        /// <summary>
        /// Вид живого юнита по его Id (единственная карта Id→вид). Нужен фазе расстановки (шаг 4), чтобы
        /// нарисовать outline на наведённом/выбранном юните по его мировой позиции.
        /// </summary>
        public bool TryGetView(int unitId, out UnitView view) => _views.TryGetValue(unitId, out view);

        /// <summary>
        /// Цвет щита из палитры дизайн-системы. Отдаётся наружу, потому что палитра — обязательная
        /// зависимость презентера и единственный владелец цветов боя: другим потребителям (телеграфы)
        /// незачем заводить вторую ссылку на неё и тем самым второго владельца цвета.
        /// </summary>
        public Color ShieldColor => _colorPalette.Shield;

        /// <summary>Сколько видов юнитов сейчас на экране. Только для dev-диагностики ленты.</summary>
        public int ViewCount => _views.Count;

        /// <summary>Сколько паспортов юнитов зарегистрировано. Только для dev-диагностики ленты.</summary>
        public int IdentityCount => _identities.Count;

        private void OnEnable()
        {
            if (_simulation == null) return;

            // Спавн — единственное, что слушаем у СИМА, и не ради показа: нужен паспорт юнита, пока
            // живой юнит ещё под рукой. Всё остальное приходит с ленты, когда его показали.
            _simulation.OnUnitSpawned += HandleUnitSpawned;
            // Рестарт — служебное событие, а не показ: лента уже очищена, и ждать «показа рестарта»
            // некому. Отсюда же сбрасываются момент показа и курсор диспетчера.
            _simulation.OnBattleReset += HandleBattleReset;

            _dispatcher.DamageDealt       += HandleDamageDealt;
            _dispatcher.Healed            += HandleHealed;
            _dispatcher.AttackEvaded      += HandleAttackEvaded;
            _dispatcher.AttackStarted     += HandleAttackStarted;
            _dispatcher.AttackInterrupted += HandleAttackInterrupted;
            _dispatcher.BattleEnded       += HandleBattleEnded;
            // Каст показывается по ПОКАЗУ, как и всё остальное. Подписка потерялась при переводе на
            // ленту (Ф3), и контур с всплеском каста молча не работали — восстановлена вместе с M3.
            _dispatcher.AbilityCast            += HandleAbilityCast;
            _dispatcher.AbilityCastStarted     += HandleAbilityCastStarted;
            _dispatcher.AbilityCastInterrupted += HandleAbilityCastInterrupted;
        }

        private void OnDisable()
        {
            if (_simulation == null) return;

            _simulation.OnUnitSpawned -= HandleUnitSpawned;
            _simulation.OnBattleReset -= HandleBattleReset;

            _dispatcher.DamageDealt       -= HandleDamageDealt;
            _dispatcher.Healed            -= HandleHealed;
            _dispatcher.AttackEvaded      -= HandleAttackEvaded;
            _dispatcher.AttackStarted     -= HandleAttackStarted;
            _dispatcher.AttackInterrupted -= HandleAttackInterrupted;
            _dispatcher.BattleEnded       -= HandleBattleEnded;
            _dispatcher.AbilityCast            -= HandleAbilityCast;
            _dispatcher.AbilityCastStarted     -= HandleAbilityCastStarted;
            _dispatcher.AbilityCastInterrupted -= HandleAbilityCastInterrupted;
        }

        private void HandleBattleReset()
        {
            // Лента чистится рекордером, а показ и курсор событий надо отмотать: иначе показ продолжит
            // с тика прошлого боя (и окажется впереди нового фронта), а первые события нового боя
            // сочтутся уже показанными.
            _playback.Reset();
            _dispatcher.Reset();
            _identities.Clear();
            _frameIndex.Clear();

            foreach (var kvp in _views)
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            _views.Clear();

            foreach (var kvp in _projViews)
                if (kvp.Value != null) ReleaseProjectile(kvp.Value);   // в пул, а не в мусор: бой ещё будет
            _projViews.Clear();
            _projById.Clear();
            _lastShotDir.Clear();   // id юнитов в новом бою свои — старое направление не про них

            // Трупы прошлого боя (виды в секвенсе смерти, снятые из _views) — иначе висят в новом бою.
            for (int i = 0; i < _corpses.Count; i++)
                if (_corpses[i] != null) Destroy(_corpses[i].gameObject);
            _corpses.Clear();

            _finisherCandidate = -1;

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
            _statusOverlay.Initialize(_simulation, _playback, _overlayMode);
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
            // рисовать — ни симуляции, ни ленты ещё нет. Это не фолбэк на пустую зависимость: с пришедшим
            // Construct приходят обе разом, поэтому отдельной проверки на _playback не нужно.
            if (_simulation == null) return;

            // Единственный в кадре, кто двигает момент показа: иначе разные потребители увидели бы
            // разное «сейчас» в одном кадре.
            _playback.Advance(Time.deltaTime);

            // Состав юнитов на экране — тоже из кадра ленты, а не из событий сима: события спавна и
            // смерти приходят на окно опережения РАНЬШЕ, и вид появлялся бы за десять секунд до того,
            // как игрок увидит выход юнита на арену.
            if (_playback.TryGetFrame(out var frame, out var projectileFrame))
            {
                SyncViewsToFrame(frame);
                SyncProjectilesToFrame(projectileFrame);
            }

            // События отдаются ровно до показанного тика: цифра, звук и вспышка садятся на свой кадр.
            _dispatcher.PumpTo(_playback.ViewTick);

            // Доля берётся у ПОКАЗА, а не у боевого луча: она отсчитывается от показанного тика.
            float alpha = _playback.Alpha;

            foreach (var kvp in _views)
            {
                kvp.Value.UpdateInterpolation(alpha);
            }

        }

        /// <summary>
        /// Снаряды на экране — из кадра показа. По живому снаряду они улетали бы за окно опережения до
        /// выстрела и прилетали задолго до цифры урона.
        /// </summary>
        private void SyncProjectilesToFrame(IReadOnlyList<Combat.Tape.ProjectileSnapshot> frame)
        {
            _seenProj.Clear();
            for (int i = 0; i < frame.Count; i++)
            {
                Combat.Tape.ProjectileSnapshot p = frame[i];
                _seenProj.Add(p.Id);

                if (!_projViews.TryGetValue(p.Id, out ProjectileView view) || view == null)
                {
                    view = SpawnProjectileView(in p);
                    if (view == null) continue;
                }

                view.Follow(p.Position, p.PreviousPosition, p.Velocity, _playback.Alpha);
                if (p.TargetId >= 0 && p.Velocity.sqrMagnitude > 1e-8f)
                    _lastShotDir[p.TargetId] = p.Velocity.normalized;
            }

            // Исчез из кадра — значит попал или вышел за поле: вид снимается там же, где показан импакт.
            _deadProj.Clear();
            foreach (var kvp in _projViews)
                if (!_seenProj.Contains(kvp.Key)) _deadProj.Add(kvp.Key);

            for (int i = 0; i < _deadProj.Count; i++)
            {
                if (_projViews.TryGetValue(_deadProj[i], out var pv) && pv != null) ReleaseProjectile(pv);
                _projViews.Remove(_deadProj[i]);
            }
        }

        private ProjectileView SpawnProjectileView(in Combat.Tape.ProjectileSnapshot p)
        {
            if (_bulletPrefab == null) return null;

            // Визуальный старт — из ShotPoint (дула) источника, если его вид есть; иначе из позиции сима.
            Vector3 origin = (Vector3)(Vector2)p.Position;
            if (p.SourceId >= 0 && _views.TryGetValue(p.SourceId, out var srcView) && srcView != null)
            {
                origin = srcView.ShotPoint;

                // Muzzle-вспышка из дула по направлению полёта снаряда.
                if (_vfx != null && _feel != null)
                {
                    float ang = p.Velocity.sqrMagnitude > 1e-6f
                        ? Mathf.Atan2(p.Velocity.y, p.Velocity.x) * Mathf.Rad2Deg
                        : 0f;
                    _vfx.Spawn(_feel.VfxMuzzle, srcView.ShotPoint, ang, tint: VfxPaletteFor(p.SourceId));
                }
            }

            ProjectileView view = RentProjectile(origin);
            // Снаряд и его след — ЭФФЕКТ стрелка, значит его VFX-цвет, а не тинт тела.
            Color tint = p.SourceId >= 0 ? VfxColorFor(p.SourceId) : Color.white;
            view.BindVisual(tint, origin, p.Position, p.Velocity);
            _projViews[p.Id] = view;
            return view;
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
        private void HandleAbilityCast(int casterId)
        {
            if (!_views.TryGetValue(casterId, out var view) || view == null) return;

            view.PlayCastOutline(VfxColorFor(casterId));   // контур — один цвет, без разброса

            if (_vfx != null && _feel != null && _feel.VfxCastBurst != null)
                _vfx.Spawn(_feel.VfxCastBurst, view.HitPoint, tint: VfxPaletteFor(casterId));
        }

        /// <summary>
        /// Началась подготовка (M3): контур наливается ВСЮ подготовку, к моменту удара — на пике. Это
        /// подводка, а не пересказ: длительность объявляет симуляция, показ её только выдерживает.
        /// </summary>
        private void HandleAbilityCastStarted(int casterId, float seconds)
        {
            if (!_views.TryGetValue(casterId, out var view) || view == null) return;
            view.PlayCastCharge(VfxColorFor(casterId), seconds);
        }

        /// <summary>Каст оборван — подводка гаснет: обещанного удара не будет, и врать об этом нельзя.</summary>
        private void HandleAbilityCastInterrupted(int casterId)
        {
            if (!_views.TryGetValue(casterId, out var view) || view == null) return;
            view.CancelCastCharge();
        }

        /// <summary>
        /// Спавн в СИМУЛЯЦИИ — это ещё не выход на экран: показ дойдёт до этого тика через окно
        /// опережения. Поэтому здесь только запоминаем паспорт юнита (определение и команду), а вид
        /// создаётся из кадра показа в <see cref="SyncViewsToFrame"/>.
        /// </summary>
        private void HandleUnitSpawned(RuntimeUnit unit)
        {
            _identities[unit.Id] = new UnitIdentity(unit.Unit, unit.Team, unit.Id);
        }

        /// <summary>
        /// Привести состав видов к кадру показа: кто появился — создать, кому пора умереть — похоронить,
        /// остальным положить состояние их тика.
        /// </summary>
        private void SyncViewsToFrame(IReadOnlyList<Combat.Tape.UnitSnapshot> frame)
        {
            _frameIndex.Clear();
            for (int i = 0; i < frame.Count; i++) _frameIndex[frame[i].Id] = frame[i];

            for (int i = 0; i < frame.Count; i++)
            {
                Combat.Tape.UnitSnapshot snapshot = frame[i];

                if (!_views.TryGetValue(snapshot.Id, out UnitView view) || view == null)
                {
                    if (snapshot.IsDead) continue; // умер до того, как показ до него дошёл — вид не нужен
                    view = CreateView(in snapshot);
                    if (view == null) continue;
                }

                bool hasTarget = snapshot.TargetId >= 0
                                 && _frameIndex.TryGetValue(snapshot.TargetId, out Combat.Tape.UnitSnapshot target);
                view.SetState(in snapshot, hasTarget ? _frameIndex[snapshot.TargetId] : default, hasTarget);
            }

            // Смерть на экране наступает тогда, когда её показали: снимок мёртв либо юнита в кадре нет.
            _viewsToBury.Clear();
            foreach (var kvp in _views)
            {
                bool alive = _frameIndex.TryGetValue(kvp.Key, out Combat.Tape.UnitSnapshot s) && !s.IsDead;
                if (!alive) _viewsToBury.Add(kvp.Key);
            }
            for (int i = 0; i < _viewsToBury.Count; i++) BuryView(_viewsToBury[i]);
        }

        private UnitView CreateView(in Combat.Tape.UnitSnapshot snapshot)
        {
            if (!_identities.TryGetValue(snapshot.Id, out UnitIdentity identity)) return null;

            // Свой префаб персонажа (визуал/анимация/размер настроены ПРЯМО в нём); фолбэк — дефолтный
            // из презентера. Никакой рантайм-подмены визуала — префаб самодостаточен.
            GameObject prefabGo = identity.Definition != null && identity.Definition.ViewPrefab != null
                ? identity.Definition.ViewPrefab
                : (_unitViewPrefab != null ? _unitViewPrefab.gameObject : null);
            if (prefabGo == null) return null;

            var go = Instantiate(prefabGo, (Vector3)(Vector2)snapshot.Position, Quaternion.identity, transform);
            if (!go.TryGetComponent(out UnitView view))
            {
                Destroy(go);
                return null;
            }

            view.Bind(in snapshot, identity.Definition);
            view.ApplyFeelConfig(_feel); // параметры вспышки/сплющивания — из design-конфига
            view.ApplyAudio(_audio);     // хруст разлёта: вид сам знает, когда начинается shatter
            view.SetContactDustHandler(OnUnitContactDust);

            // Тинт тела: ступень приглушения различает тех, кто делит один спрайт. Плюс подпись над баром.
            view.SetTint(TintFor(in identity));
            view.SetLabel(NameFor(in identity));

            // «Свой» разброс цвета — осколкам смерти: роль в цвет превращает палитра, не вид.
            view.SetVfxSpread(VfxPaletteFor(snapshot.Id));

            // Цвет HP-бара по принадлежности к смотрящему (дизайн-система).
            view.SetHealthColor(_colorPalette.HealthBarColor(IsAllyOfViewer(identity.Team)));

            // Цвет щита — общий из палитры (не зависит от принадлежности).
            view.SetShieldColor(_colorPalette.Shield);

            _views[snapshot.Id] = view;
            return view;
        }

        private void BuryView(int unitId)
        {
            if (_views.TryGetValue(unitId, out var view))
            {
                if (view != null)
                {
                    view.OnDeath();
                    _corpses.Add(view); // труп доигрывает секвенс смерти сам; сносим при рестарте
                }
                _views.Remove(unitId);
            }
        }

        /// <summary>
        /// Смерть в СИМУЛЯЦИИ — ещё не смерть на экране: показ дойдёт до неё через окно опережения.
        /// Хоронит вид <see cref="SyncViewsToFrame"/>, когда мёртвый снимок доедет до кадра показа.
        /// </summary>
        private void HandleUnitDied(RuntimeUnit unit) { }

        private void HandleDamageDealt(int sourceId, int targetId, DamageResult result)
        {
            bool hasSource = TryGetShown(sourceId, out Combat.Tape.UnitSnapshot source);
            if (!TryGetShown(targetId, out Combat.Tape.UnitSnapshot target)) return;
            bool sourceIsMelee = IsMelee(sourceId);

            // Урон совпадает с кадром контакта (конец замаха): здесь — импакт-фидбэк цели.
            // Свинг источника запускается раньше, на OnAttackStarted (вики «14»).
            //
            // Направленный фидбэк (искры, отброс тела, выпад бьющего) полагается ТОЛЬКО прямому
            // попаданию: у тика яда и у ответки шипов нет ни момента, ни стороны, а рисуются они
            // так же часто, как удары — то есть дают шум там, где показывать нечего.
            // Удар, целиком съеденный щитом, — это БЛОК, и подаётся он вместо импакта, а не вместе с ним
            // (решение Макса 31.07.2026). Тело удара не приняло: ему незачем дёргаться, и искрам неоткуда
            // взяться. Остаётся синяя вспышка щита, цифра поглощённого, hitstop (вес у столкновения есть)
            // и выпад бьющего — он-то ударил.
            bool blocked = result.HpDamage <= 0f && result.ShieldDamage > 0f;

            Vector2 nudgeDir = Vector2.zero;
            if (hasSource && result.IsDirectHit)
            {
                // Мили: от бьющего к цели. Дальний: ОТКУДА ПРИЛЕТЕЛ СНАРЯД — вектор «стрелок → цель»
                // врёт ровно в тех случаях, ради которых это и делается (стрелок сместился за время
                // полёта, цель шагнула вбок). Снаряда в этот момент уже нет — берём запомненное.
                bool ranged = IsRanged(sourceId);
                if (ranged && _lastShotDir.TryGetValue(targetId, out var shotDir) && shotDir.sqrMagnitude > 1e-8f)
                {
                    nudgeDir = shotDir;
                }
                else
                {
                    Vector2 delta = target.Position - source.Position;
                    if (delta.sqrMagnitude > 1e-8f) nudgeDir = delta.normalized;
                }
            }

            if (_views.TryGetValue(targetId, out var view) && view != null)
            {
                Color flash = _feel != null
                    ? _feel.ResolveHitFlashColor(result.Type)
                    : Color.white;

                // Блок вспыхивает ЦВЕТОМ ЩИТА: иначе «пробил» и «не пробил» выглядят одинаково, а разница
                // между ними — самое интересное, что есть в этом ударе.
                if (blocked && _colorPalette != null) flash = _colorPalette.Shield;

                // Отброс тела — только тому, кто удар ПРИНЯЛ. Заблокировавший стоит: он для того щит и
                // поднимал, и дёрнувшееся тело читалось бы как пробитие.
                view.OnDamageReceived(flash, blocked ? Vector2.zero : nudgeDir);
            }

            // Доля HP-урона от MaxHP цели — общий «вес удара» для hitstop и размера цифры.
            float maxHp = target.MaxHP;
            float frac  = maxHp > 0f ? result.HpDamage / maxHp : 0f;

            // Локальный hitstop пары «источник + цель» по значимости удара (кривая в feel-конфиге).
            if (view != null && _feel != null)
            {
                float stop = _feel.EvaluateHitstopSeconds(frac);
                view.OnHitstop(stop);
                if (hasSource && _views.TryGetValue(sourceId, out var sourceView) && sourceView != null)
                {
                    sourceView.OnHitstop(stop);
                    if (nudgeDir.sqrMagnitude > 1e-8f)
                        sourceView.OnAttackLunge(nudgeDir);
                }
            }

            // Кандидат в финишеры: автор добивающего удара, если он мили (снаряд/яд позу удара не держат).
            if (result.KilledTarget)
                _finisherCandidate = sourceIsMelee ? sourceId : -1;

            int shield = Mathf.RoundToInt(result.ShieldDamage);
            int hp     = Mathf.RoundToInt(result.HpDamage);

            // Цифры — в точку попадания (грудь) цели. Размер HP-цифры растёт с весом удара (тяжёлый = крупнее).
            Vector3 anchor  = AnchorFor(targetId, target.Position);
            float   hpScale = Mathf.Lerp(1f, _feel.NumberMaxScale, Mathf.Clamp01(frac / Mathf.Max(1e-4f, _feel.NumberFullFrac)));

            // VFX-префабы: искры в точку попадания + пыль у ног на мили-ударе. Только прямое попадание:
            // яд, горение и шипы брони бьют тиками и без стороны — искры на них читались бы как удары.
            // Блок искр не даёт вовсе: они рисуют удар, ВОШЕДШИЙ в тело, а этот в тело не вошёл.
            if (_vfx != null && _feel != null && view != null && result.IsDirectHit && !blocked)
            {
                // Искры летят ПРОЧЬ ОТ УДАРА: у мили — от бьющего, у стрелка — по траектории снаряда.
                // Направление берём то же, что и отброс тела: один удар — один вектор, спорить им не о чем.
                float? sparkDir = nudgeDir.sqrMagnitude > 1e-8f
                    ? Mathf.Atan2(nudgeDir.y, nudgeDir.x) * Mathf.Rad2Deg
                    : (float?)null;

                _vfx.Spawn(_feel.VfxHitSpark, anchor, sparkDir,
                           _feel.EvaluateHitVfxIntensity(frac), _feel.EvaluateHitVfxCount(frac),
                           VfxPaletteFor(sourceId));   // искры — палитры бьющего

                if (sourceIsMelee)
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

            // Событие несёт id и данные ПОКАЗАННОГО тика: потребители (джус, тряска, звук) иначе
            // прочитали бы позицию и HP из будущего.
            _damageDealtPublisher.Publish(new DamageDealtEvent(
                sourceId, targetId, target.Position, target.MaxHP, result));
        }

        private void HandleHealed(int sourceId, int targetId, float amount)
        {
            if (!TryGetShown(targetId, out Combat.Tape.UnitSnapshot target)) return;

            // Хил-цифра в точку попадания цели (+N). Мелкие тики регена округляются в 0 и не спамят.
            int healed = Mathf.RoundToInt(amount);
            if (healed <= 0) return;

            SpawnNumber(AnchorFor(targetId, target.Position), "+" + healed, _healColor);

            if (_views.TryGetValue(targetId, out var tView) && tView != null)
            {
                tView.OnHealed();                                    // тело отвечает на лечение, а не только цифра
                if (_vfx != null && _feel != null)
                    _vfx.Spawn(_feel.VfxHeal, tView.HitPoint, tint: VfxPaletteFor(sourceId));  // палитра лечащего
            }
        }

        private void HandleAttackEvaded(int targetId)
        {
            if (!TryGetShown(targetId, out Combat.Tape.UnitSnapshot target)) return;

            // Полный негейт удара («Изворотливость») — урона нет, показываем «evade».
            // Движение тела сюда НЕ добавляем: по дизайну трата заряда — это кувырок с уходом с места,
            // то есть настоящее перемещение в симуляции (gdd/20-combat/positioning). Появится оно — вид
            // поедет за ним сам.
            SpawnNumber(AnchorFor(targetId, target.Position), "evade", _evadeColor);
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
        private Vector3 AnchorFor(int unitId, Vector2 shownPosition)
        {
            if (_views.TryGetValue(unitId, out var v) && v != null)
                return v.HitPoint;
            return (Vector3)shownPosition + Vector3.up * 0.4f;
        }

        /// <summary>Снимок юнита на ПОКАЗАННОМ тике. <c>false</c> — его в этом кадре нет.</summary>
        private bool TryGetShown(int unitId, out Combat.Tape.UnitSnapshot snapshot)
        {
            if (unitId >= 0 && _frameIndex.TryGetValue(unitId, out snapshot)) return true;
            snapshot = default;
            return false;
        }

        private bool IsMelee(int unitId) =>
            _identities.TryGetValue(unitId, out UnitIdentity id) && id.Definition != null
            && id.Definition.AttackType == AttackType.Melee;

        private bool IsRanged(int unitId) =>
            _identities.TryGetValue(unitId, out UnitIdentity id) && id.Definition != null
            && id.Definition.AttackType == AttackType.Ranged;

        /// <summary>Палитра эффектов по id — из паспорта, потому что живого юнита здесь уже нет.</summary>
        private Gradient VfxPaletteFor(int unitId) =>
            _colorPalette != null && _identities.TryGetValue(unitId, out UnitIdentity id) && id.Definition != null
                ? _colorPalette.UnitSpread(id.Definition.VfxTone)
                : null;

        private Color VfxColorFor(int unitId) =>
            _colorPalette != null && _identities.TryGetValue(unitId, out UnitIdentity id) && id.Definition != null
                ? _colorPalette.UnitMain(id.Definition.VfxTone)
                : Color.white;

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
        /// Паспорт юнита: неизменная за бой часть — определение, команда, id. Вид создаётся из кадра
        /// показа, когда живого юнита уже может не быть под рукой, поэтому «кто это» запоминается
        /// отдельно от «что с ним сейчас».
        /// </summary>
        private readonly struct UnitIdentity
        {
            public readonly Data.Definitions.UnitData Definition;
            public readonly int Team;
            public readonly int Id;

            public UnitIdentity(Data.Definitions.UnitData definition, int team, int id)
            {
                Definition = definition;
                Team       = team;
                Id         = id;
            }
        }

        /// <summary>
        /// Тинт тела по персонажу: ступень приглушения из данных, цвет — из палитры проекта (тот же путь,
        /// которым красится карточка инвентаря). У болванчиков без данных — по стороне смотрящего: это
        /// дев-стенд без контента, там различить своих и чужих больше нечем.
        /// </summary>
        private Color TintFor(RuntimeUnit unit) =>
            unit.Unit != null
                ? BodyTintOf(unit.Unit)
                : (IsAllyOfViewer(unit) ? new Color(0.7f, 0.8f, 1f) : new Color(1f, 0.7f, 0.7f));

        /// <summary>То же, но по паспорту: так вид красится, когда создаётся из кадра показа.</summary>
        private Color TintFor(in UnitIdentity identity) =>
            identity.Definition != null
                ? BodyTintOf(identity.Definition)
                : (IsAllyOfViewer(identity.Team) ? new Color(0.7f, 0.8f, 1f) : new Color(1f, 0.7f, 0.7f));

        // Ступень → цвет. Без палитры не гадаем: белый значит «арт как нарисован», и это честнее пурпура,
        // потому что тинт по умолчанию и есть «не красим» — большинство юнитов носит None.
        private Color BodyTintOf(UnitData definition) =>
            _colorPalette != null ? _colorPalette.BodyTint(definition.BodyShade) : Color.white;

        /// <summary>
        /// Цвета ЭФФЕКТОВ юнита. Их два, и они про разное: ГЛАВНЫЙ цвет — там, где цвет один (тело снаряда,
        /// его след, контур каста), ПАЛИТРА — диапазон разброса для частиц. Оба выводятся из ОДНОЙ роли на
        /// <c>UnitData</c>, а не живут в префабах эффектов: иначе холод криоманта и свет пастыря выглядели бы
        /// одинаково просто потому, что летят из одного префаба.
        /// <para>Пыль под ногами сюда НЕ входит: она принадлежит земле, а не бойцу.</para>
        /// </summary>
        private Gradient VfxPaletteFor(RuntimeUnit unit) =>
            _colorPalette != null && unit?.Unit != null ? _colorPalette.UnitSpread(unit.Unit.VfxTone) : null;

        /// <summary>Главный цвет эффектов юнита — для того, у чего нет ни длины, ни россыпи.</summary>
        private Color VfxColorFor(RuntimeUnit unit) =>
            _colorPalette != null && unit?.Unit != null
                ? _colorPalette.UnitMain(unit.Unit.VfxTone)
                : TintFor(unit);

        /// <summary>
        /// Юнит на стороне смотрящего? Единственное место, где в презентере решается «свой/чужой».
        /// Без <see cref="Core.Players.ILocalPlayer"/> (сцена без DI, дев-запуск) считаем команду 0 своей.
        /// </summary>
        private bool IsAllyOfViewer(RuntimeUnit unit) => IsAllyOfViewer(unit.Team);

        /// <summary>Та же проверка по номеру команды — для пути, где живого юнита нет.</summary>
        private bool IsAllyOfViewer(int team) =>
            team == (_localPlayer != null ? _localPlayer.Team : 0);

        /// <summary>Подпись персонажа: имя реликвии (SO) либо «Ally/Enemy N» для болванчиков.</summary>
        private string NameFor(RuntimeUnit unit)
        {
            if (unit.Unit != null) return unit.Unit.name;
            return (IsAllyOfViewer(unit) ? "Ally " : "Enemy ") + unit.Id;
        }

        /// <summary>Та же подпись по паспорту.</summary>
        private string NameFor(in UnitIdentity identity)
        {
            if (identity.Definition != null) return identity.Definition.name;
            return (IsAllyOfViewer(identity.Team) ? "Ally " : "Enemy ") + identity.Id;
        }

        private void HandleAttackStarted(int sourceId, int targetId)
        {
            // Вход в замах: запускаем анимацию свинга у источника (вики «14»).
            if (!_views.TryGetValue(sourceId, out var sourceView) || sourceView == null) return;
            if (!TryGetShown(sourceId, out Combat.Tape.UnitSnapshot source)) return;

            Vector2 away = Vector2.zero;
            if (TryGetShown(targetId, out Combat.Tape.UnitSnapshot target))
            {
                Vector2 delta = source.Position - target.Position; // от цели = назад
                if (delta.sqrMagnitude > 1e-8f) away = delta.normalized;
            }
            sourceView.OnAttackStarted(away);
        }

        private void HandleAttackInterrupted(int unitId)
        {
            if (_views.TryGetValue(unitId, out var view) && view != null)
                view.OnAttackInterrupted();
        }

        private void HandleBattleEnded(BattleOutcome outcome)
        {
            // Финишер-мили держит кадр контакта весь финальный slowmo (перекрывает free-run у него).
            UnitView finisher = null;
            if (_finisherCandidate >= 0 && _views.TryGetValue(_finisherCandidate, out finisher) && finisher != null)
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
