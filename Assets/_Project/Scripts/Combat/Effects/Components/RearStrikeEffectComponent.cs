using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Гниль» Ночного лезвия (карточка [[the-fang]]): пассивка носителя, которая за удар В ТЫЛ добавляет
    /// цели ещё один <see cref="_bonusEffect"/> — сверх того, что уже наложила авто-атака. Так удар в
    /// спину удваивает яд, а вместе с ним и шред брони, который яд несёт.
    /// <para><b>Числа:</b> <see cref="_bonusStacks"/> — сколько РАЗ накладывается бонусный эффект (Клык = 2).
    /// Силы у компонента нет намеренно: она вся в <see cref="_bonusEffect"/> и балансится там же, где
    /// обычный яд. Множитель урона на «удар в спину» сделал бы владельцев силы двумя.</para>
    /// <para><b>Когда срабатывает:</b> после прямого удара носителя (авто-атака или способность, по
    /// <see cref="_autoAttacksOnly"/>), если носитель стоял в тыловом конусе цели.</para>
    /// </summary>
    /// <remarks>
    /// Пост-факт реактив, а не pre-damage: он ничего не меняет в самом ударе, только добавляет эффект.
    /// Дееспособность не требуется — удар уже случился, а наложить яд с клинка оглушение не мешает.
    /// <para><b>Что такое «тыл»</b> — <see cref="CombatPositioning.IsRearAttack"/>: конвенция сторон, а не
    /// разворот юнита (своего «лица» в симуляции нет). Ограничение прокси описано там же.</para>
    /// </remarks>
    [Serializable]
    public sealed class RearStrikeEffectComponent : IReactiveComponent
    {
        [Tooltip("Эффект, добавляемый цели за удар в тыл (Ночное лезвие — второй стак яда).")]
        [SerializeField] private EffectData _bonusEffect;

        [Tooltip("Только авто-атаки. Выкл = любой прямой удар носителя, включая способности.")]
        [SerializeField] private bool _autoAttacksOnly = true;

        [Tooltip("Косинус тылового конуса: 1 = строго со спины, 0.5 = ±60°, 0 = вся задняя полуплоскость.")]
        [Range(0f, 1f)]
        [SerializeField] private float _rearConeCos = 0.5f;

        [Tooltip("Сколько раз наложить бонусный эффект за удар в тыл (Ночное лезвие = 2 стака яда).")]
        [Min(1)]
        [SerializeField] private int _bonusStacks = 1;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_bonusEffect == null) return;
            if (_autoAttacksOnly ? !e.IsAutoAttack : !e.IsDirectHit) return;

            RuntimeUnit carrier = ctx.Target;   // носитель пассивки = тот, кто ударил
            RuntimeUnit victim  = e.Target;
            if (carrier == null || victim == null || victim.IsDead) return;
            if (!CombatPositioning.IsRearAttack(carrier, victim, _rearConeCos)) return;

            // Повторное наложение, а не «стаки одним вызовом»: сколько стаков даёт одно применение —
            // свойство самого эффекта (StacksPerApplication), и дублировать его здесь значило бы завести
            // второго владельца. Компонент говорит только «сколько РАЗ», а не «насколько сильно».
            int times = _bonusStacks < 1 ? 1 : _bonusStacks;
            for (int i = 0; i < times; i++)
                ctx.Combat.ApplyEffect(victim, _bonusEffect, carrier);
        }
    }
}
