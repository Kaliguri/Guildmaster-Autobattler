using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Урон носителя растёт от того, сколько стаков <see cref="_marker"/> он накопил НА СЕБЕ.
    /// «Живой уголь» Хранителя углей (карточка [[the-hearth]]): чем сильнее он разогрет, тем больнее
    /// бьёт — это его вторая валюта вместо классового коридора.
    /// <para><b>Числа:</b> <c>_bonusPerStack</c> — прибавка за стак долей (0.02 = +2%);
    /// <c>_maxBonus</c> — потолок суммарной прибавки (0 = без потолка); <c>_marker</c> — эффект, стаки
    /// которого считаются.</para>
    /// <para><b>Когда срабатывает:</b> на каждом ударе носителя — прибавка складывается с другими
    /// усилениями источника, как статы.</para>
    /// </summary>
    /// <remarks>
    /// Считает стаки на СЕБЕ, в отличие от <see cref="TaggedTargetDamageBonusComponent"/>, который
    /// смотрит на цель: разогрев — свойство хозяина огня, и от того, кого он бьёт, не зависит.
    /// <para>Читаются <c>VisibleStacks</c>, то есть снимок на начало тика: стак, набранный этим же тиком,
    /// в исход того же тика не попадает — закон видимости (см. <see cref="RuntimeEffect.VisibleStacks"/>).
    /// Иначе один и тот же удар и грел бы, и получал прибавку от своего же нагрева.</para>
    /// </remarks>
    [Serializable]
    public sealed class SelfStackDamageBonusComponent : IOutgoingDamageBonusComponent
    {
        [Tooltip("Эффект-маркер, стаки которого на САМОМ носителе считаются («Тёплый пепел» Хранителя).")]
        [SerializeField] private EffectData _marker;

        [Tooltip("Прибавка к урону за один стак, долей (0.02 = +2%).")]
        [SerializeField] private float _bonusPerStack = 0.02f;

        [Tooltip("Потолок суммарной прибавки, долей (0.5 = +50%). 0 = без потолка.")]
        [SerializeField] private float _maxBonus;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public float BonusAgainst(RuntimeUnit attacker, RuntimeUnit target, bool isAutoAttack, in EffectContext ctx)
        {
            if (_marker == null || attacker == null || _bonusPerStack <= 0f) return 0f;

            int stacks = CountStacks(attacker);
            if (stacks <= 0) return 0f;

            float bonus = stacks * _bonusPerStack;
            return _maxBonus > 0f && bonus > _maxBonus ? _maxBonus : bonus;
        }

        /// <summary>Стаки маркера на носителе; 0 — если его нет. Обход по индексу — детерминизм и без аллокаций.</summary>
        private int CountStacks(RuntimeUnit unit)
        {
            List<RuntimeEffect> effects = unit.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
                if (ReferenceEquals(effects[i].Def, _marker)) return effects[i].VisibleStacks;

            return 0;
        }
    }
}
