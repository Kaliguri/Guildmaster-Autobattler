using Guildmaster.Combat;
using Guildmaster.Combat.Commands;
using Guildmaster.Core.Input;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Guildmaster.Presentation;
using MessagePipe;
using QFSW.QC;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using VContainer;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Набор отладочных команд Quantum Console для Фазы 1 (вики «10» §9).
    /// Инъектируется через VContainer — зависимости появляются только при наличии активной симуляции.
    /// </summary>
    public sealed class GuildmasterCommands : MonoBehaviour
    {
        // Ссылок на семь конкретных реликвий в инспекторе больше нет: релик дев-среза резолвится по id
        // из контент-БД — тем же способом, что и болванчик строкой ниже. Семь serialized-полей означали,
        // что переименование или замена ассета ломает команду молча, а сцена помнит контент (2026-07-26).

        [Tooltip("Тот же SimTuningConfig, что и на CombatLifetimeScope — для gm_tuning_rebake (QC).")]
        [SerializeField] private SimTuningConfig _simTuningConfig;

        private CombatSimulation   _simulation;
        private CombatDebugDraw    _debugDraw;
        private RuntimeUnitFactory _factory;
        private IInputService      _input;
        private QuantumConsole     _console;
        private Guildmaster.Game.Flow.IBattleSession _session; // опц.: перезапуск боя забега на R (null в standalone-арене)

        // Ристалище: интент входа и состояние площадки. Живут ВЫШЕ боевого скоупа (Root), поэтому
        // резолвятся опционально — в standalone-арене без Root их нет, и команда честно об этом скажет.
        private Core.Flow.IRunControl _runControl;
        private MessagePipe.IPublisher<Core.Flow.OpenProvingGroundsRequest> _provingGroundsPub;
        private MessagePipe.ISubscriber<Data.Definitions.TestZoneChangedEvent> _provingGroundsChangedSub;

        // Заказ состава площадки: на Ристалище бойцов ставит расстановка, а команда только говорит, каких.
        // Спавнить их самим нельзя — см. StageOnProvingGrounds.
        private MessagePipe.IPublisher<Data.Definitions.ProvingGroundsSetupRequest> _groundsSetupPub;
        private System.IDisposable _provingGroundsSubscription;
        private bool _onProvingGrounds; // площадка открыта прямо сейчас — повторный запрос не нужен

        // Дамми-болванчики оформлены как полноценный юнит (EnemyData «enemy.training_dummy»): свой SO,
        // визуал MedievalWarrior (→ анимации). Резолвится из контент-БД, поэтому не нужен serialized-ref в сцене.
        private UnitData _dummyEnemy;

        // Дев-дуэлянт на скелетном визуале (EnemyData «enemy.bone_dev»): отдельный ассет ради того, чтобы
        // смотр анимации не требовал подмены вида у игровой реликвии. Бьёт и умирает — иначе не увидеть ни
        // удар, ни разлёт на осколки.
        private UnitData _boneDuelist;

        // Контент-БД для дев-срезов: релик берётся по id (relic.*) в момент вызова команды.
        private IContentDatabase _content;

        // Снапшот арены: по зонам расстановки команды считают КРАЯ поля, а не хардкодят координаты —
        // «максимально далеко друг от друга» на разных аренах означает разные числа.
        private Core.Arena.ArenaLayoutData _arena;

        // Открыта ли консоль сейчас: пока да — глушим наш игровой ввод (кроме F5), чтобы набор
        // команд в консоли не протекал в геймплей (пауза/смена вида/пан-зум/перезапуск боя).
        private bool _consoleOpen;

        // Последний сетап боя для быстрого перезапуска по R. static — переживает релоад сцены (F5),
        // чтобы R после F5 всё ещё знал последний бой.
        private static System.Action<GuildmasterCommands> _lastBattleSetup;

        /// <summary>
        /// Задать «последний бой» для R извне (dev-панель энкаунтеров) — единый владелец R остаётся здесь,
        /// а внешний источник просто регистрирует свой рестарт. Делегат должен резолвить живой скоуп сам
        /// (переживает F5). Перекрывается следующим gm_spawn_* (last-write-wins).
        /// </summary>
        public static void SetLastBattle(System.Action<GuildmasterCommands> setup) => _lastBattleSetup = setup;

        [Inject]
        public void Construct(CombatSimulation simulation, CombatDebugDraw debugDraw, RuntimeUnitFactory factory,
            IInputService input, IContentDatabase contentDatabase, Core.Arena.ArenaLayoutData arena,
            IObjectResolver resolver)
        {
            _simulation = simulation;
            _debugDraw  = debugDraw;
            _factory    = factory;
            _input      = input;
            _content = contentDatabase;
            _arena   = arena;
            contentDatabase.TryGet("enemy.training_dummy", out _dummyEnemy);
            contentDatabase.TryGet("enemy.bone_dev", out _boneDuelist);
            // Сессия боя живёт в RootScope: в реальном забеге резолвится, в standalone dev-арене (без Root) — null.
            resolver.TryResolve(out _session);
            resolver.TryResolve(out _runControl);
            resolver.TryResolve(out _provingGroundsPub);
            resolver.TryResolve(out _provingGroundsChangedSub);
            resolver.TryResolve(out _groundsSetupPub);
            _provingGroundsSubscription = _provingGroundsChangedSub?.Subscribe(e => OnProvingGroundsChanged(e));
        }

        // Пауза сима, пока консоль открыта: настраиваешь бой за консолью, закрываешь — он идёт с начала
        // на виду (без этого бой проигрывается за полноэкранной консолью и заканчивается невидимым).
        private void Start()
        {
            _console = FindAnyObjectByType<QuantumConsole>(FindObjectsInactive.Include);
            if (_console != null)
            {
                _console.OnActivate   += PauseForConsole;
                _console.OnDeactivate += ResumeAfterConsole;
            }
        }

        // Состояние площадки: открыта ли она прямо сейчас. Ставить по этому событию бой больше нечего —
        // состав площадки заказывается ДО входа и применяется её собственным владельцем (см. StageOnProvingGrounds).
        private void OnProvingGroundsChanged(Data.Definitions.TestZoneChangedEvent e) => _onProvingGrounds = e.Active;

        private void OnDestroy()
        {
            _provingGroundsSubscription?.Dispose();
            if (_console != null)
            {
                _console.OnActivate   -= PauseForConsole;
                _console.OnDeactivate -= ResumeAfterConsole;
            }
        }

        // Пауза сима, которая была ДО открытия консоли. Консоль паузой не владеет — она её одалживает:
        // владелец (расстановка, тумблер Space) мог поставить паузу задолго до нас, и снимать её за него
        // нельзя. Ровно это и стреляло: команда вводила игрока в расстановку Ристалища (пауза), а
        // закрытие консоли её снимало — бой начинался сам, без нажатия «Начать».
        private bool _pausedBeforeConsole;

        // Консоль открыта: пауза сима (настраиваешь бой за консолью, закрываешь — он идёт с начала на
        // виду) + глушим игровой ввод, чтобы буквы команд не текли в геймплей.
        private void PauseForConsole()
        {
            _consoleOpen = true;
            _pausedBeforeConsole = _simulation != null && _simulation.IsPaused;
            _simulation?.SetPaused(true);
            if (_input != null) _input.SetSuppressed(Core.Input.InputSuppressSource.DevConsole, true);
        }

        private void ResumeAfterConsole()
        {
            _consoleOpen = false;
            // Паузу возвращаем ВЛАДЕЛЬЦУ, а не «снимаем». В расстановке владелец — она сама: мир там стоит
            // по определению, и бой начинает кнопка «Начать», а не закрытая консоль. Фазу спрашиваем, а не
            // помним: команда могла увести игрока в расстановку уже ПОСЛЕ открытия консоли — ровно так и
            // делает gm_proving_grounds.
            bool deploying = _session != null && _session.Phase == Data.Definitions.BattlePhase.Deployment;
            _simulation?.SetPaused(deploying || _pausedBeforeConsole);
            if (_input != null) _input.SetSuppressed(Core.Input.InputSuppressSource.DevConsole, false);
        }

        // Dev-хоткеи (new Input System): F5 — полный релоад сцены (пустая арена), R — рестарт боя НА МЕСТЕ.
        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // F5 работает всегда (жёсткий сброс сцены) — даже с открытой консолью.
            if (kb.f5Key.wasPressedThisFrame) Restart();

            // R (перезапуск боя) глушим, пока консоль открыта: иначе буква «r» в команде дёргает рестарт.
            // Dev-спавн (gm_spawn_*) задал последний бой → перезапускаем его; иначе это бой ЗАБЕГА (грузится
            // BattleFlow→BattleBootstrap, dev-сетап пуст) → перезапуск на месте через сессию.
            if (!_consoleOpen && kb.rKey.wasPressedThisFrame)
            {
                if (_lastBattleSetup != null) RestartLastBattle();
                else if (_session == null || !_session.RestartInPlace()) RestartLastBattle(); // варнинг «нет боя»
            }
        }

        /// <summary>Зафиксировать сид боя для детерминизм-отладки (только до старта).</summary>
        [Command("gm_rng_seed", "Зафиксировать сид боя (до старта симуляции)")]
        public void SetRngSeed(ulong seed)
        {
            Debug.Log($"[GuildmasterCommands] - gm_rng_seed {seed}: изменение сида поддерживается только через CombatLifetimeScope до запуска");
        }

        /// <summary>Поднять тест-бой N×M юнитов с заданными HP.</summary>
        [Command("gm_spawn_battle", "Запустить тест-бой N юнитов за каждую сторону")]
        public void SpawnBattle(int countPerTeam = 2)
        {
            if (!SimReady()) return;
            if (!FactoryReady()) return;

            ResetForNewBattle();

            for (int i = 0; i < countPerTeam; i++)
            {
                _simulation.EnqueueUnitSpawn(MakeDummy(0, new Vector2(-5f + i, i)));
                _simulation.EnqueueUnitSpawn(MakeDummy(1, new Vector2( 5f - i, i)));
            }

            _lastBattleSetup = self => self.SpawnBattle(countPerTeam);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_battle: добавлено {countPerTeam}×2 болванчиков");
        }

        /// <summary>
        /// Плотный «клубок» юнитов обеих команд для теста расталкивания (SeparationSystem, коллизия).
        /// Спавнит сеткой с шагом МЕНЬШЕ диаметра тела → юниты сразу перекрываются и разъезжаются; высокий
        /// HP и низкий урон держат толпу живой, чтобы видеть spacing и при сшибке блобов в центре.
        /// Крути на глаз <c>SimTuningConfig</c> (Strength / Iterations / BodyRadiusPerSize) или live gm_sep_*,
        /// перезапуск на месте — R. Параметр <paramref name="size"/> — «толщина» тел (Size-стат).
        /// </summary>
        [Command("gm_spawn_crowd", "Плотный клубок обеих команд для теста коллизии/расталкивания")]
        public void SpawnCrowd(int perTeam = 8)
        {
            if (!SimReady()) return;
            if (!FactoryReady()) return;

            ResetForNewBattle();

            int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(perTeam)));
            const float spacing = 0.15f; // << диаметра тела (~0.5 при Size 1) → перекрытие на старте

            for (int i = 0; i < perTeam; i++)
            {
                int cx = i % cols, cy = i / cols;
                float ox = (cx - (cols - 1) * 0.5f) * spacing;
                float oy = (cy - (cols - 1) * 0.5f) * spacing;
                _simulation.EnqueueUnitSpawn(MakeDummy(0, new Vector2(-3f + ox, oy)));
                _simulation.EnqueueUnitSpawn(MakeDummy(1, new Vector2( 3f + ox, oy)));
            }

            _lastBattleSetup = self => self.SpawnCrowd(perTeam);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_crowd: {perTeam}×2 болванчиков, плотный клубок");
        }

        /// <summary>Показать текущие параметры расталкивания (SeparationSystem).</summary>
        [Command("gm_sep", "Показать параметры расталкивания (радиус/сила/итерации)")]
        public void SepInfo()
        {
            if (!SimReady()) return;
            var s = _simulation.Separation;
            Debug.Log($"[GuildmasterCommands] - gm_sep: BodyRadiusPerSize={s.BodyRadiusPerSize} (⌀ при Size1 = {s.BodyRadiusPerSize * 2f}), Strength={s.Strength}, Iterations={s.Iterations}, SameTeamScale={s.SameTeamScale}");
        }

        /// <summary>Радиус тела на единицу Size (0.25 = ⌀0.5 при Size1). Крути под ширину спрайта.</summary>
        [Command("gm_sep_radius", "Радиус тела на единицу Size (live)")]
        public void SepRadius(float radiusPerSize)
        {
            if (!SimReady()) return;
            _simulation.Separation.BodyRadiusPerSize = Mathf.Max(0.01f, radiusPerSize);
            SepInfo();
        }

        /// <summary>Сила расталкивания за тик (0..1; 1 = жёстко, мягче = плавнее). Live.</summary>
        [Command("gm_sep_strength", "Сила расталкивания за тик (live)")]
        public void SepStrength(float strength)
        {
            if (!SimReady()) return;
            _simulation.Separation.Strength = Mathf.Clamp(strength, 0f, 1f);
            SepInfo();
        }

        /// <summary>Проходов расталкивания за тик (больше = жёстче/дороже). Live.</summary>
        [Command("gm_sep_iters", "Проходов расталкивания за тик (live)")]
        public void SepIters(int iterations)
        {
            if (!SimReady()) return;
            _simulation.Separation.Iterations = Mathf.Max(1, iterations);
            SepInfo();
        }

        /// <summary>Множитель расталкивания СВОИХ (0..1): меньше = свои расступаются мягче, задние просачиваются к фронту. Live.</summary>
        [Command("gm_sep_ally", "Мягкость расталкивания своих (0..1, live)")]
        public void SepAlly(float scale)
        {
            if (!SimReady()) return;
            _simulation.Separation.SameTeamScale = Mathf.Clamp01(scale);
            SepInfo();
        }

        /// <summary>Заспавнить «Железного копейщика» (team 0) против кластера болванчиков (team 1) — срез шага 4.</summary>
        [Command("gm_spawn_spearman", "Заспавнить Железного копейщика против кластера (срез шага 4)")]
        public void SpawnSpearman(int enemies = 3)
        {
            if (!SimReady()) return;
            if (!FactoryReady()) return;
            RelicData relic = DevRelic("relic.iron_spearman");
            if (relic == null) return;

            ResetForNewBattle();

            // Копейщик слева — через фабрику (реальный путь сборки: статы/линейная АА/активка/AI-профиль/мана).
            _simulation.EnqueueUnitSpawn(_factory.Create(relic, null, team: 0, new Vector2(-5f, 0f)));

            // Кластер болванчиков справа — чтобы линейная АА задевала нескольких и сработало условие «≥2 в радиусе».
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 0.8f; // компактно по вертикали
                _simulation.EnqueueUnitSpawn(MakeDummy(1, new Vector2(5f, y)));
            }

            _lastBattleSetup = self => self.SpawnSpearman(enemies);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_spearman: копейщик vs {enemies} болванчиков");
        }

        /// <summary>Заспавнить «Светлого пастыря» (team 0) + раненых союзников против болванчиков — срез §10.1.</summary>
        [Command("gm_spawn_shepherd", "Заспавнить Светлого пастыря + раненых союзников против болванчиков (срез §10.1)")]
        public void SpawnShepherd(int allies = 2)
        {
            if (!SimReady()) return;
            if (!FactoryReady()) return;
            RelicData relic = DevRelic("relic.light_shepherd");
            if (relic == null) return;

            ResetForNewBattle();

            // Пастырь в тылу слева — через фабрику (реальный путь: AI-профиль Heal, хил-снаряд, активка «Длань жизни»).
            _simulation.EnqueueUnitSpawn(_factory.Create(relic, null, team: 0, new Vector2(-6f, 0f)));

            // Раненые союзники-болванчики (team 0) на фронте: старт на 40% HP — видно выбор раненого и хил-снаряды.
            for (int i = 0; i < allies; i++)
            {
                float y = (i - (allies - 1) * 0.5f) * 1.2f;
                var ally = MakeDummy(0, new Vector2(-3f, y));
                ally.CurrentHP = ally.Stats.Get(StatType.MaxHP) * 0.4f; // 40% — есть кого лечить
                _simulation.EnqueueUnitSpawn(ally);
            }

            // Пара болванчиков справа (team 1) — чтобы союзники завязли в бою и просаживались под «Длань».
            for (int i = 0; i < 2; i++)
            {
                float y = (i - 0.5f) * 1.2f;
                _simulation.EnqueueUnitSpawn(MakeDummy(1, new Vector2(4f, y)));
            }

            _lastBattleSetup = self => self.SpawnShepherd(allies);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_shepherd: пастырь + {allies} раненых союзника vs 2 болванчика");
        }

        /// <summary>Заспавнить «Криоманта» (team 0) против кластера болванчиков (team 1) — срез §10.2.</summary>
        [Command("gm_spawn_cryomancer", "Заспавнить Криоманта против кластера болванчиков (срез §10.2)")]
        public void SpawnCryomancer(int enemies = 3)
        {
            if (!SimReady()) return;
            if (!FactoryReady()) return;
            RelicData relic = DevRelic("relic.cryomancer");
            if (relic == null) return;

            ResetForNewBattle();

            // Криомант в тылу слева — через фабрику (реальный путь: on-hit «Заморозка», масс-стан «Ледяные оковы», AI PreferUntagged).
            _simulation.EnqueueUnitSpawn(_factory.Create(relic, null, team: 0, new Vector2(-6f, 0f)));

            // Кластер болванчиков справа: пока Криомант раздаёт «Заморозку», их накапливается ≥2 → срабатывают «Ледяные оковы».
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 1f;
                _simulation.EnqueueUnitSpawn(MakeDummy(1, new Vector2(4f, y)));
            }

            _lastBattleSetup = self => self.SpawnCryomancer(enemies);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_cryomancer: криомант vs {enemies} болванчиков");
        }

        /// <summary>Заспавнить «Надёжного защитника» (team 0) против ударных болванчиков (team 1) — срез §10.3.</summary>
        [Command("gm_spawn_defender", "Заспавнить Надёжного защитника против ударных болванчиков (срез §10.3)")]
        public void SpawnDefender(int enemies = 3)
        {
            if (!SimReady()) return;
            if (!FactoryReady()) return;
            RelicData relic = DevRelic("relic.defender");
            if (relic == null) return;

            ResetForNewBattle();

            // Защитник по центру-слева — через фабрику (реальный путь: пассив «Оплот» pre-damage, HighestThreat, ульта).
            _simulation.EnqueueUnitSpawn(_factory.Create(relic, null, team: 0, new Vector2(-4f, 0f)));

            // Болванчики справа бьют защитника. «Оплот» поднимает щит на ЛЮБОЙ удар (PassiveTrigger.AnyHit, внутр. КД 4с).
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 1.2f;
                _simulation.EnqueueUnitSpawn(MakeDummy(1, new Vector2(4f, y)));
            }

            _lastBattleSetup = self => self.SpawnDefender(enemies);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_defender: защитник vs {enemies} ударных болванчиков");
        }

        /// <summary>Заспавнить «Лесного следопыта» (team 0) против кластера болванчиков (team 1) — срез §10.4.</summary>
        [Command("gm_spawn_ranger", "Заспавнить Лесного следопыта против кластера болванчиков (срез §10.4)")]
        public void SpawnRanger(int enemies = 3)
        {
            if (!SimReady()) return;
            if (!FactoryReady()) return;
            RelicData relic = DevRelic("relic.ranger");
            if (relic == null) return;

            ResetForNewBattle();

            // Следопыт слева — через фабрику (реальный путь: кайт, стрельба на ходу, «Метка охотника» с переносом).
            _simulation.EnqueueUnitSpawn(_factory.Create(relic, null, team: 0, new Vector2(-6f, 0f)));

            // Кластер болванчиков справа лезет в ближний бой — видно кайт (отход) и стрельбу на ходу.
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 1.2f;
                _simulation.EnqueueUnitSpawn(MakeDummy(1, new Vector2(4f, y)));
            }

            _lastBattleSetup = self => self.SpawnRanger(enemies);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_ranger: следопыт vs {enemies} болванчиков");
        }

        /// <summary>Заспавнить «Скрытного убийцу» (team 0) против болванчиков (team 1) — срез §10.5.</summary>
        [Command("gm_spawn_assassin", "Заспавнить Скрытного убийцу против болванчиков (срез §10.5)")]
        public void SpawnAssassin(int enemies = 3)
        {
            if (!SimReady()) return;
            if (!FactoryReady()) return;
            RelicData relic = DevRelic("relic.assassin");
            if (relic == null) return;

            ResetForNewBattle();

            // Убийца слева — через фабрику (реальный путь: пассивы «Скрытность» + «Изворотливость» из GrantedEffects,
            // усиленный первый удар, негейт крупных ударов, рестелс после убийства).
            _simulation.EnqueueUnitSpawn(_factory.Create(relic, null, team: 0, new Vector2(-5f, 0f)));

            // Болванчики справа. «Изворотливость» гейтит ЛЮБУЮ автоатаку независимо от размера урона (PassiveTrigger.AnyHit).
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 1.2f;
                _simulation.EnqueueUnitSpawn(MakeDummy(1, new Vector2(4f, y)));
            }

            _lastBattleSetup = self => self.SpawnAssassin(enemies);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_assassin: убийца vs {enemies} болванчиков");
        }

        /// <summary>Заспавнить «Монаха вихря» (team 0) против кластера болванчиков (team 1) — срез §10.6.</summary>
        [Command("gm_spawn_monk", "Заспавнить Монаха вихря против болванчиков (срез §10.6)")]
        public void SpawnMonk(int enemies = 4)
        {
            if (!SimReady()) return;
            if (!FactoryReady()) return;
            RelicData relic = DevRelic("relic.whirl_monk");
            if (relic == null) return;

            ResetForNewBattle();

            // Монах слева — через фабрику (реальный путь: рывок → фиксация → отбрасывание → телепорт, §10.6).
            _simulation.EnqueueUnitSpawn(_factory.Create(relic, null, team: 0, new Vector2(-6f, 0f)));

            // Болванчики справа — раскиданы ХАОТИЧНО (детерминированный хэш по индексу, чтобы R повторял ту же
            // расстановку), далеко друг от друга: видно заход к конкретной цели и УГЛОВОЙ цепной толчок «ядра»,
            // а не ровный ряд. x∈[2,8], y∈[-3.5,3.5].
            for (int i = 0; i < enemies; i++)
            {
                float hx = Frac(Mathf.Sin((i + 1) * 12.9898f) * 43758.5453f);
                float hy = Frac(Mathf.Sin((i + 1) * 78.233f)  * 43758.5453f);
                var pos = new Vector2(2f + hx * 6f, -3.5f + hy * 7f);
                _simulation.EnqueueUnitSpawn(MakeDummy(1, pos));
            }

            _lastBattleSetup = self => self.SpawnMonk(enemies);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_monk: монах vs {enemies} болванчиков (хаос)");
        }

        // Дробная часть — детерминированный «хэш» [0,1) для хаотичной, но воспроизводимой расстановки.
        private static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>Выставить HP юниту по ID.</summary>
        [Command("gm_set_hp", "Выставить HP юниту по ID")]
        public void SetHp(int unitId, float hp)
        {
            if (_simulation == null) return;

            for (int i = 0; i < _simulation.Units.Count; i++)
            {
                var unit = _simulation.Units[i];
                if (unit.Id == unitId)
                {
                    unit.CurrentHP = Mathf.Max(0f, hp);
                    Debug.Log($"[GuildmasterCommands] - gm_set_hp: юнит {unitId} HP = {unit.CurrentHP}");
                    return;
                }
            }

            Debug.LogWarning($"[GuildmasterCommands] - gm_set_hp: юнит {unitId} не найден");
        }

        /// <summary>Мгновенно завершить бой (убить всех из команды 1).</summary>
        [Command("gm_skip_battle", "Мгновенно завершить бой в пользу команды A")]
        public void SkipBattle()
        {
            if (_simulation == null) return;

            for (int i = 0; i < _simulation.Units.Count; i++)
            {
                var unit = _simulation.Units[i];
                if (unit.Team == 1) unit.CurrentHP = -1f;
            }

            Debug.Log("[GuildmasterCommands] - gm_skip_battle: все юниты команды B убиты");
        }

        /// <summary>Перезагрузить боевую сцену для нового прогона (бой одноразовый: после конца loop останавливается).</summary>
        [Command("gm_restart", "Перезапустить бой (перезагрузка сцены)")]
        public void Restart()
        {
            Scene active = SceneManager.GetActiveScene();
            Debug.Log($"[GuildmasterCommands] - gm_restart: перезагружаю {active.name}");
            SceneManager.LoadScene(active.name);
        }

        /// <summary>
        /// R: перезапустить ПОСЛЕДНИЙ бой НА МЕСТЕ — сброс сима (юниты/снаряды/исход) + повтор последнего сетапа.
        /// Сцену и камеру НЕ перезагружаем: заново начинается только бой (dev-итерация).
        /// </summary>
        [Command("gm_restart_battle", "Перезапустить последний бой на месте (без перезагрузки сцены)")]
        public void RestartLastBattle()
        {
            if (_lastBattleSetup == null)
            {
                Debug.LogWarning("[GuildmasterCommands] - gm_restart_battle: последний бой не задан (сначала запусти любой gm_spawn_*)");
                return;
            }
            ResetForNewBattle();
            _lastBattleSetup.Invoke(this);
        }

        /// <summary>Включить/выключить Shapes debug-слой.</summary>
        [Command("gm_toggle_debug_draw", "Вкл/выкл debug-отрисовку боя")]
        public void ToggleDebugDraw()
        {
            if (_debugDraw == null) { Debug.LogWarning("[GuildmasterCommands] - CombatDebugDraw не найден"); return; }
            _debugDraw.IsEnabled = !_debugDraw.IsEnabled;
            Debug.Log($"[GuildmasterCommands] - gm_toggle_debug_draw: {(_debugDraw.IsEnabled ? "ON" : "OFF")}");
        }

        /// <summary>Включить/выключить dev-слой статус-колец (метка/стан/щит/заморозка/усиление).</summary>
        [Command("gm_toggle_status", "Вкл/выкл dev-подсветку статусов юнитов (кольца)")]
        public void ToggleStatusOverlay()
        {
            var overlay = FindAnyObjectByType<CombatStatusOverlay>(FindObjectsInactive.Include);
            if (overlay == null) { Debug.LogWarning("[GuildmasterCommands] - CombatStatusOverlay не найден (создаётся в бою)"); return; }
            overlay.IsEnabled = !overlay.IsEnabled;
            Debug.Log($"[GuildmasterCommands] - gm_toggle_status: {(overlay.IsEnabled ? "ON" : "OFF")}");
        }

        /// <summary>Пересобрать SimTuning из SO и применить к идущему бою (QC-тюнинг без рекомпиляции).</summary>
        [Command("gm_tuning_rebake", "Пересобрать SimTuning из SO и применить к бою (бой становится TAINTED)")]
        public void TuningRebake()
        {
            if (_simulation == null) { Debug.LogWarning("[GuildmasterCommands] - gm_tuning_rebake: нет активного боя"); return; }
            if (_simTuningConfig == null) { Debug.LogWarning("[GuildmasterCommands] - gm_tuning_rebake: SimTuningConfig не назначен"); return; }

            _simulation.RebakeTuning(_simTuningConfig.ToSnapshot());
            Debug.LogWarning("[GuildmasterCommands] - gm_tuning_rebake: тюнинг применён к бою → battle TAINTED (реплей невалиден, вики «13» §4.1)");
        }

        // Начать НОВЫЙ бой: сбросить текущий (юниты/снаряды/исход/очереди) + счётчик Id фабрики + снять
        // заморозку времени. Вызывается всеми gm_spawn_* — новая команда старта ПРЕРЫВАЕТ предыдущий бой,
        // а не копит юнитов поверх (иначе Id-коллизии и каша из нескольких боёв).
        private void ResetForNewBattle()
        {
            // ResetBattle() шлёт OnBattleReset → презентация снимает виды/цифры и сбрасывает slowmo/тряску
            // (CombatPresenter.HandleBattleReset → TimeScaleService.Reset). Ручной Time.timeScale тут больше не
            // нужен и вреден: перетёр бы выбранную игроком скорость (единый писатель — TimeScaleService).
            _simulation?.ResetBattle();
            _factory?.ResetIds();
        }

        // Единый dev-болванчик: собирается фабрикой из SO «enemy.training_dummy» (реальный путь — статы,
        // Brain из _ai, стартовый HP=MaxHP). Статы дамми правятся ТОЛЬКО в самом SO (1000 HP / 100 урона),
        // без хардкода в харнессе — один дамми на все сценарии gm_spawn_*.
        // Релик дев-среза по id. Нет в БД — говорим вслух и не спавним: молчаливый пропуск читался бы
        // как «команда не сработала», а причина (контент переименован/не в базе) осталась бы невидимой.
        /// <summary>
        /// Уйти на Ристалище из любого состояния игры (ГДД «Modes - Proving Grounds»): свернуть забег
        /// штатным возвратом в меню, затем послать тот же интент, что кнопка площадки.
        /// </summary>
        /// <remarks>
        /// Команда НЕ делает ничего своими руками: и выход, и вход идут теми же швами, что живой UI, — иначе
        /// у площадки появился бы второй способ открыться, и он бы разошёлся с первым. Решение по интенту
        /// принимает <c>DeploymentController</c>: если отряда нет (мы в меню), он ставит состав из
        /// <c>ProvingGroundsConfig</c>.
        /// </remarks>
        [Command("gm_proving_grounds", "Уйти на Ристалище: свернуть забег и открыть площадку вне забега")]
        public void ProvingGrounds() => RequestProvingGrounds();

        /// <summary>
        /// Поставить дев-бой на Ристалище: заказать площадке состав и уйти на неё. Возвращает false, если
        /// площадки в этой сборке нет (standalone dev-арена без Root) — тогда зовущий ставит бой сам.
        /// </summary>
        /// <remarks>
        /// Почему команда НЕ спавнит бойцов сама. На площадке составом распоряжается расстановка: она держит
        /// его в слотах, пересобирает превью при каждом перетаскивании и владеет паузой, которую снимает
        /// кнопка «Начать». Прямой спавн в симуляцию проигрывал ей трижды — юниты стирались первой же
        /// пересборкой, сброс боя снимал чужую паузу (бой стартовал сам), а слоты оставались от прежнего
        /// состава. Плюс порядок: событие «площадка открылась» приходит СИНХРОННО внутри запроса входа,
        /// поэтому отложенный спавн, записанный после запроса, не исполнялся никогда — команда молча не
        /// делала ничего, и игрок видел штатный расклад площадки. Заказ снимает все четыре разом: он уходит
        /// ДО входа, а ставит бойцов тот, кто ими и так распоряжается.
        /// </remarks>
        private bool StageOnProvingGrounds(System.Collections.Generic.List<Data.Definitions.ProvingGroundsSpawn> mine,
            System.Collections.Generic.List<Data.Definitions.ProvingGroundsSpawn> theirs, string what)
        {
            if (_groundsSetupPub == null || _provingGroundsPub == null) return false;

            _groundsSetupPub.Publish(new Data.Definitions.ProvingGroundsSetupRequest(mine, theirs, what));

            // Уже на площадке — заказ применён её владельцем сразу, входить некуда.
            if (!_onProvingGrounds) RequestProvingGrounds();
            return true;
        }

        /// <summary>
        /// Запросить Ристалище. Возвращает false, если запрашивать некому (dev-арена без Root-скоупа).
        /// </summary>
        private bool RequestProvingGrounds()
        {
            if (_provingGroundsPub == null)
            {
                Debug.LogWarning("[GuildmasterCommands] - Ристалище недоступно: нет Root-скоупа " +
                                 "(запущена standalone dev-арена, а не игра)");
                return false;
            }

            // Сначала выход: пока идёт забег, площадка вне забега открыться не имеет права. Отмена
            // всплывает до верхнего цикла, тот возвращается в меню — и там запрос его и встречает.
            _runControl?.RequestReturnToMainMenu();
            _provingGroundsPub.Publish(new Core.Flow.OpenProvingGroundsRequest());
            Debug.Log("[GuildmasterCommands] - gm_proving_grounds: запрошено Ристалище");
            return true;
        }

        /// <summary>
        /// Зеркальный отряд 4v4 из реальных китов — ровно тот бой, на котором стенд поймал преимущество
        /// стороны. Составы, роли и позиции обеих команд отражены по оси X, поэтому честный исход —
        /// ничья: любой перевес означает, что порядок обработки решает бой за бойцов.
        /// </summary>
        [Command("gm_spawn_mirror", "Зеркальный отряд 4v4 из реальных китов (проверка преимущества стороны)")]
        public void SpawnMirror()
        {
            // Тот же строй, что у командного бенча: фронт вплотную, тыл за спинами.
            (string id, float x, float y)[] squad =
            {
                ("relic.defender",        2.2f, -0.6f),
                ("relic.flame_swordsman", 2.2f,  0.6f),
                ("relic.cryomancer",      4.4f, -0.6f),
                ("relic.light_shepherd",  4.4f,  0.6f),
            };

            var relics = new RelicData[squad.Length];
            for (int i = 0; i < squad.Length; i++)
            {
                relics[i] = DevRelic(squad[i].id);
                if (relics[i] == null) return;
            }

            var mine   = new System.Collections.Generic.List<Data.Definitions.ProvingGroundsSpawn>();
            var theirs = new System.Collections.Generic.List<Data.Definitions.ProvingGroundsSpawn>();
            for (int i = 0; i < squad.Length; i++)
            {
                mine.Add(new Data.Definitions.ProvingGroundsSpawn(relics[i], new Vector2(-squad[i].x, squad[i].y)));
                theirs.Add(new Data.Definitions.ProvingGroundsSpawn(relics[i], new Vector2(squad[i].x, squad[i].y)));
            }

            _lastBattleSetup = self => self.SpawnMirror();

            if (StageOnProvingGrounds(mine, theirs, "gm_spawn_mirror"))
            {
                Debug.Log("[GuildmasterCommands] - gm_spawn_mirror: зеркальный отряд 4v4 заказан площадке " +
                          "(Защитник, Огненный мечник, Криомант, Пастырь). Честный исход — ничья. " +
                          "Бой начинает кнопка «Начать».");
                return;
            }

            SpawnMirrorNow(relics, squad); // площадки нет (standalone dev-арена) — ставим бой прямо здесь
        }

        // Прямой спавн без площадки: dev-арена, где расстановки-владельца попросту нет.
        private void SpawnMirrorNow(RelicData[] relics, (string id, float x, float y)[] squad)
        {
            if (!SimReady()) return;
            if (!FactoryReady()) return;

            ResetForNewBattle();

            // Порядок спавна — вся левая команда, затем вся правая: так же, как в бою и на стенде.
            for (int i = 0; i < squad.Length; i++)
                _simulation.EnqueueUnitSpawn(_factory.Create(relics[i], null, 0, new Vector2(-squad[i].x, squad[i].y)));
            for (int i = 0; i < squad.Length; i++)
                _simulation.EnqueueUnitSpawn(_factory.Create(relics[i], null, 1, new Vector2(squad[i].x, squad[i].y)));

            Debug.Log("[GuildmasterCommands] - gm_spawn_mirror: зеркальный отряд 4v4 поставлен на dev-арене.");
        }

        /// <summary>
        /// 1×1 зеркально на дев-дуэлянте (<c>enemy.bone_dev</c>) — смотреть скелетный визуал в живом бою.
        /// У него СВОЙ контент-ассет со ссылкой на костяной вид, поэтому команда работает всегда и ничего не
        /// подменяет в игровом контенте: раньше костяной вид приходилось руками вписывать в <c>relic.base</c>,
        /// и пока подмена стояла, костяным становился каждый бой базовой реликвии.
        /// Тот же вход на Ристалище, что у <see cref="SpawnMirror"/>.
        /// </summary>
        [Command("gm_spawn_bone_duel", "1×1 bone dev duelist mirror (skeletal UnitView smoke)")]
        public void SpawnBoneDuel()
        {
            if (_boneDuelist == null)
            {
                Debug.LogError("[GuildmasterCommands] - юнита 'enemy.bone_dev' нет в контент-БД → дуэль не запущена");
                return;
            }

            ResolveDuelEdges(out Vector2 left, out Vector2 right);
            var mine   = new System.Collections.Generic.List<Data.Definitions.ProvingGroundsSpawn>
                { new Data.Definitions.ProvingGroundsSpawn(_boneDuelist, left) };
            var theirs = new System.Collections.Generic.List<Data.Definitions.ProvingGroundsSpawn>
                { new Data.Definitions.ProvingGroundsSpawn(_boneDuelist, right) };

            _lastBattleSetup = self => self.SpawnBoneDuel();
            string view = _boneDuelist.ViewPrefab != null ? _boneDuelist.ViewPrefab.name : "null";

            if (StageOnProvingGrounds(mine, theirs, "gm_spawn_bone_duel"))
            {
                Debug.Log($"[GuildmasterCommands] - gm_spawn_bone_duel: дуэль заказана площадке " +
                          $"({left.x:0.##} vs {right.x:0.##}, дистанция {(right.x - left.x):0.##}; ViewPrefab={view}). " +
                          "Бой начинает кнопка «Начать».");
                return;
            }

            // Площадки нет (standalone dev-арена) — владельца состава тоже, ставим бой сами.
            if (!SimReady()) return;
            if (!FactoryReady()) return;

            ResetForNewBattle();
            _simulation.EnqueueUnitSpawn(_factory.Create(_boneDuelist, null, 0, left));
            _simulation.EnqueueUnitSpawn(_factory.Create(_boneDuelist, null, 1, right));
            Debug.Log($"[GuildmasterCommands] - gm_spawn_bone_duel: дуэль поставлена на dev-арене " +
                      $"(дистанция {(right.x - left.x):0.##}; ViewPrefab={view})");
        }

        /// <summary>
        /// Крайние точки своих зон расстановки — дуэлянты встают максимально далеко друг от друга. Дистанция
        /// здесь не украшение: подход, спринт и атака с разбега видны только тогда, когда бойцам есть куда
        /// разбегаться. Зоны читаются из снапшота арены, а не задаются числом: на другой арене «край» другой.
        /// </summary>
        private void ResolveDuelEdges(out Vector2 left, out Vector2 right)
        {
            const float margin = 0.6f;   // запас от кромки: юнит не должен влипать в границу зоны
            const float fallbackX = 6f;  // бесконечное поле (dev-арена без авторинга) — разводим фиксированно

            float y = 0f;
            float xLeft = float.NaN, xRight = float.NaN;

            if (_arena != null)
            {
                y = _arena.Bounds.Center.y;
                for (int i = 0; i < _arena.Zones.Count; i++)
                {
                    Core.Arena.DeploymentZone zone = _arena.Zones[i];
                    float min = zone.Area.Center.x - zone.Area.HalfSize.x + margin;
                    float max = zone.Area.Center.x + zone.Area.HalfSize.x - margin;

                    // Команда 0 живёт в зонах игрока (слева), команда 1 — в зонах врага (справа).
                    if (zone.Side == Core.Arena.DeploymentSide.Player)
                        xLeft = float.IsNaN(xLeft) ? min : Mathf.Min(xLeft, min);
                    else
                        xRight = float.IsNaN(xRight) ? max : Mathf.Max(xRight, max);
                }
            }

            // Зон нет — берём кромки поля; поле бесконечное (Unbounded) — фиксированный разнос.
            if (float.IsNaN(xLeft) || float.IsNaN(xRight))
            {
                float halfWidth = _arena != null ? _arena.Bounds.Rect.HalfSize.x : float.PositiveInfinity;
                float edge = float.IsFinite(halfWidth) ? Mathf.Max(1f, halfWidth - margin) : fallbackX;
                if (float.IsNaN(xLeft))  xLeft  = -edge;
                if (float.IsNaN(xRight)) xRight =  edge;
            }

            left  = new Vector2(xLeft,  y);
            right = new Vector2(xRight, y);
        }

        // Гейты боевых команд. Сообщение одно на все команды — раньше эта же строка стояла шестнадцатью
        // копиями, и «Симуляция не активна» уводило по ложному следу: чаще всего симуляция как раз запущена,
        // а потеряна ИНЪЕКЦИЯ. Скоуп инжектит этот объект один раз на буте, и перекомпиляция скриптов во
        // время play-mode (domain reload) обнуляет ссылки, оставляя объект живым и команды видимыми.
        private bool SimReady()
        {
            if (_simulation != null) return true;
            Debug.LogWarning("[GuildmasterCommands] - симуляция не внедрена. Если игра сейчас запущена — " +
                             "скрипты перекомпилировались во время play-mode, и domain reload снёс инъекцию " +
                             "боевого скоупа: перезапусти play-mode.");
            return false;
        }

        private bool FactoryReady()
        {
            if (_factory != null) return true;
            Debug.LogWarning("[GuildmasterCommands] - RuntimeUnitFactory не внедрён (см. подсказку про " +
                             "перезапуск play-mode в сообщении о симуляции).");
            return false;
        }

        private RelicData DevRelic(string id)
        {
            if (_content != null && _content.TryGet(id, out RelicData relic) && relic != null) return relic;
            Debug.LogError($"[GuildmasterCommands] - реликвии '{id}' нет в контент-БД → срез не запущен");
            return null;
        }

        private RuntimeUnit MakeDummy(int team, Vector2 pos) => _factory.Create(_dummyEnemy, null, team, pos);
    }
}
