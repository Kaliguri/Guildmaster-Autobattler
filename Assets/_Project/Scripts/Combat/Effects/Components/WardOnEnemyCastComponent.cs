using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Отражающий налёт» Антимага (карточка [[the-aegis]]): пассивка, выдающая носителю
    /// <see cref="_ward"/> каждый раз, когда ВРАГ применяет активную способность. Щиты копятся, поэтому
    /// против магического состава кит крепнет, а против чистой физики остаётся обычным танком.
    /// <para><b>Числа:</b> своих нет — величина и срок щита живут в <see cref="_ward"/>. Регулятор силы
    /// кита там же, и это важно: у накопления должен быть один тормоз (длительность щита), иначе оно
    /// растёт снежком.</para>
    /// <para><b>Когда срабатывает:</b> на <see cref="CombatEvent.AbilityCast"/> — то есть на КАСТ, а не на
    /// прилетевший урон. Носитель получает защиту раньше, чем в него попадут: игрок видит причину прежде
    /// следствия.</para>
    /// </summary>
    /// <remarks>
    /// Событие каста широковещательное и приходит уже отфильтрованным по команде (рассылку делает
    /// <c>CombatSimulation.DrainEventQueue</c>), поэтому компонент не обходит список юнитов сам — иначе
    /// порядок обхода стал бы частью исхода боя.
    /// <para>Дееспособность не требуется: налёт оседает на доспехе сам, оглушённый Антимаг продолжает его
    /// собирать. Это осознанно — иначе контроль отнимал бы у кита и защиту, и ответ разом.</para>
    /// </remarks>
    [Serializable]
    public sealed class WardOnEnemyCastComponent : IReactiveComponent
    {
        [Tooltip("Щит, выдаваемый носителю за каждое вражеское заклинание (обычно с фильтром по школе).")]
        [SerializeField] private EffectData _ward;

        public CombatEvent Events => CombatEvent.AbilityCast;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_ward == null) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            ctx.Combat.ApplyEffect(self, _ward, self);
        }
    }
}
