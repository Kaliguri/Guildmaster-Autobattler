using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Гильдмастер (GDD 4, вики «13» §3.2): пассивный бонус гильдии, уникальные особенности,
    /// стартовые реликвии (по id) и ресурс.
    /// <para>Заклинаний у Гильдмастера НЕТ: ввода игрока во время боя не существует, бой — чистый
    /// результат подготовки (решение 2026-07, см. ГДД <c>combat-system</c>). Поле <c>_spells</c>
    /// пережило вырезание механики и снято 2026-07-26 — его никто не читал.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Guildmaster", fileName = "Guildmaster")]
    public sealed class GuildmasterData : ContentDefinition
    {
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

        public StatModifier[] Mods => _mods;
        public EffectData[] UniqueEffects => _uniqueEffects;
        public string[] StartingRelicIds => _startingRelicIds;
        public int StartingGold => _startingGold;
        public Sprite Portrait => _portrait;
        public TagData[] InfoTags => _infoTags;
    }
}
