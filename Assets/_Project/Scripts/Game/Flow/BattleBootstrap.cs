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
        private readonly Services.TimeScaleService      _time;

        private IDisposable      _endedSubscription;
        private BattlePresetData _lastPreset;
        private bool             _arenaStaged;   // на арене стоял бой (враги/трупы) → возврат мира имеет смысл

        public BattleBootstrap(IBattleSession session, EncounterLoader loader, CombatSimulation sim,
                               RunStateService runStates, IContentDatabase content,
                               ISubscriber<BattleEndedEvent> endedSub, Services.TimeScaleService time)
        {
            _time = time;
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

        // Запуск боя: доспавн врагов к СТОЯЩЕМУ отряду, затем ОБЯЗАТЕЛЬНАЯ фаза расстановки (пауза, враги
        // видны, отряд двигается) — «Начать» из панели снимет паузу и запустит бой. Отряд обычно уже на
        // арене (WorldStageController поставил на старте забега); если нет (dev-одиночный) — ставим сейчас.
        private void LaunchBattle(BattlePresetData preset)
        {
            if (preset == null || preset.Encounter == null)
            {
                Debug.LogWarning("[BattleBootstrap] - LaunchBattle: пустой пресет/энкаунтер");
                return;
            }

            _lastPreset  = preset;
            _arenaStaged = true;                   // с этого момента на арене есть что убирать
            if (!HasLivingParty()) DeployParty();  // отряд не стоял → поставить из RunState.Guild

            _loader.SpawnEnemies(preset.Encounter);
            _sim.FlushSpawns();
            _sim.SetPaused(true);               // пауза — фаза расстановки, а не сразу бой
            _time.SetPaused(false);             // а пауза игрока в новый узел не переезжает (см. ResetToWorld)
            _loader.RequestDeployment(preset);  // DeploymentController: показать врагов, drag, кнопка «Начать»
        }

        // Возврат мира: убрать врагов и трупы, поставить отряд из RunState (полный HP, сохранённое построение),
        // пауза. PlaceParty внутри DeployParty делает ResetBattle — это чистит и врагов, и старый отряд.
        // ФАЗУ НЕ ТРОГАЕТ: возврат случается и в передышке между узлами (там фаза Interlude — мир на экране),
        // и при выходе из забега (там None). Кто зовёт — тот и знает, где игрок оказался.
        // Боя не было (ивент, магазин, сундук, привал) — убирать нечего, и трогать арену НЕЛЬЗЯ: пере-расстановка
        // отряда на ровном месте читается игроком как «открылся бой» (QA #43a, реш. Макса 2026-07-26). Признак
        // ведём здесь, а не по типу узла: «?»-узел роллится в бой уже на входе, петля об этом не знает.
        // TODO (замысел Макса 2026-07-26): это должна быть АНИМАЦИЯ — трупы тают, отряд возвращается на места;
        // «К построению» проигрывает её ×3. Сейчас возврат мгновенный, шов под скорость появится вместе с ней.
        private void ResetToWorld()
        {
            if (!_arenaStaged) return;
            _arenaStaged = false;

            DeployParty();
            _sim.FlushSpawns();
            _sim.SetPaused(true);        // «сим заморожен сценарием»: отряд стоит в построении

            // А вот пауза ИГРОКА (Time.timeScale) узел не переживает. Боевой скоуп в persist-мире не
            // разрушается между узлами, поэтому её некому было снять: Dispose() возвращает timeScale к 1
            // только при выгрузке боя, которой больше нет. Игрок, поставивший паузу перед последним ударом,
            // оставался с замершим миром на всю передышку (аудит 2026-07-26, T-4/RL-1).
            _time.SetPaused(false);
        }

        // Ретрай боя (пул перезапусков акта + dev-R): пере-поставить отряд и врагов, снова в фазу расстановки.
        private void RestartBattle()
        {
            _arenaStaged = true;
            DeployParty();
            if (_lastPreset?.Encounter != null) _loader.SpawnEnemies(_lastPreset.Encounter);
            _sim.FlushSpawns();
            _sim.SetPaused(true);
            _time.SetPaused(false);      // рестарт снимает паузу игрока: иначе новый бой стартует замороженным
            if (_lastPreset != null) _loader.RequestDeployment(_lastPreset);
        }

        private void DeployParty() => RosterDeployer.Deploy(_loader, _runStates.Current, _content);

        private bool HasLivingParty()
        {
            System.Collections.Generic.IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
                if (units[i].Team == 0 && !units[i].IsDead) return true;
            return false;
        }

        // Бой кончился — арену НЕ трогаем: поле с трупами живёт, пока игрок на узле (досмотр добивания, мост к
        // награде, выбор награды). Фаза Interlude держит мир видимым: на None UI-слой кладёт поверх непрозрачный
        // задник, и раньше он падал прямо на кадр победы. Чистку зовёт петля акта, когда узел пройден.
        private void OnBattleEnded(BattleEndedEvent e)
        {
            _session.SetPhase(BattlePhase.Interlude);
            _session.ReportOutcome(e.Outcome);
        }
    }
}
