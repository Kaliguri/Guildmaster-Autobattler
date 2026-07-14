using System;

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

        public OpenTextEventRequest(TextEventData ev, Action<int> onChosen)
        {
            Event    = ev;
            OnChosen = onChosen;
        }
    }
}
