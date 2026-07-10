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

        /// <summary>Носитель совершил убийство (доставляется УБИЙЦЕ = Source). «Скрытность» ассасина (§10.5).</summary>
        UnitKilled    = 1 << 5,

        /// <summary>
        /// Эффект истёк/снят с юнита (доставляется ИСТОЧНИКУ эффекта = Source). Единый шов «эффект закончился»:
        /// реактив фильтрует по тегам истёкшего эффекта (<see cref="CombatEventData.Tags"/>) и команде юнита,
        /// на котором он закончился (<see cref="CombatEventData.Target"/>). Так реагируют «Вихревой заход»
        /// монаха (конец отбрасывания врага) и приземление рывка (конец смещения самого монаха), §10.6.
        /// Смещение — это эффект (тег <see cref="Guildmaster.Data.Definitions.EffectTag.KnockUp"/>), не жёсткое
        /// состояние: только длительность не скейлится (Neutral), всё остальное — через систему эффектов.
        /// </summary>
        EffectExpired = 1 << 6,
    }

    /// <summary>Полезная нагрузка боевого события, диспатчится через внутреннюю FIFO-очередь (Stage 6).</summary>
    public readonly struct CombatEventData
    {
        public readonly CombatEvent Type;
        public readonly RuntimeUnit Source;
        public readonly RuntimeUnit Target;
        public readonly float       Amount;

        /// <summary>Теги, релевантные событию: для <see cref="CombatEvent.EffectExpired"/> — теги истёкшего эффекта. Иначе None.</summary>
        public readonly Data.Definitions.EffectTag Tags;

        public CombatEventData(CombatEvent type, RuntimeUnit source, RuntimeUnit target, float amount)
            : this(type, source, target, amount, Data.Definitions.EffectTag.None) { }

        public CombatEventData(CombatEvent type, RuntimeUnit source, RuntimeUnit target, float amount, Data.Definitions.EffectTag tags)
        {
            Type   = type;
            Source = source;
            Target = target;
            Amount = amount;
            Tags   = tags;
        }
    }
}
