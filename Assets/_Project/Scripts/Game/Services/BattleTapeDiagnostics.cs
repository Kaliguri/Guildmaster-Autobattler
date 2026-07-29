using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Dev-диагностика ленты боя: говорит вслух, что показ делает прямо сейчас. Нужна потому, что
    /// «сим впереди, показ с лагом» ломается тихо: картинка либо стоит, либо обрывается, а по экрану не
    /// понять, кто виноват — просчёт, запас или доставка событий.
    /// <para>Пишет три вещи: старт показа, конец боя ОБА раза (в симуляции и на экране — разница между
    /// ними и есть лаг), и раз в <see cref="HeartbeatSeconds"/> строку состояния во время боя. Тише не
    /// имеет смысла, громче — спам.</para>
    /// </summary>
    public sealed class BattleTapeDiagnostics : ITickable
    {
        /// <summary>Как часто печатать строку состояния во время боя, сек.</summary>
        private const float HeartbeatSeconds = 2f;

        private readonly CombatSimulation      _simulation;
        private readonly BattleTape            _tape;
        private readonly BattleTapePlayback    _playback;
        private readonly BattleTapeDispatcher  _dispatcher;
        private readonly IBattleClock          _clock;

        private bool  _loggedStart;
        private bool  _loggedSimEnd;
        private bool  _warnedNoLead;
        private float _nextHeartbeat;

        public BattleTapeDiagnostics(
            CombatSimulation simulation,
            BattleTape tape,
            BattleTapePlayback playback,
            BattleTapeDispatcher dispatcher,
            IBattleClock clock)
        {
            _simulation = simulation;
            _tape       = tape;
            _playback   = playback;
            _dispatcher = dispatcher;
            _clock      = clock;

            _dispatcher.BattleEnded += OnBattleEndedOnScreen;
            _dispatcher.BattleReset += OnBattleReset;
        }

        public void Tick()
        {
            bool fighting = _clock != null && _clock.Phase == BattlePhase.Fighting;

            if (!_loggedStart && _playback.IsPlaying)
            {
                _loggedStart = true;
                Debug.Log($"[BattleTape] - показ начался: тик {_playback.ViewTick}, фронт {_tape.FrontTick}, " +
                          $"требуемый запас {_playback.TargetLead}");
            }

            // Конец боя в СИМУЛЯЦИИ — не то же, что конец на экране: между ними целое окно опережения.
            if (!_loggedSimEnd && !_simulation.Outcome.IsOngoing)
            {
                _loggedSimEnd = true;
                Debug.Log($"[BattleTape] - бой досчитан симом на тике {_simulation.CurrentTick} " +
                          $"({_simulation.Outcome}); показ на тике {_playback.ViewTick} — " +
                          $"игроку осталось смотреть {Lead()} тиков ({Lead() / (float)SimConstants.TickRate:F1} с)");
            }

            if (!fighting) return;

            // Показ догнал фронт в бою — значит запас потерян и телеграфы работать не могут.
            if (_playback.IsPlaying && _playback.TargetLead > 0 && Lead() == 0 && _simulation.Outcome.IsOngoing)
            {
                if (!_warnedNoLead)
                {
                    _warnedNoLead = true;
                    Debug.LogWarning($"[BattleTape] - показ догнал симуляцию (запас 0 из {_playback.TargetLead}): " +
                                     "просчёт не успевает уходить вперёд, знания будущего нет");
                }
            }
            else _warnedNoLead = false;

            if (Time.unscaledTime < _nextHeartbeat) return;
            _nextHeartbeat = Time.unscaledTime + HeartbeatSeconds;

            Debug.Log($"[BattleTape] - фаза {_clock.Phase}: показ {_playback.ViewTick} / фронт {_tape.FrontTick}, " +
                      $"запас {Lead()} тиков (цель {_playback.TargetLead}), " +
                      $"событий отдано до тика {_dispatcher.ShownTick} из {_tape.EventCount} записанных");
        }

        private int Lead() => _playback.Lead;

        private void OnBattleEndedOnScreen(BattleOutcome outcome) =>
            Debug.Log($"[BattleTape] - бой закончился НА ЭКРАНЕ: тик показа {_playback.ViewTick} ({outcome}). " +
                      "Именно этот момент и есть конец боя для игрока, наград и итогов");

        private void OnBattleReset()
        {
            _loggedStart   = false;
            _loggedSimEnd  = false;
            _warnedNoLead  = false;
            Debug.Log("[BattleTape] - рестарт: лента очищена, показ отмотан в начало");
        }
    }
}
