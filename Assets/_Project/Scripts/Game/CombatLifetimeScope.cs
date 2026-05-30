using Guildmaster.Combat;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
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
        [Tooltip("Конфиг базовых характеристик.")]
        [SerializeField] private StatsConfig _statsConfig;

        [Tooltip("Константа K в формуле брони.")]
        [SerializeField] private float _armorK = 100f;

        [Tooltip("Размер ячейки пространственного хэша.")]
        [SerializeField] private float _spatialHashCellSize = 3f;

        protected override void Configure(IContainerBuilder builder)
        {
            RegisterRng(builder);
            RegisterCombatSystems(builder);
            RegisterSimulation(builder);
            RegisterPresentation(builder);
        }

        private void RegisterRng(IContainerBuilder builder)
        {
            builder.RegisterInstance<IRngService>(new XorShiftRng(GenerateBattleSeed()));
        }

        private void RegisterCombatSystems(IContainerBuilder builder)
        {
            float cellSize = _spatialHashCellSize;
            builder.Register<SpatialHash>(_ => new SpatialHash(cellSize), Lifetime.Scoped);
            builder.Register<TargetingSystem>(Lifetime.Scoped);
            builder.Register<MovementSystem>(Lifetime.Scoped);
            builder.Register<AutoAttackSystem>(Lifetime.Scoped);
            builder.Register<ProjectileSystem>(Lifetime.Scoped);
            builder.Register<DeathSystem>(Lifetime.Scoped);
        }

        private void RegisterSimulation(IContainerBuilder builder)
        {
            float armorK = _armorK;
            builder.Register<CombatSimulation>(r => new CombatSimulation(
                r.Resolve<IRngService>(),
                armorK,
                r.Resolve<SpatialHash>(),
                r.Resolve<TargetingSystem>(),
                r.Resolve<MovementSystem>(),
                r.Resolve<AutoAttackSystem>(),
                r.Resolve<ProjectileSystem>(),
                r.Resolve<DeathSystem>()),
                Lifetime.Scoped);

            StatsConfig cfg = _statsConfig;
            builder.Register<RuntimeUnitFactory>(_ => new RuntimeUnitFactory(cfg), Lifetime.Scoped);
            builder.Register<BattleSetupBuilder>(Lifetime.Scoped);

            builder.RegisterEntryPoint<CombatLoopService>(Lifetime.Scoped).AsSelf();
        }

        private void RegisterPresentation(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<CombatPresenter>();
            builder.RegisterComponentInHierarchy<CombatDebugDraw>();
        }

        private static ulong GenerateBattleSeed()
        {
            return (ulong)System.DateTime.UtcNow.Ticks ^
                   ((ulong)(uint)UnityEngine.Random.Range(0, int.MaxValue) << 32);
        }
    }
}
