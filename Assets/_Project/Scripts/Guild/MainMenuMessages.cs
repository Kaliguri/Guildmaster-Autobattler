using System;

namespace Guildmaster.Guild
{
    /// <summary>Выбор в главном меню (план [[act-map-run-loop]] §4 D1).</summary>
    public enum MainMenuChoice
    {
        StartRun,       // начать новый забег
        Continue,       // продолжить из автосейва
        ProvingGrounds, // уйти на Ристалище — площадку вне забега (ГДД «Modes - Proving Grounds»)
        Quit,           // выход из игры
    }

    /// <summary>
    /// Запрос показать главное меню (D1): Начать / Продолжить / Настройки / Выход. Публикует <c>GameFlow</c> на
    /// входе и между забегами, слушает UI. <see cref="OnChoice"/> — ровно один вызов (Начать/Продолжить/Выход);
    /// «Настройки» открываются поверх меню через <see cref="OnSettings"/> и меню НЕ закрывают.
    /// </summary>
    public readonly struct OpenMainMenuRequest
    {
        /// <summary>Есть ли автосейв (кнопка «Продолжить» активна только тогда).</summary>
        public readonly bool HasSave;

        /// <summary>Колбэк выбора Начать/Продолжить/Выход (ровно один вызов).</summary>
        public readonly Action<MainMenuChoice> OnChoice;

        /// <summary>Открыть настройки поверх меню (меню остаётся).</summary>
        public readonly Action OnSettings;

        public OpenMainMenuRequest(bool hasSave, Action<MainMenuChoice> onChoice, Action onSettings)
        {
            HasSave    = hasSave;
            OnChoice   = onChoice;
            OnSettings = onSettings;
        }
    }
}
