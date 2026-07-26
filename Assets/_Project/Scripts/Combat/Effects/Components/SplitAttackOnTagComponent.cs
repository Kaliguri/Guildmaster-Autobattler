using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Расщепляет авто-атаку носителя по школам, когда на цели висит нужный тег (The Pyre: по уже
    /// горящей цели половина клинка бьёт Огнём). Суммарный урон удара не меняется — меняется то,
    /// какой бронёй он гасится, что копит и чем усиливается.
    /// </summary>
    /// <remarks>
    /// Тот же шов пригодится рунам-зачарованиям (оружие «с огоньком») — потому условие и школа
    /// вынесены в данные, а не зашиты в кит.
    /// </remarks>
    [Serializable]
    public sealed class SplitAttackOnTagComponent : IAttackSplitComponent
    {
        [Tooltip("Тег, который должен быть на ЦЕЛИ, чтобы расщепление сработало (Мечник = Burn).")]
        [SerializeField] private EffectTag _requiredTargetTag = EffectTag.Burn;

        [Tooltip("Доля урона, уходящая другой школой (0.5 = половина).")]
        [Range(0f, 1f)]
        [SerializeField] private float _share = 0.5f;

        [Tooltip("Школа, которой уходит отщеплённая доля.")]
        [SerializeField] private DamageSchool _school = DamageSchool.Magical;

        [Tooltip("Стихия отщеплённой доли (при магической школе). Мечник = Огонь.")]
        [SerializeField] private MagicElement _element = MagicElement.Fire;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public bool TrySplit(RuntimeUnit attacker, RuntimeUnit target, in EffectContext ctx, out AttackSplit split)
        {
            split = default;
            if (_share <= 0f || target == null) return false;
            if ((target.EffectTagMask & _requiredTargetTag) == EffectTag.None) return false;

            split = new AttackSplit(_share, _school, _element);
            return true;
        }
    }
}
