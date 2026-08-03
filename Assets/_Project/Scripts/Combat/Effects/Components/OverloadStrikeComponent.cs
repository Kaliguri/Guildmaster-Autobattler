using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Перегрузка» Антимага (карточка [[the-aegis]]): мгновенный эффект, который бьёт цель уроном, равным
    /// ПОГЛОЩЁННОМУ щитами-по-школе урону носителя (<see cref="RuntimeUnit.AbsorbedByWard"/>) с масштабом
    /// <see cref="_scale"/>, и обнуляет счётчик.
    /// <para><b>Числа:</b> <c>_scale</c> — доля поглощённого, уходящая в удар (0.4); <c>_damageType</c> —
    /// какой школой бьёт ответ. Своего числа урона у способности нет намеренно: она целиком функция того,
    /// сколько магии в носителя вложил противник.</para>
    /// <para><b>Когда срабатывает:</b> в момент наложения (эффект мгновенный). Стоял без магического
    /// давления — «Перегрузка» бьёт слабо, и это честно.</para>
    /// </summary>
    /// <remarks>
    /// Считается ПОГЛОЩЁННЫЙ урон, а не размер поднятых щитов — в отличие от взрыва щита
    /// (<see cref="ShieldBurstComponent"/>), который платит за прочность. Разница смысловая: взрыв
    /// награждает за то, что щит пришлось пробивать, а «Перегрузка» — за то, что в носителя реально били
    /// магией. Щит, выданный впустую, ответ не усиливает.
    /// <para>Счётчик обнуляется кастом, а не по таймеру: у величины ответа один владелец — то, что
    /// накопилось с прошлой «Перегрузки».</para>
    /// </remarks>
    [Serializable]
    public sealed class OverloadStrikeComponent : IRuntimeEffectComponent
    {
        [Tooltip("Доля поглощённого урона, уходящая в удар (0.4 = 40%).")]
        [SerializeField] private float _scale = 0.4f;

        [Tooltip("Школа урона ответа.")]
        [SerializeField] private DamageType _damageType = DamageType.Undefined;

        public void OnApply(in EffectContext ctx)
        {
            RuntimeUnit victim = ctx.Target;
            RuntimeUnit source = ctx.Source;
            if (victim == null || victim.IsDead || source == null) return;

            float absorbed = source.AbsorbedByWard;
            source.AbsorbedByWard = 0f;   // тратится кастом целиком, даже если удар не дошёл до цели

            float damage = absorbed * _scale;
            if (damage <= 0f) return;

            ctx.Combat.DealDamage(new DamageRequest(
                source, victim, damage, _damageType, ctx.Combat.ArmorK,
                sourceKind: DamageSourceKind.Ability));
        }

        public void OnExpire(in EffectContext ctx) { }
    }
}
