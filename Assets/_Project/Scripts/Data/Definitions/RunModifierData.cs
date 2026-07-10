using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Модификатор забега (заготовка ascension/pact, вики «13» §3.2). Точная механика не задизайнена —
    /// поля минимальны, применяются тем же resolve-механизмом при бейке, что и DifficultyConfig.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Run Modifier", fileName = "RunModifier")]
    public sealed class RunModifierData : ContentDefinition
    {
        [SerializeField] private RunModTarget _target = RunModTarget.Players;
        [Tooltip("Статовая нагрузка на цель.")]
        [SerializeField] private StatModifier[] _mods;
        [Tooltip("Эффект-нагрузка на цель.")]
        [SerializeField] private EffectData[] _grantedEffects;
        [Tooltip("Множитель награды за взятый модификатор (если по дизайну).")]
        [SerializeField] private float _rewardMult = 1f;
        [Tooltip("Ступень (кумулятивные уровни а-ля Ascension).")]
        [SerializeField] private int _tier;
        [Tooltip("Информационные теги.")]
        [SerializeField] private TagData[] _infoTags;

        public RunModTarget Target => _target;
        public StatModifier[] Mods => _mods;
        public EffectData[] GrantedEffects => _grantedEffects;
        public float RewardMult => _rewardMult;
        public int Tier => _tier;
        public TagData[] InfoTags => _infoTags;
    }
}
