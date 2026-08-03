using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Отложенный взрыв: пока эффект висит — ничего, по ИСТЕЧЕНИИ он бьёт носителя. «Ядовитая печать»
    /// Ночного лезвия (карточка [[the-fang]]): через три секунды печать детонирует двумя школами сразу.
    /// <para><b>Числа:</b> <c>_firstShare</c> и <c>_secondShare</c> — доли от <c>AutoAttackDamage</c>
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
        [Tooltip("Доля AutoAttackDamage источника, летящая ПЕРВЫМ типом. 0 = без первой части.")]
        [SerializeField] private float _firstShare = 0.5f;

        [Tooltip("Тип урона первой половины взрыва (у Ядовитой печати — Яд физический).")]
        [SerializeField] private DamageType _firstType = DamageType.Undefined;

        [Tooltip("Доля AutoAttackDamage источника, летящая ВТОРЫМ типом. 0 = взрыв одночастный.")]
        [SerializeField] private float _secondShare = 0.5f;

        [Tooltip("Тип урона второй половины (у печати — Яд магический). Undefined = второй части нет.")]
        [SerializeField] private DamageType _secondType = DamageType.Undefined;

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
            // Две половины — ДВА удара своих типов, а не одна цифра с половинчатой школой: только так
            // каждая режется своей бронёй и будит своих потребителей.
            if (_firstShare > 0f)
                ctx.Combat.DealDamage(new DamageRequest(
                    source, victim, basis * _firstShare, _firstType, ctx.Combat.ArmorK,
                    sourceKind: DamageSourceKind.Ability));

            if (_secondShare > 0f && _secondType != DamageType.Undefined)
                ctx.Combat.DealDamage(new DamageRequest(
                    source, victim, basis * _secondShare, _secondType, ctx.Combat.ArmorK,
                    sourceKind: DamageSourceKind.Ability));
        }
    }
}
