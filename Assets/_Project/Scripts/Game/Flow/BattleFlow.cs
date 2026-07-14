using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Services;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Узел забега «бой» (план 11 §3.2, §4 A2): оборачивает готовую боевую клетку в <see cref="IEventFlow"/>.
    /// Prep (расстановка/loadout — <c>DeploymentController</c> в боевом скоупе) и Combat (<c>CombatLoopService</c>)
    /// уже готовы; здесь добавлен слой <b>Outcome</b>: маппинг <see cref="BattleOutcome"/> в <see cref="EventResult"/>
    /// с ретраями поражения (вики «7» §6). Бой грузится через <see cref="IBattleSession"/> (мост в боевой скоуп),
    /// а не из dev-панели. Данные узла — <see cref="BattlePresetData"/> (враги + ростер + режим расстановки).
    /// </summary>
    public sealed class BattleFlow : IEventFlow
    {
        /// <summary>Число ретраев поражения по умолчанию (вики «7» §6). TODO: вынести в GameConfig под баланс.</summary>
        public const int DefaultMaxRetries = 2;

        private readonly BattlePresetData _preset;
        private readonly ISceneLoader     _scenes;
        private readonly IBattleSession   _session;
        private readonly int              _maxRetries;

        public BattleFlow(BattlePresetData preset, ISceneLoader scenes, IBattleSession session,
                          int maxRetries = DefaultMaxRetries)
        {
            _preset     = preset;
            _scenes     = scenes;
            _session    = session;
            _maxRetries = Mathf.Max(0, maxRetries);
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            if (_preset == null)
            {
                Debug.LogWarning("[BattleFlow] - preset == null → Aborted");
                return EventResult.Aborted;
            }

            // Ставим бой в очередь ДО загрузки сцены: боевой bootstrap заберёт запрос на своём старте.
            _session.SetPending(_preset);
            await _scenes.LoadBattleAsync();

            try
            {
                BattleOutcome outcome = await _session.WaitOutcomeAsync(CancellationToken.None);

                int retries = 0;
                while (!IsPlayerWin(outcome) && retries < _maxRetries)
                {
                    retries++;
                    Debug.Log($"[BattleFlow] - поражение ({outcome}), ретрай {retries}/{_maxRetries}");
                    if (!_session.RequestRestart())
                    {
                        Debug.LogWarning("[BattleFlow] - некому перезапустить бой (нет боевого скоупа) → Defeated");
                        break;
                    }
                    outcome = await _session.WaitOutcomeAsync(CancellationToken.None);
                }

                bool won = IsPlayerWin(outcome);
                Debug.Log($"[BattleFlow] - бой '{_preset.Id}' завершён: {outcome} → {(won ? "Completed" : "Defeated")}");
                return won ? EventResult.Completed : EventResult.Defeated;
            }
            finally
            {
                await _scenes.UnloadBattleAsync();
            }
        }

        // Player-сторона всегда team 0 (EncounterLoader спавнит ростер team:0, врагов team:1) → TeamAWins = победа.
        private static bool IsPlayerWin(BattleOutcome outcome) => outcome == BattleOutcome.TeamAWins;
    }
}
