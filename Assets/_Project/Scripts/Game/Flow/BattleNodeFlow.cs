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
    /// <para>Пока игрок на узле, поле боя остаётся как есть — с трупами и последним кадром (фаза
    /// <see cref="Data.Definitions.BattlePhase.Interlude"/> держит мир видимым). Возврат мира зовёт петля акта
    /// (<c>ActRunner</c> → <see cref="IRunBeatStage"/>), когда узел засчитан. Раньше чистку делал
    /// <see cref="BattleFlow"/> прямо на исходе — досмотр добивания шёл по пустому полю, да ещё за непрозрачным
    /// задником UI (фаза падала в None).</para>
    /// </summary>
    public sealed class BattleNodeFlow : IEventFlow
    {
        private readonly IEventFlow         _battle;
        private readonly RewardTier         _tier;
        private readonly IRewardPresenter   _reward;
        private readonly RunStateService    _runStates;
        private readonly IContinuePresenter _continue;
        private readonly int                _rewardCount;
        private readonly float              _postWinDelaySeconds;
        private readonly IBattleSession     _session;
        private readonly Func<RunContext, UniTask<EventResult>> _awaitReplayOutcome;

        /// <param name="session">
        /// Источник сигнала «узел переигран» (dev-R после конца боя). null = откат недоступен (тесты, dev-бой).
        /// </param>
        /// <param name="awaitReplayOutcome">Чем дождаться исхода переигранного боя; парой к <paramref name="session"/>.</param>
        public BattleNodeFlow(IEventFlow battle, RewardTier tier, IRewardPresenter reward, RunStateService runStates,
                              IContinuePresenter continuePresenter,
                              int rewardCount = 1, float postWinDelaySeconds = 2f,
                              IBattleSession session = null,
                              Func<RunContext, UniTask<EventResult>> awaitReplayOutcome = null)
        {
            _battle              = battle;
            _tier                = tier;
            _reward              = reward;
            _runStates           = runStates;
            _continue            = continuePresenter;
            _rewardCount         = rewardCount < 1 ? 1 : rewardCount;
            _postWinDelaySeconds = postWinDelaySeconds < 0f ? 0f : postWinDelaySeconds;
            _session             = session;
            _awaitReplayOutcome  = awaitReplayOutcome;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            EventResult result = await _battle.Run(ctx);

            // Досмотр и мост к награде можно отмотать назад: dev-R откатывает узел к бою, и тогда всё,
            // что мы успели показать поверх победы, снимается, а мы снова ждём приговор.
            while (true)
            {
                if (result.Outcome != EventOutcome.Completed) return result;
                if (!await WaitBeatOrReplay(ctx)) break;
                if (_awaitReplayOutcome == null) break;

                result = await _awaitReplayOutcome(ctx);
            }

            // +золото за победу (B1). Считаем узел взятым, когда игрок ушёл с досмотра: до этого его ещё
            // можно откатить, и начисленное пришлось бы отбирать назад.
            _runStates.AwardBattleReward();

            for (int i = 0; i < _rewardCount; i++)        // элитка = 2 выбора подряд (B5)
                await _reward.PresentAsync(_tier, ctx.Cancellation); // ct → отмена забега размотает награду (QA #37)
            return result;
        }

        /// <summary>
        /// Досмотр добивания и мост «Продолжить» — но в гонке с откатом узла. true = игрок отмотал бой назад.
        /// </summary>
        private async UniTask<bool> WaitBeatOrReplay(RunContext ctx)
        {
            var replay = new UniTaskCompletionSource();
            Action onReplay = () => replay.TrySetResult();
            if (_session != null) _session.ReplayRequested += onReplay;

            try
            {
                // Досмотр добивания (п.4): пауза перед мостом к награде. DeltaType по умолчанию — пауза забега
                // (timeScale=0) замораживает таймер, а ct («В меню» из паузы) размотает ожидание (QA #37).
                if (_postWinDelaySeconds > 0f)
                {
                    int first = await UniTask.WhenAny(
                        UniTask.Delay(TimeSpan.FromSeconds(_postWinDelaySeconds), cancellationToken: ctx.Cancellation),
                        replay.Task);
                    if (first == 1) return true;
                }

                // Мост к награде: не переносим в неё автоматом — игрок жмёт сам (п.4). Подпись — общая «Продолжить»
                // (реш. Макса 2026-07-26): игрок и так видит, что дальше, а лишнее слово только дробит ритм.
                int winner = await UniTask.WhenAny(
                    _continue.WaitForContinueAsync(ct: ctx.Cancellation),
                    replay.Task);
                return winner == 1;
            }
            finally
            {
                if (_session != null) _session.ReplayRequested -= onReplay;
            }
        }
    }
}
