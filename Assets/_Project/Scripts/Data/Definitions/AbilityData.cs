using System;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>Как выбирается цель активной способности (полный AI-таргетинг — Фаза 3).</summary>
    public enum AbilityTargetMode
    {
        /// <summary>На себя.</summary>
        Self = 0,

        /// <summary>Ближайший враг (в Фазе 2 — текущая цель автоатаки).</summary>
        NearestEnemy = 1,

        /// <summary>Ближайший союзник (для бафф/хил-способностей).</summary>
        NearestAlly = 2,
    }

    /// <summary>
    /// Определение активной способности реликвии: эффекты при касте, кулдаун, стоимость ресурса,
    /// способ выбора цели. Сериализуется на <see cref="RelicData"/> (вики «6» §1, «12» §2.4).
    /// Пассивки — это <see cref="RelicData.GrantedEffects"/> с постоянной длительностью, отдельной
    /// сущности не требуют.
    /// </summary>
    [Serializable]
    public sealed class AbilityData
    {
        [SerializeField] private string _id;

        [Tooltip("Эффекты, накладываемые на цель при касте.")]
        [SerializeField] private EffectData[] _effects;

        [Tooltip("Базовый кулдаун, сек. Фактический = base × CooldownEff кастующего.")]
        [SerializeField] private float _baseCooldown = 5f;

        [Tooltip("Стоимость ресурса за каст (0 = бесплатно).")]
        [SerializeField] private float _resourceCost;

        [SerializeField] private AbilityTargetMode _targetMode = AbilityTargetMode.NearestEnemy;

        public string Id => _id;
        public EffectData[] Effects => _effects;
        public float BaseCooldown => _baseCooldown;
        public float ResourceCost => _resourceCost;
        public AbilityTargetMode TargetMode => _targetMode;
    }
}
