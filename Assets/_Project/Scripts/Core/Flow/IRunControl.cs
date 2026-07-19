namespace Guildmaster.Core.Flow
{
    /// <summary>
    /// Управление жизненным циклом забега из UI (QA #18): системное меню (pause) может прервать текущий
    /// забег и вернуться в главное меню либо выйти из игры. Живёт в Core, чтобы UI-слой (MenuRouter) не
    /// зависел от Game-слоя; реализация — <c>GameFlow</c> (держит токен отмены верхнего цикла).
    /// </summary>
    public interface IRunControl
    {
        /// <summary>Прервать текущий забег и вернуться в главное меню (сейв остаётся — можно продолжить).</summary>
        void RequestReturnToMainMenu();

        /// <summary>Выйти из игры (в редакторе — остановить play).</summary>
        void RequestQuit();
    }
}
