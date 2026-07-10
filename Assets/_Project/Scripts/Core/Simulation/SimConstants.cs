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

        // --- Разделение тел (SeparationSystem, локальное избегание) ---

        /// <summary>
        /// Радиус тела юнита = <see cref="Data.Stats.StatType.Size"/> × это (мировые ед.).
        /// Size 1.0 → 0.575 (диаметр 1.15). Подгоняется под видимую ширину спрайта (dev live: gm_sep_radius).
        /// </summary>
        public const float BodyRadiusPerSize = 0.575f;

        /// <summary>
        /// Доля перекрытия, устраняемая за тик (мягкая усадка за пару тиков; 1 = жёстко за тик).
        /// Балансная ручка «интенсивности» коллизии.
        /// </summary>
        public const float SeparationStrength = 0.5f;

        /// <summary>Проходов разделения за тик (больше = жёстче и дороже). Обычно 1.</summary>
        public const int SeparationIterations = 1;

        /// <summary>
        /// Множитель расталкивания для пары ОДНОЙ команды (0..1). Свои расступаются мягче — задние
        /// просачиваются сквозь ряды к фронту; враги всегда на полную (боевая линия держится на стыке).
        /// </summary>
        public const float SeparationSameTeamScale = 0.35f;

        // --- Снаряды и поиск целей (единый источник значений; пакет 0 §4.2) ---
        // TODO Фаза 4 (пакет 2): переезжают в SimTuningConfig (снапшот на старте боя, вики «13» §3.4).

        /// <summary>
        /// «Глобальный» радиус поиска целей (мировые ед.): метка охотника, ближайший враг для
        /// монаха/активок — по сути «без ограничения дальности» на масштабе арены (вики «13» §4.2 п.3).
        /// </summary>
        public const float GlobalSearchRadius = 500f;

        /// <summary>
        /// Радиус коллизии снаряда/хил-снаряда = <see cref="Data.Stats.StatType.Size"/> × это (вики «13» §4.2 п.5).
        /// </summary>
        public const float ProjectileHitRadiusFactor = 0.25f;

        /// <summary>
        /// Отступ деспавна снаряда ЗА границами арены (мировые ед.): снаряд гаснет, выйдя за поле на
        /// этот запас, а не по несвязанному мировому нулю (вики «13» §4.2 п.4). Бесконечное поле → не гаснет.
        /// </summary>
        public const float ProjectileDespawnMargin = 5f;
    }
}
