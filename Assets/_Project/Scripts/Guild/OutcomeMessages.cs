using System;

namespace Guildmaster.Guild
{
    /// <summary>Куда игрок уходит с экрана исхода забега.</summary>
    public enum RunOutcomeChoice
    {
        /// <summary>В главное меню. Личный выбор: уйти к себе можно, не спрашивая напарника.</summary>
        ToMenu = 0,

        /// <summary>Во двор гильдии: забег окончен, дом остаётся. Общий выбор.</summary>
        ToGuild = 1,

        /// <summary>Новый забег тем же домом, мимо двора. Общий выбор.</summary>
        Restart = 2,
    }

    /// <summary>
    /// Запрос показать экран исхода (план [[act-map-run-loop]] §4 C2): победа (босс пройден) или
    /// поражение (пул перезапусков пуст). Экран объявляется шагом узла, показывают его обе роли.
    /// </summary>
    /// <remarks>
    /// <b>Экран один, а кнопок на нём разное число.</b> После забега это «заново», «во двор» и «в
    /// меню»; на площадке — «продолжить» и «в меню». Разницу задаёт вызывающий, передавая <c>null</c>
    /// вместо действия: кнопки тогда не будет вовсе, а не будет погашенной, обещающей возможность.
    /// <para><b>«В меню» — личная кнопка, остальные общие</b> (вердикт Макса 08.08.2026). Общие ходят
    /// одним решением с вариантами — см. <c>DecisionKeys.RunAfter</c>.</para>
    /// </remarks>
    public readonly struct OpenOutcomeRequest
    {
        /// <summary>true = победа, false = поражение.</summary>
        public readonly bool Victory;

        /// <summary>Колбэк «В меню» (ровно один вызов). Личный: голосовать не за что.</summary>
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

        /// <summary>Голос за «Начать забег заново»; <c>null</c> — кнопки нет.</summary>
        public readonly Action OnRestart;

        /// <summary>Голос за «Во двор гильдии»; <c>null</c> — кнопки нет.</summary>
        public readonly Action OnToGuild;

        public OpenOutcomeRequest(bool victory, Action onToMenu, Action onContinue = null,
                                  Action onRestart = null, Action onToGuild = null)
        {
            Victory    = victory;
            OnToMenu   = onToMenu;
            OnContinue = onContinue;
            OnRestart  = onRestart;
            OnToGuild  = onToGuild;
        }
    }
}
