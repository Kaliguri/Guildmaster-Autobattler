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

        /// <summary>
        /// Колбэк «Продолжить»; <c>null</c> — продолжать нечем, и кнопки не будет.
        /// </summary>
        /// <remarks>
        /// После забега продолжать действительно нечего: акт кончился. На площадке — наоборот: состав и
        /// расстановка целы, и повторить бой тем же строем это норма, а не исключение. Разница живёт
        /// здесь, а не в двух экранах: экран один, и различает их наличие действия.
        /// </remarks>
        public readonly Action OnContinue;

        public OpenOutcomeRequest(bool victory, Action onToMenu, Action onContinue = null)
        {
            Victory    = victory;
            OnToMenu   = onToMenu;
            OnContinue = onContinue;
        }
    }
}
