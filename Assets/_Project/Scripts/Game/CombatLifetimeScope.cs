using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Input;
using Guildmaster.Game.Services;
using Guildmaster.Presentation;
using Guildmaster.Presentation.Audio;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game
{
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

        [Tooltip("Состав Ристалища по умолчанию — кто встаёт на площадку вне забега (ГДД «Modes - Proving Grounds»). " +
                 "Пусто = вход на площадку из главного меню недоступен (скажет вслух), бой забега не затронут.")]
        [SerializeField] private ProvingGroundsConfig _provingGroundsConfig;

        [Tooltip("Размер ячейки пространственного хэша.")]
        [SerializeField] private float _spatialHashCellSize = 3f;

        [Tooltip("Design-конфиг «сочности» боя (вспышка/сплющивание/hitstop/slowmo/тряска). ОБЯЗАТЕЛЕН. " +
                 "Пусто = красная ошибка и НЕТ джуса вовсе (не «дефолты» — своих чисел потребители не держат).")]
        [SerializeField] private Presentation.Design.CombatFeelConfig _feelConfig;

        protected override void Configure(IContainerBuilder builder)
        {
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
            // Состав Ристалища идёт параметром: он может быть не разведён (тогда площадка вне забега
            // просто не открывается), поэтому Require здесь не к месту — бой от этого не зависит.
            builder.RegisterEntryPoint<DeploymentController>(Lifetime.Scoped)
                   .WithParameter("provingGrounds", _provingGroundsConfig);

            // Сборка боя, ради которого родился скоуп: отряд, враги, фаза расстановки, отчёт исхода.
            // Регистрируется ПОСЛЕ DeploymentController — чтобы его подписка на Free-расстановку встала
            // до того, как загрузчик её поднимет.
            builder.RegisterEntryPoint<Flow.BattleStartup>(Lifetime.Scoped);
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

            // Петля гонит сим и пишет ленту. Долю интерполяции она больше не отдаёт: её отсчитывает
            // момент ПОКАЗА (BattleTapePlayback), потому что показ живёт на своём тике, а не на симовом.
            builder.RegisterEntryPoint<CombatLoopService>(Lifetime.Scoped);

            RegisterCoop(builder);
        }

        /// <summary>
        /// Сетевые половины боя. Регистрируются <b>обе и всегда</b>: скоуп поднимается на буте, когда
        /// роль ещё неизвестна, поэтому расходятся узлы в рантайме — каждый потребитель спрашивает
        /// <see cref="Core.Net.IBattleAuthority"/> сам. В соло это стоит двух объектов, которые молчат.
        /// </summary>
        private void RegisterCoop(IContainerBuilder builder)
        {
            // Приёмная сторона: читает чанки в ТУ ЖЕ BattleTape, которую в соло пишет рекордер. Значит
            // весь показ ниже (презентер, телеграфы, диспетчер) у гостя работает без единой правки —
            // он не знает и не должен знать, кто наполнил ленту.
            builder.Register<Net.Tape.TapeChunkReader>(Lifetime.Scoped);
            builder.Register<Net.Tape.TapeIntake>(Lifetime.Scoped);

            // Стример регистрируется фабрикой: у его конструктора есть параметры со значениями по
            // умолчанию (нарезка и глубина истории), а VContainer на таком ctor роняет всю ветку.
            builder.Register<Net.Tape.TapeStreamer>(r => new Net.Tape.TapeStreamer(
                    r.Resolve<Net.Transport.INetTransport>(),
                    r.Resolve<Combat.Tape.BattleTape>()),
                Lifetime.Scoped);

            builder.RegisterEntryPoint<Net.Tape.BattleTapeBroadcast>(Lifetime.Scoped).AsSelf();
            builder.RegisterEntryPoint<Net.Tape.TapeIntakePump>(Lifetime.Scoped);

            // Состав боя: в снимках его нет (за бой не меняется), а показу он нужен — кто это, какой
            // арт, чья команда. В соло эту роль играет событие спавна, у гостя его неоткуда взять.
            builder.RegisterEntryPoint<Net.Tape.BattleRosterRelay>(Lifetime.Scoped);

            // Пауза: сеть объявляет, показ применяет. Мост нужен и в соло — путь применения паузы один
            // на все режимы (см. его докстринг).
            builder.RegisterEntryPoint<Services.NetPauseBridge>(Lifetime.Scoped);
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
