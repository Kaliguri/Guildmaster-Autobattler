using System;
using System.Collections.Generic;
using System.Threading;
using Guildmaster.Core.Flow;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Экраны узла — ОДИН код на обе роли: слушает объявленный шаг узла и показывает то, что на нём.
    /// </summary>
    /// <remarks>
    /// <b>Заведён 08.08.2026 под HARD-правило «хозяин и гость — равные игроки».</b> До него экраны узла
    /// публиковала петля акта, а она собирается только владельцу (<c>ActivityInstaller</c>,
    /// <c>_role == Owner</c>): из восьми экранов узла гость видел ровно один — витрину награды, да и ту
    /// вторым, отдельным путём. Разделение при этом было не решением, а несделанной работой, и игроку
    /// читалось как поломка: у клиента просто нет кнопок.
    /// <para><b>Роль осталась там, где ей и место</b> — в том, кто ОБЪЯВЛЯЕТ шаг: узел ведёт владелец,
    /// у него состояние забега и броски. Показ же одинаков, потому что смотрят оба.</para>
    /// <para><b>Передышка — навигация, а не решение</b> (вердикт Макса: «Каждый нажимает отдельно чисто
    /// для себя»). Поэтому кнопки не голосуют и ничего не ждут: они публикуют тот же запрос смены
    /// режима, что и табы верхней панели.</para>
    /// </remarks>
    public sealed class SessionStageScreens : IStartable, IDisposable
    {
        private readonly ISessionStageView _stage;
        private readonly IPublisher<OpenContinueRequest> _continuePub;
        private readonly IPublisher<GoToModeRequest>     _modePub;
        private readonly IPublisher<OpenRewardRequest>   _rewardPub;
        private readonly IPublisher<OpenNodeFarewellRequest> _farewellPub;
        private readonly IPublisher<OpenChestRequest>        _chestPub;
        private readonly IPublisher<OpenTextEventRequest>    _eventPub;
        private readonly IPublisher<OpenOutcomeRequest>      _outcomePub;
        // «В меню» — тот же путь, что из паузы: у владельца отменяет забег, у гостя уводит из сеанса.
        private readonly Core.Flow.IRunControl _runControl;
        // Реестр контента: по проводу едут id, а витрине нужны определения. Реестры сторон совпадают —
        // это проверено рукопожатием, поэтому промах по id здесь означает поломку, а не редкий случай.
        private readonly IContentDatabase _content;
        // Запас реликвий — чтобы при полном показать, что придётся выбросить. Есть у ОБЕИХ ролей:
        // у владельца это держатель забега, у гостя — приёмник снимков.
        private readonly ISessionRunState _runs;
        private readonly Core.Net.ISharedDecision _decision;

        // Срок жизни объявленного шага: сменился шаг — прежний экран снят. Один способ закрывать на обе
        // роли, потому что и открывает их обеим один и тот же код.
        private CancellationTokenSource _life;

        public SessionStageScreens(ISessionStageView stage,
                                IPublisher<OpenContinueRequest> continuePub,
                                IPublisher<GoToModeRequest> modePub,
                                IPublisher<OpenRewardRequest> rewardPub,
                                IPublisher<OpenNodeFarewellRequest> farewellPub,
                                IPublisher<OpenChestRequest> chestPub,
                                IPublisher<OpenTextEventRequest> eventPub,
                                IPublisher<OpenOutcomeRequest> outcomePub,
                                Core.Flow.IRunControl runControl,
                                IContentDatabase content,
                                ISessionRunState runs,
                                Core.Net.ISharedDecision decision)
        {
            _stage       = stage;
            _continuePub = continuePub;
            _modePub     = modePub;
            _rewardPub   = rewardPub;
            _farewellPub = farewellPub;
            _chestPub    = chestPub;
            _eventPub    = eventPub;
            _outcomePub  = outcomePub;
            _runControl  = runControl;
            _content     = content;
            _runs        = runs;
            _decision    = decision;
        }

        public void Start()
        {
            if (_stage == null) return;

            _stage.Changed += OnStageChanged;

            // Шаг мог быть объявлен ДО нашего рождения: сеанс и его скоупы пересоздаются на каждой
            // смене режима, а узел при этом продолжается. Догоняем текущее состояние, а не ждём смены.
            OnStageChanged(_stage.Current);
        }

        public void Dispose()
        {
            if (_stage != null) _stage.Changed -= OnStageChanged;

            EndPreviousStage();
        }

        private void OnStageChanged(SessionStageState state)
        {
            // Прежний экран снимаем ВСЕГДА, даже если новый вид ничего не показывает: объявленный шаг
            // и есть срок жизни экрана. Без этого прощание с прошлым узлом висело бы поверх нового.
            EndPreviousStage();

            _life = new CancellationTokenSource();
            CancellationToken alive = _life.Token;

            // Сначала экран узла, потом хвост: кнопки «дальше» ложатся ПОВЕРХ него, а не вместо. У
            // текстового события под ними остаётся само событие с текстом результата, у сундука —
            // кадр-прощание, у боя — арена.
            switch (state.Kind)
            {
                case SessionStageKind.Reward:    ShowReward(in state);          break;
                case SessionStageKind.TextEvent: ShowTextEvent(in state, alive); break;
                case SessionStageKind.Chest:     ShowChest(alive);              break;
                case SessionStageKind.Outcome:   ShowOutcome(in state);         break;
            }

            if (state.Rest.Ended) ShowNodeEnd(in state, alive);
        }

        private void EndPreviousStage()
        {
            if (_life == null) return;

            _life.Cancel();
            _life.Dispose();
            _life = null;
        }

        /// <summary>
        /// Узел пройден: кадр-прощание (если узел его оставил) и кнопки «дальше» поверх всего.
        /// </summary>
        /// <remarks>
        /// Кадр публикуем ПЕРВЫМ: он задник, и придя вторым, лёг бы поверх кнопок.
        /// </remarks>
        private void ShowNodeEnd(in SessionStageState state, CancellationToken alive)
        {
            if (state.Rest.HasFarewell)
                _farewellPub?.Publish(new OpenNodeFarewellRequest(
                    state.Rest.TitleKey, state.Rest.BodyKey, alive));

            _continuePub?.Publish(new OpenContinueRequest(
                labelKey:    null,
                onContinue:  () => _modePub?.Publish(new GoToModeRequest(RunMode.Map)),
                onFormation: () => _modePub?.Publish(new GoToModeRequest(RunMode.Battle))));
        }

        /// <summary>
        /// Исход забега: победа или поражение, и три выхода с экрана.
        /// </summary>
        /// <remarks>
        /// «Заново» и «во двор» — голоса за варианты одного решения; «в меню» уводит того, кто нажал,
        /// и зовёт для этого тот же <c>IRunControl</c>, что и пауза (вердикт Макса 08.08.2026).
        /// </remarks>
        private void ShowOutcome(in SessionStageState state)
        {
            if (!state.TryOpenOutcome(out OutcomeStage outcome)) return;

            _outcomePub?.Publish(new OpenOutcomeRequest(
                outcome.Victory,
                onToMenu:  () => _runControl?.RequestReturnToMainMenu(),
                onContinue: null,   // акт кончился: продолжать нечем
                onRestart: () => _decision?.Choose(Core.Net.RunAfterOptions.Restart),
                onToGuild: () => _decision?.Choose(Core.Net.RunAfterOptions.Guild)));
        }

        /// <summary>Закрытый сундук: клик по крышке — голос, крышку открывает вся группа.</summary>
        private void ShowChest(CancellationToken alive) =>
            _chestPub?.Publish(new OpenChestRequest(
                () => _decision?.Choose(Core.Net.DecisionOptions.Agree), alive));

        /// <summary>Текстовое событие: собрать из id и отдать выбор общему решению.</summary>
        /// <remarks>
        /// Голосуем НОМЕРОМ строки, а не её текстом: текст переводится, и у игроков с разным языком
        /// голоса за один и тот же ответ не сошлись бы никогда.
        /// </remarks>
        private void ShowTextEvent(in SessionStageState state, CancellationToken alive)
        {
            if (!state.TryOpenTextEvent(out TextEventStage box)) return;

            if (_content == null || !_content.TryGet(box.EventId, out TextEventData data))
            {
                Debug.LogError($"[SessionStageScreens] - события '{box.EventId}' нет в реестре: " +
                               "контент разъехался, хотя рукопожатие это проверяло.");
                return;
            }

            _eventPub?.Publish(new OpenTextEventRequest(
                data,
                index => _decision?.Choose(index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                box.Gold,
                alive));
        }

        /// <summary>Собрать витрину из объявленных id и открыть её.</summary>
        /// <remarks>
        /// <b>Клик — голос, а не взятие</b>: награда общая. Экран закрывается признаком срабатывания от
        /// общего решения, а не по клику и не по объявлению <see cref="SessionStageState.Idle"/> — два пути
        /// к одному закрытию разошлись бы, и у кого-то витрина осталась бы висеть после того, как
        /// награду уже взяли.
        /// </remarks>
        private void ShowReward(in SessionStageState state)
        {
            if (!state.TryOpenReward(out RewardStage shelf)) return;

            IReadOnlyList<string> ids = shelf.Options;

            var choices = new List<RelicData>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                if (_content != null && _content.TryGet(ids[i], out RelicData relic)) choices.Add(relic);
                else Debug.LogError($"[SessionStageScreens] - реликвии '{ids[i]}' нет в реестре: " +
                                    "контент разъехался, хотя рукопожатие это проверяло.");
            }

            if (choices.Count == 0) return;

            IReadOnlyList<string> inventory = _runs?.Current?.RelicInventory ?? Array.Empty<string>();

            _rewardPub?.Publish(new OpenRewardRequest(
                choices,
                // Признак «запас полон» приехал вместе с витриной: от него зависит текст ГОЛОСА, а
                // согласие сравнивает голоса побайтово. Считай мы его тут сами — при полном запасе у
                // владельца голоса не сошлись бы никогда.
                shelf.InventoryFull,
                inventory,
                option => _decision?.Choose(option)));
        }
    }
}
