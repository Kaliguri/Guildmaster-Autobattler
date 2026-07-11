using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Гильдмастер (GDD 4, вики «13» §3.2): начальные заклинания, пассивный бонус гильдии, уникальные
    /// особенности, стартовые реликвии (по id) и ресурс.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Guildmaster", fileName = "Guildmaster")]
    public sealed class GuildmasterData : ContentDefinition
    {
        [Tooltip("Начальные заклинания ГМ (реюз AbilityData: кулдаун/стоимость/таргетинг).")]
        [SerializeField] private AbilityData[] _spells;
        [Tooltip("Модификаторы гильдии (пассивный бонус всей команде).")]
        [SerializeField] private StatModifier[] _mods;
        [Tooltip("Уникальные особенности ГМ — эффект-нагрузка на забег/бой.")]
        [SerializeField] private EffectData[] _uniqueEffects;
        [Tooltip("Начальные реликвии (id, пикер из реестра).")]
        [SerializeField] private string[] _startingRelicIds;
        [Tooltip("Стартовый ресурс забега.")]
        [SerializeField] private int _startingGold;
        [Tooltip("Портрет для выбора/HUD.")]
        [SerializeField] private Sprite _portrait;
        [Tooltip("Информационные теги (стиль ГМ).")]
        [SerializeField] private TagData[] _infoTags;

        public AbilityData[] Spells => _spells;
        public StatModifier[] Mods => _mods;
        public EffectData[] UniqueEffects => _uniqueEffects;
        public string[] StartingRelicIds => _startingRelicIds;
        public int StartingGold => _startingGold;
        public Sprite Portrait => _portrait;
        public TagData[] InfoTags => _infoTags;
    }
}
