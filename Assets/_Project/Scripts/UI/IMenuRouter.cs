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
    }
}
