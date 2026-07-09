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

        private void RegisterSimulation(IContainerBuilder builder)
        {
            // VContainer сам разрешит зависимости конструктора (RNG, SpatialHash, все системы) —
            // вручную перечислять Resolve не нужно. Единственный не-инъектируемый параметр —
            // float armorK — передаём через WithParameter по имени. Добавил систему в ctor —
            // ничего тут править не надо, лишь бы она была зарегистрирована.
            builder.Register<CombatSimulation>(Lifetime.Scoped)
                   .WithParameter("armorK", _armorK);

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
