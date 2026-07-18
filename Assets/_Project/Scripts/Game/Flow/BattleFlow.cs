using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Core.Players;
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
        private readonly BattlePresetData _preset;
        private readonly ISceneLoader     _scenes;
        private readonly IBattleSession   _session;
        private readonly ILocalPlayer     _localPlayer;
        private readonly Func<bool>       _tryConsumeRestart;

        /// <param name="tryConsumeRestart">
        /// Спросить пул перезапусков акта (реш. №65): вернуть true и списать одну попытку, если можно переиграть.
        /// null = без перезапусков (legacy dev-бой). Заменяет прежний фикс-счётчик на бой (техдолг).
        /// </param>
        public BattleFlow(BattlePresetData preset, ISceneLoader scenes, IBattleSession session,
                          ILocalPlayer localPlayer, Func<bool> tryConsumeRestart = null)
        {
            _preset            = preset;
            _scenes            = scenes;
            _session           = session;
            _localPlayer       = localPlayer;
            _tryConsumeRestart = tryConsumeRestart;
        }

        public async UniTask<EventResult> Run(RunContext ctx)
        {
            if (_preset == null)
            {
                Debug.LogWarning("[BattleFlow] - preset == null → Aborted");
                return EventResult.Aborted;
            }

            // Persist-мир: боевой скоуп уже жив (BattleScene загружена на буте и не выгружается). «Запуск боя»
            // = команда в живой sim (доспавн врагов + снятие паузы), а не загрузка сцены. RequestLaunch взводит
            // ожидание исхода сам. false = скоуп ещё не поднят (сбой бута) → Aborted.
            if (!_session.RequestLaunch(_preset))
            {
                Debug.LogWarning("[BattleFlow] - некому запустить бой (боевой скоуп не поднят) → Aborted");
                return EventResult.Aborted;
            }

            BattleOutcome outcome = await _session.WaitOutcomeAsync(CancellationToken.None);

            // Поражение → тратим перезапуск из пула акта (реш. №65) и переигрываем ТОТ ЖЕ бой.
            while (!Won(outcome) && _tryConsumeRestart != null && _tryConsumeRestart())
            {
                Debug.Log("[BattleFlow] - поражение, трачу перезапуск акта");
                if (!_session.RequestRestart())
                {
                    Debug.LogWarning("[BattleFlow] - некому перезапустить бой (нет боевого скоупа) → Defeated");
                    break;
                }
                outcome = await _session.WaitOutcomeAsync(CancellationToken.None);
            }

            bool won = Won(outcome);
            Debug.Log($"[BattleFlow] - бой '{_preset.Id}' завершён: {outcome} → {(won ? "Completed" : "Defeated")}");
            return won ? EventResult.Completed : EventResult.Defeated;
        }

        // Победа = победила МОЯ команда. Ничья победой не считается → для игрока это поражение (ретрай).
        private bool Won(BattleOutcome outcome) => outcome.IsWinFor(_localPlayer.Team);
    }
}
