using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Core.Flow;
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
    /// заведены сейчас, соло-тела.
    /// </summary>
    /// <remarks>
    /// Сцен этот класс не грузит вовсе: и мир, и боевые системы поднимаются один раз на буте
    /// (<c>GameBootstrap</c>) и живут всю сессию. Legacy-вход «загрузить боевую сцену → выгрузить после боя»
    /// снят: он спорил с persist-моделью, где бой — команда в живой симуляции.
    /// </remarks>
    public sealed class GameFlow : IRunControl
    {
        // Токен отмены текущего забега (QA #18): взводится на время RunActAsync, Cancel() из системного меню
        // прерывает висящие await'ы петли (выбор узла/«Продолжить»/исход боя) → возврат в главное меню.
        private CancellationTokenSource _runCts;

        private readonly IBattleSession      _session;
        private readonly RunStateService     _runStates;
        private readonly IRewardPresenter    _rewardPresenter;
        private readonly IOutcomePresenter   _outcomePresenter;
        private readonly ITitleCardPresenter _titleCardPresenter;
        private readonly IMainMenuPresenter  _mainMenuPresenter;
        private readonly ActRunner           _actRunner;
        private readonly ActConfig           _actConfig;
        private readonly EventEffectApplier  _eventEffects;
        private readonly IRngService         _rng;
        private readonly IReadyGate          _readyGate;
        private readonly IPlayerIntentSource _intents;
        private readonly ILocalPlayer        _localPlayer;
        private readonly IScreenTransition   _transition;
        private readonly IPublisher<OpenTextEventRequest> _openEventPub;
        private readonly IPublisher<RunPartyReadyEvent>   _partyReadyPub;

        public GameFlow(
            IBattleSession      session,
            RunStateService     runStates,
            IRewardPresenter    rewardPresenter,
            IOutcomePresenter   outcomePresenter,
            ITitleCardPresenter titleCardPresenter,
            IMainMenuPresenter  mainMenuPresenter,
            ActRunner           actRunner,
            ActConfig           actConfig,
            EventEffectApplier  eventEffects,
            IRngService         rng,
            IReadyGate          readyGate,
            IPlayerIntentSource intents,
            ILocalPlayer        localPlayer,
            IScreenTransition   transition,
            IPublisher<OpenTextEventRequest> openEventPub,
            IPublisher<RunPartyReadyEvent>   partyReadyPub)
        {
            _session         = session;
            _runStates        = runStates;
            _rewardPresenter  = rewardPresenter;
            _outcomePresenter = outcomePresenter;
            _titleCardPresenter = titleCardPresenter;
            _mainMenuPresenter = mainMenuPresenter;
            _actRunner       = actRunner;
            _actConfig       = actConfig;
            _eventEffects    = eventEffects;
            _rng             = rng;
            _readyGate       = readyGate;
            _intents         = intents;
            _localPlayer     = localPlayer;
            _transition      = transition;
            _openEventPub    = openEventPub;
            _partyReadyPub   = partyReadyPub;
        }

        /// <summary>
        /// A2-разрез: прогнать один бой как узел забега — запустить его в живой симуляции, дождаться исхода
        /// (с ретраями), вернуть арену в мир. Сцен не грузит: боевые системы подняты на буте и живут всегда.
        /// Заводит забег (<see cref="RunState"/>), если его ещё нет. Возвращает исход узла для будущей
        /// награды/перехода (A3). Полноценная петля «узел за узлом» — на карте (B1).
        /// </summary>
        public async UniTask<EventResult> RunSingleBattleAsync(
            BattlePresetData preset, RewardTier tier = RewardTier.Battle, bool presentReward = true)
        {
            RunState run = _runStates.Current
                           ?? _runStates.NewDefaultRun(DateTime.UtcNow.Ticks);

            var ctx  = new RunContext(run, _rng, _readyGate, _intents);
            var flow = new BattleFlow(preset, _session, _localPlayer);

            EventResult result = await flow.Run(ctx);
            _runStates.Autosave(); // точка автосейва после узла (вики «7» §5)

            // Победа → награда (A3): витрина 1-из-3, выбор пишется в RunState (enforce вместимости — §5.4).
            if (presentReward && result.Outcome == EventOutcome.Completed)
                await _rewardPresenter.PresentAsync(tier);

            // Арена живёт всё время после боя (фаза Interlude) и возвращается в мир на стыке узлов — в петле акта
            // это делает RunBeatStage; здесь (dev-разрез одного боя) петли нет, поэтому возвращаем сами.
            _session.RequestReset();
            _session.SetPhase(BattlePhase.None);

            return result;
        }

        /// <summary>
        /// Верхний цикл игры (план D1): title card → главное меню → забег → меню. Начать = новый забег,
        /// Продолжить = из автосейва, Выход = закрыть игру. Точка входа при обычном старте (не dev-разрез).
        /// </summary>
        public async UniTask RunGameAsync()
        {
            await _titleCardPresenter.ShowAsync(); // один раз за сессию, до первого меню

            while (true)
            {
                MainMenuChoice choice = await _mainMenuPresenter.ShowAsync(_runStates.HasSave);

                if (choice == MainMenuChoice.Quit) { QuitGame(); return; }

                if (choice == MainMenuChoice.Continue)
                {
                    if (_runStates.Load() == null) { Debug.LogWarning("[GameFlow] - нет автосейва → назад в меню"); continue; }
                }
                else
                {
                    _runStates.NewDefaultRun(DateTime.UtcNow.Ticks);
                }

                // QA #18: «В главное меню» из системного меню отменяет забег → OperationCanceledException
                // всплывает из петли акта; ловим и уходим на новый виток while (показ главного меню). Сейв
                // остаётся (autosave по ходу) — забег можно продолжить.
                try { await RunActAsync(); } // BeginAct + петля + экран исхода + чистка сейва
                catch (OperationCanceledException)
                {
                    Debug.Log("[GameFlow] - забег прерван из меню → возврат в главное меню");
                }
            }
        }

        private static void QuitGame()
        {
            Debug.Log("[GameFlow] - выход из игры");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// A2-разрез забега: сгенерировать карту акта (если нет) и прогнать петлю обхода через <see cref="ActRunner"/>
        /// (делегирование). Заводит забег, если его ещё нет (dev-запуск «начать акт»). Возвращает итог акта:
        /// <c>Completed</c> — босс пройден; <c>PlayerDefeated</c> — поражение; <c>Aborted</c> — сбой.
        /// </summary>
        public async UniTask<EventResult> RunActAsync()
        {
            RunState run = _runStates.Current
                           ?? _runStates.NewDefaultRun(DateTime.UtcNow.Ticks);

            _runStates.BeginAct(_actConfig != null ? _actConfig.ToGenConfig() : null); // карта из под-сида по ActConfig (no-op, если уже есть)
            _runStates.Autosave();       // зафиксировать свежую карту

            // Persist-мир (план 12 Ф2): отряд забега готов → боевой скоуп ставит его на тест-арену вне боя.
            // Публикуем ПОСЛЕ BeginAct (гильдия+карта собраны) и ДО обхода узлов, чтобы отряд уже стоял.
            _partyReadyPub.Publish(new RunPartyReadyEvent());

            // Токен отмены забега на время акта (QA #18): «В главное меню» → Cancel → OperationCanceledException.
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            try
            {
                var ctx = new RunContext(run, _rng, _readyGate, _intents, _runCts.Token);
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
            finally
            {
                // Забег кончился ЛЮБЫМ путём (босс, поражение, «В главное меню»): мир перестаёт быть первым
                // планом. Без этого фаза Interlude пережила бы забег, и задник UI не вернулся бы под меню.
                // Шторка перехода — туда же: «В меню», нажатое посреди нырка в узел, обрывало забег, но
                // оставляло чернила на экране, потому что вести их было уже некому (аудит 2026-07-26,
                // волна 2 — ровно тот вызов, который Cancel() описывает в своём докстринге).
                _transition?.Cancel();
                _session.RequestReset();
                _session.SetPhase(BattlePhase.None);
                _runCts.Dispose();
                _runCts = null;
            }
        }

        // QA #18: управление забегом из системного меню (pause) через IRunControl.
        public void RequestReturnToMainMenu()
        {
            Debug.Log("[GameFlow] - запрос «В главное меню» → прерываю текущий забег");
            _runCts?.Cancel();
        }

        public void RequestQuit() => QuitGame();

        /// <summary>
        /// Прогнать узел текстового ивента (план 11 §5.1): показать ивент, дождаться выбора, применить
        /// последствия к <see cref="RunState"/>. Заводит забег, если его ещё нет (dev-запуск в отрыве от боя).
        /// </summary>
        public async UniTask<EventResult> RunTextEventAsync(TextEventData ev)
        {
            RunState run = _runStates.Current
                           ?? _runStates.NewDefaultRun(DateTime.UtcNow.Ticks);

            var ctx  = new RunContext(run, _rng, _readyGate, _intents);
            var flow = new TextEventFlow(ev, _openEventPub, _eventEffects);
            return await flow.Run(ctx);
        }
    }
}
