using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Диспел: при наложении снимает с носителя подходящие эффекты (вики «6» §5.4). Покрывает
    /// purge (баффы врага) и cleanse (дебаффы союзника) через <see cref="DispelTargetPolarity"/>.
    /// Обычно живёт в мгновенном эффекте (BaseDuration = 0).
    /// <para><b>Числа:</b> <c>_targetPolarity</c> — что снимаем (дебаффы союзнику / баффы врагу / любое);
    /// <c>_targetTags</c> — сузить до категории (None = любая); <c>_dispelPower</c> — СИЛА: снимает
    /// эффекты с тиром ≤ этого числа (1 — базовые, 2 — плюс жёсткий контроль, 3 — особые);
    /// <c>_maxCount</c> — сколько эффектов максимум за раз (0 = все подходящие).</para>
    /// <para><b>Готча:</b> «снять эффект» ≠ «снять всё накопленное». У эффекта может быть своя цена
    /// очистки в стаках (<see cref="EffectData.CleanseStacks"/>): «Угли» отдают 10 стаков или четверть
    /// и живут дальше. Так что <c>_maxCount</c> считает ЭФФЕКТЫ, а не стаки.</para>
    /// <para><b>Когда срабатывает:</b> в момент наложения несущего эффекта, один раз.</para>
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
