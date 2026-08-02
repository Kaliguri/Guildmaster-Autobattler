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
    /// Собирает бой, ради которого родился его скоуп: ставит отряд из <c>RunState.Guild</c>, доспавнивает
    /// врагов пресета и уводит в фазу расстановки. Он же репортит исход во флоу.
    /// </summary>
    /// <remarks>
    /// <b>Границы боя здесь больше нет</b> — «когда бой начинается и кончается» знает
    /// <see cref="BattleHost"/> в мире, а этот энтрипоинт знает только «как бой собрать». Пока обе роли
    /// жили в одном классе (<c>BattleBootstrap</c>), скоуп был вечным, и каждый переход приходилось
    /// доводить руками: чистить арену сбросом, пересевать генератор, пере-ставить отряд. Теперь переход —
    /// это рождение и смерть скоупа, а всё перечисленное случается само.
    /// <para><b>Бой стартует на паузе</b>: сначала расстановка (враги видны, отряд двигается), и только
    /// «Начать» из панели снимает паузу. Пресет с <c>DeploymentMode.Fixed</c> проходит ту же дорогу — за
    /// него кнопку нажмёт <c>DeploymentController</c>.</para>
    /// </remarks>
    public sealed class BattleStartup : IStartable, IDisposable
    {
        private readonly IBattleSession                _session;
        private readonly BattleScopeParams             _params;
        private readonly EncounterLoader               _loader;
        private readonly CombatSimulation              _sim;
        // Забег бой только ЧИТАЕТ, и то не всегда: дев-арена, Ристалище и PvP собираются там, где его
        // нет вовсе. Держатель состояния сюда не приходит намеренно — иначе боевой скоуп нельзя было бы
        // поднять без владельца сейва (канон: бой собирается без RunState, без сети, без карты).
        private readonly IRunStateView                 _runStates;
        private readonly IContentDatabase              _content;
        private readonly ISubscriber<BattleEndedEvent> _endedSub;
        private readonly Services.TimeScaleService     _time;

        private IDisposable _endedSubscription;

        public BattleStartup(IBattleSession session, BattleScopeParams parameters, EncounterLoader loader,
                             CombatSimulation sim, IRunStateView runStates, IContentDatabase content,
                             ISubscriber<BattleEndedEvent> endedSub, Services.TimeScaleService time)
        {
            _session   = session;
            _params    = parameters;
            _loader    = loader;
            _sim       = sim;
            _runStates = runStates;
            _content   = content;
            _endedSub  = endedSub;
            _time      = time;
        }

        public void Start()
        {
            _endedSubscription = _endedSub.Subscribe(OnBattleEnded);

            BattlePresetData preset = _params.Preset;
            if (preset == null)
            {
                // Скоуп подняли без пресета — так бывает только в одиночной dev-арене, где бой ставят
                // руками из консоли. Систем это не касается: они уже собраны и ждут состав.
                Debug.Log("[BattleStartup] - боевой скоуп без пресета: арена пуста, бой ставится вручную");
                return;
            }

            RosterDeployer.Deploy(_loader, _runStates?.Current, _content);
            if (preset.Encounter != null) _loader.SpawnEnemies(preset.Encounter);

            _sim.FlushSpawns();
            _sim.SetPaused(true);   // пауза — это фаза расстановки, а не остановленный бой
            _time.SetPaused(false); // а пауза ИГРОКА в новый бой не переезжает

            _loader.RequestDeployment(preset);
        }

        public void Dispose() => _endedSubscription?.Dispose();

        // Бой кончился — арену НЕ трогаем: поле с трупами живёт, пока игрок на узле (досмотр добивания,
        // мост к награде, выбор награды). Уберёт его смерть скоупа, когда петля акта уйдёт с узла.
        private void OnBattleEnded(BattleEndedEvent e)
        {
            _session.SetPhase(BattlePhase.Interlude);
            _session.ReportOutcome(e.Outcome);
        }
    }
}
