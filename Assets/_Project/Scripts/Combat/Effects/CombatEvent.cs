using System;

namespace Guildmaster.Combat.Effects
{
    /// <summary>
    /// Внутренние боевые события для реактивных компонентов эффектов (вампиризм/шипы и т.п.).
    /// <c>[Flags]</c> — компонент подписывается на несколько типов через <see cref="IReactiveComponent.Events"/>.
    /// Это НЕ outward-события презентации (те идут C#-event'ами/MessagePipe) и НЕ команды сим
    /// (вики «6» §7, «12» §3.4).
    /// </summary>
    [Flags]
    public enum CombatEvent
    {
        None          = 0,
        DamageDealt   = 1 << 0,
        DamageTaken   = 1 << 1,
        Healed        = 1 << 2,
        UnitDied      = 1 << 3,
        EffectApplied = 1 << 4,
    }

    /// <summary>Полезная нагрузка боевого события, диспатчится через внутреннюю FIFO-очередь (Stage 6).</summary>
    public readonly struct CombatEventData
    {
        public readonly CombatEvent Type;
        public readonly RuntimeUnit Source;
        public readonly RuntimeUnit Target;
        public readonly float       Amount;

        public CombatEventData(CombatEvent type, RuntimeUnit source, RuntimeUnit target, float amount)
        {
            Type   = type;
            Source = source;
            Target = target;
            Amount = amount;
        }
    }
}
