using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Input;
using Guildmaster.Game.Services;
using Guildmaster.Net.Tape;
using Guildmaster.Presentation;
using Guildmaster.Presentation.Audio;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game
{
    /// <summary>
    /// Как поднят боевой скоуп: обычный бой (роль владельца/гостя решает сессия) или воспроизведение
    /// повтора с диска — без сима-водителя, сессии и джуса времени.
    /// </summary>
    public enum BattleScopeMode
    {
        /// <summary>Живой бой: владелец считает и раздаёт, гость принимает. Роль берётся у сессии.</summary>
        Auto,

        /// <summary>Повтор: лента приезжает из файла, симуляции нет. Фон меню и будущий «посмотреть бой».</summary>
        Replay,
    }

    /// <summary>
    /// DI-скоуп боевых систем: RNG боя, системы, симуляция, презентация. Живёт в
    /// <c>CombatSystemsScene</c> и дочерний к <see cref="WorldLifetimeScope"/> — камера и арена
    /// резолвятся из предка, без дублей Main Camera/Brain (вики «Scenes», «16» §5).
    /// </summary>
    /// <remarks>
    /// Вопреки имени, по одному бою НЕ пересоздаётся: сцена грузится один раз на буте и не выгружается,
    /// а бой начинается командой в живую симуляцию. Значит боевое состояние между узлами не чистится
    /// сносом скоупа — за возврат отвечает <c>BattleBootstrap.ResetToWorld</c>.
    /// </remarks>
    public class CombatLifetimeScope : LifetimeScope
    {
        [Tooltip("Общий конфиг игры — ОТСЮДА берутся стат-конфиги (StatsConfig, ClassBalanceConfig). " +
                 "ОБЯЗАТЕЛЕН. Держать здесь ссылку на GameConfig, а не на сами конфиги, обязательно: " +
                 "боевая сцена поднимается и без CoreScene (dev-арена), а играющий экземпляр конфигов " +
                 "должен быть выбран в одном месте — иначе арена и игра расходятся молча.")]
        [SerializeField] private GameConfig _gameConfig;

        [Tooltip("Балансный тюнинг симуляции (вики «13» §3.4): печётся в снапшот SimTuning на старте боя.")]
        [SerializeField] private SimTuningConfig _simTuningConfig;

        [Tooltip("Размер ячейки пространственного хэша.")]
        [SerializeField] private float _spatialHashCellSize = 3f;

        [Tooltip("Design-конфиг «сочности» боя (вспышка/сплющивание/hitstop/slowmo/тряска). ОБЯЗАТЕЛЕН. " +
                 "Пусто = красная ошибка и НЕТ джуса вовсе (не «дефолты» — своих чисел потребители не держат).")]
        [SerializeField] private Presentation.Design.CombatFeelConfig _feelConfig;

        [Tooltip("Как поднят скоуп. Auto — обычный бой (роль решает сессия). Replay — воспроизведение " +
                 "повтора с диска: без сима-водителя, сессии, расстановки и джуса времени (фон меню).")]
        [SerializeField] private BattleScopeMode _mode = BattleScopeMode.Auto;

        protected override void Configure(IContainerBuilder builder)
        {
            // Повтор — иной состав скоупа, а не «бой с выключенным симом»: у него нет ни расстановки, ни
            // сессии, ни водителя тика. Собираем отдельной веткой и выходим — так «а мы точно реплей?»
            // не появляется внутри живой ветки ни одним ветвлением.
            if (_mode == BattleScopeMode.Replay)
            {
                ConfigureReplay(builder);
                return;
            }

            RegisterArena(builder);
            RegisterRng(builder);
            RegisterCombatSystems(builder);
            RegisterSimulation(builder);
            RegisterPresentation(builder);

            // Конфиг «сочности»: без ассета джус выключен целиком, а не «примерно такой» — потребители
            // читают только конфиг и своих чисел не держат (аудит 2026-07-26, R1-34/T-9).
            var feel = ScopeWiring.Optional(_feelConfig, nameof(CombatLifetimeScope), nameof(_feelConfig),
                "боевого джуса не будет: ни вспышек, ни сплющивания, ни тряски");
            builder.RegisterInstance(feel);

            // Единый арбитр Time.timeScale (пауза/скорость/cinematic slowmo). EntryPoint — чтобы Tick()
            // вёл возврат slowmo, а Dispose вернул timeScale к 1; AsSelf — для инъекции в потребителей.
            builder.RegisterEntryPoint<TimeScaleService>(Lifetime.Scoped).AsSelf();

            // Режиссёр «сочности»: политика global-эффектов (slowmo на килл/конец боя) по MessagePipe-событиям.
            builder.RegisterEntryPoint<CombatFeelDirector>(Lifetime.Scoped);

            // Аудио-презентер: тот же приём (POCO-подписчик на боевые события). Каталог и IAudioService —
            // из RootScope. Пусто/заглушка = молчит, бой не падает.
            builder.RegisterEntryPoint<AudioPresenter>(Lifetime.Scoped);

            // Боевой ввод: пауза/скорость на время этого боя (вики «16» §4).
            builder.RegisterEntryPoint<BattleInputController>(Lifetime.Scoped);

            // Интерактивная фаза расстановки (шаг 4): активна на Free-пресетах; иначе спит.
            // Состава Ристалища «по умолчанию» здесь больше нет: площадка открывается ПУСТОЙ, а бойцов
            // приносит заказ (сегодня — дев-команда, позже — экран сборки боя). Ассетный расклад делал
            // вход на Ристалище готовым боем 4×4, которого игрок не заказывал (наход. Макса 02.08.2026).
            //
            // ТОЛЬКО У ВЛАДЕЛЬЦА. У гостя расстановки нет: её поднимает пресет боя, а пресета у него не
            // бывает — бой приезжает лентой. Пока контроллер создавался и ему, он молча ломал гостю
            // кнопку «Начать»: на старте перехватывал и ключ гейта, и саму кнопку, а нажатие уводил в
            // свою проверку «мы вообще в расстановке?» и тихо выходил. В кампании это не било только по
            // счастливой очерёдности, а на Ристалище он вставал последним и кнопка умирала
            // (разбор логов прогона вдвоём, 04.08.2026).
            //
            // ОТСЮДА ЖЕ у гостя нет ни кругов под ногами, ни драга бойцов, ни драга реликвии из
            // инвентаря: всё это рисует и ведёт он. Гостевая расстановка — отдельная работа, и она
            // записана в docs/player-capability-registry.md.
            if (!IsGuestSession()) builder.RegisterEntryPoint<DeploymentController>(Lifetime.Scoped);

            // Сборка боя, ради которого родился скоуп: отряд, враги, фаза расстановки, отчёт исхода.
            // Регистрируется ПОСЛЕ DeploymentController — чтобы его подписка на Free-расстановку встала
            // до того, как загрузчик её поднимет.
            builder.RegisterEntryPoint<Flow.BattleStartup>(Lifetime.Scoped);
        }

        /// <summary>
        /// Реплей: показ той же ленты, но наполняет её файл, а не сим. Регистрируем показ, ленту,
        /// плейбек и плеера — и НЕ регистрируем ничего, что тянет сессию или водит тик: расстановку
        /// (<c>DeploymentController</c> → <c>IReadyGate</c>/<c>IBattleSession</c>), старт боя, ввод,
        /// петлю, кооп, режиссёра джуса времени. Именно эти зависимости и роняли фон меню, поднятый как
        /// обычный бой: здесь их просто нет.
        /// </summary>
        /// <remarks>
        /// <b>Симуляция всё же есть — простаивающая, как у гостя.</b> Её держит ссылкой
        /// <see cref="Flow.BattlePresenterBinder"/> (дев-оверлеи читают живой сим), но тикать её некому:
        /// <c>CombatLoopService</c> в реплее не регистрируется, а спавнить нечего — расстановки нет.
        /// Состав приходит из файла в <see cref="Combat.Tape.BattleUnitRegistry"/> через
        /// <c>RegisterRemote</c>. Ленту наполняет <see cref="Net.Tape.ReplayFilePlayer"/>.
        /// </remarks>
        private void ConfigureReplay(IContainerBuilder builder)
        {
            // Локальный пофрейм-фидбэк (вспышки, цифры урона) читает этот конфиг — он остаётся. А вот
            // РЕЖИССЁРА времени (slowmo/тряска через глобальный timeScale) не регистрируем: фон меню не
            // должен дёргать глобальное время (журнал 2026-08-04-replay-juice-acts-on-the-view-not-global-time).
            var feel = ScopeWiring.Optional(_feelConfig, nameof(CombatLifetimeScope), nameof(_feelConfig),
                "локального боевого фидбэка в фоне меню не будет");
            builder.RegisterInstance(feel);

            RegisterRng(builder);            // сид из BattleScopeParams — для простаивающего сима
            RegisterCombatSystems(builder);  // системы конструирует idle-сим; сами не тикают
            RegisterReplaySimulationCore(builder);

            // Показ — те же биндеры, что у живого боя, С ОДНОЙ заменой: фокус камеры берётся из ЛЕНТЫ, а
            // не из простаивающего сима (иначе камера в Action кадрирует пустоту). Всё остальное — как в
            // настоящем бою: презентер, телеграфы, диспетчер работают поверх ленты, не зная её источника.
            builder.RegisterEntryPoint<Flow.BattlePresenterBinder>(Lifetime.Scoped);
            builder.RegisterEntryPoint<Presentation.ReplayFocusBinder>(Lifetime.Scoped);
        }

        /// <summary>Ядро реплея: простаивающий сим ради ссылок, лента без рекордера, плеер из файла.</summary>
        private void RegisterReplaySimulationCore(IContainerBuilder builder)
        {
            // Простаивающий сим — те же параметры, что у живого (armorK/arena/tuning/cameraZone из
            // конфига и арены мира), но без фабрики юнитов и загрузчика энкаунтера: спавнить нечего.
            builder.Register<CombatSimulation>(Lifetime.Scoped)
                   .WithParameter("armorK", Stats().ArmorConstantK)
                   .WithParameter("arena", r => (ArenaBounds?)r.Resolve<ArenaLayoutData>().Bounds)
                   .WithParameter("tuning", (SimTuning?)ScopeWiring.Require(_simTuningConfig, nameof(CombatLifetimeScope), nameof(_simTuningConfig)).ToSnapshot())
                   .WithParameter("cameraZone", r => (Rect2D?)r.Resolve<ArenaLayoutData>().CameraZone);

            // Лента и её показ — та же тройка, что в живом бою, минус рекордер: нам не писать, а читать.
            builder.Register<Combat.Tape.BattleTape>(
                       _ => new Combat.Tape.BattleTape(Combat.Tape.BattleTapeRecorder.DefaultWindowTicks),
                       Lifetime.Scoped);
            builder.Register<Combat.Tape.BattleTapePlayback>(Lifetime.Scoped);
            builder.Register<Combat.Tape.BattleTapeDispatcher>(Lifetime.Scoped);
            builder.Register<Combat.Tape.BattleUnitRegistry>(Lifetime.Scoped);

            // Кадр и паспорта — показу; тела прошлого боя хоронит отсутствие в кадре.
            builder.RegisterEntryPoint<Flow.BattleStageBinder>(Lifetime.Scoped);
            builder.Register<Presentation.DevOverlayMode>(Lifetime.Scoped);

            // Читатель чанков в ту же ленту (IContentDatabase — из корня) и плеер, что кормит её из
            // файла по темпу показа. Байты файла приходят заказом ReplayPlaybackRequest от создателя.
            builder.Register<Net.Tape.TapeChunkReader>(Lifetime.Scoped);
            builder.RegisterEntryPoint<Net.Tape.ReplayFilePlayer>(Lifetime.Scoped)
                   .WithParameter("fileBytes", r => r.Resolve<Net.Tape.ReplayPlaybackRequest>().FileBytes);
        }

        /// <summary>
        /// Арена принадлежит МИРУ и резолвится из него: своей копии боевой скоуп не держит.
        /// </summary>
        /// <remarks>
        /// До 02.08.2026 он строил из авторинга вторую и регистрировал её же — при том что
        /// комментарий в <see cref="WorldLifetimeScope"/> утверждал обратное («бой берёт тот же layout
        /// из предка»). Дефект был невидим, потому что VContainer отдаёт ближайшую регистрацию, а
        /// значения совпадали: оба скоупа искали один и тот же объект в загруженных сценах.
        /// <para><b>Одиночная боевая сцена</b> (dev-арена без мира) — законный режим запуска, и в нём
        /// брать layout неоткуда: родителя нет. Тогда регистрируем бесконечное поле явно и говорим об
        /// этом вслух — это не фолбэк на отказ, а другой состав сцены.</para>
        /// </remarks>
        private void RegisterArena(IContainerBuilder builder)
        {
            if (Parent == null)
            {
                Debug.LogWarning("[CombatLifetimeScope] - боевая сцена поднята без мира → бесконечное " +
                                 "поле без зон (движение не клампится). В игре арену держит WorldLifetimeScope.");
                builder.RegisterInstance(ArenaLayoutData.Unbounded);

                // Родителя нет — значит скоуп поднялся сам, из сцены, и параметров боя ему никто не
                // подал. Пустой бой с нулевым сидом честнее падения: dev-арена запускается ради того,
                // чтобы что-то показать, а бой на ней ставят руками.
                builder.RegisterInstance(new Flow.BattleScopeParams(preset: null, seed: 0UL));
            }

            builder.Register<DeploymentService>(Lifetime.Scoped);
        }

        /// <summary>
        /// Генератор боя рождается вместе с боем и сразу с его сидом (<see cref="Flow.BattleScopeParams"/>).
        /// </summary>
        /// <remarks>
        /// Пересева больше нет — не спрятан, а не нужен: пока скоуп жил всю сессию, его генератор тянул
        /// одну последовательность через весь забег, и это лечили ручным <c>Reseed</c> перед каждым узлом.
        /// Теперь неправильно посеянного генератора не существует в природе.
        /// </remarks>
        private static void RegisterRng(IContainerBuilder builder)
            => builder.Register<IRngService>(
                r => new XorShiftRng(r.Resolve<Flow.BattleScopeParams>().Seed), Lifetime.Scoped);

        private void RegisterCombatSystems(IContainerBuilder builder)
        {
            float cellSize = _spatialHashCellSize;
            builder.Register<SpatialHash>(_ => new SpatialHash(cellSize), Lifetime.Scoped);
            builder.Register<BrainSystem>(Lifetime.Scoped);
            builder.Register<AbilitySystem>(Lifetime.Scoped);
            builder.Register<MovementSystem>(Lifetime.Scoped);
            builder.Register<AutoAttackSystem>(Lifetime.Scoped);
            builder.Register<ProjectileSystem>(Lifetime.Scoped);
            builder.Register<DeathSystem>(Lifetime.Scoped);
            builder.Register<EffectSystem>(Lifetime.Scoped);
            // Скорость капания ресурса — из StatsConfig (единственный источник числа); без конфига
            // остаётся код-дефолт системы, а не тихий ноль.
            StatsConfig statsForRegen = _gameConfig != null ? _gameConfig.Stats : null;
            float resourcePerSecond = statsForRegen != null
                ? statsForRegen.ResourceRegenPerSecond
                : new RegenSystem().ResourcePerSecond;
            builder.Register<RegenSystem>(_ => new RegenSystem { ResourcePerSecond = resourcePerSecond },
                                          Lifetime.Scoped);
            builder.Register<DisplacementSystem>(Lifetime.Scoped);
        }

        /// <summary>Стат-конфиг из <see cref="GameConfig"/>: единственный владелец играющего экземпляра.</summary>
        private StatsConfig Stats() => ScopeWiring.Require(
            ScopeWiring.Require(_gameConfig, nameof(CombatLifetimeScope), nameof(_gameConfig)).Stats,
            nameof(GameConfig), nameof(GameConfig.Stats));

        private void RegisterSimulation(IContainerBuilder builder)
        {
            // VContainer сам разрешит зависимости конструктора (RNG, SpatialHash, все системы) —
            // вручную перечислять Resolve не нужно. Не-инъектируемые параметры передаём через
            // WithParameter по имени: float armorK (единственный источник — StatsConfig.ArmorConstantK,
            // вики «13» §4.2 п.1) и границы поля arena (ArenaBounds? — значение, не сервис). Добавил
            // систему в ctor — ничего тут править не надо, лишь бы она была зарегистрирована.
            // Границы поля и зона камеры приходят из АРЕНЫ МИРА, и берутся они лямбдой от резолвера, а
            // не значением: значение потребовало бы держать layout здесь, то есть заводить его второго
            // владельца. Лямбда резолвит его в момент создания симуляции — из предка, где он и живёт.
            builder.Register<CombatSimulation>(Lifetime.Scoped)
                   .WithParameter("armorK", Stats().ArmorConstantK)
                   .WithParameter("arena", r => (ArenaBounds?)r.Resolve<ArenaLayoutData>().Bounds)
                   .WithParameter("tuning", (SimTuning?)ScopeWiring.Require(_simTuningConfig, nameof(CombatLifetimeScope), nameof(_simTuningConfig)).ToSnapshot())
                   .WithParameter("cameraZone", r => (Rect2D?)r.Resolve<ArenaLayoutData>().CameraZone);

            StatsConfig cfg = Stats();
            ClassBalanceConfig classCfg = ScopeWiring.Require(
                ScopeWiring.Require(_gameConfig, nameof(CombatLifetimeScope), nameof(_gameConfig)).ClassBalance,
                nameof(GameConfig), nameof(GameConfig.ClassBalance));
            builder.Register<RuntimeUnitFactory>(r => new RuntimeUnitFactory(
                cfg,
                classCfg,
                r.Resolve<EffectSystem>(),
                r.Resolve<CombatSimulation>()),
                Lifetime.Scoped);
            // Data-driven загрузчик боя из EncounterData (сменил заготовку BattleSetupBuilder, вики «13» §3.1).
            // IContentDatabase — из RootScope (родитель); фабрика/симуляция — из этого скоупа.
            builder.Register<EncounterLoader>(Lifetime.Scoped);

            // Лента боя: сим пишет вперёд, показ читает с лагом. Ф1 — только запись; потребители
            // приходят фазами (см. docs/lookahead-presentation-lag.md §8).
            builder.Register<Combat.Tape.BattleTape>(
                       _ => new Combat.Tape.BattleTape(Combat.Tape.BattleTapeRecorder.DefaultWindowTicks),
                       Lifetime.Scoped);
            builder.Register<Combat.Tape.BattleTapeRecorder>(Lifetime.Scoped);
            builder.Register<Combat.Tape.BattleTapePlayback>(Lifetime.Scoped);
            builder.Register<Combat.Tape.BattleTapeDispatcher>(Lifetime.Scoped);
            builder.Register<Combat.Tape.BattleUnitRegistry>(Lifetime.Scoped);

            // Пока идёт бой, кадр показу поставляет лента; вне боя — тела мира. Привязку делает бой,
            // потому что он же её и снимает (шаг 1б: этот энтрипоинт переедет в боевой скоуп как есть).
            builder.RegisterEntryPoint<Flow.BattleStageBinder>(Lifetime.Scoped);

            // Режим dev-оверлеев: один владелец на бой, иначе оверлеи разъедутся между собой.
            builder.Register<Presentation.DevOverlayMode>(Lifetime.Scoped);

            // Телеграфы: подводки к тому, что показ ещё не дошёл (щит «Оплота» до удара). Первая
            // фича, которая живёт ИМЕННО за счёт лага показа.
            builder.RegisterEntryPoint<Presentation.BattleTelegraphPresenter>(Lifetime.Scoped);

            // Dev-диагностика ленты: без неё «сим впереди, показ с лагом» ломается молча.
            builder.RegisterEntryPoint<BattleTapeDiagnostics>(Lifetime.Scoped);

            // Петля тика (CombatLoopService) регистрируется не здесь, а в составе ВЛАДЕЛЬЦА: гость бой
            // не считает вовсе. Долю интерполяции она больше не отдаёт — её отсчитывает момент ПОКАЗА
            // (BattleTapePlayback), потому что показ живёт на своём тике, а не на симовом.
            RegisterCoop(builder);
        }

        /// <summary>
        /// Сетевые половины боя — <b>по роли сеанса, а не обе сразу</b>: владелец считает бой и раздаёт
        /// его лентой, гость свою симуляцию не тикает и живёт присланной.
        /// </summary>
        /// <remarks>
        /// <b>Что здесь изменилось 02.08.2026.</b> Раньше регистрировались обе половины и каждый
        /// потребитель спрашивал роль сам, каждый кадр (<c>IBattleAuthority</c>) — потому что скоуп
        /// поднимался на буте, в главном меню, когда сети ещё не было. Причина отпала: бой рождается по
        /// требованию ВНУТРИ сеанса, и роль известна в момент сборки. Вместе с ней ушли пять ветвлений
        /// и сам интерфейс.
        /// <para><b>Соло — это владелец без поднятого транспорта.</b> Отдельной роли под него нет
        /// намеренно: разница между «играю один» и «играю хостом» вся в том, есть ли кому слать, а это
        /// вопрос к соединению, а не к составу. Иначе включение лобби посреди забега требовало бы
        /// переоткрыть сеанс.</para>
        /// <para><b>Роль спрашивается у предка</b>, а не приезжает параметром боя: она свойство сеанса,
        /// и бой её только читает. Сеанса нет вовсе (одиночная боевая сцена, dev-арена) — собираем
        /// владельческий состав: там некому быть хостом и нечего принимать.</para>
        /// </remarks>
        private void RegisterCoop(IContainerBuilder builder)
        {
            if (IsGuestSession()) RegisterGuestBattle(builder);
            else                  RegisterOwnerBattle(builder);

            // Пауза: сеть объявляет, показ применяет. Мост нужен обеим ролям и в соло — путь применения
            // паузы один на все режимы (см. его докстринг).
            builder.RegisterEntryPoint<Services.NetPauseBridge>(Lifetime.Scoped);
        }

        /// <summary>Владелец: тикает бой сам и раздаёт его гостям.</summary>
        private static void RegisterOwnerBattle(IContainerBuilder builder)
        {
            // Петля гонит сим и пишет ленту.
            builder.RegisterEntryPoint<CombatLoopService>(Lifetime.Scoped);

            // Стример регистрируется фабрикой: у его конструктора есть параметры со значениями по
            // умолчанию (нарезка и глубина истории), а VContainer на таком ctor роняет всю ветку.
            builder.Register<Net.Tape.TapeStreamer>(r => new Net.Tape.TapeStreamer(
                    r.Resolve<Net.Transport.INetTransport>(),
                    r.Resolve<Combat.Tape.BattleTape>()),
                Lifetime.Scoped);

            builder.RegisterEntryPoint<Net.Tape.BattleTapeBroadcast>(Lifetime.Scoped).AsSelf();

            // Подключение посреди боя: держим общую паузу, пока напарник догружает ленту, и снимаем её
            // через короткий отсчёт (реш. Макса 04.08.2026). Живёт у владельца, потому что паузу для
            // всех объявляет он.
            builder.RegisterEntryPoint<Net.Tape.MidBattleJoinHold>(Lifetime.Scoped).AsSelf();

            // Состав боя: в снимках его нет (за бой не меняется), а показу он нужен — кто это, какой
            // арт, чья команда.
            builder.RegisterEntryPoint<Net.Tape.BattleRosterAnnouncer>(Lifetime.Scoped);
        }

        /// <summary>Гость: своей симуляции нет, есть присланная лента и состав к ней.</summary>
        private static void RegisterGuestBattle(IContainerBuilder builder)
        {
            // Приёмная сторона читает чанки в ТУ ЖЕ BattleTape, которую у владельца пишет рекордер.
            // Значит весь показ ниже (презентер, телеграфы, диспетчер) у гостя работает без единой
            // правки — он не знает и не должен знать, кто наполнил ленту.
            builder.Register<Net.Tape.TapeChunkReader>(Lifetime.Scoped);
            builder.Register<Net.Tape.TapeIntake>(Lifetime.Scoped);
            builder.RegisterEntryPoint<Net.Tape.TapeIntakePump>(Lifetime.Scoped);

            // Кто на арене: у владельца это событие спавна собственной симуляции, у гостя её нет.
            builder.RegisterEntryPoint<Net.Tape.BattleRosterIntake>(Lifetime.Scoped);

            // Вместо тикового цикла — одно требование к отставанию показа.
            builder.RegisterEntryPoint<Services.GuestPlaybackLoop>(Lifetime.Scoped);
        }

        /// <summary>
        /// Играем ли мы в чужом сеансе. Спрашиваем предка напрямую: <c>Configure</c> резолвера ещё не
        /// имеет, а <c>Parent</c> к этому моменту уже заполнен (проверено по исходнику VContainer 1.18).
        /// </summary>
        private bool IsGuestSession()
        {
            if (Parent == null || Parent.Container == null) return false;

            return Parent.Container.TryResolve(out Session.SessionContext session)
                   && session.Role == Session.SessionRole.Guest;
        }

        private void RegisterPresentation(IContainerBuilder builder)
        {
            // Сами презентеры зарегистрированы МИРОМ: они живут в персист-сцене и переживают бои,
            // поэтому инъекция в них — мировая половина зависимостей. Боевую половину раздаёт этот
            // энтрипоинт, пока бой жив, и забирает, когда бой уходит.
            builder.RegisterEntryPoint<Flow.BattlePresenterBinder>(Lifetime.Scoped);

            // Камера-риг (focus/controller/IScreenShake, вики «16» §5) переехал в персистентный
            // WorldLifetimeScope — боевой скоуп дочерний к нему и резолвит риг из предка (единая камера,
            // без дублей Brain). Здесь только боевой мост: на время боя подаём камере живые точки фокуса.
            builder.RegisterEntryPoint<Presentation.BattleFocusBinder>(Lifetime.Scoped);
        }

        // TODO Фаза MP: сид боя выводится из RunState.Seed, а сам RunState хост реплицирует клиентам —
        // значит суб-сид узла совпадёт у всех сам собой. Отдельная команда «вот сид боя» не понадобится,
        // пока RunState доезжает до клиента раньше запуска узла.
    }
}
