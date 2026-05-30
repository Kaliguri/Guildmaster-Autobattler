using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat.Effects
{
    /// <summary>
    /// Экземпляр эффекта на конкретном юните (POCO, живёт один бой). Несёт per-unit рантайм-
    /// состояние, которого НЕ может быть в общих <c>[SerializeReference]</c>-компонентах
    /// <see cref="EffectData"/> — те шарятся между всеми носителями эффекта и обязаны быть
    /// stateless (вики «12» §2.2, «6» §5).
    /// </summary>
    public sealed class RuntimeEffect
    {
        /// <summary>Иммутабельное определение.</summary>
        public EffectData Def;

        /// <summary>Кто наложил — атрибуция урона/исцеления, скейл потенции, триггеры.</summary>
        public RuntimeUnit Source;

        /// <summary>Остаток длительности в тиках. <c>-1</c> = постоянный (пассивка), <c>0</c> = мгновенный.</summary>
        public int RemainingTicks;

        /// <summary>Полная длительность в тиках на момент наложения (для StackRule.Refresh).</summary>
        public int FullDurationTicks;

        /// <summary>Текущее число стаков (≥ 1 у активного эффекта).</summary>
        public int Stacks;

        /// <summary>
        /// Снимок потенции на компонент (параллельно <see cref="EffectData.Components"/>),
        /// резолвится из статов источника при наложении: per-second rate для DoT/HoT, величина
        /// щита и т.п. Храним rate-per-second, НЕ запечённый total (вики «11» §5.1).
        /// </summary>
        public float[] ScaledPotency;

        /// <summary>Аккумулятор времени (сек) на периодический компонент — параллельно компонентам.</summary>
        public float[] TickTimer;

        /// <summary>Постоянный эффект (пассивка) — не истекает по таймеру.</summary>
        public bool IsPermanent => RemainingTicks < 0;
    }
}
