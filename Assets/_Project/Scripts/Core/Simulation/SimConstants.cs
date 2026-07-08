namespace Guildmaster.Core.Simulation
{
    /// <summary>
    /// Константы тиковой симуляции. Единый источник правды для частоты сим/AI
    /// (см. вики «10. Архитектура реализации — Фаза 1» §5.1).
    /// </summary>
    public static class SimConstants
    {
        /// <summary>Частота шагов симуляции, Гц.</summary>
        public const int TickRate = 30;

        /// <summary>Фиксированный шаг симуляции, сек. Единственный «dt» детерминированного шага.</summary>
        public const float TickDelta = 1f / TickRate;

        /// <summary>Частота пересчёта AI, Гц (реже сим ради перфа; полный AI — Фаза 3).</summary>
        public const int AiTickRate = 10;

        /// <summary>Сколько сим-тиков приходится на один AI-тик (<c>30 / 10 = 3</c>).</summary>
        public const int AiTickInterval = TickRate / AiTickRate;

        /// <summary>
        /// Анти-лавина: максимум догоняющих тиков за один кадр рендера. Если кадр подвис
        /// (GC, загрузка ассета, alt-tab), аккумулятор копит время — без потолка внутренний
        /// while прогнал бы десятки тиков, усилив подвис («спираль смерти»). Излишек долга
        /// сверх этого капа отбрасывается (см. <c>CombatLoopService</c>).
        /// </summary>
        public const int MaxCatchUpTicksPerFrame = 5;

        /// <summary>
        /// Глобальный потолок длительности свинга авто-атаки, в сим-тиках (≈1с при 30 Гц).
        /// Свинг всегда играет за фикс. короткое время; остаток интервала юнит ждёт в idle.
        /// Балансная ручка «хлёсткости» (вики «14»). Замах ужимается до интервала при быстрой атаке.
        /// </summary>
        public const int MaxAttackAnimTicks = 30;

        /// <summary>
        /// Минимальный пол замаха авто-атаки, в сим-тиках. Гарантирует читаемый телеграф
        /// у ударов с маленьким кадром контакта (windup — геймплейная механика, не косметика; вики «14»).
        /// </summary>
        public const int MinWindupTicks = 3;
    }
}
