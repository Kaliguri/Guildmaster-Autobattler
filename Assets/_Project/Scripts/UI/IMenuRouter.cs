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

        /// <summary>
        /// Показать единую кнопку «Продолжить» (A4) в правом нижнем углу. Гарантирует ровно один вызов
        /// <see cref="Guildmaster.Guild.OpenContinueRequest.OnContinue"/> — включая закрытие без нажатия,
        /// чтобы петля акта не зависла.
        /// </summary>
        void ShowContinue(Guildmaster.Guild.OpenContinueRequest req);

        /// <summary>
        /// Открыть экран магазина (B2). Гарантирует ровно один вызов
        /// <see cref="Guildmaster.Guild.OpenShopRequest.OnLeave"/> — включая закрытие без нажатия «Уйти».
        /// </summary>
        void OpenShop(Guildmaster.Guild.OpenShopRequest req);

        /// <summary>
        /// Открыть экран сундука (B3). Гарантирует ровно один вызов
        /// <see cref="Guildmaster.Guild.OpenChestRequest.OnOpen"/> — включая закрытие без клика по крышке.
        /// </summary>
        void OpenChest(Guildmaster.Guild.OpenChestRequest req);

        /// <summary>
        /// Показать экран исхода забега (C2). Гарантирует ровно один вызов
        /// <see cref="Guildmaster.Guild.OpenOutcomeRequest.OnToMenu"/> — включая закрытие без нажатия.
        /// </summary>
        void ShowOutcome(Guildmaster.Guild.OpenOutcomeRequest req);

        /// <summary>
        /// Показать главное меню (D1). Гарантирует ровно один вызов
        /// <see cref="Guildmaster.Guild.OpenMainMenuRequest.OnChoice"/> (Начать/Продолжить/Выход);
        /// «Настройки» открываются поверх меню и его не закрывают.
        /// </summary>
        void OpenMainMenu(Guildmaster.Guild.OpenMainMenuRequest req);
    }
}
