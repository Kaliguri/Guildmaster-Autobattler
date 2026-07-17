using System;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать экран исхода забега (план [[act-map-run-loop]] §4 C2): победа (босс пройден) или поражение
    /// (пул перезапусков пуст). Публикует <c>GameFlow</c> после акта, слушает UI. Ровно один вызов <see cref="OnToMenu"/>.
    /// </summary>
    public readonly struct OpenOutcomeRequest
    {
        /// <summary>true = победа, false = поражение.</summary>
        public readonly bool Victory;

        /// <summary>Колбэк «В меню» (ровно один вызов).</summary>
        public readonly Action OnToMenu;

        public OpenOutcomeRequest(bool victory, Action onToMenu)
        {
            Victory  = victory;
            OnToMenu = onToMenu;
        }
    }
}
