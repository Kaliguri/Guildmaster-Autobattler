using System;
using System.Threading;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать кнопки «бита» между узлами (план [[act-map-run-loop]] §4 A4). Публикует петля акта
    /// (<c>ContinuePresenter</c>), слушает UI (<c>MenuRouter</c> рисует их в правом нижнем углу).
    /// <para>Два применения одной формы:</para>
    /// <list type="bullet">
    /// <item><b>Гейт</b> — одна кнопка, петля ЖДЁТ нажатия (мост «бой добит → пойдём к награде»).</item>
    /// <item><b>Передышка</b> — две кнопки, петля НЕ ждёт: узел уже засчитан, игрок стоит в живом мире и сам
    /// решает, идти ли на карту («Продолжить») или править строй («К построению»). Кнопки — удобный шорткат,
    /// те же места достижимы табами; экран снимается по <see cref="Cancellation"/>, когда узел выбран.</item>
    /// </list>
    /// </summary>
    public readonly struct OpenContinueRequest
    {
        /// <summary>Лок-ключ подписи главной кнопки (пусто → дефолт «Продолжить»).</summary>
        public readonly string LabelKey;

        /// <summary>Колбэк нажатия главной кнопки (ровно один вызов).</summary>
        public readonly Action OnContinue;

        /// <summary>Колбэк второй кнопки («К построению»). null = кнопки нет (режим гейта).</summary>
        public readonly Action OnFormation;

        /// <summary>Лок-ключ подписи второй кнопки (пусто → дефолт «К построению»).</summary>
        public readonly string FormationLabelKey;

        /// <summary>Токен отмены (QA #37): отмена забега или выбор узла снимают кнопки через навигатор.</summary>
        public readonly CancellationToken Cancellation;

        public OpenContinueRequest(string labelKey, Action onContinue, CancellationToken cancellation = default,
                                   Action onFormation = null, string formationLabelKey = null)
        {
            LabelKey          = labelKey;
            OnContinue        = onContinue;
            OnFormation       = onFormation;
            FormationLabelKey = formationLabelKey;
            Cancellation      = cancellation;
        }
    }
}
