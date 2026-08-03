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
            // Классовый профиль грузим здесь, чтобы бенчи считали базу так же, как бой («таблица не врёт»).
            Factory = new RuntimeUnitFactory(config, BalanceAssets.LoadClassBalanceConfig(), Effects, Sim);

            // Без этой строки призывать в бою НЕЧЕМ: сим отдаёт рождение тел наружу, и до 2026-08-03
            // стенд фабрику не подавал — призыватели дрались в бенчах в одиночку, а таблица показывала
            // это как их честную силу. Игровой путь биндит фабрику в EncounterLoader.
            Sim.BindSummonFactory(Factory);
        }

        /// <summary>Собрать реального юнита из контент-SO через боевую фабрику (те же шаги, что в бою).</summary>
        public RuntimeUnit Real(UnitData unit, VesselData vessel, int team, Vector2 position)
            => Factory.Create(unit, vessel, team, position);
    }
}
