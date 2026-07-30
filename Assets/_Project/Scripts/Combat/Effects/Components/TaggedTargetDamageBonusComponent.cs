using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Больнее по тем, кого сам обработал» (§10.3): носитель наносит больше урона цели, несущей
    /// нужные теги. Криомант так добивает замороженных, и это его позиционная награда — сначала
    /// охладить строй, потом собирать урожай.
    /// <para><b>Числа:</b> <c>_rules</c> — список правил «какие теги нужны цели → сколько накинуть».
    /// У правила три величины: <c>Bonus</c> — плоская прибавка (0.25 = +25%), <c>BonusPerStack</c> —
    /// прибавка за каждый стак требуемых тегов (Криомант: 0.05 = +5% за стак «Изморози»),
    /// <c>MaxBonus</c> — потолок правила (1 = +100%). Правила — АЛЬТЕРНАТИВЫ: берётся самое сильное
    /// подходящее, а не сумма, иначе «заморожен» и «заморожен и обездвижен» складывались бы в одно
    /// значение и отдельная строка теряла бы смысл. Условие требует ВСЕ теги правила разом.
    /// <c>_autoAttackOnly</c> — считать прибавку только для авто-атак.</para>
    /// <para><b>Потолок принадлежит правилу, а не компоненту</b>: у разных правил разная цена входа
    /// («по обмороженному» и «по обмороженному и обездвиженному» — разные условия), поэтому общий кап
    /// делал бы слабое правило потолком для сильного.</para>
    /// <para><b>Когда срабатывает:</b> в момент удара, до расщепления по школам — то есть прибавка
    /// достаётся обеим половинам расщеплённого удара, как свойство удара, а не школы.</para>
    /// </summary>
    [Serializable]
    public sealed class TaggedTargetDamageBonusComponent : IOutgoingDamageBonusComponent
    {
        /// <summary>Одно правило: какие теги обязана нести цель и сколько за это накидывается.</summary>
        [Serializable]
        public struct Rule
        {
            [Tooltip("Цель обязана нести ВСЕ эти теги. Несколько тегов = условие «и то, и то».")]
            public EffectTag RequiredTags;

            [Tooltip("Прибавка к урону долей: 0.25 = +25%. Плоская часть, не зависит от числа стаков.")]
            public float Bonus;

            [Tooltip("Прибавка за КАЖДЫЙ стак требуемых тегов на цели (0.05 = +5% за стак). 0 = только плоская.")]
            public float BonusPerStack;

            [Tooltip("Потолок суммарной прибавки правила (1 = +100%). 0 = без потолка.")]
            public float MaxBonus;
        }

        [Tooltip("Правила-альтернативы: срабатывает самое сильное подходящее, не сумма.")]
        [SerializeField] private Rule[] _rules;

        [Tooltip("Считать прибавку только для авто-атак (иначе — для любого урона носителя).")]
        [SerializeField] private bool _autoAttackOnly = true;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public float BonusAgainst(RuntimeUnit attacker, RuntimeUnit target, bool isAutoAttack, in EffectContext ctx)
        {
            if (_rules == null || _rules.Length == 0) return 0f;
            if (_autoAttackOnly && !isAutoAttack) return 0f;

            EffectTag onTarget = target.EffectTagMask;
            float best = 0f;
            for (int i = 0; i < _rules.Length; i++)
            {
                EffectTag required = _rules[i].RequiredTags;
                if (required == EffectTag.None) continue;
                if ((onTarget & required) != required) continue; // нужны ВСЕ теги правила

                float bonus = _rules[i].Bonus;
                if (_rules[i].BonusPerStack != 0f)
                    bonus += _rules[i].BonusPerStack * StacksOfTags(target, required);
                if (_rules[i].MaxBonus > 0f && bonus > _rules[i].MaxBonus)
                    bonus = _rules[i].MaxBonus;

                if (bonus > best) best = bonus;
            }
            return best;
        }

        /// <summary>
        /// Сумма стаков эффектов цели, несущих ХОТЯ БЫ ОДИН из требуемых тегов.
        /// </summary>
        /// <remarks>
        /// Условие входа в правило требует ВСЕ теги разом, а счёт идёт по любому из них — и это не
        /// рассогласование, а разные вопросы: «правило вообще применимо?» и «насколько глубоко цель
        /// обработана?». Криоманту нужен счёт стаков холода, а не пересечение тегов «холод и обездвижен»:
        /// пересечение дало бы ноль стаков ровно там, где цель проморожена сильнее всего.
        /// </remarks>
        private static int StacksOfTags(RuntimeUnit unit, EffectTag tags)
        {
            int stacks = 0;
            for (int i = 0; i < unit.ActiveEffects.Count; i++)
            {
                RuntimeEffect e = unit.ActiveEffects[i];
                if (e.Def != null && (e.Def.Tags & tags) != 0) stacks += e.VisibleStacks;
            }
            return stacks;
        }
    }
}
