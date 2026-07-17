namespace Guildmaster.UI
{
    /// <summary>
    /// Навигация оверлейных меню поверх игры (стек экранов). НЕ пауза — в хост-авторитативном коопе
    /// мир не останавливается. Открытие/закрытие глушит только локальный геймплейный ввод.
    /// </summary>
    public interface IMenuRouter
    {
        /// <summary>Открыт ли хоть один экран.</summary>
        bool IsOpen { get; }

        /// <summary>ESC: пусто → открыть системное меню; вложенный экран → назад; корневой → закрыть всё.</summary>
        void ToggleSystemMenu();

        /// <summary>Закрыть все экраны и снять глушение ввода.</summary>
        void CloseAll();

        /// <summary>
        /// Открыть экран награды после боя (A3). Гарантирует ровно один вызов
        /// <see cref="Guildmaster.Data.Definitions.OpenRewardRequest.OnResolved"/> — включая закрытие без
        /// выбора (= пропуск), чтобы флоу забега не завис в ожидании.
        /// </summary>
        void OpenReward(Guildmaster.Data.Definitions.OpenRewardRequest req);

        /// <summary>
        /// Открыть экран текстового ивента (StS-style). Гарантирует ровно один вызов
        /// <see cref="Guildmaster.Data.Definitions.OpenTextEventRequest.OnChosen"/> — закрытие без выбора
        /// шлёт индекс -1 (пропуск), чтобы флоу не завис.
        /// </summary>
        void OpenTextEvent(Guildmaster.Data.Definitions.OpenTextEventRequest req);

        /// <summary>
        /// Открыть экран карты акта для выбора следующего узла (A3). Гарантирует ровно один вызов
        /// <see cref="Guildmaster.Guild.OpenMapRequest.OnChosen"/> — закрытие без выбора шлёт null, чтобы
        /// петля акта не зависла (трактуется как прерывание).
        /// </summary>
        void OpenMap(Guildmaster.Guild.OpenMapRequest req);
    }
}
