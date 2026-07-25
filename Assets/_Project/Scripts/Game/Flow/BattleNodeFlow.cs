using System;
using Cysharp.Threading.Tasks;
using Guildmaster.Guild;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Обёртка боевого узла (план [[act-map-run-loop]]): прогоняет внутренний бой (<see cref="BattleFlow"/>) и при
    /// победе начисляет золото + катит награду тира. Держит награду В САМОМ УЗЛЕ (не в петле по типу карты) — так
    /// «?»-узел, роллящий бой, тоже получает награду, а <c>ActRunner</c> остаётся тонким. Поражение/прерывание —
    /// пробрасываются как есть.
    /// <para>Между победой и наградой — «бит» на досмотр добивания (п.4 брифа оверхола карты): короткая пауза,
    /// затем кнопка-мост «К наградам». Награду НЕ показываем автоматом — игрок сам решает, когда идти к выбору
    /// (может досмотреть финишер/переход сцены). Кнопка «Продолжить» ПОСЛЕ награды (узел → карта) живёт в
    /// <c>ActRunner</c> и здесь не дублируется.</para>
    /// <para>Узел ВЛАДЕЕТ ареной, пока игрок на нём: весь этот бит поле боя остаётся как есть (фаза
    /// <see cref="Data.Definitions.BattlePhase.Aftermath"/>, трупы на месте), и только выход из узла зовёт
    /// <see cref="IBattleSession.RequestReset"/>. Раньше чистку делал <see cref="BattleFlow"/> прямо на исходе —
    /// досмотр добивания шёл по пустому полю, да ещё за непрозрачным задником UI (фаза падала в None).</para>
    /// </summary>
    public sealed class BattleNodeFlow : IEventFlow
    {
        // Loc-ключ подписи кнопки-моста «бой → награда» (RU «К наградам»); таблица Content.
        private const string ContinueToRewardKey = "ui.reward.continue";

        private readonly IEventFlow         _battle;
        private readonly RewardTier         _tier;
        private readonly IRewardPresenter   _reward;
        private readonly RunStateService    _runStates;
        private readonly IContinuePresenter _continue;
        private readonly IBattleSession     _session;
        private readonly int                _rewardCount;
        private readonly float              _postWinDelaySeconds;

        /// <param name="session">
        /// Мост в боевой скоуп: по нему узел на выходе возвращает арену во вне-боевое состояние. null =
        /// узел без арены (тесты/заглушки) — чистка просто не зовётся.
        /// </param>
        public BattleNodeFlow(IEventFlow battle, RewardTier tier, IRewardPresenter reward, RunStateService runStates,
                              IContinuePresenter continuePresenter, IBattleSession session = null,
                              int rewardCount = 1, float postWinDelaySeconds = 2f)
        {
            _battle              = battle;
            _tier                = tier;
            _reward              = reward;
            _runStates           = runStates;
            _continue            = continuePresenter;
            _session             = session;
            _rewardCount         = rewardCount < 1 ? 1 : rewardCount;
            _postWinDelaySeconds = postWinDelaySeconds < 0f ? 0f : postWinDelaySeconds;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            try
            {
                EventResult result = await _battle.Run(ctx);
                if (result.Outcome != EventOutcome.Completed) return result;

                _runStates.AwardBattleReward();               // +золото за победу (B1)

                // Досмотр добивания (п.4): пауза перед мостом к награде. DeltaType по умолчанию — пауза забега
                // (timeScale=0) замораживает таймер, а ct («В меню» из паузы) размотает ожидание (QA #37).
                if (_postWinDelaySeconds > 0f)
                    await UniTask.Delay(TimeSpan.FromSeconds(_postWinDelaySeconds), cancellationToken: ctx.Cancellation);

                // Кнопка-мост «К наградам»: не переносим в награду автоматом — игрок жмёт сам (п.4).
                await _continue.WaitForContinueAsync(ContinueToRewardKey, ctx.Cancellation);

                for (int i = 0; i < _rewardCount; i++)        // элитка = 2 выбора подряд (B5)
                    await _reward.PresentAsync(_tier, ctx.Cancellation); // ct → отмена забега размотает награду (QA #37)
                return result;
            }
            finally
            {
                // Уход с узла ЛЮБЫМ путём (награда взята, поражение, отмена забега из меню) возвращает арену в мир
                // и фазу в None. Через finally, а не после награды: иначе отмена оставляла бы поле боя и фазу
                // Aftermath висеть — а на них завязаны guard'ы топбара и вход в тест-зону.
                _session?.RequestReset();
            }
        }
    }
}
