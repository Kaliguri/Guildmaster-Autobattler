using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Предмет двух скоупов в одном типе (вики «13» §3.2): <see cref="ItemScope.Vessel"/> — на персонажа
    /// (до N слотов, GameConfig.VesselItemSlots), <see cref="ItemScope.Party"/> — на всю команду/бой.
    /// Один тип, т.к. поля почти совпадают; разделим, если разъедутся.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Item", fileName = "Item")]
    public sealed class ItemData : ContentDefinition
    {
        [SerializeField] private ItemScope _scope = ItemScope.Vessel;
        [Tooltip("Статовая нагрузка (носителю при Vessel, всей команде при Party).")]
        [SerializeField] private StatModifier[] _mods;
        [Tooltip("Эффект-нагрузка (аналогично по скоупу).")]
        [SerializeField] private EffectData[] _grantedEffects;
        [Tooltip("Активка предмета (зелье и т.п.); пусто = пассивный предмет.")]
        [SerializeField] private AbilityData _activeAbility;
        [Tooltip("Заряды активки на бой/забег (0 = без ограничения).")]
        [SerializeField] private int _charges;
        [Tooltip("Базовая цена в магазине.")]
        [SerializeField] private int _cost;
        [Tooltip("Вес в пуле магазина.")]
        [SerializeField] private float _shopWeight = 1f;
        [SerializeField] private Sprite _icon;
        [Tooltip("Информационные теги.")]
        [SerializeField] private TagData[] _infoTags;

        public ItemScope Scope => _scope;
        public StatModifier[] Mods => _mods;
        public EffectData[] GrantedEffects => _grantedEffects;
        public AbilityData ActiveAbility => _activeAbility;
        public int Charges => _charges;
        public int Cost => _cost;
        public float ShopWeight => _shopWeight;
        public Sprite Icon => _icon;
        public TagData[] InfoTags => _infoTags;
    }
}
