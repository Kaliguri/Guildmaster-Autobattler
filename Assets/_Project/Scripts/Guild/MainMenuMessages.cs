using System;

namespace Guildmaster.Guild
{
    /// <summary>Чем кончился показ главного меню.</summary>
    public enum MainMenuAction
    {
        /// <summary>Игрок собрал заказ и входит в игру — см. <see cref="MainMenuOutcome.Start"/>.</summary>
        StartGame,

        /// <summary>Выход из игры.</summary>
        Quit,

        /// <summary>
        /// Нас приняли в чужую игру: меню закрывается не кликом, а приглашением Steam, доехавшим до
        /// рукопожатия. Выбор игрока при этом состоялся раньше — в оверлее друзей.
        /// </summary>
        JoinCoop,
    }

    /// <summary>
    /// Исход главного меню: что игрок выбрал и с чем входит.
    /// </summary>
    /// <remarks>
    /// Прежде это был плоский перечень (Начать / Продолжить / Ристалище / Выход), где каждый новый
    /// режим требовал своей строки, а «Продолжить» было отдельным входом вместо выбора дома. С меню на
    /// две кнопки (модель Макса 02.08.2026) исходов ровно три, а всё прочее — содержимое заказа.
    /// </remarks>
    public readonly struct MainMenuOutcome
    {
        public readonly MainMenuAction Action;

        /// <summary>Заказ игрока. Осмыслен только при <see cref="MainMenuAction.StartGame"/>.</summary>
        public readonly GameStartRequest Start;

        private MainMenuOutcome(MainMenuAction action, GameStartRequest start)
        {
            Action = action;
            Start  = start;
        }

        public static MainMenuOutcome StartGame(GameStartRequest start) =>
            new(MainMenuAction.StartGame, start);

        public static MainMenuOutcome Quit => new(MainMenuAction.Quit, default);

        public static MainMenuOutcome JoinCoop => new(MainMenuAction.JoinCoop, default);
    }

    /// <summary>
    /// Запрос показать главное меню: «Создать игру» / «Присоединиться» / «Настройки» / «Выход».
    /// Публикует <c>GameFlow</c> на входе и между забегами, слушает UI. <see cref="OnChoice"/> — ровно
    /// один вызов; настройки и выбор режима открываются поверх меню и его НЕ закрывают.
    /// </summary>
    /// <remarks>
    /// Поля «есть ли автосейв» здесь больше нет: оно отвечало кнопке «Продолжить», а её не стало.
    /// Дома со своими забегами показывает экран выбора режима — он спрашивает профиль напрямую, потому
    /// что домов много, а вопрос «есть ли хоть один сейв» отвечал сразу на все и потому лгал.
    /// </remarks>
    public readonly struct OpenMainMenuRequest
    {
        /// <summary>Колбэк исхода (ровно один вызов).</summary>
        public readonly Action<MainMenuOutcome> OnChoice;

        /// <summary>Открыть настройки поверх меню (меню остаётся).</summary>
        public readonly Action OnSettings;

        public OpenMainMenuRequest(Action<MainMenuOutcome> onChoice, Action onSettings)
        {
            OnChoice   = onChoice;
            OnSettings = onSettings;
        }
    }
}
