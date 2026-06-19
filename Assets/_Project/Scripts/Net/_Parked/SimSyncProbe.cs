using Guildmaster.Combat;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Guildmaster.Net
{
    /// <summary>
    /// ⛔ ЗАПАРКОВАНО (lockstep). Сетевая модель проекта — <b>host-authoritative</b> (решение
    /// зафиксировано 2026-06-19, см. вики «Архитектура кода» → «Сетевая модель»). Этот класс
    /// сравнивает checksum симуляции между пирами — инструмент <b>детерминированного lockstep</b>,
    /// где КАЖДЫЙ пир тикает идентичный сим. При host-authoritative считает только хост, второй
    /// симуляции для сравнения нет — класс не используется. НЕ удалён намеренно: пригодится для
    /// SP-реплеев/дебага и как референс, если модель когда-нибудь пересмотрят. Не вешать на сцены.
    /// <para>
    /// Исходное назначение: хост рассылает checksum каждые N тиков; клиенты сравнивают и логируют
    /// рассинхроны (вики «10» §6.2).
    /// </para>
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
