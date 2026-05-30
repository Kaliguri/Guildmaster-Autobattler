using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat.Effects
{
    /// <summary>
    /// Рантайм-контракт поведения эффекта. Производный от Data-маркера <see cref="IEffectComponent"/>:
    /// сериализационный якорь живёт в Data, а хуки, оперирующие боевым состоянием (<see cref="EffectContext"/>),
    /// — здесь, в Combat (кросс-сборочный шов, вики «10» §2.2, «12» §3.1).
    /// </summary>
    /// <remarks>
    /// Компоненты <b>stateless</b>: экземпляр шарится между всеми носителями эффекта. Per-unit
    /// состояние — в <see cref="RuntimeEffect"/>/<see cref="EffectContext"/>. <c>OnApply</c> обязан
    /// быть идемпотентен при re-apply на стак/рефреш (EffectSystem зовёт OnExpire→OnApply).
    /// </remarks>
    public interface IRuntimeEffectComponent : IEffectComponent
    {
        void OnApply(in EffectContext ctx);
        void OnExpire(in EffectContext ctx);
    }

    /// <summary>Периодический компонент: <c>OnTick</c> каждые <see cref="Interval"/> секунд (DoT/HoT/реген).</summary>
    public interface IPeriodicComponent : IRuntimeEffectComponent
    {
        float Interval { get; }
        void OnTick(in EffectContext ctx);
    }

    /// <summary>Реактивный компонент: реагирует на боевые события (вампиризм/шипы). Диспатч — внутренняя FIFO-очередь.</summary>
    public interface IReactiveComponent : IRuntimeEffectComponent
    {
        CombatEvent Events { get; }
        void OnEvent(in EffectContext ctx, in CombatEventData e);
    }

    /// <summary>
    /// Опциональный шов: компонент объявляет масштабируемую потенцию. EffectSystem резолвит её
    /// из статов источника один раз при наложении и кладёт снимок в <see cref="RuntimeEffect.ScaledPotency"/>
    /// (per-second rate для DoT/HoT — НЕ запечённый total, вики «11» §5.1).
    /// </summary>
    public interface IScalablePotency
    {
        ScalableValue Potency { get; }
    }
}
