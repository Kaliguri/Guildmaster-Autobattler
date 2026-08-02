using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Persist-мир: ставит отряд игрока (team 0) на арену ВНЕ боя. Слушает <see cref="RunPartyReadyEvent"/>
    /// (публикует <c>GameFlow</c> после <c>BeginAct</c>), резолвит гильдию забега (<c>RunState.Guild</c>) в
    /// ростер через <see cref="GuildRoster.Resolve"/> и кладёт тела в <see cref="WorldBodyStage"/>.
    /// </summary>
    /// <remarks>
    /// <b>Врагов не ставит и не должен:</b> вне боя их нет вовсе — их приносит вход в бой вместе с
    /// боевым скоупом.
    /// <para><b>Раньше отряд ставился спавном в боевую симуляцию</b>, которую тут же морозили паузой:
    /// вечная симуляция была единственным, что умело держать тела. Из-за этого у боя не было границы
    /// в жизненном цикле — её объявляли вручную сбросами. Теперь тела держит мир, и пауза здесь ни при
    /// чём: стоящему телу нечего тикать (решение 02.08.2026, журнал «The Simulation Belongs To The
    /// Battle»).</para>
    /// </remarks>
    public sealed class WorldStageController : IPartyStage, IStartable, IDisposable
    {
        private readonly ISubscriber<RunPartyReadyEvent> _partyReadySub;
        // Мир переживает сеансы, поэтому забег он только ЧИТАЕТ — и через роутер, а не прямой ссылкой
        // на держателя из скоупа сессии (тот умрёт вместе с ней, а мир останется).
        private readonly IRunStateView    _runStates;
        private readonly IContentDatabase _content;
        private readonly WorldBodyBuilder _bodies;
        private readonly WorldBodyStage   _stage;

        private IDisposable _subscription;

        public WorldStageController(ISubscriber<RunPartyReadyEvent> partyReadySub, IRunStateView runStates,
                                    IContentDatabase content, WorldBodyBuilder bodies, WorldBodyStage stage)
        {
            _partyReadySub = partyReadySub;
            _runStates     = runStates;
            _content       = content;
            _bodies        = bodies;
            _stage         = stage;
        }

        public void Start() => _subscription = _partyReadySub.Subscribe(OnPartyReady);

        public void Dispose() => _subscription?.Dispose();

        private void OnPartyReady(RunPartyReadyEvent _) => PlaceParty();

        /// <summary>
        /// Поставить отряд забега на арену заново — из durable-состояния гильдии (полный HP, сохранённое
        /// построение). Зовётся на старте акта и после каждого боя: когда боевой скоуп уходит, арену
        /// снова показывает мир, и показать ему нужно тот же отряд.
        /// </summary>
        /// <remarks>
        /// Забега нет — арена очищается. Это не отказ, а честный ответ: в главном меню отряду стоять
        /// негде и не с чего.
        /// </remarks>
        public void PlaceParty()
        {
            RunState run = _runStates.Current;
            if (run == null)
            {
                _stage.Clear();
                return;
            }

            PlayerSlot[] roster = GuildRoster.Resolve(run, _content);
            ItemData[]   party  = GuildRoster.ResolveItems(run.PartyItemIds, _content);

            List<WorldBody> bodies = _bodies.Build(roster, party);
            _stage.Set(bodies);
        }
    }
}
