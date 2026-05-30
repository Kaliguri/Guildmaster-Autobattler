using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Game
{
    /// <summary>
    /// Минимальный строитель начального состояния боя для Фазы 1:
    /// принимает массивы SO реликвий/сосудов и передаёт юнитов в симуляцию
    /// через <see cref="CombatSimulation.EnqueueUnitSpawn"/>.
    /// Полный <c>RunState</c> — Фаза 5.
    /// </summary>
    public sealed class BattleSetupBuilder
    {
        private readonly RuntimeUnitFactory _factory;
        private readonly CombatSimulation   _simulation;

        public BattleSetupBuilder(RuntimeUnitFactory factory, CombatSimulation simulation)
        {
            _factory    = factory;
            _simulation = simulation;
        }

        /// <summary>Создать тестовый бой: N юнитов за каждую сторону по SO-данным.</summary>
        public void SetupTestBattle(
            RelicData[] teamARelics,
            RelicData[] teamBRelics,
            float       spreadDistance = 1.5f)
        {
            float teamAX = -5f;
            float teamBX =  5f;

            for (int i = 0; i < teamARelics.Length; i++)
            {
                Vector2 pos = new Vector2(teamAX, i * spreadDistance);
                var unit = _factory.Create(teamARelics[i], null, 0, pos);
                _simulation.EnqueueUnitSpawn(unit);
            }

            for (int i = 0; i < teamBRelics.Length; i++)
            {
                Vector2 pos = new Vector2(teamBX, i * spreadDistance);
                var unit = _factory.Create(teamBRelics[i], null, 1, pos);
                _simulation.EnqueueUnitSpawn(unit);
            }
        }
    }
}
