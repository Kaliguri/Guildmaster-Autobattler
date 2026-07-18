using System;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Persist-мир (план 12 Ф2): ставит отряд игрока (team 0) на тест-арену ВНЕ боя. Слушает
    /// <see cref="RunPartyReadyEvent"/> (публикует <c>GameFlow</c> после <c>BeginAct</c>), резолвит гильдию
    /// забега (<c>RunState.Guild</c>) в боевой ростер через <see cref="GuildRoster.Resolve"/> и ставит отряд
    /// замороженным (пауза) на сохранённые позиции. Врагов НЕ спавнит — их доспавнит вход в бой
    /// (<c>BattleFlow</c> → launch). Живёт в боевом скоупе как EntryPoint (скоуп persist — переживает бои).
    /// </summary>
    public sealed class WorldStageController : IStartable, IDisposable
    {
        private readonly ISubscriber<RunPartyReadyEvent> _partyReadySub;
        private readonly RunStateService  _runStates;
        private readonly IContentDatabase _content;
        private readonly EncounterLoader  _loader;
        private readonly CombatSimulation _sim;

        private IDisposable _subscription;

        public WorldStageController(ISubscriber<RunPartyReadyEvent> partyReadySub, RunStateService runStates,
                                    IContentDatabase content, EncounterLoader loader, CombatSimulation sim)
        {
            _partyReadySub = partyReadySub;
            _runStates     = runStates;
            _content       = content;
            _loader        = loader;
            _sim           = sim;
        }

        public void Start() => _subscription = _partyReadySub.Subscribe(OnPartyReady);

        public void Dispose() => _subscription?.Dispose();

        private void OnPartyReady(RunPartyReadyEvent _)
        {
            RunState run = _runStates.Current;
            if (run == null)
            {
                Debug.LogWarning("[WorldStageController] - RunPartyReady без активного забега (пропуск)");
                return;
            }

            RosterDeployer.Deploy(_loader, run, _content);
            _sim.FlushSpawns();   // материализовать очередь спавна (юниты присутствуют)
            _sim.SetPaused(true); // отряд стоит замороженным на тест-арене, пока не начался бой
        }
    }
}
