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
    /// <summary>Одна строка итогов забега: что считали и сколько вышло.</summary>
    /// <remarks>
    /// Метка едет КЛЮЧОМ, а значение — готовой строкой: перевести «Пройдено узлов» каждый обязан у
    /// себя, а число во всех языках одно. Гость показывает тот же экран, что хозяин.
    /// </remarks>
    public readonly struct OutcomeSummaryRow
    {
        public readonly string LabelKey;
        public readonly string LabelFallback;
        public readonly string Value;

        public OutcomeSummaryRow(string labelKey, string labelFallback, string value)
        {
            LabelKey      = labelKey;
            LabelFallback = labelFallback;
            Value         = value;
        }
    }

    public readonly struct OpenOutcomeRequest
    {
        /// <summary>true = победа, false = поражение.</summary>
        public readonly bool Victory;

        /// <summary>
        /// Чем кончился забег: строки итогов под знаком (вердикт Макса 22.08.2026, вариант III-Б).
        /// Пусто — экран покажет только знак и подпись, как раньше.
        /// </summary>
        /// <remarks>
        /// Итоги собирает тот, у кого есть состояние забега, а не экран: у гостя своего RunState нет
        /// вовсе, и посчитать он ничего не может — ему приезжают готовые строки.
        /// </remarks>
        public readonly System.Collections.Generic.IReadOnlyList<OutcomeSummaryRow> Summary;

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
                                  Action onRestart = null, Action onToGuild = null,
                                  System.Collections.Generic.IReadOnlyList<OutcomeSummaryRow> summary = null)
        {
            Victory    = victory;
            Summary    = summary;
            OnToMenu   = onToMenu;
            OnContinue = onContinue;
            OnRestart  = onRestart;
            OnToGuild  = onToGuild;
        }
    }
}
