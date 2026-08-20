using System;
using System.Threading;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Запрос показать текстовый ивент (публикует флоу забега, слушает UI). Несёт данные ивента и
    /// <see cref="OnChosen"/>-колбэк с индексом выбранного варианта — так Data-слой не зависит от UniTask
    /// (флоу оборачивает колбэк в await). Тем же приёмом, что <see cref="OpenRewardRequest"/>.
    /// </summary>
    public readonly struct OpenTextEventRequest
    {
        public readonly TextEventData Event;

        /// <summary>Колбэк выбора (ровно один вызов): индекс варианта в <see cref="TextEventData.Choices"/>.</summary>
        public readonly Action<int> OnChosen;

        /// <summary>Токен отмены забега (QA #37): отмена закрывает ивент через навигатор.</summary>
        public readonly CancellationToken Cancellation;

        /// <summary>
        /// Золото забега на момент открытия — по нему экран гасит варианты, которые игроку не по карману.
        /// </summary>
        /// <remarks>
        /// Приезжает числом в запросе, а не запрашивается экраном у забега: UI не знает про
        /// <c>RunState</c> и знать не должен, а ивент открыт ровно на один выбор — за это время золото
        /// измениться неоткуда. Настоящий гейт всё равно стоит в <c>EventEffectApplier</c>: здесь только
        /// то, что игрок видит.
        /// </remarks>
        public readonly int Gold;

        public OpenTextEventRequest(TextEventData ev, Action<int> onChosen, int gold = 0,
                                    CancellationToken cancellation = default)
        {
            Event        = ev;
            OnChosen     = onChosen;
            Gold         = gold;
            Cancellation = cancellation;
        }
    }
}
