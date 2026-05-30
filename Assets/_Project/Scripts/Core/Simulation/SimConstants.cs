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
    }
}
