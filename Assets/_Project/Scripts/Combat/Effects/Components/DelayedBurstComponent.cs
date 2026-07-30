using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Отложенный взрыв: пока эффект висит — ничего, по ИСТЕЧЕНИИ он бьёт носителя. «Ядовитая печать»
    /// Ночного лезвия (карточка [[the-fang]]): через три секунды печать детонирует двумя школами сразу.
    /// <para><b>Числа:</b> <c>_physicalShare</c> и <c>_magicalShare</c> — доли от <c>AutoAttackDamage</c>
    /// ИСТОЧНИКА (0.5 = 50% базы), каждая летит своей школой и гасится своей бронёй; ноль в доле = эта
    /// школа не участвует. Длительность до взрыва — не здесь, а у самого эффекта: это его срок жизни.</para>
    /// <para><b>Когда срабатывает:</b> ровно один раз, в момент истечения. Цель умерла раньше — печать
    /// потрачена зря, и это осмысленный выбор AI: вешать на живучего или на опасного.</para>
    /// </summary>
    /// <remarks>
    /// <b>Почему две доли, а не одно число с выбором школы:</b> кит про то, что открывает цель обеим
    /// половинам команды, и взрыв обязан читаться так же — половина через физброню, половина через
    /// магическую. Одна школа с «наполовину истинным» уроном давала бы то же число, но врала бы про
    /// природу удара, а по школе цель ещё и резисты считает.
    /// <para><b>Готча:</b> система эффектов зовёт <c>OnExpire</c> и на истечении, и на снятии — причину
    /// она не различает. Поэтому носителя-печать в ассете держим НЕСНИМАЕМЫМ: иначе диспел работал бы
    /// детонатором, то есть очистка союзника подрывала бы бомбу досрочно и в плюс её хозяину. Различение
    /// «истекло / сняли» — долг системы эффектов, и когда он появится, печать можно снова делать
    /// снимаемой.</para>
    /// </remarks>
    [Serializable]
    public sealed class DelayedBurstComponent : IRuntimeEffectComponent
    {
        [Tooltip("Доля AutoAttackDamage источника, летящая ФИЗИЧЕСКОЙ школой. 0 = без физической части.")]
        [SerializeField] private float _physicalShare = 0.5f;

        [Tooltip("Доля AutoAttackDamage источника, летящая МАГИЧЕСКОЙ школой. 0 = без магической части.")]
        [SerializeField] private float _magicalShare = 0.5f;

        [Tooltip("Магический элемент магической части.")]
        [SerializeField] private MagicElement _magicElement = MagicElement.None;

        [Tooltip("Сродство обеих частей (Яд у печати: она гниёт, а не просто бьёт).")]
        [SerializeField] private DamageAffinity _affinity = DamageAffinity.Poison;

        [Tooltip("Умножать урон на число стаков. Выкл = взрыв один и тот же, сколько бы печатей ни повесили.")]
        [SerializeField] private bool _scalesWithStacks;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx)
        {
            RuntimeUnit victim = ctx.Target;
            RuntimeUnit source = ctx.Source;
            if (victim == null || victim.IsDead || source == null) return;

            float basis = source.Stats.Get(StatType.AutoAttackDamage);
            if (_scalesWithStacks) basis *= ctx.Stacks;
            if (basis <= 0f) return;

            // Ability, а не Periodic: детонация — прямой удар, она обязана будить шипы и щиты цели.
            if (_physicalShare > 0f)
                ctx.Combat.DealDamage(new DamageRequest(
                    source, victim, basis * _physicalShare, DamageSchool.Physical, ctx.Combat.ArmorK,
                    sourceKind: DamageSourceKind.Ability, affinity: _affinity));

            if (_magicalShare > 0f)
                ctx.Combat.DealDamage(new DamageRequest(
                    source, victim, basis * _magicalShare, DamageSchool.Magical, ctx.Combat.ArmorK,
                    sourceKind: DamageSourceKind.Ability, affinity: _affinity, element: _magicElement));
        }
    }
}
