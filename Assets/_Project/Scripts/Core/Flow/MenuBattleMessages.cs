namespace Guildmaster.Core.Flow
{
    /// <summary>
    /// СОСТОЯНИЕ фонового боя за меню: идёт он или нет. Вещает владелец боя, слушает презентация —
    /// задник меню обязан уйти, когда за меню живая арена, и вернуться, когда её нет.
    /// </summary>
    /// <remarks>
    /// Заведено 04.08.2026 вместе с боем за главным меню. Отдельное событие, а не флаг в
    /// <see cref="ScreenBackdropChangedEvent"/>, по той же причине, по которой разведены сами задники:
    /// «нужен ли стол» решает UI по своему экрану, «есть ли за меню бой» — игра по своему состоянию.
    /// Слить их в один флаг значило бы завести факту двух владельцев, которые расходятся молча.
    /// </remarks>
    public readonly struct MenuBattleChangedEvent
    {
        /// <summary>true — за меню идёт бой, false — арены нет.</summary>
        public readonly bool Running;

        public MenuBattleChangedEvent(bool running) => Running = running;
    }
}
