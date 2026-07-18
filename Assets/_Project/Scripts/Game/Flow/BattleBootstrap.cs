using System;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using Guildmaster.Presentation;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Дочерний EntryPoint боевого скоупа (persist-мир, план 12 Ф2): оркеструет боевой цикл в ЖИВОМ скоупе,
    /// который переживает бои. Держит три перехода через <see cref="IBattleSession"/>:
    /// <list type="bullet">
    /// <item><b>launch</b> — доспавн врагов к уже стоящему отряду + снятие паузы (бой пошёл);</item>
    /// <item><b>reset</b> — после боя вернуть вне-боевое состояние (враги прочь, отряд к строю, пауза);</item>
    /// <item><b>restart</b> — ретрай: пере-поставить отряд + врагов и снять паузу.</item>
    /// </list>
    /// Отряд всегда материализуется из <c>RunState.Guild</c> через <see cref="RosterDeployer"/> (durable-построение,
    /// полный HP). Исход боя репортится во флоу по <c>BattleEndedEvent</c>.
    /// </summary>
    public sealed class BattleBootstrap : IStartable, IDisposable
    {
        private readonly IBattleSession                _session;
        private readonly EncounterLoader               _loader;
        private readonly CombatSimulation              _sim;
        private readonly RunStateService               _runStates;
        private readonly IContentDatabase              _content;
        private readonly ISubscriber<BattleEndedEvent> _endedSub;

        private IDisposable      _endedSubscription;
        private BattlePresetData _lastPreset;

        public BattleBootstrap(IBattleSession session, EncounterLoader loader, CombatSimulation sim,
                               RunStateService runStates, IContentDatabase content,
                               ISubscriber<BattleEndedEvent> endedSub)
        {
            _session   = session;
            _loader    = loader;
            _sim       = sim;
            _runStates = runStates;
            _content   = content;
            _endedSub  = endedSub;
        }

        public void Start()
        {
            // Исход боя → флоу. Подписка живёт весь боевой скоуп (ретраи переиспользуют её).
            _endedSubscription = _endedSub.Subscribe(OnBattleEnded);

            // Persist-мир: боевой скоуп живёт всю сессию, переходы — команды в живой sim (не создание сцены).
            _session.BindLaunch(LaunchBattle);
            _session.BindReset(ResetToWorld);
            _session.BindRestart(RestartBattle);

            // Legacy-совместимость: бой, положенный через SetPending (старый путь до persist), запустить.
            if (_session.TryConsumePending(out BattlePresetData pending) && pending != null)
                LaunchBattle(pending);
        }

        public void Dispose()
        {
            _session.UnbindLaunch();
            _session.UnbindReset();
            _session.UnbindRestart();
            _endedSubscription?.Dispose();
        }

        // ── Переходы боевого цикла ───────────────────────────────────────────

        // Запуск боя: доспавн врагов к СТОЯЩЕМУ отряду + снятие паузы. Отряд обычно уже на арене
        // (WorldStageController поставил на старте забега); если нет (dev-одиночный бой) — ставим сейчас.
        private void LaunchBattle(BattlePresetData preset)
        {
            if (preset == null || preset.Encounter == null)
            {
                Debug.LogWarning("[BattleBootstrap] - LaunchBattle: пустой пресет/энкаунтер");
                return;
            }

            _lastPreset = preset;
            if (!HasLivingParty()) DeployParty();  // отряд не стоял → поставить из RunState.Guild

            _loader.SpawnEnemies(preset.Encounter);
            _sim.FlushSpawns();
            _sim.SetPaused(false);
        }

        // После боя: вернуть вне-боевое состояние. PlaceParty внутри DeployParty делает ResetBattle —
        // это убирает и врагов, и старый отряд; отряд встаёт заново из RunState (полный HP), пауза.
        private void ResetToWorld()
        {
            DeployParty();
            _sim.FlushSpawns();
            _sim.SetPaused(true);
        }

        // Ретрай боя на месте (пул перезапусков акта + dev-R): пере-поставить отряд и врагов, снять паузу.
        private void RestartBattle()
        {
            DeployParty();
            if (_lastPreset?.Encounter != null) _loader.SpawnEnemies(_lastPreset.Encounter);
            _sim.FlushSpawns();
            _sim.SetPaused(false);
        }

        private void DeployParty() => RosterDeployer.Deploy(_loader, _runStates.Current, _content);

        private bool HasLivingParty()
        {
            System.Collections.Generic.IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
                if (units[i].Team == 0 && !units[i].IsDead) return true;
            return false;
        }

        private void OnBattleEnded(BattleEndedEvent e) => _session.ReportOutcome(e.Outcome);
    }
}
