using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// «Чемпион» — реликвия игрока: боевой кит (<see cref="UnitData"/>) + мета забега (редкость,
    /// нагрузка на забег, альт-пресеты AI, доступ к Extended-зонам). Из неё + <see cref="StatsConfig"/>
    /// фабрика собирает рантайм-юнита (вики «10» §4.2, «13» §3.1).
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Relic", fileName = "Relic")]
    public sealed class RelicData : UnitData
    {
        [Header("Relic meta")]
        [Tooltip("Легаси строковые теги. Новый путь — InfoTags (TagData[]) на UnitData.")]
        [SerializeField] private string[] _tags;

        [Tooltip("Редкость (GDD 5). У вертикального среза — все Common.")]
        [SerializeField] private RelicRarity _rarity = RelicRarity.Common;

        [Tooltip("Нагрузка на весь забег (штрафы Cursed / бонусы Divine); у Common пусто. Чисто статовый штраф = EffectData со StatModifierComponent и бессрочной длительностью.")]
        [SerializeField] private EffectData[] _runEffects;

        [Tooltip("Альтернативные пресеты AI на выбор игрока в Prep (дефолт — AiPreset из базы UnitData).")]
        [SerializeField] private AIPresetData[] _altAiPresets;

        [Header("Deployment (Arena)")]
        [Tooltip("Доступ к расширенным зонам расстановки (Extended): манёвренные чемпионы/убийцы. false = только базовые зоны (вики «15» §6).")]
        [SerializeField] private bool _canUseExtendedDeployment;

        public string[] Tags => _tags;
        public RelicRarity Rarity => _rarity;
        public EffectData[] RunEffects => _runEffects;
        public AIPresetData[] AltAiPresets => _altAiPresets;
        public bool CanUseExtendedDeployment => _canUseExtendedDeployment;
    }
}
