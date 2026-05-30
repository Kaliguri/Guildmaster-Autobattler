using Guildmaster.Combat;
using Guildmaster.Combat.Commands;
using Guildmaster.Core.Simulation;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Guildmaster.Net
{
    /// <summary>
    /// Host-authoritative реле команд симуляции.
    /// Клиент → ServerRpc(intent); хост штампует TargetTick/Seq и broadcast ClientRpc;
    /// все применяют команду детерминированно на одном тике — основа синхрона (вики «10» §6.1).
    /// </summary>
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
