using System;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Core.Players;
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
        private readonly IRewardPresenter    _rewardPresenter;
        private readonly IOutcomePresenter   _outcomePresenter;
        private readonly ActRunner           _actRunner;
        private readonly EventEffectApplier  _eventEffects;
        private readonly IRngService         _rng;
        private readonly IReadyGate          _readyGate;
        private readonly IPlayerIntentSource _intents;
        private readonly ILocalPlayer        _localPlayer;
        private readonly IPublisher<OpenTextEventRequest> _openEventPub;

        public GameFlow(
            ISceneLoader        scenes,
            IBattleSession      session,
            RunStateService     runStates,
            IRewardPresenter    rewardPresenter,
            IOutcomePresenter   outcomePresenter,
            ActRunner           actRunner,
            EventEffectApplier  eventEffects,
            IRngService         rng,
            IReadyGate          readyGate,
            IPlayerIntentSource intents,
            ILocalPlayer        localPlayer,
            IPublisher<OpenTextEventRequest> openEventPub)
        {
            _scenes          = scenes;
            _session         = session;
            _runStates        = runStates;
            _rewardPresenter  = rewardPresenter;
            _outcomePresenter = outcomePresenter;
            _actRunner       = actRunner;
            _eventEffects    = eventEffects;
            _rng             = rng;
            _readyGate       = readyGate;
            _intents         = intents;
            _localPlayer     = localPlayer;
            _openEventPub    = openEventPub;
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
            var flow = new BattleFlow(preset, _scenes, _session, _localPlayer);

            EventResult result = await flow.Run(ctx);
            _runStates.Autosave(); // точка автосейва после узла (вики «7» §5)

            // Победа → награда (A3): витрина 1-из-3, выбор пишется в RunState (enforce вместимости — §5.4).
            if (presentReward && result.Outcome == EventOutcome.Completed)
                await _rewardPresenter.PresentAsync(tier);

            return result;
        }

        /// <summary>
        /// A2-разрез забега: сгенерировать карту акта (если нет) и прогнать петлю обхода через <see cref="ActRunner"/>
        /// (делегирование). Заводит забег, если его ещё нет (dev-запуск «начать акт»). Возвращает итог акта:
        /// <c>Completed</c> — босс пройден; <c>PlayerDefeated</c> — поражение; <c>Aborted</c> — сбой.
        /// </summary>
        public async UniTask<EventResult> RunActAsync()
        {
            RunState run = _runStates.Current
                           ?? _runStates.NewRun(DateTime.UtcNow.Ticks, Array.Empty<RosterSlot>());

            _runStates.BeginAct();       // генерация карты из под-сида (no-op, если уже есть)
            _runStates.Autosave();       // зафиксировать свежую карту

            var ctx = new RunContext(run, _rng, _readyGate, _intents);
            EventResult result = await _actRunner.RunActAsync(ctx);
            _runStates.Autosave();
            Debug.Log($"[GameFlow] - акт завершён: {result.Outcome}");

            // Экран исхода (C2): победа (босс) / поражение (пул перезапусков пуст). Забег окончен — чистим сейв.
            if (result.Outcome == EventOutcome.Completed || result.Outcome == EventOutcome.PlayerDefeated)
            {
                await _outcomePresenter.ShowAsync(result.Outcome == EventOutcome.Completed);
                _runStates.DeleteSave();
            }
            return result;
        }

        /// <summary>
        /// Прогнать узел текстового ивента (план 11 §5.1): показать ивент, дождаться выбора, применить
        /// последствия к <see cref="RunState"/>. Заводит забег, если его ещё нет (dev-запуск в отрыве от боя).
        /// </summary>
        public async UniTask<EventResult> RunTextEventAsync(TextEventData ev)
        {
            RunState run = _runStates.Current
                           ?? _runStates.NewRun(DateTime.UtcNow.Ticks, Array.Empty<RosterSlot>());

            var ctx  = new RunContext(run, _rng, _readyGate, _intents);
            var flow = new TextEventFlow(ev, _openEventPub, _eventEffects);
            return await flow.Run(ctx);
        }
    }
}
