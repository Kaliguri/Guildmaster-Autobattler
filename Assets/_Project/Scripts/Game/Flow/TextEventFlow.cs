using Cysharp.Threading.Tasks;
using Guildmaster.Data.Definitions;
using MessagePipe;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Узел забега «текстовый ивент» (план 11 §3.4, §5.1) в стиле Slay the Spire: показывает
    /// <see cref="TextEventData"/> (заголовок/тело/картинка/варианты), ждёт выбор игрока и применяет
    /// последствия выбранного варианта к <see cref="RunState"/> через <see cref="EventEffectApplier"/>.
    /// Полиморфен через <see cref="IEventFlow"/> — новый ивент = новый ассет, центральный switch не трогаем.
    /// </summary>
    public sealed class TextEventFlow : IEventFlow
    {
        private readonly TextEventData _event;
        private readonly IPublisher<OpenTextEventRequest> _openPub;
        private readonly EventEffectApplier _applier;

        public TextEventFlow(TextEventData ev, IPublisher<OpenTextEventRequest> openPub, EventEffectApplier applier)
        {
            _event   = ev;
            _openPub = openPub;
            _applier = applier;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            if (_event == null)
            {
                Debug.LogWarning("[TextEventFlow] - event == null → Aborted");
                return EventResult.Aborted;
            }

            var choices = _event.Choices;
            if (choices.Count == 0)
            {
                Debug.LogWarning($"[TextEventFlow] - ивент '{_event.Id}' без вариантов ответа → показан и закрыт");
                return EventResult.Completed;
            }

            var tcs = new UniTaskCompletionSource<int>();
            _openPub.Publish(new OpenTextEventRequest(_event, i => tcs.TrySetResult(i)));
            int index = await tcs.Task;

            if (index < 0 || index >= choices.Count)
            {
                Debug.Log($"[TextEventFlow] - ивент '{_event.Id}' закрыт без выбора");
                return EventResult.Completed;
            }

            EventChoice chosen = choices[index];
            Debug.Log($"[TextEventFlow] - ивент '{_event.Id}': выбор [{index}] «{chosen.Label}»");
            _applier.Apply(chosen.Effects);
            return EventResult.Completed;
        }
    }
}
