using System;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Фаза текущего боя для верхней панели забега (план 12 Фаза 2). Определяет, что показывать в центре
    /// панели: <see cref="Deployment"/> — кнопку «Начать», <see cref="Fighting"/> — таймер боя,
    /// <see cref="None"/> — панель скрыта (боевой скоуп не загружен).
    /// </summary>
    public enum BattlePhase
    {
        None       = 0, // боевого скоупа нет (меню/карта) — панель скрыта
        Deployment = 1, // фаза расстановки (Free) — центр панели = «Начать»
        Fighting   = 2, // бой идёт — центр панели = таймер боя
    }

    /// <summary>
    /// Read-side абстракция боевого таймера/фазы для UI-слоя (план 12 Фаза 2). Живёт в Data, чтобы верхняя
    /// панель (Guildmaster.UI) читала фазу/время боя, не завися от Guildmaster.Game (там Game→UI, обратная
    /// ссылка создала бы цикл). Реализуется боевым мостом <c>IBattleSession</c> в RootScope; DI связывает.
    /// </summary>
    public interface IBattleClock
    {
        /// <summary>Текущая фаза боя (пусто/расстановка/бой). Панель опрашивает каждый кадр.</summary>
        BattlePhase Phase { get; }

        /// <summary>
        /// Фаза сменилась (расстановка ↔ бой ↔ нет боя). Навигатор подписывается и пересчитывает глушение/
        /// контекст ввода из новой фазы — вместо ручного <c>SetContext</c> в боевом слое (снос K8, план II.3).
        /// Поднимается только на РЕАЛЬНОЙ смене значения (тот же phase дважды = без события).
        /// </summary>
        event Action PhaseChanged;

        /// <summary>Прошедшее время текущего боя, сек (0, если боя нет).</summary>
        float ElapsedSeconds { get; }

        /// <summary>UI → бой: начать бой из фазы расстановки (кнопка «Начать»). No-op, если некому.</summary>
        void RequestStart();
    }
}
