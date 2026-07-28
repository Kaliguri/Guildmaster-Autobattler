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
        [Tooltip("Конфиг базовых характеристик (в т.ч. armor-константа K — единственный источник).")]
        [SerializeField] private StatsConfig _statsConfig;

        [Tooltip("Классовый профиль баланса (база HP/скорости от класса, 2-й уровень стат-каскада). ОБЯЗАТЕЛЕН: " +
                 "пусто = скоуп не соберётся (раньше классы молча не применялись, и юниты уезжали на MaxHP 0).")]
        [SerializeField] private ClassBalanceConfig _classBalanceConfig;

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
            // Арену печём из авторинга в сцене (если он есть); иначе — бесконечное поле.
            // prefab-per-arena через Addressables — будущий свап (вики «15» §4-5): тогда снапшот
            // придёт из загруженного префаба, а не из поиска по сцене.
            ArenaLayoutData layout = BuildArenaLayout();

            RegisterArena(builder, layout);
            RegisterRng(builder);
            RegisterCombatSystems(builder);
            RegisterSimulation(builder, layout);
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

            // Persist-мир (план 12 Ф2): ставит отряд забега на тест-арену вне боя по RunPartyReadyEvent.
            builder.RegisterEntryPoint<Flow.WorldStageController>(Lifetime.Scoped);

            // Мост в макро-флоу (план 11 §4 A2): забирает запрос боя из IBattleSession и грузит его, репортит
            // исход. Регистрируется ПОСЛЕ DeploymentController — чтобы его подписка на Free-расстановку встала
            // до LoadPreset. Пусто (запуск из dev-панели) = просто ждёт исход, LoadPreset не зовёт.
            builder.RegisterEntryPoint<Flow.BattleBootstrap>(Lifetime.Scoped);
        }

        private ArenaLayoutData BuildArenaLayout()
        {
            var authoring = FindAnyObjectByType<ArenaLayoutAuthoring>();
            if (authoring == null)
            {
                Debug.LogWarning("[CombatLifetimeScope] - ArenaLayoutAuthoring не найден в сцене → " +
                                 "бесконечное поле без зон (движение не клампится).");
                return ArenaLayoutData.Unbounded;
            }
            return authoring.BuildLayout();
        }

        private void RegisterArena(IContainerBuilder builder, ArenaLayoutData layout)
        {
            builder.RegisterInstance(layout);
            builder.Register<DeploymentService>(Lifetime.Scoped);
        }

        // Сид здесь больше не разыгрывается. Скоуп в persist-мире поднимается ОДИН раз на сессию, поэтому
        // всё, что он посеет, — это состояние на весь забег сразу; настоящий сид боя приносит BattleBootstrap
        // перед каждым запуском узла, выводя его из RunState.Seed (единственный сохраняемый сид, T-19).
        // Стартовое значение — нейтральный ноль: до первого боя из этого генератора никто не тянет.
        private void RegisterRng(IContainerBuilder builder)
            => builder.RegisterInstance<IRngService>(new XorShiftRng(0UL));

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
            float resourcePerSecond = _statsConfig != null
                ? _statsConfig.ResourceRegenPerSecond
                : new RegenSystem().ResourcePerSecond;
            builder.Register<RegenSystem>(_ => new RegenSystem { ResourcePerSecond = resourcePerSecond },
                                          Lifetime.Scoped);
            builder.Register<DisplacementSystem>(Lifetime.Scoped);
        }

        private void RegisterSimulation(IContainerBuilder builder, ArenaLayoutData layout)
        {
            // VContainer сам разрешит зависимости конструктора (RNG, SpatialHash, все системы) —
            // вручную перечислять Resolve не нужно. Не-инъектируемые параметры передаём через
            // WithParameter по имени: float armorK (единственный источник — StatsConfig.ArmorConstantK,
            // вики «13» §4.2 п.1) и границы поля arena (ArenaBounds? — значение, не сервис). Добавил
            // систему в ctor — ничего тут править не надо, лишь бы она была зарегистрирована.
            builder.Register<CombatSimulation>(Lifetime.Scoped)
                   .WithParameter("armorK", ScopeWiring.Require(_statsConfig, nameof(CombatLifetimeScope), nameof(_statsConfig)).ArmorConstantK)
                   .WithParameter("arena", (ArenaBounds?)layout.Bounds)
                   .WithParameter("tuning", (SimTuning?)ScopeWiring.Require(_simTuningConfig, nameof(CombatLifetimeScope), nameof(_simTuningConfig)).ToSnapshot())
                   .WithParameter("cameraZone", (Rect2D?)layout.CameraZone);

            StatsConfig cfg = ScopeWiring.Require(_statsConfig, nameof(CombatLifetimeScope), nameof(_statsConfig));
            ClassBalanceConfig classCfg = ScopeWiring.Require(_classBalanceConfig, nameof(CombatLifetimeScope), nameof(_classBalanceConfig));
            builder.Register<RuntimeUnitFactory>(r => new RuntimeUnitFactory(
                cfg,
                classCfg,
                r.Resolve<EffectSystem>(),
                r.Resolve<CombatSimulation>()),
                Lifetime.Scoped);
            // Data-driven загрузчик боя из EncounterData (сменил заготовку BattleSetupBuilder, вики «13» §3.1).
            // IContentDatabase — из RootScope (родитель); фабрика/симуляция — из этого скоупа.
            builder.Register<EncounterLoader>(Lifetime.Scoped);

            // Петля — ещё и владелец доли интерполяции: она копит остаток тика, презентация его читает.
            builder.RegisterEntryPoint<CombatLoopService>(Lifetime.Scoped)
                   .As<Core.Simulation.ISimInterpolation>();
        }

        private void RegisterPresentation(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<CombatPresenter>();
            builder.RegisterComponentInHierarchy<CombatDebugDraw>();
            builder.RegisterComponentInHierarchy<CombatAreaFlash>();

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
