using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Random;
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

        [Tooltip("Размер ячейки пространственного хэша.")]
        [SerializeField] private float _spatialHashCellSize = 3f;

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

            // Боевой ввод: пауза/рестарт на время этого боя (вики «16» §4).
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
            builder.RegisterInstance<IRngService>(new XorShiftRng(GenerateBattleSeed()));
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
                   .WithParameter("arena", (ArenaBounds?)layout.Bounds);

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
                builder.RegisterComponentInHierarchy<Presentation.CameraModeController>();
            }
        }

        // TODO Фаза MP: сид боя должен прийти от хоста (в команде старта боя), а не
        // генерироваться локально — иначе RNG хоста и клиента разойдутся. Сейчас ок:
        // модель хост-авторитетная, тикает только хост (см. CombatLoopService).
        private static ulong GenerateBattleSeed()
        {
            return (ulong)System.DateTime.UtcNow.Ticks ^
                   ((ulong)(uint)UnityEngine.Random.Range(0, int.MaxValue) << 32);
        }
    }
}
