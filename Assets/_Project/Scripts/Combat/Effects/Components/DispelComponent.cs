using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Диспел: при наложении снимает с носителя подходящие эффекты (вики «6» §5.4). Покрывает
    /// purge (баффы врага) и cleanse (дебаффы союзника) через <see cref="DispelTargetPolarity"/>.
    /// Обычно живёт в мгновенном эффекте (BaseDuration = 0).
    /// </summary>
    [Serializable]
    public sealed class DispelComponent : IRuntimeEffectComponent
    {
        [Tooltip("Какую полярность снимать: Any / Buff (purge) / Debuff (cleanse).")]
        [SerializeField] private DispelTargetPolarity _targetPolarity = DispelTargetPolarity.Debuff;

        [Tooltip("Категории-теги (None = любая): напр. снять только DoT или только Control.")]
        [SerializeField] private EffectTag _targetTags = EffectTag.None;

        [Tooltip("Снимает эффекты с CleanseTier ≤ DispelPower.")]
        [SerializeField] private int _dispelPower = 1;

        [Tooltip("Сколько максимум снять (0 = все подходящие).")]
        [SerializeField] private int _maxCount;

        public void OnApply(in EffectContext ctx)
        {
            ctx.Combat.Dispel(new DispelRequest(ctx.Target, _targetPolarity, _targetTags, _dispelPower, _maxCount));
        }

        public void OnExpire(in EffectContext ctx) { }
    }
}
