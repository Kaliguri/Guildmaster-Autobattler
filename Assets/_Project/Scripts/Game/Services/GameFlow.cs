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

        // Участники ЗАНЯТИЯ (рукопожатие боя, раннер акта, награды, последствия ивентов, гейты) живут
        // в его скоупе и умирают вместе с ним, поэтому берутся у хоста в момент использования, а не
        // инъекцией на всю жизнь верхней петли.
        private readonly Activity.ActivityHost _activities;

        // Состояние забега живёт в скоупе СЕССИИ и умирает вместе с ней (смена профиля, вход гостем),
        // поэтому верхняя петля спрашивает держателя у хоста в момент использования — тем же приёмом,
        // что и участников занятия выше. Ссылка полем пережила бы своего владельца.
        private readonly Session.SessionHost _sessions;
        private readonly IOutcomePresenter   _outcomePresenter;
        private readonly ITitleCardPresenter _titleCardPresenter;
        private readonly IMainMenuPresenter  _mainMenuPresenter;
        private readonly ActConfig           _actConfig;
        private readonly IRngService         _rng;
        private readonly ILocalPlayer        _localPlayer;
        private readonly IScreenTransition   _transition;
        private readonly IPublisher<OpenTextEventRequest> _openEventPub;
        private readonly IPublisher<RunPartyReadyEvent>   _partyReadyPub;

        // Ристалище: интент входа и состояние площадки — цикл открывает её и ждёт, пока игрок не выйдет.
        private readonly IPublisher<Data.Definitions.SetTestZoneRequest>    _provingGroundsPub;
        private readonly ISubscriber<Data.Definitions.TestZoneChangedEvent> _provingGroundsChangedSub;

        public GameFlow(
            Activity.ActivityHost activities,
            Session.SessionHost sessions,
            IOutcomePresenter   outcomePresenter,
            ITitleCardPresenter titleCardPresenter,
            IMainMenuPresenter  mainMenuPresenter,
            ActConfig           actConfig,
            IRngService         rng,
            ILocalPlayer        localPlayer,
            IScreenTransition   transition,
            IPublisher<OpenTextEventRequest> openEventPub,
            IPublisher<RunPartyReadyEvent>   partyReadyPub,
            IPublisher<Data.Definitions.SetTestZoneRequest>    provingGroundsPub,
            ISubscriber<Data.Definitions.TestZoneChangedEvent> provingGroundsChangedSub)
        {
            _provingGroundsPub = provingGroundsPub;
            _provingGroundsChangedSub = provingGroundsChangedSub;
            _activities      = activities;
            _sessions         = sessions;
            _outcomePresenter = outcomePresenter;
            _titleCardPresenter = titleCardPresenter;
            _mainMenuPresenter = mainMenuPresenter;
            _actConfig       = actConfig;
            _rng             = rng;
            _localPlayer     = localPlayer;
            _transition      = transition;
            _openEventPub    = openEventPub;
            _partyReadyPub   = partyReadyPub;
        }

        /// <summary>
        /// Держатель забега текущего сеанса. <c>null</c> — не режим работы, а незаведённый сеанс:
        /// вести забег в этом случае некому и некуда, поэтому говорим об этом громко и не пытаемся
        /// «как-нибудь» продолжить.
        /// </summary>
        private RunStateService RequireRun()
        {
            RunStateService runStates = _sessions.Run;
            if (runStates == null)
                Debug.LogError("[GameFlow] - нет сеанса владельца состояния → вести забег некому. " +
                               "Сессию открывает GameBootstrap сразу после подъёма мира (SessionHost.Open).");
            return runStates;
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
            RunStateService runStates = RequireRun();
            if (runStates == null) return EventResult.Aborted;

            RunState run = runStates.Current
                           ?? runStates.NewDefaultRun(DateTime.UtcNow.Ticks);

            // Даже один бой — мероприятие: ему нужны рукопожатие боя и владелец боевого скоупа.
            _activities.Open();
            try
            {
                var ctx  = new RunContext(run, _rng, _activities.ReadyGate, _activities.Intents);
                var flow = new BattleFlow(preset, _activities.Battles, _localPlayer);

                EventResult result = await flow.Run(ctx);
                runStates.Autosave(); // точка автосейва после узла (вики «7» §5)

                // Победа → награда (A3): витрина 1-из-3, выбор пишется в RunState (enforce вместимости — §5.4).
                if (presentReward && result.Outcome == EventOutcome.Completed)
                    await _activities.Rewards.PresentAsync(tier);

                return result;
            }
            finally
            {
                // Арену в мир не возвращаем руками: занятие уходит вместе с боем, и мир снова показывает
                // свой отряд сам (BattleHost.Close → WorldStageController.PlaceParty).
                _activities.Close();
            }
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
                RunStateService runStates = RequireRun();
                if (runStates == null) return;

                MainMenuChoice choice = await _mainMenuPresenter.ShowAsync(runStates.HasSave);

                if (choice == MainMenuChoice.Quit) { QuitGame(); return; }

                // Ристалище — не забег: ни сейва, ни акта, ни карты. Открываем площадку, ждём выхода
                // и возвращаемся к меню тем же витком.
                if (choice == MainMenuChoice.ProvingGrounds)
                {
                    await ShowProvingGroundsAsync();
                    continue;
                }

                if (choice == MainMenuChoice.Continue)
                {
                    Core.Persistence.SaveLoadResult<Guild.RunState> loaded = runStates.TryLoad();
                    if (!loaded.IsOk)
                    {
                        // Показать это игроку экраном — фаза E ТЗ [[save-system]]; пока внятный лог, но
                        // молча в меню не возвращаемся: «сейв есть, но заблокирован» ≠ «сейва нет».
                        Debug.LogWarning($"[GameFlow] - продолжить не вышло ({loaded.Status}" +
                                         (loaded.IsBlocked ? $", записан версией {loaded.SavedGameVersion}" : "") +
                                         ") → назад в меню");
                        continue;
                    }
                }
                else
                {
                    runStates.NewDefaultRun(DateTime.UtcNow.Ticks);
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

        /// <summary>
        /// Открыть Ристалище и держать цикл здесь, пока игрок не выйдет с площадки
        /// (ГДД «Modes - Proving Grounds»).
        /// </summary>
        /// <remarks>
        /// Цикл обязан ждать: без ожидания он тут же показал бы главное меню поверх площадки, и вышло бы
        /// ровно то, на что нельзя смотреть — бой под меню. Решение о входе принимает не этот метод, а
        /// владелец расстановки (интент может быть и отклонён), поэтому выход ловим по СОСТОЯНИЮ площадки,
        /// а не по своему предположению о нём.
        /// </remarks>
        private async UniTask ShowProvingGroundsAsync()
        {
            if (_provingGroundsPub == null || _provingGroundsChangedSub == null)
            {
                Debug.LogWarning("[GameFlow] - Ристалище не разведено (нет интента или состояния) → назад в меню");
                return;
            }

            var closed = new UniTaskCompletionSource();
            using (_provingGroundsChangedSub.Subscribe(e => { if (!e.Active) closed.TrySetResult(); }))
            {
                _provingGroundsPub.Publish(new Data.Definitions.SetTestZoneRequest(true));
                await closed.Task;
            }

            Debug.Log("[GameFlow] - Ристалище закрыто → главное меню");
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
            RunStateService runStates = RequireRun();
            if (runStates == null) return EventResult.Aborted;

            RunState run = runStates.Current
                           ?? runStates.NewDefaultRun(DateTime.UtcNow.Ticks);

            runStates.BeginAct(_actConfig != null ? _actConfig.ToGenConfig() : null); // карта из под-сида по ActConfig (no-op, если уже есть)
            runStates.Autosave();       // зафиксировать свежую карту

            // Persist-мир (план 12 Ф2): отряд забега готов → боевой скоуп ставит его на тест-арену вне боя.
            // Публикуем ПОСЛЕ BeginAct (гильдия+карта собраны) и ДО обхода узлов, чтобы отряд уже стоял.
            _partyReadyPub.Publish(new RunPartyReadyEvent());

            // Токен отмены забега на время акта (QA #18): «В главное меню» → Cancel → OperationCanceledException.
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            _activities.Open();
            try
            {
                var ctx = new RunContext(run, _rng, _activities.ReadyGate, _activities.Intents, _runCts.Token);
                EventResult result = await _activities.Runner.RunActAsync(ctx);
                runStates.Autosave();
                Debug.Log($"[GameFlow] - акт завершён: {result.Outcome}");

                // Экран исхода (C2): победа (босс) / поражение (пул перезапусков пуст). Забег окончен — чистим сейв.
                if (result.Outcome == EventOutcome.Completed || result.Outcome == EventOutcome.PlayerDefeated)
                {
                    await _outcomePresenter.ShowAsync(result.Outcome == EventOutcome.Completed);
                    runStates.DeleteSave();
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

                // Ни сброса арены, ни сброса фазы здесь больше нет: забег кончился — кончилось и
                // занятие, а вместе с ним ушли бой, его скоуп и часы. Раньше эти две строки были
                // единственным, что отделяло один забег от другого, и любая забытая делала следующий
                // забег чужим.
                _activities.Close();
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
            RunStateService runStates = RequireRun();
            if (runStates == null) return EventResult.Aborted;

            RunState run = runStates.Current
                           ?? runStates.NewDefaultRun(DateTime.UtcNow.Ticks);

            _activities.Open();
            var ctx  = new RunContext(run, _rng, _activities.ReadyGate, _activities.Intents);
            var flow = new TextEventFlow(ev, _openEventPub, _activities.EventEffects);
            return await flow.Run(ctx);
        }
    }
}
