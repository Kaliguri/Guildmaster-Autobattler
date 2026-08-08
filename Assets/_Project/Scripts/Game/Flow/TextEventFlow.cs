using Cysharp.Threading.Tasks;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Узел забега «текстовый ивент» (план 11 §3.4, §5.1) в стиле Slay the Spire: показывает
    /// <see cref="TextEventData"/> (заголовок/тело/картинка/варианты), ждёт выбор группы и применяет
    /// последствия выбранного варианта к <see cref="Guildmaster.Guild.RunState"/> через
    /// <see cref="EventEffectApplier"/>. Полиморфен через <see cref="IEventFlow"/> — новый ивент =
    /// новый ассет, центральный switch не трогаем.
    /// </summary>
    /// <remarks>
    /// <b>Ответ выбирают все вместе</b>: последствия ложатся в общий забег, и первый нажавший решал бы
    /// за группу. Голосуют номером строки — см. <see cref="Core.Net.DecisionKeys.EventChoice"/>.
    /// <para><b>Экран объявляется, а не публикуется отсюда</b>: показывает его общий для обеих ролей
    /// потребитель (<c>NodeStageScreens</c>). Пока показ жил здесь, гость экрана не видел вовсе — петля
    /// акта собирается только владельцу.</para>
    /// </remarks>
    public sealed class TextEventFlow : IEventFlow
    {
        private readonly TextEventData _event;
        private readonly EventEffectApplier _applier;
        private readonly Core.Net.ISharedDecision _decision;
        private readonly Session.Net.HostNodeStage _stage;

        public TextEventFlow(TextEventData ev, EventEffectApplier applier,
                             Core.Net.ISharedDecision decision, Session.Net.HostNodeStage stage = null)
        {
            _event    = ev;
            _applier  = applier;
            _decision = decision;
            _stage    = stage;
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

            var chosen = new UniTaskCompletionSource<string>();

            _decision?.Bind(Core.Net.DecisionKeys.EventChoice, option => chosen.TrySetResult(option));
            _stage?.Announce(Session.Net.NodeStageState.TextEvent(_event.Id, _applier.Gold));

            string option;
            try
            {
                option = await chosen.Task.AttachExternalCancellation(ctx.Cancellation);
            }
            finally
            {
                _decision?.Unbind(Core.Net.DecisionKeys.EventChoice);
            }

            if (!int.TryParse(option, out int index) || index < 0 || index >= choices.Count)
            {
                Debug.Log($"[TextEventFlow] - ивент '{_event.Id}' закрыт без выбора");
                return EventResult.Completed;
            }

            EventChoice picked = choices[index];
            Debug.Log($"[TextEventFlow] - ивент '{_event.Id}': выбор [{index}] ({_event.ChoiceLabelKey(index)})");
            _applier.Apply(picked.Effects);

            // Шаг НЕ снимаем: экран события с текстом-результатом висит всю передышку (QA #49), а
            // кнопки «дальше» петля навесит поверх него хвостом.
            return EventResult.Completed;
        }
    }
}
