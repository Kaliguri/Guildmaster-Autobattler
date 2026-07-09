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

        /// <summary>Союзник с наименьшим HP% — глобально, без ограничения дальности (хилер-ульта «Длань жизни»).</summary>
        LowestHpAlly = 3,
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

        [Header("Direct damage (Phase 3)")]
        [Tooltip("Множитель прямого урона от AutoAttackDamage кастующего. 0 = только эффекты (поведение Ф2). «Стальной вихрь» = 3.")]
        [SerializeField] private float _damageMultiplier;

        [Header("Area of effect (Phase 3)")]
        [Tooltip("Форма зоны поражения. None = одиночная цель по TargetMode (поведение Ф2).")]
        [SerializeField] private AreaShape _areaShape = AreaShape.None;

        [Tooltip("Радиус зоны для Circle (мировые единицы). Центр — кастующий (удар вокруг себя).")]
        [SerializeField] private float _areaRadius;

        [Header("Heal payload (Phase 3) — Светлый пастырь")]
        [Tooltip("Плоское лечение цели (X). >0 делает способность лечащей: Heal вместо урона. Итог × HealShieldDealtEff/TakenEff.")]
        [SerializeField] private float _healFlat;

        [Tooltip("Доля недостающего HP цели, добавляемая к лечению («Длань жизни» = 1.0 → долечивает до полного). >0 делает способность лечащей.")]
        [SerializeField] private float _healPctTargetMissingHp;

        [Header("Cast condition (blocks D/E, Phase 3)")]
        [Tooltip("Когда кастовать: Immediately = как только готова; EnemiesInRadius = врагов в радиусе ≥ CastConditionCount; AllyTargetHpBelowPct = HP% выбранной цели ≤ CastConditionHpPct.")]
        [SerializeField] private CastCondition _castCondition = CastCondition.Immediately;

        [Tooltip("X для EnemiesInRadius: минимум врагов в радиусе условия.")]
        [SerializeField] private int _castConditionCount = 1;

        [Tooltip("Радиус подсчёта врагов для условия каста. ≤ 0 = взять AreaRadius.")]
        [SerializeField] private float _castConditionRadius;

        [Tooltip("Порог HP% (0..1) для AllyTargetHpBelowPct: кастуем, когда HP% выбранной цели ≤ этого.")]
        [SerializeField] private float _castConditionHpPct = 0.5f;

        [Tooltip("Отмена условия (блок E): если HP% кастующего ≤ этого — кастуем независимо от условия. У лечащих способностей цель тогда = сам кастующий. 0 = выкл.")]
        [SerializeField] private float _castOverrideSelfHpPct;

        public string Id => _id;
        public EffectData[] Effects => _effects;
        public float BaseCooldown => _baseCooldown;
        public float ResourceCost => _resourceCost;
        public AbilityTargetMode TargetMode => _targetMode;

        public float DamageMultiplier => _damageMultiplier;
        public AreaShape AreaShape => _areaShape;
        public float AreaRadius => _areaRadius;
        public float HealFlat => _healFlat;
        public float HealPctTargetMissingHp => _healPctTargetMissingHp;
        /// <summary>Способность лечит (а не бьёт), если задана любая хил-нагрузка.</summary>
        public bool IsHeal => _healFlat > 0f || _healPctTargetMissingHp > 0f;
        public CastCondition CastCondition => _castCondition;
        public int CastConditionCount => _castConditionCount;
        /// <summary>Радиус условия каста; при ≤ 0 откатывается к <see cref="AreaRadius"/>.</summary>
        public float CastConditionRadius => _castConditionRadius > 0f ? _castConditionRadius : _areaRadius;
        public float CastConditionHpPct => _castConditionHpPct;
        public float CastOverrideSelfHpPct => _castOverrideSelfHpPct;
    }
}
