using Guildmaster.Combat;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Guildmaster.Net
{
    /// <summary>
    /// Периодически проверяет checksum симуляции между хостом и клиентами.
    /// Хост рассылает checksum каждые N тиков; клиенты сравнивают и логируют рассинхроны.
    /// Цель спайка — убедиться, что команды от клиента применяются детерминированно
    /// у обоих участников (вики «10» §6.2).
    /// </summary>
    public sealed class SimSyncProbe : NetworkBehaviour
    {
        [Tooltip("Частота проверки синхрона: каждые N тиков.")]
        [SerializeField] private int _checkInterval = 30;

        private CombatSimulation _simulation;
        private int              _lastCheckedTick = -1;

        [Inject]
        public void Construct(CombatSimulation simulation)
        {
            _simulation = simulation;
        }

        private void Update()
        {
            if (_simulation == null || !IsServer) return;

            int tick = _simulation.CurrentTick;
            if (tick - _lastCheckedTick < _checkInterval) return;

            _lastCheckedTick = tick;
            ulong checksum = _simulation.ComputeChecksum();
            BroadcastChecksumClientRpc(tick, checksum);
        }

        [ClientRpc]
        private void BroadcastChecksumClientRpc(int tick, ulong hostChecksum)
        {
            if (IsServer) return;
            if (_simulation == null) return;

            ulong localChecksum = _simulation.ComputeChecksum();

            if (localChecksum != hostChecksum)
            {
                Debug.LogError(
                    $"[SimSyncProbe] - РАССИНХРОН на тике {tick}: " +
                    $"хост={hostChecksum:X16}, клиент={localChecksum:X16}");
            }
            else
            {
                Debug.Log($"[SimSyncProbe] - Тик {tick}: checksum совпадает ({localChecksum:X16})");
            }
        }
    }
}
