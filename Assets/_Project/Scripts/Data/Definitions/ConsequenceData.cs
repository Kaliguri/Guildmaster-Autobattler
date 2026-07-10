using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Последствие боя: травма или закалка (объединяет бывш. Injury/Mettle; GDD 9, вики «13» §3.2).
    /// Травма снимается «Лечением» (<see cref="HealCostGold"/>), закалка постоянна.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Consequence", fileName = "Consequence")]
    public sealed class ConsequenceData : ContentDefinition
    {
        [SerializeField] private ConsequencePolarity _polarity = ConsequencePolarity.Injury;
        [Tooltip("Модификаторы «Сосуду».")]
        [SerializeField] private StatModifier[] _mods;
        [Tooltip("Для сложных (триггерных) последствий.")]
        [SerializeField] private EffectData[] _grantedEffects;
        [Tooltip("Тяжесть/ступень.")]
        [SerializeField] private int _severity = 1;
        [Tooltip("Цена снятия в «Лечении» (используется только при Injury).")]
        [SerializeField] private int _healCostGold;
        [Tooltip("Вес при выдаче.")]
        [SerializeField] private float _weight = 1f;
        [Tooltip("Информационные теги.")]
        [SerializeField] private TagData[] _infoTags;

        public ConsequencePolarity Polarity => _polarity;
        public StatModifier[] Mods => _mods;
        public EffectData[] GrantedEffects => _grantedEffects;
        public int Severity => _severity;
        public int HealCostGold => _healCostGold;
        public float Weight => _weight;
        public TagData[] InfoTags => _infoTags;
    }
}
