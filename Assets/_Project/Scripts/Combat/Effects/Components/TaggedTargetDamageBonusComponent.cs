using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Больнее по тем, кого сам обработал» (§10.3): носитель наносит больше урона цели, несущей
    /// нужные теги. Криомант так добивает замороженных, и это его позиционная награда — сначала
    /// охладить строй, потом собирать урожай.
    /// <para><b>Числа:</b> <c>_rules</c> — список правил «какие теги нужны цели → сколько накинуть»
    /// (0.25 = +25%). Правила — АЛЬТЕРНАТИВЫ: берётся самое сильное подходящее, а не сумма, иначе
    /// «заморожен» и «заморожен и обездвижен» складывались бы в одно значение и отдельная строка
    /// теряла бы смысл. Условие требует ВСЕ теги правила разом. <c>_autoAttackOnly</c> — считать
    /// прибавку только для авто-атак (у Криоманта в карточке именно так).</para>
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

            [Tooltip("Прибавка к урону долей: 0.25 = +25%.")]
            public float Bonus;
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
                if (_rules[i].Bonus > best) best = _rules[i].Bonus;
            }
            return best;
        }
    }
}
