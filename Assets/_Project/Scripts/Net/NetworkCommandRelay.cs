using Guildmaster.Combat;
using Guildmaster.Combat.Commands;
using Guildmaster.Core.Simulation;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Guildmaster.Net
{
    /// <summary>
    /// Реле команд симуляции. Концепт «клиент → ServerRpc(intent) → хост ставит в очередь» —
    /// <b>keeper</b> при host-authoritative модели (решение 2026-06-19, см. вики «Сетевая модель»).
    /// </summary>
    /// <remarks>
    /// ⚠️ Текущая реализация частично в стиле lockstep: хост штампует TargetTick и broadcast'ит
    /// команду ВСЕМ пирам, чтобы те применили её «на одном тике». При host-authoritative клиенты
    /// свою симуляцию не тикают — хост применяет команду у себя и реплицирует РЕЗУЛЬТАТ (состояние).
    /// Поэтому при сборке MP (Фаза MP) часть с broadcast-ClientRpc будет переработана: останется
    /// путь intent→host, уйдёт «все применяют на одном тике». Исходный замысел — вики «10» §6.1.
    /// </remarks>
    public sealed class NetworkCommandRelay : NetworkBehaviour
    {
        [Tooltip("Lookahead в тиках: на сколько тиков вперёд хост назначает команде TargetTick.")]
        [SerializeField] private int _lookaheadTicks = 2;

        private CombatSimulation _simulation;

        [Inject]
        public void Construct(CombatSimulation simulation)
        {
            _simulation = simulation;
        }

        /// <summary>
        /// Клиент вызывает этот метод, чтобы отправить команду на хост.
        /// Хост назначает tick и рассылает всем.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void SubmitCommandServerRpc(CommandType commandType, ServerRpcParams rpc = default)
        {
            if (!IsServer) return;

            int targetTick = _simulation != null
                ? _simulation.CurrentTick + _lookaheadTicks
                : 0;

            BroadcastCommandClientRpc(commandType, targetTick);
        }

        [ClientRpc]
        private void BroadcastCommandClientRpc(CommandType commandType, int targetTick)
        {
            if (_simulation == null)
            {
                Debug.LogWarning("[NetworkCommandRelay] - Симуляция не зарегистрирована");
                return;
            }

            ICombatCommand command = commandType switch
            {
                CommandType.Pause  => new PauseCommand (targetTick),
                CommandType.Resume => new ResumeCommand(targetTick),
                _                  => null,
            };

            if (command != null)
            {
                _simulation.EnqueueCommand(command);
                Debug.Log($"[NetworkCommandRelay] - {commandType} применится на тике {targetTick}");
            }
        }
    }

    /// <summary>Тип команды, передаваемой через сеть.</summary>
    public enum CommandType : byte
    {
        Pause  = 0,
        Resume = 1,
    }
}
