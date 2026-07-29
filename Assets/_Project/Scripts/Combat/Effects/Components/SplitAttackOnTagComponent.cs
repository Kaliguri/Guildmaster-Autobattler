using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Расщепляет авто-атаку носителя по школам, когда на цели висит нужный тег (The Pyre: по уже
    /// горящей цели половина клинка бьёт Огнём). По умолчанию суммарный урон удара не меняется —
    /// меняется то, какой бронёй он гасится, что копит и чем усиливается.
    /// <para><b>Числа:</b> <c>_requiredTargetTag</c> — при каком теге на ЦЕЛИ расщепляем (Burn);
    /// <c>_share</c> — какая доля удара уходит в другую школу (0.5 = половина); <c>_school</c> и
    /// <c>_element</c> — во что именно (Магическая + Огонь). При ударе на 100 по горящей цели это
    /// 50 сталью и 50 огнём — вторая половина копит «Угли» и ими же усиливается.</para>
    /// <para><b>Процентная половина (Мечник, решение 2026-07-28):</b> <c>_pctTargetMaxHp</c> — доля
    /// макс. HP цели, которой бьёт отщеплённая часть ВМЕСТО доли удара (0.01 = 1%);
    /// <c>_pctPerStack</c> — прибавка к ней за каждый стак <c>_stackTag</c> на цели (0.0025 = +0.25%
    /// за уголь). Клинок при этом всё равно ополовинивается: выходит «половина стали + процент огнём»,
    /// и суммарный урон удара растёт вместе с накоплением. Ноль в <c>_pctTargetMaxHp</c> = прежнее
    /// поведение, доля от удара.</para>
    /// <para><b>Когда срабатывает:</b> в момент авто-атаки, до расчёта урона — удар уходит двумя
    /// половинами, каждая своим типом.</para>
    /// </summary>
    /// <remarks>
    /// Тот же шов пригодится рунам-зачарованиям (оружие «с огоньком») — потому условие и школа
    /// вынесены в данные, а не зашиты в кит.
    /// <para>Процент считается от МАКСИМАЛЬНОГО HP цели, а не от текущего: иначе удар слабеет по мере
    /// добивания, и антитанк выдыхается ровно там, где должен доводить дело до конца. Рост от стаков
    /// намеренно не имеет потолка — «Мечник обязан ломать игру в одну цель» (вердикт 2026-07-28);
    /// сдерживает его не потолок, а потеря накопленного при смене цели.</para>
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

        [Header("Процентная половина (0 = отщепляем долю удара, как раньше)")]
        [Tooltip("Доля МАКС. HP цели, которой бьёт отщеплённая часть вместо доли удара (0.01 = 1%).")]
        [SerializeField] private float _pctTargetMaxHp;

        [Tooltip("Прибавка к проценту за каждый стак StackTag на цели (0.0025 = +0.25% за уголь). Потолка нет.")]
        [SerializeField] private float _pctPerStack;

        [Tooltip("Тег эффекта, чьи стаки на ЦЕЛИ разгоняют процент (Мечник = Ember).")]
        [SerializeField] private EffectTag _stackTag = EffectTag.None;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public bool TrySplit(RuntimeUnit attacker, RuntimeUnit target, in EffectContext ctx, out AttackSplit split)
        {
            split = default;
            if (_share <= 0f || target == null) return false;
            if ((target.EffectTagMask & _requiredTargetTag) == EffectTag.None) return false;

            split = new AttackSplit(_share, _school, _element, OwnDamage(target));
            return true;
        }

        /// <summary>Своя величина огненной половины: <c>(база + за-стак × стаки) × макс. HP цели</c>. 0 = долевое расщепление.</summary>
        private float OwnDamage(RuntimeUnit target)
        {
            if (_pctTargetMaxHp <= 0f) return 0f;

            float pct = _pctTargetMaxHp + _pctPerStack * StacksOfTag(target, _stackTag);
            return pct * target.Stats.Get(StatType.MaxHP);
        }

        /// <summary>Сумма стаков всех эффектов с данным тегом на юните. <see cref="EffectTag.None"/> = ноль.</summary>
        private static int StacksOfTag(RuntimeUnit unit, EffectTag tag)
        {
            if (tag == EffectTag.None) return 0;

            int stacks = 0;
            for (int i = 0; i < unit.ActiveEffects.Count; i++)
            {
                RuntimeEffect e = unit.ActiveEffects[i];
                if (e.Def != null && (e.Def.Tags & tag) != 0) stacks += e.VisibleStacks;
            }
            return stacks;
        }
    }
}
