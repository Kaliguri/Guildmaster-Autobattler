using System;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using MessagePipe;
using UnityEngine;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Оркестратор макро-флоу игры (план 11 §2, §4). A2: умеет прогнать узел боя через <see cref="BattleFlow"/>
    /// (Prep→Combat→Outcome) поверх <see cref="RunState"/>. Полный флоу забега (MainMenu → карта → узлы →
    /// награды) достраивается шагами A3/B/C; швы (<see cref="IReadyGate"/>, <see cref="IPlayerIntentSource"/>)
    /// заведены сейчас, соло-тела. Legacy-вход <see cref="BootAsync"/> оставлен для прямого запуска боевой
    /// сцены (dev-панель F2) — не ломает итерацию.
    /// </summary>
    public sealed class GameFlow
    {
        private readonly ISceneLoader        _scenes;
        private readonly IBattleSession      _session;
        private readonly RunStateService     _runStates;
        private readonly RewardService       _rewards;
        private readonly IRngService         _rng;
        private readonly IReadyGate          _readyGate;
        private readonly IPlayerIntentSource _intents;
        private readonly IPublisher<OpenRewardRequest> _openRewardPub;

        public GameFlow(
            ISceneLoader        scenes,
            IBattleSession      session,
            RunStateService     runStates,
            RewardService       rewards,
            IRngService         rng,
            IReadyGate          readyGate,
            IPlayerIntentSource intents,
            IPublisher<OpenRewardRequest> openRewardPub)
        {
            _scenes        = scenes;
            _session       = session;
            _runStates     = runStates;
            _rewards       = rewards;
            _rng           = rng;
            _readyGate     = readyGate;
            _intents       = intents;
            _openRewardPub = openRewardPub;
        }

        /// <summary>Legacy (Фаза 1): просто загрузить боевую сцену. Прямой dev-вход, бой запускает F2-панель.</summary>
        public async UniTask BootAsync()
        {
            Debug.Log("[GameFlow] - Boot (legacy): загружаю BattleScene");
            await _scenes.LoadBattleAsync();
        }

        /// <summary>Legacy: выгрузить боевую сцену после боя (парный к <see cref="BootAsync"/>).</summary>
        public async UniTask OnBattleEndedAsync(BattleOutcome outcome)
        {
            Debug.Log($"[GameFlow] - Бой завершён (legacy): {outcome}");
            await _scenes.UnloadBattleAsync();
        }

        /// <summary>
        /// A2-разрез: прогнать один бой как узел забега — грузит сцену, ждёт исход (с ретраями), выгружает.
        /// Заводит забег (<see cref="RunState"/>), если его ещё нет. Возвращает исход узла для будущей
        /// награды/перехода (A3). Полноценная петля «узел за узлом» — на карте (B1).
        /// </summary>
        public async UniTask<EventResult> RunSingleBattleAsync(
            BattlePresetData preset, RewardTier tier = RewardTier.Battle, bool presentReward = true)
        {
            RunState run = _runStates.Current
                           ?? _runStates.NewRun(DateTime.UtcNow.Ticks, Array.Empty<RosterSlot>());

            var ctx  = new RunContext(run, _rng, _readyGate, _intents);
            var flow = new BattleFlow(preset, _scenes, _session);

            EventResult result = await flow.Run(ctx);
            _runStates.Autosave(); // точка автосейва после узла (вики «7» §5)

            // Победа → награда (A3): витрина 1-из-3, выбор пишется в RunState (enforce вместимости — §5.4).
            if (presentReward && result.Outcome == EventOutcome.Completed)
                await PresentRewardAsync(tier);

            return result;
        }

        /// <summary>
        /// Экран награды после победы (A3): катит витрину, публикует запрос в UI, ждёт выбор игрока, пишет его
        /// в <see cref="RunState"/> через <see cref="RunStateService"/> (единая точка вместимости реликов).
        /// Требует слушателя <c>OpenRewardRequest</c> (UiRootBootstrap в CoreScene) — иначе выбор не придёт.
        /// </summary>
        private async UniTask PresentRewardAsync(RewardTier tier)
        {
            var choices = _rewards.RollChoices(tier);
            if (choices.Count == 0)
            {
                Debug.LogWarning("[GameFlow] - пул наград пуст (нет реликов в контент-БД) → без награды");
                return;
            }

            RunState run  = _runStates.Current;
            bool     full = _runStates.RelicInventoryFull;

            var tcs = new UniTaskCompletionSource<RewardChoiceResult>();
            _openRewardPub.Publish(new OpenRewardRequest(
                choices, full, run.RelicInventory, r => tcs.TrySetResult(r)));

            RewardChoiceResult result = await tcs.Task;
            if (result.Skipped)
            {
                Debug.Log("[GameFlow] - награда пропущена");
                return;
            }

            if (full && !string.IsNullOrEmpty(result.DropRelicId))
                _runStates.RemoveRelic(result.DropRelicId);

            bool added = _runStates.TryAddRelic(result.Chosen.Id);
            Debug.Log($"[GameFlow] - награда: взят '{result.Chosen.Id}'" +
                      (result.DropRelicId != null ? $" (сброшен '{result.DropRelicId}')" : "") +
                      (added ? "" : " — НЕ добавлен (нет места?)"));
            _runStates.Autosave();
        }
    }
}
