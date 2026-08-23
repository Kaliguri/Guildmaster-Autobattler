using UnityEngine;
using UnityEngine.Serialization;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// «Чемпион» — мементо игрока: боевой кит (<see cref="UnitData"/>) + мета забега (редкость,
    /// нагрузка на забег, альт-пресеты AI, доступ к Extended-зонам). Из неё + <see cref="StatsConfig"/>
    /// фабрика собирает рантайм-юнита (вики «10» §4.2, «13» §3.1).
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Relic", fileName = "Relic")]
    public sealed class RelicData : UnitData
    {
        [Header("Relic meta")]
        [Tooltip("Легаси строковые теги. Новый путь — InfoTags (TagData[]) на UnitData.")]
        [SerializeField] private string[] _tags;

        [Tooltip("Класс кита (GDD 5): обычный / проклятый (сила ценой платы) / божественный. Боевая характеристика, НЕ шанс выпадения.")]
        [FormerlySerializedAs("_rarity")]
        [SerializeField] private KitPower _kitPower = KitPower.Common;

        [Tooltip("Редкость выпадения (экономика): как часто мементо встречается в наградах и магазине. Ортогональна классу кита.")]
        [SerializeField] private DropRarity _dropRarity = DropRarity.Common;

        [Tooltip("Нагрузка на весь забег (штрафы Cursed / бонусы Divine); у Common пусто. Чисто статовый штраф = EffectData со StatModifierComponent и бессрочной длительностью.")]
        [SerializeField] private EffectData[] _runEffects;

        [Tooltip("Альтернативные пресеты AI на выбор игрока в Prep (дефолт — AiPreset из базы UnitData).")]
        [SerializeField] private AIPresetData[] _altAiPresets;

        [Header("Deployment (Arena)")]
        [Tooltip("Доступ к расширенным зонам расстановки (Extended): манёвренные чемпионы/убийцы. false = только базовые зоны (вики «15» §6).")]
        [SerializeField] private bool _canUseExtendedDeployment;

        public string[] Tags => _tags;
        public KitPower KitPower => _kitPower;
        public DropRarity DropRarity => _dropRarity;
        public EffectData[] RunEffects => _runEffects;
        public AIPresetData[] AltAiPresets => _altAiPresets;
        public bool CanUseExtendedDeployment => _canUseExtendedDeployment;
    }
}
