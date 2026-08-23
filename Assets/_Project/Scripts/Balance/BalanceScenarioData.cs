using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Balance
{
    /// <summary>
    /// Контракт кастом-боя для стенда баланса SimBench: два состава, потолок длительности, сид.
    /// Гоняется editor-раннером (<c>Guildmaster.Balance.Editor</c>) — SO лежит в runtime-сборке
    /// только чтобы ассеты сериализовались. Дополняет процедурные бенчи (DPS/выживаемость/дуэли),
    /// не заменяет их. Дизайн: <c>docs/wiki/tech/40-planning/simbench.md</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Balance/Scenario", fileName = "BalanceScenario")]
    public sealed class BalanceScenarioData : ScriptableObject
    {
        /// <summary>Одна запись состава: кто, с каким пилотом и сколько копий.</summary>
        [Serializable]
        public struct SideEntry
        {
            [Tooltip("Боевой кит: мементо или враг. Пусто = запись пропускается.")]
            public UnitData Unit;

            [Tooltip("Пилот (перки). Пусто = без сосуда.")]
            public VesselData Vessel;

            [Min(1)]
            [Tooltip("Сколько копий этого юнита выставить.")]
            public int Count;
        }

        [Header("Team 0 (левая сторона)")]
        [SerializeField] private SideEntry[] _teamA = Array.Empty<SideEntry>();

        [Header("Team 1 (правая сторона)")]
        [SerializeField] private SideEntry[] _teamB = Array.Empty<SideEntry>();

        [Header("Run")]
        [Min(1f)]
        [Tooltip("Потолок длительности боя, сек. По достижении — timeout (по существу не ничья).")]
        [SerializeField] private float _maxSeconds = 120f;

        [Tooltip("Сид RNG. Сейчас бой RNG-free по исходу — поле на будущее (криты/разброс).")]
        [SerializeField] private ulong _seed = 1UL;

        public IReadOnlyList<SideEntry> TeamA => _teamA;
        public IReadOnlyList<SideEntry> TeamB => _teamB;
        public float MaxSeconds => _maxSeconds;
        public ulong Seed => _seed;
    }
}
