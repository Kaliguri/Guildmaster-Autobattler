using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// «Пилот» — носитель реликвии. Несёт идентичность и (Фаза 2/4) перки-модификаторы,
    /// которые накладываются на стат-сборку поверх реликвии (вики «10» §4.2).
    /// </summary>
    /// <remarks>СКЕЛЕТ Фазы 1: только идентичность. Перки — Фаза 2/4.</remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Vessel", fileName = "Vessel")]
    public sealed class VesselData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _displayNameKey;
        [SerializeField] private string[] _tags;

        // --- Фаза 2/4: перки ---
        [Header("Perks (Фаза 2/4)")]
        [Tooltip("Плейсхолдер перков-модификаторов. Структура и наполнение — Фаза 2/4.")]
        [SerializeField] private StatModifier[] _perkModifiers;

        public string DisplayNameKey => _displayNameKey;
        public string[] Tags => _tags;
        public StatModifier[] PerkModifiers => _perkModifiers;
    }
}
