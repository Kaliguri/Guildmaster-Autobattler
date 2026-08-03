using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using MessagePipe;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ экрана награды после узла (витрина 1-из-N → запись выбора в RunState).</summary>
    public interface IRewardPresenter
    {
        /// <summary>Скатать витрину тира, показать её, дождаться выбора и применить к <see cref="RunState"/>.
        /// <paramref name="ct"/> прерывает ожидание при выходе из забега (QA #37).</summary>
        UniTask PresentAsync(RewardTier tier, CancellationToken ct = default);
    }

    /// <summary>
    /// Презентер награды (план 11 §4 A3): катит витрину через <see cref="RewardService"/>, публикует запрос в UI
    /// (<see cref="OpenRewardRequest"/>), ждёт выбор игрока и пишет его в <see cref="RunState"/> через
    /// <see cref="RunStateService"/> (единая точка вместимости реликов, §5.4). Вынесен из <c>GameFlow</c>, чтобы
    /// показ награды переиспользовали и петля акта (<c>ActRunner</c>), и legacy-вход одного боя.
    /// <para>Требует слушателя <see cref="OpenRewardRequest"/> (UiRootBootstrap в CoreScene) — иначе выбор не придёт.</para>
    /// </summary>
    public sealed class RewardPresenter : IRewardPresenter
    {
        private readonly RewardService   _rewards;
        private readonly RunStateService _runStates;
        // Сброс реликвии ради места — односторонняя запись, идёт через шину и попадает в лог. Взятие
        // награды осталось прямым: оно спрашивает «влезло ли» синхронно, то есть транзакция.
        private readonly Guildmaster.Guild.Commands.IRunCommands _commands;
        private readonly IPublisher<OpenRewardRequest> _openRewardPub;

        public RewardPresenter(RewardService rewards, RunStateService runStates,
                               Guildmaster.Guild.Commands.IRunCommands commands,
                               IPublisher<OpenRewardRequest> openRewardPub)
        {
            _rewards       = rewards;
            _runStates     = runStates;
            _commands      = commands;
            _openRewardPub = openRewardPub;
        }

        public async UniTask PresentAsync(RewardTier tier, CancellationToken ct = default)
        {
            var choices = _rewards.RollChoices(tier);
            if (choices.Count == 0)
            {
                Debug.LogWarning("[RewardPresenter] - пул наград пуст (нет реликов в контент-БД) → без награды");
                return;
            }

            RunState run  = _runStates.Current;
            bool     full = _runStates.RelicInventoryFull;

            var tcs = new UniTaskCompletionSource<RewardChoiceResult>();
            _openRewardPub.Publish(new OpenRewardRequest(
                choices, full, run.RelicInventory, r => tcs.TrySetResult(r), ct)); // ct → закрыть экран при отмене (QA #37)

            RewardChoiceResult result = await tcs.Task.AttachExternalCancellation(ct);
            if (result.Skipped)
            {
                Debug.Log("[RewardPresenter] - награда пропущена");
                return;
            }

            if (full && !string.IsNullOrEmpty(result.DropRelicId))
                _commands.RemoveRelic(result.DropRelicId);

            bool added = _runStates.TryAddRelic(result.Chosen.Id);
            Debug.Log($"[RewardPresenter] - награда: взят '{result.Chosen.Id}'" +
                      (result.DropRelicId != null ? $" (сброшен '{result.DropRelicId}')" : "") +
                      (added ? "" : " — НЕ добавлен (нет места?)"));
            _runStates.Autosave();
        }
    }
}
