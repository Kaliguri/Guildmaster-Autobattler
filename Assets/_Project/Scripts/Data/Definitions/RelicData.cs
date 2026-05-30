using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// «Чемпион» — стат-блок и боевые категории юнита. Из него + <see cref="StatsConfig"/>
    /// фабрика собирает рантайм-юнита (вики «10» §4.2, §5.2).
    /// </summary>
    /// <remarks>СКЕЛЕТ Фазы 1: стат-блок + категории. Активки/пассивки (через <see cref="EffectData"/>) — Фаза 2.</remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Relic", fileName = "Relic")]
    public sealed class RelicData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Ключ локализации отображаемого имени (EN/RU задаётся в таблице локализации).")]
        [SerializeField] private string _displayNameKey;
        [SerializeField] private string[] _tags;

        [Header("Combat categories")]
        [SerializeField] private DamageType _damageType = DamageType.Physical;
        [SerializeField] private AttackType _attackType = AttackType.Melee;
        [SerializeField] private ResourceType _resourceType = ResourceType.None;

        [Header("Base stat block")]
        [Tooltip("Базовые модификаторы реликвии. Накладываются поверх дефолтов StatsConfig при сборке юнита.")]
        [SerializeField] private StatModifier[] _stats;

        // --- Фаза 2: даруемые эффекты/способности ---
        [Header("Passives")]
        [Tooltip("Пассивные эффекты (накладываются при сборке юнита, обычно постоянные).")]
        [SerializeField] private EffectData[] _grantedEffects;

        [Header("Active abilities")]
        [Tooltip("Активные способности (кулдаун/ресурс). Слотов — по редкости (Common 1 → Rare 2 → Epic 3).")]
        [SerializeField] private AbilityData[] _abilities;

        public string DisplayNameKey => _displayNameKey;
        public string[] Tags => _tags;
        public DamageType DamageType => _damageType;
        public AttackType AttackType => _attackType;
        public ResourceType ResourceType => _resourceType;
        public StatModifier[] Stats => _stats;
        public EffectData[] GrantedEffects => _grantedEffects;
        public AbilityData[] Abilities => _abilities;
    }
}
