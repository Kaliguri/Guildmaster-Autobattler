using Guildmaster.Combat;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Одноразовое headless-окружение одного боя: детерминированный <see cref="CombatSimulation"/>
    /// без презентации + общий <see cref="EffectSystem"/> + фабрика юнитов. Сборка ядра копирует
    /// проверенный конструктор из <c>CombatSimulationTests.BuildSim</c> (тот же короткий overload:
    /// Displacement/Separation — внутренние дефолты сима). Одно окружение = один бой; переиспользовать
    /// между боями нельзя (состояние сима накапливается).
    /// </summary>
    internal sealed class SimEnvironment
    {
        public readonly CombatSimulation Sim;
        public readonly EffectSystem Effects;
        public readonly RuntimeUnitFactory Factory;
        public readonly StatsConfig Config;

        private const float DefaultArmorK = 100f;
        private const float SpatialCellSize = 3f;

        public SimEnvironment(ulong seed, StatsConfig config)
        {
            Config = config;
            IRngService rng = new XorShiftRng(seed);
            Effects = new EffectSystem();

            float armorK = config != null ? config.ArmorConstantK : DefaultArmorK;

            Sim = new CombatSimulation(
                rng,
                armorK,
                new SpatialHash(SpatialCellSize),
                new BrainSystem(),
                new AbilitySystem(),
                new MovementSystem(),
                new AutoAttackSystem(),
                new ProjectileSystem(),
                new DeathSystem(),
                Effects,
                new RegenSystem());

            // Фабрика делит EffectSystem и контекст с симом (пассивки/HP-init зовут OnApply в этом же боевом контексте).
            Factory = new RuntimeUnitFactory(config, Effects, Sim);
        }

        /// <summary>Собрать реального юнита из контент-SO через боевую фабрику (те же шаги, что в бою).</summary>
        public RuntimeUnit Real(UnitData unit, VesselData vessel, int team, Vector2 position)
            => Factory.Create(unit, vessel, team, position);
    }
}
