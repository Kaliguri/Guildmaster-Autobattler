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
        // Токен отмены текущего МЕРОПРИЯТИЯ (QA #18): взводится на время забега и на время Ристалища,
        // Cancel() из системного меню прерывает висящие await'ы (выбор узла, «Продолжить», исход боя,
        // ожидание выхода с площадки) → возврат в главное меню. Пока токен взводил только забег, кнопка
        // «В главное меню» на площадке молча ничего не делала — отменять было нечего.
        private CancellationTokenSource _activityCts;

        // Участники ЗАНЯТИЯ (рукопожатие боя, раннер акта, награды, последствия ивентов, гейты) живут
        // в его скоупе и умирают вместе с ним, поэтому берутся у хоста в момент использования, а не
        // инъекцией на всю жизнь верхней петли.
        private readonly Activity.ActivityHost _activities;

        // Состояние забега живёт в скоупе СЕССИИ и умирает вместе с ней (смена профиля, вход гостем),
        // поэтому верхняя петля спрашивает держателя у хоста в момент использования — тем же приёмом,
        // что и участников занятия выше. Ссылка полем пережила бы своего владельца.
        private readonly Session.SessionHost _sessions;

        // Меню спрашивает про сейв ДО того, как сеанс открыт, поэтому диск и профиль оно спрашивает
        // напрямую — знание об этом одно и живёт в RunSaves.
        private readonly Core.Persistence.ISaveService    _save;
        private readonly Core.Persistence.IProfileService _profiles;

        private readonly IOutcomePresenter   _outcomePresenter;
        private readonly IMainMenuPresenter  _mainMenuPresenter;
        private readonly IProfilePresenter   _profilePresenter;
        private readonly IHubPresenter       _hubPresenter;
        private readonly ActConfig           _actConfig;
        private readonly IRngService         _rng;
        private readonly ILocalPlayer        _localPlayer;
        private readonly IScreenTransition   _transition;
        private readonly IPublisher<RunPartyReadyEvent>   _partyReadyPub;

        // Ристалище: интент входа и состояние площадки — цикл открывает её и ждёт, пока игрок не выйдет.
        private readonly ISubscriber<Data.Definitions.TestZoneChangedEvent> _provingGroundsChangedSub;

        // Кооп-сессия: цикл спрашивает её только об одном — жива ли ещё чужая игра, в которой мы гостим.
        private readonly Core.Net.ICoopSessionControl _coop;

        public GameFlow(
            Activity.ActivityHost activities,
            Session.SessionHost sessions,
            Core.Net.ICoopSessionControl coop,
            Core.Persistence.ISaveService    save,
            Core.Persistence.IProfileService profiles,
            IOutcomePresenter   outcomePresenter,
            IMainMenuPresenter  mainMenuPresenter,
            IProfilePresenter   profilePresenter,
            IHubPresenter       hubPresenter,
            ActConfig           actConfig,
            IRngService         rng,
            ILocalPlayer        localPlayer,
            IScreenTransition   transition,
            IPublisher<RunPartyReadyEvent>   partyReadyPub,
            ISubscriber<Data.Definitions.TestZoneChangedEvent> provingGroundsChangedSub)
        {
            _provingGroundsChangedSub = provingGroundsChangedSub;
            _activities      = activities;
            _sessions         = sessions;
            _coop             = coop;
            _save             = save;
            _profiles         = profiles;
            _outcomePresenter = outcomePresenter;
            _mainMenuPresenter = mainMenuPresenter;
            _profilePresenter  = profilePresenter;
            _hubPresenter      = hubPresenter;
            _actConfig       = actConfig;
            _rng             = rng;
            _localPlayer     = localPlayer;
            _transition      = transition;
            _partyReadyPub   = partyReadyPub;
        }

        /// <summary>
        /// Держатель забега текущего сеанса. <c>null</c> — не режим работы, а незаведённый сеанс:
        /// вести забег в этом случае некому и некуда, поэтому говорим об этом громко и не пытаемся
        /// «как-нибудь» продолжить.
        /// </summary>
        private RunStateService RequireRun()
        {
            // Сеанс открывается входом в режим, а не бутом. Дев-разрезы входят мимо меню, поэтому
            // сеанс им заводим здесь: разрез — это тот же вход в игру, просто без выбора.
            if (_sessions.Run == null && !_sessions.IsOpen)
                _sessions.Open(Session.SessionRole.Owner);

            RunStateService runStates = _sessions.Run;
            if (runStates == null)
                Debug.LogError("[GameFlow] - нет сеанса владельца состояния → вести забег некому. " +
                               "Сеанс открывает вход в режим (SessionHost.Open), мир — GameBootstrap.");
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
            _activities.Open(ActivitySetup.Campaign);
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
        /// Верхний цикл игры: главное меню → мероприятие → меню. Точка входа при обычном старте
        /// (не dev-разрез).
        /// <para>Бут-экран сюда не входит: он показывается раньше, вокруг загрузки мира
        /// (<c>GameBootstrap</c>), — иначе между поднятым миром и первым UI мелькает пустая арена.</para>
        /// </summary>
        /// <remarks>
        /// <b>Цикл не выбирает режим — он исполняет заказ</b> (модель Макса 02.08.2026). Меню отдаёт
        /// сюда готовый <see cref="GameStartRequest"/>: во что играем, в каком доме и пускаем ли
        /// друзей. Прежде на каждый режим здесь была своя ветка, а кооп был отдельным входом рядом с
        /// игрой — то есть две двери вели в одно и то же.
        /// </remarks>
        public async UniTask RunGameAsync()
        {
            while (true)
            {
                // Главное меню живёт ВНЕ сеанса: сеанс рождается выбором в меню, вместе с ролью.
                _sessions.Close();

                // Кооп кончается вместе с игрой, а не отдельной кнопкой. Отдельного экрана «Сетевая
                // игра» с «Отключиться» больше нет: сессия — свойство игры, и пережить возврат в меню
                // она не может. У хоста это конец сессии для всех — так и задумано, миграции авторитета
                // мы не пишем (решение 01.08.2026).
                // ...КРОМЕ случая, когда мы УЖЕ идём к кому-то в гости. Приглашение принимается и из
                // оверлея Steam посреди своего забега: забег рвётся, цикл возвращается сюда — и
                // закрыл бы то самое подключение, ради которого всё и прервалось. Признак гостя —
                // само состояние сессии: у хоста оно Hosting, у гостя Connecting/Connected.
                bool joiningSomeone = _coop != null
                                      && (_coop.State == Core.Net.CoopSessionState.Connecting
                                          || _coop.State == Core.Net.CoopSessionState.Connected);

                if (joiningSomeone)
                {
                    // Меню не показываем вовсе: игрок уже сделал выбор — в оверлее Steam. Лишний кадр
                    // главного меню между «принял приглашение» и «я в чужой игре» читался бы как сбой.
                    await PlayAsGuestAsync();
                    continue;
                }

                if (_coop != null && _coop.State != Core.Net.CoopSessionState.Offline) _coop.Leave();

                // Кем заходим — спрашивается ДО меню и только когда профиля нет: дом живёт внутри
                // профиля, и выбирать дом раньше слота попросту нечем. Профиль есть — экран не мелькает.
                if (_profilePresenter != null) await _profilePresenter.RequireAsync();

                MainMenuOutcome outcome = await _mainMenuPresenter.ShowAsync();

                if (outcome.Action == MainMenuAction.Quit) { QuitGame(); return; }

                // Приглашение доехало до рукопожатия: играем в чужом сеансе, пока он не кончится.
                if (outcome.Action == MainMenuAction.JoinCoop)
                {
                    await PlayAsGuestAsync();
                    continue;
                }

                if (!await PlayAsync(outcome.Start)) return;
            }
        }

        /// <summary>
        /// Исполнить заказ игрока: поднять лобби, если звал друзей, открыть сеанс владельца и провести
        /// выбранный режим. <c>false</c> — играть не с кем и не в чем, цикл дальше не идёт.
        /// </summary>
        private async UniTask<bool> PlayAsync(GameStartRequest request)
        {
            // Лобби поднимается ДО входа в режим: пока мы внутри, звать друзей будет уже некогда — а
            // кооп у нас свойство сеанса, а не отдельная игра, и открыт для всех трёх режимов сразу.
            if (request.OnlineLobby && _coop != null && _coop.State == Core.Net.CoopSessionState.Offline)
                _coop.StartHost();

            if (request.Mode != GameMode.Campaign)
            {
                // Сеанс нужен и здесь, хотя забега нет: мероприятие рождается ВНУТРИ сеанса, и без него
                // площадке не от кого родиться — SessionHost.CreateChild возвращал null, а игрок видел
                // синий экран (наход. Макса 04.08.2026). Прежде сеанс открывала только Кампания, потому
                // что открывал его RequireRun, а площадке забег не нужен.
                _sessions.Open(Session.SessionRole.Owner);

                // Площадка и матч — не забег: ни сейва, ни акта, ни карты. Открываем, ждём выхода и
                // возвращаемся к меню тем же витком.
                await ShowProvingGroundsAsync(request.Mode == GameMode.Pvp
                    ? ActivitySetup.Pvp
                    : ActivitySetup.ProvingGrounds);
                return true;
            }

            // Дом выбран в меню, и выбран ДО сеанса: ключ сейва растёт из активной гильдии, поэтому
            // сеанс, открытый раньше неё, писал бы забег не туда.
            if (!SelectGuild(request)) return true;

            // Кампания: с этого мгновения у состояния есть владелец — мы.
            RunStateService runStates = RequireRun();
            if (runStates == null) return false;

            // Забег в доме либо уже идёт, либо начинается. Отдельного «Продолжить» нет: игрок выбрал
            // дом, а не действие (ТЗ [[save-system]] §3 — гильдия и есть слот сохранения).
            if (RunSaves.Exists(_save, _profiles))
            {
                Core.Persistence.SaveLoadResult<RunState> loaded = runStates.TryLoad();
                if (!loaded.IsOk)
                {
                    // Показать это игроку экраном — фаза E ТЗ [[save-system]]; пока внятный лог, но
                    // молча в меню не возвращаемся: «сейв есть, но заблокирован» ≠ «сейва нет».
                    Debug.LogWarning($"[GameFlow] - забег дома не открылся ({loaded.Status}" +
                                     (loaded.IsBlocked ? $", записан версией {loaded.SavedGameVersion}" : "") +
                                     ") → назад в меню");
                    return true;
                }
            }
            else
            {
                runStates.NewDefaultRun(DateTime.UtcNow.Ticks);
            }

            // Двор гильдии: дом выбран, забег заряжен — но уходит игрок из него сам. Хаб стоит ЗДЕСЬ, а
            // не внутри RunActAsync: акт — это уже дорога, а двор — то, откуда на неё выходят, и всё
            // междузабежное (ростер, найм, лавка) будет жить тут же. Пока заглушка (ГДД
            // [[guild-hub-courtyard]]).
            bool throughCourtyard = true;
            while (true)
            {
                if (throughCourtyard && _hubPresenter != null) await _hubPresenter.ShowAsync();

                // QA #18: «В главное меню» отменяет забег → OperationCanceledException всплывает из
                // петли акта; ловим и уходим на новый виток while снаружи (показ главного меню). Сейв
                // остаётся (autosave по ходу) — забег можно продолжить.
                RunOutcomeChoice next;
                try { next = await RunActAsync(); } // BeginAct + петля + экран исхода + чистка сейва
                catch (OperationCanceledException)
                {
                    Debug.Log("[GameFlow] - забег прерван из меню → возврат в главное меню");
                    return true;
                }

                if (next == RunOutcomeChoice.ToMenu) return true;

                // Забег кончился, а дом остался: сейв старого забега уже стёрт, заводим новый прямо
                // здесь. Двор проходим только по пути «во двор» — «заново» тем и быстрый, что мимо.
                runStates.NewDefaultRun(DateTime.UtcNow.Ticks);
                throughCourtyard = next == RunOutcomeChoice.ToGuild;

                Debug.Log($"[GameFlow] - с экрана исхода: {next} → новый забег" +
                          (throughCourtyard ? " через двор" : " сразу"));
            }
        }

        /// <summary>
        /// Сделать активным дом из заказа, заведя новый, если игрок выбрал «новая гильдия».
        /// <c>false</c> — дома нет и не завелось, играть в кампанию некуда.
        /// </summary>
        private bool SelectGuild(GameStartRequest request)
        {
            if (_profiles == null)
            {
                Debug.LogError("[GameFlow] - нет службы профилей: кампании негде жить");
                return false;
            }

            if (!request.IsNewGuild) return _profiles.SelectGuild(request.GuildId);

            // Профиль здесь уже обязан быть: до главного меню игрок проходит через выбор слота
            // (03.08.2026). Молчаливое создание отсюда убрано — оно заводило профиль под именем,
            // которого игрок не выбирал, и делало это в момент, когда он думал о доме, а не о слоте.
            if (!_profiles.HasActiveProfile)
            {
                Debug.LogError("[GameFlow] - активного профиля нет → кампанию не начать. " +
                               "Экран выбора профиля обязан отработать до главного меню.");
                return false;
            }

            if (_profiles.CreateGuild(DefaultGuildName(_profiles)) != null) return true;

            Debug.LogWarning("[GameFlow] - новая гильдия не завелась (лимит домов?) → назад в меню");
            return false;
        }

        /// <summary>Имя дома по умолчанию. Переименование — дело игрока, а не условие входа в игру.</summary>
        private static string DefaultGuildName(Core.Persistence.IProfileService profiles) =>
            $"Гильдия {profiles.Guilds.Count + 1}";

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
        private async UniTask ShowProvingGroundsAsync(ActivitySetup setup)
        {
            if (_provingGroundsChangedSub == null)
            {
                Debug.LogWarning("[GameFlow] - Ристалище не разведено (нет интента или состояния) → назад в меню");
                return;
            }

            // Площадка — это МЕРОПРИЯТИЕ, а не флаг на мире: с ним рождаются и арена, и владелец
            // расстановки, которому адресован интент ниже. Пока площадку открывали одним интентом,
            // отвечать на него было некому — боевой скоуп рождается по требованию, и в этот момент
            // его ещё не существовало.
            _activities.Open(setup);

            // Токен мероприятия взводим и здесь, а не только на забеге: «В главное меню» из системного
            // меню отменяет ТЕКУЩЕЕ мероприятие, каким бы оно ни было. Пока токен взводил только забег,
            // на площадке кнопка молча ничего не делала — отменять было нечего (наход. Макса 02.08.2026).
            _activityCts?.Dispose();
            _activityCts = new CancellationTokenSource();
            try
            {
                // Интента «включи площадку» здесь НЕТ намеренно: площадка встаёт сама, по виду
                // мероприятия. Интент, посланный отсюда, не доходил — расстановка рождается вместе с
                // площадкой и подписывается позже, чем он уходит.
                var closed = new UniTaskCompletionSource();
                using (_activityCts.Token.Register(() => closed.TrySetCanceled()))
                using (_provingGroundsChangedSub.Subscribe(e => { if (!e.Active) closed.TrySetResult(); }))
                    await closed.Task;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[GameFlow] - Ристалище прервано из меню → возврат в главное меню");
            }
            finally
            {
                _activities.Close();
                _activityCts.Dispose();
                _activityCts = null;
            }

            Debug.Log("[GameFlow] - Ристалище закрыто → главное меню");
        }

        /// <summary>
        /// Играть гостем: открыть чужой сеанс и держать цикл здесь, пока сессия жива.
        /// </summary>
        /// <remarks>
        /// <b>Гость ничего не ведёт.</b> Куда идти, что покупать и когда начинать бой — решает владелец
        /// забега; гостю приезжает состояние снимком, а место — объявлением. Поэтому здесь нет ни петли
        /// акта, ни узлов: только жизнь сеанса от рукопожатия до разрыва.
        /// <para><b>Конец сессии — это конец гостевой игры,</b> и другого исхода у неё нет: гильдия
        /// живёт у хоста, миграции авторитета мы не пишем (решение 01.08.2026). Ушёл хост — гость
        /// возвращается в своё меню, унося открытия в свой профиль.</para>
        /// </remarks>
        private async UniTask PlayAsGuestAsync()
        {
            if (_coop == null)
            {
                Debug.LogError("[GameFlow] - гостевой вход без кооп-сессии: разводка разъехалась");
                return;
            }

            _sessions.Open(Session.SessionRole.Guest);

            var ended = new UniTaskCompletionSource();
            void OnState(Core.Net.CoopSessionState state)
            {
                if (state == Core.Net.CoopSessionState.Offline) ended.TrySetResult();
            }

            _coop.StateChanged += OnState;
            try
            {
                // Проверяем ПОСЛЕ подписки: сессия могла оборваться, пока меню закрывалось, и тогда
                // ждать было бы нечего — гость завис бы в чужой игре, которой уже нет.
                if (_coop.State == Core.Net.CoopSessionState.Offline) ended.TrySetResult();
                await ended.Task;
            }
            finally
            {
                _coop.StateChanged -= OnState;
                _sessions.Close();
            }

            Debug.Log($"[GameFlow] - гостевая сессия кончилась ({_coop.EndReason}) → главное меню");
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
        /// (делегирование). Заводит забег, если его ещё нет (dev-запуск «начать акт»).
        /// </summary>
        /// <returns>
        /// Куда игрок ушёл с экрана исхода. Сбой акта и путь, на котором экрана не было, читаются как
        /// <see cref="RunOutcomeChoice.ToMenu"/>: продолжать нечего.
        /// </returns>
        public async UniTask<RunOutcomeChoice> RunActAsync()
        {
            RunStateService runStates = RequireRun();
            if (runStates == null) return RunOutcomeChoice.ToMenu;

            RunState run = runStates.Current
                           ?? runStates.NewDefaultRun(DateTime.UtcNow.Ticks);

            runStates.BeginAct(_actConfig != null ? _actConfig.ToGenConfig() : null); // карта из под-сида по ActConfig (no-op, если уже есть)
            runStates.Autosave();       // зафиксировать свежую карту

            // Persist-мир (план 12 Ф2): отряд забега готов → боевой скоуп ставит его на тест-арену вне боя.
            // Публикуем ПОСЛЕ BeginAct (гильдия+карта собраны) и ДО обхода узлов, чтобы отряд уже стоял.
            _partyReadyPub.Publish(new RunPartyReadyEvent());

            // Токен отмены забега на время акта (QA #18): «В главное меню» → Cancel → OperationCanceledException.
            _activityCts?.Dispose();
            _activityCts = new CancellationTokenSource();
            _activities.Open(ActivitySetup.Campaign);
            try
            {
                var ctx = new RunContext(run, _rng, _activities.ReadyGate, _activities.Intents, _activityCts.Token);
                EventResult result = await _activities.Runner.RunActAsync(ctx);
                runStates.Autosave();
                Debug.Log($"[GameFlow] - акт завершён: {result.Outcome}");

                // Экран исхода (C2): победа (босс) / поражение (пул перезапусков пуст). Забег окончен — чистим сейв.
                if (result.Outcome != EventOutcome.Completed && result.Outcome != EventOutcome.PlayerDefeated)
                    return RunOutcomeChoice.ToMenu;

                // Токен забега сюда передаётся не для порядка: «В меню» на этом экране — тот же путь,
                // что и из паузы, то есть отмена. Без токена ожидание общего выбора пережило бы её и
                // держало бы игрока на экране, с которого он уже ушёл.
                RunOutcomeChoice choice = await _outcomePresenter.ShowAsync(
                    result.Outcome == EventOutcome.Completed, _activityCts.Token);

                runStates.DeleteSave();
                return choice;
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
                _activityCts.Dispose();
                _activityCts = null;
            }
        }

        /// <summary>
        /// QA #18: управление забегом из системного меню (pause) через <c>IRunControl</c>.
        /// </summary>
        /// <remarks>
        /// <b>У гостя прерывать нечего</b> — своего забега у него нет, а токен мероприятия взводят те
        /// две петли, которых он не проходит вовсе (акт и площадка). Пока эта разница не учитывалась,
        /// кнопка «В главное меню» у гостя молча не делала НИЧЕГО: отменять было нечего, и он оставался
        /// в чужой игре (найдено 07.08.2026 при разборе живого прогона).
        /// <para>Уйти из чужой игры значит покинуть сеанс — тогда гостевая петля кончается сама и
        /// возвращает игрока в его меню. Признак гостя берём у сеанса, а не у состояния сессии: роль
        /// назвали при входе, и это факт, а не догадка по признакам.</para>
        /// </remarks>
        public void RequestReturnToMainMenu()
        {
            if (_sessions?.Context?.Role == Session.SessionRole.Guest)
            {
                Debug.Log("[GameFlow] - запрос «В главное меню» у гостя → покидаю чужой сеанс");
                _coop?.Leave();
                return;
            }

            Debug.Log("[GameFlow] - запрос «В главное меню» → прерываю текущий забег");
            _activityCts?.Cancel();
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

            _activities.Open(ActivitySetup.Campaign);
            try
            {
                var ctx  = new RunContext(run, _rng, _activities.ReadyGate, _activities.Intents);
                var flow = new TextEventFlow(ev, _activities.EventEffects,
                                             _sessions.Decision, _sessions.SessionStage);
                return await flow.Run(ctx);
            }
            finally
            {
                // Как и у одиночного боя: занятие закрывает тот, кто его открыл. Оставленное открытым,
                // оно переживёт метод, и следующий Open закроет его уже посреди чужой работы.
                _activities.Close();
            }
        }
    }
}
