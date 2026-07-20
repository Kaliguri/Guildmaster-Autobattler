using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat.Effects
{
    /// <summary>
    /// Экземпляр эффекта на конкретном юните (POCO, живёт один бой). Несёт per-unit рантайм-
    /// состояние, которого НЕ может быть в общих <c>[SerializeReference]</c>-компонентах
    /// <see cref="EffectData"/> — те шарятся между всеми носителями эффекта и обязаны быть
    /// stateless (вики «12» §2.2, «6» §5).
    /// </summary>
    public sealed class RuntimeEffect : IModifierSource
    {
        /// <summary>Иммутабельное определение.</summary>
        public EffectData Def;

        /// <summary>
        /// Имя, под которым эффект показывается игроку в разборе стата («+12 (Ярость)»).
        /// Эффект — основной источник стат-модификаторов в бою, поэтому именно он делает
        /// разбор читаемым; безымянные источники в тултипе схлопываются в «прочее».
        /// </summary>
        public string ModifierSourceLocKey => ContentKeys.NameKey(Def);

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

        /// <summary>
        /// Счётчик сим-тиков с прошлого срабатывания на периодический компонент (параллельно
        /// компонентам). Целочисленный — float-аккумулятор дрейфует и ломает детерминизм периодики.
        /// </summary>
        public int[] PeriodicTicks;

        /// <summary>
        /// Внутренний кулдаун реактив/pre-damage компонента (§9.3, «Оплот»): абсолютный тик, с которого
        /// компонент снова готов сработать. Сверяется с <c>ctx.Combat.CurrentTick</c> — без потиковых
        /// декрементов (детерминизм). 0 = готов с начала боя.
        /// </summary>
        public int ReactiveReadyTick;

        /// <summary>
        /// Фактически поднятая величина щита с runtime-расчётом (§9.3, «Оплот»: <c>flat + %·недостающее HP</c>) —
        /// снимок при наложении, чтобы <c>OnExpire</c> снял ровно её (потенцию из статов тут не выразить).
        /// </summary>
        public float PendingShield;

        /// <summary>
        /// Заряды реактив-компонента (§9.4, «Изворотливость»): на каждый заряд — абсолютный тик готовности
        /// (≤ CurrentTick = готов). Независимая перезарядка. null у эффектов без зарядов.
        /// </summary>
        public int[] ChargeReadyTicks;

        /// <summary>Постоянный эффект (пассивка) — не истекает по таймеру.</summary>
        public bool IsPermanent => RemainingTicks < 0;
    }
}
