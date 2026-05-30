using UnityEngine;

namespace Guildmaster.Combat.Commands
{
    /// <summary>Добавляет предварительно созданного юнита в симуляцию на указанном тике.</summary>
    public sealed class SpawnUnitCommand : ICombatCommand
    {
        public int TargetTick { get; }

        private readonly RuntimeUnit _unit;

        public SpawnUnitCommand(int targetTick, RuntimeUnit unit)
        {
            TargetTick = targetTick;
            _unit      = unit;
        }

        public void Apply(CombatSimulation sim) => sim.EnqueueUnitSpawn(_unit);
    }
}
