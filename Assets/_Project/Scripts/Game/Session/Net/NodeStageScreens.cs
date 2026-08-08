using System;
using System.Collections.Generic;
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
    public sealed class NodeStageScreens : IStartable, IDisposable
    {
        private readonly INodeStageView _stage;
        private readonly IPublisher<OpenContinueRequest> _continuePub;
        private readonly IPublisher<GoToModeRequest>     _modePub;
        private readonly IPublisher<OpenRewardRequest>   _rewardPub;
        // Реестр контента: по проводу едут id, а витрине нужны определения. Реестры сторон совпадают —
        // это проверено рукопожатием, поэтому промах по id здесь означает поломку, а не редкий случай.
        private readonly IContentDatabase _content;
        // Запас реликвий — чтобы при полном показать, что придётся выбросить. Есть у ОБЕИХ ролей:
        // у владельца это держатель забега, у гостя — приёмник снимков.
        private readonly ISessionRunState _runs;
        private readonly Core.Net.ISharedDecision _decision;

        public NodeStageScreens(INodeStageView stage,
                                IPublisher<OpenContinueRequest> continuePub,
                                IPublisher<GoToModeRequest> modePub,
                                IPublisher<OpenRewardRequest> rewardPub,
                                IContentDatabase content,
                                ISessionRunState runs,
                                Core.Net.ISharedDecision decision)
        {
            _stage       = stage;
            _continuePub = continuePub;
            _modePub     = modePub;
            _rewardPub   = rewardPub;
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
        }

        private void OnStageChanged(NodeStageState state)
        {
            switch (state.Kind)
            {
                case NodeStageKind.Interlude: ShowInterlude();          break;
                case NodeStageKind.Reward:    ShowReward(in state);     break;
            }
        }

        private void ShowInterlude() =>
            _continuePub?.Publish(new OpenContinueRequest(
                labelKey:    null,
                onContinue:  () => _modePub?.Publish(new GoToModeRequest(RunMode.Map)),
                onFormation: () => _modePub?.Publish(new GoToModeRequest(RunMode.Battle))));

        /// <summary>Собрать витрину из объявленных id и открыть её.</summary>
        /// <remarks>
        /// <b>Клик — голос, а не взятие</b>: награда общая. Экран закрывается признаком срабатывания от
        /// общего решения, а не по клику и не по объявлению <see cref="NodeStageState.Idle"/> — два пути
        /// к одному закрытию разошлись бы, и у кого-то витрина осталась бы висеть после того, как
        /// награду уже взяли.
        /// </remarks>
        private void ShowReward(in NodeStageState state)
        {
            IReadOnlyList<string> ids = state.Options;

            var choices = new List<RelicData>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                if (_content != null && _content.TryGet(ids[i], out RelicData relic)) choices.Add(relic);
                else Debug.LogError($"[NodeStageScreens] - реликвии '{ids[i]}' нет в реестре: " +
                                    "контент разъехался, хотя рукопожатие это проверяло.");
            }

            if (choices.Count == 0) return;

            IReadOnlyList<string> inventory = _runs?.Current?.RelicInventory ?? Array.Empty<string>();

            _rewardPub?.Publish(new OpenRewardRequest(
                choices,
                // Признак «запас полон» приехал вместе с витриной: от него зависит текст ГОЛОСА, а
                // согласие сравнивает голоса побайтово. Считай мы его тут сами — при полном запасе у
                // владельца голоса не сошлись бы никогда.
                state.InventoryFull,
                inventory,
                option => _decision?.Choose(option)));
        }
    }
}
