using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Input;
using Guildmaster.Game.Services;
using Guildmaster.Presentation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game
{
    /// <summary>
    /// Дочерний DI-скоуп BattleScene. Живёт один бой.
    /// Регистрирует все боевые сервисы: RNG боя, системы, симуляцию, презентацию.
    /// Является дочерним от <see cref="RootLifetimeScope"/> (вики «10» §8.2).
    /// </summary>
    public class CombatLifetimeScope : LifetimeScope
    {
        [Tooltip("Конфиг базовых характеристик (в т.ч. armor-константа K — единственный источник).")]
        [SerializeField] private StatsConfig _statsConfig;

        [Tooltip("Балансный тюнинг симуляции (вики «13» §3.4): печётся в снапшот SimTuning на старте боя.")]
        [SerializeField] private SimTuningConfig _simTuningConfig;

        [Tooltip("Размер ячейки пространственного хэша.")]
        [SerializeField] private float _spatialHashCellSize = 3f;

        [Tooltip("Фиксированный сид боя для воспроизводимости (баг/баланс/реплей). 0 = случайный каждый бой.")]
        [SerializeField] private long _fixedSeed;

        protected override void Configure(IContainerBuilder builder)
        {
            // Арену печём из авторинга в сцене (если он есть); иначе — бесконечное поле.
            // prefab-per-arena через Addressables — будущий свап (вики «15» §4-5): тогда снапшот
            // придёт из загруженного префаба, а не из FindFirstObjectByType.
            ArenaLayoutData layout = BuildArenaLayout();

            RegisterArena(builder, layout);
            RegisterRng(builder);
            RegisterCombatSystems(builder);
            RegisterSimulation(builder, layout);
            RegisterPresentation(builder);

            // Единый арбитр Time.timeScale (пауза/скорость/cinematic slowmo). EntryPoint — чтобы Tick()
            // вёл возврат slowmo, а Dispose вернул timeScale к 1; AsSelf — для инъекции в потребителей.
            builder.RegisterEntryPoint<TimeScaleService>(Lifetime.Scoped).AsSelf();

            // Режиссёр «сочности»: политика global-эффектов (slowmo на килл/конец боя) по MessagePipe-событиям.
            builder.RegisterEntryPoint<CombatFeelDirector>(Lifetime.Scoped);

            // Боевой ввод: пауза/скорость на время этого боя (вики «16» §4).
            builder.RegisterEntryPoint<BattleInputController>(Lifetime.Scoped);
        }

        private ArenaLayoutData BuildArenaLayout()
        {
            var authoring = FindFirstObjectByType<ArenaLayoutAuthoring>();
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

        private void RegisterRng(IContainerBuilder builder)
        {
            bool fixedSeed = _fixedSeed != 0L;
            ulong seed = fixedSeed ? (ulong)_fixedSeed : GenerateBattleSeed();

            Debug.Log($"[CombatLifetimeScope] - Battle seed = {seed}{(fixedSeed ? " (fixed)" : "")}");

            // Сид доступен из DI (лог/реплей/MP), а не только «внутри» RNG.
            builder.RegisterInstance(new BattleSeed(seed));
            builder.RegisterInstance<IRngService>(new XorShiftRng(seed));
        }

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
            builder.Register<RegenSystem>(Lifetime.Scoped);
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
                   .WithParameter("armorK", _statsConfig.ArmorConstantK)
                   .WithParameter("arena", (ArenaBounds?)layout.Bounds)
                   .WithParameter("tuning", (SimTuning?)_simTuningConfig.ToSnapshot())
                   .WithParameter("cameraZone", (Rect2D?)layout.CameraZone);

            StatsConfig cfg = _statsConfig;
            builder.Register<RuntimeUnitFactory>(r => new RuntimeUnitFactory(
                cfg,
                r.Resolve<EffectSystem>(),
                r.Resolve<CombatSimulation>()),
                Lifetime.Scoped);
            builder.Register<BattleSetupBuilder>(Lifetime.Scoped);

            builder.RegisterEntryPoint<CombatLoopService>(Lifetime.Scoped).AsSelf();
        }

        private void RegisterPresentation(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<CombatPresenter>();
            builder.RegisterComponentInHierarchy<CombatDebugDraw>();
            builder.RegisterComponentInHierarchy<CombatAreaFlash>();

            // Камера (вики «16» §5): регистрируем ТОЛЬКО если риг собран в сцене — иначе бой не падает.
            // Держим здесь, рядом с прочей презентацией (отдельный метод внешний форматтер уже сносил).
            if (FindFirstObjectByType<Presentation.CameraModeController>() != null)
            {
                builder.RegisterComponentInHierarchy<Presentation.CombatFocusTarget>();
                builder.RegisterComponentInHierarchy<Presentation.CameraModeController>()
                       .AsSelf().As<Presentation.IScreenShake>();
            }
            else
            {
                // Нет камеры-рига → тряска-заглушка, чтобы CombatFeelDirector резолвился и бой не падал.
                builder.RegisterInstance<Presentation.IScreenShake>(new Presentation.NullScreenShake());
            }
        }

        // TODO Фаза MP: сид боя должен прийти от хоста (в команде старта боя) и лечь в BattleSeed,
        // а не генерироваться локально — иначе RNG хоста и клиента разойдутся. Сейчас ок:
        // модель хост-авторитетная, тикает только хост (см. CombatLoopService).
        private static ulong GenerateBattleSeed()
        {
            return (ulong)System.DateTime.UtcNow.Ticks ^
                   ((ulong)(uint)UnityEngine.Random.Range(0, int.MaxValue) << 32);
        }
    }
}
