using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Трейт «Сосуда» (бывш. «перк»; GDD: выбор 1 из 3, вики «13» §3.2). Стат/эффект-нагрузка + вес и
    /// группа взаимоисключения для генерации.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Trait", fileName = "Trait")]
    public sealed class TraitData : ContentDefinition
    {
        [SerializeField] private TraitPolarity _polarity = TraitPolarity.Positive;
        [Tooltip("Статовая нагрузка трейта.")]
        [SerializeField] private StatModifier[] _mods;
        [Tooltip("Эффект-нагрузка (трейты-триггеры).")]
        [SerializeField] private EffectData[] _grantedEffects;
        [Tooltip("Вес в генерации «1 из 3».")]
        [SerializeField] private float _selectionWeight = 1f;
        [Tooltip("Группа взаимоисключения (пусто = нет); один трейт из группы на «Сосуда».")]
        [SerializeField] private string _exclusiveGroup;
        [Tooltip("Информационные теги.")]
        [SerializeField] private TagData[] _infoTags;

        public TraitPolarity Polarity => _polarity;
        public StatModifier[] Mods => _mods;
        public EffectData[] GrantedEffects => _grantedEffects;
        public float SelectionWeight => _selectionWeight;
        public string ExclusiveGroup => _exclusiveGroup;
        public TagData[] InfoTags => _infoTags;
    }
}
