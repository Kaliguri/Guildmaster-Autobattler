using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Каждый урон огнём копит „Угли“» (карточка [[burn]]): реактив носителя, кладущий стак
    /// <see cref="_emberEffect"/> на цель, по которой он только что попал огнём. Тики собственного
    /// «Поджога» тоже считаются — цель разогревается сама, пока горит, и это намеренно (2026-07-26/4).
    /// <para><b>Числа:</b> <c>_emberEffect</c> — какой эффект класть; сколько он даёт за стак, как
    /// сходит и сколько уносит очищение, живёт в НЁМ, не здесь. Здесь только правило «попал огнём →
    /// добавь уголёк», по одному стаку на попадание.</para>
    /// <para><b>Когда срабатывает:</b> на <see cref="CombatEvent.DamageDealt"/> носителя, если удар
    /// был огненным (магическая школа + стихия Огонь).</para>
    /// </summary>
    /// <remarks>
    /// Живёт на пассивке огневика, а не в пайплайне урона: правило «огонь копит Угли» описано в каноне
    /// как свойство стихии, но вешать наложение эффекта в ядро значило бы прошить контент в симуляцию.
    /// Второму огненному киту компонент просто выдаётся так же — а вот ПОЛЬЗУЮТСЯ уже накопленными
    /// «Углями» все, потому что множитель живёт на цели.
    /// </remarks>
    [Serializable]
    public sealed class EmberIgniterComponent : IReactiveComponent
    {
        [Tooltip("Эффект «Угли», накладываемый на цель (стакающийся, бессрочный, с EmberComponent внутри).")]
        [SerializeField] private EffectData _emberEffect;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (!e.IsFire || _emberEffect == null) return;

            RuntimeUnit victim = e.Target;
            if (victim == null || victim.IsDead) return;

            ctx.Combat.ApplyEffect(victim, _emberEffect, ctx.Target);
        }
    }
}
