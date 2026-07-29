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

        /// <summary>И как часто — вне боя: там состояние меняется редко, но знать его тоже надо.</summary>
        private const float IdleHeartbeatSeconds = 5f;

        private readonly CombatSimulation      _simulation;
        private readonly BattleTape            _tape;
        private readonly BattleTapePlayback    _playback;
        private readonly BattleTapeDispatcher  _dispatcher;
        private readonly IBattleClock          _clock;
        private readonly Presentation.CombatPresenter _presenter;

        private bool  _loggedStart;
        private bool  _loggedSimEnd;
        private bool  _warnedNoLead;
        private float _nextHeartbeat;

        public BattleTapeDiagnostics(
            CombatSimulation simulation,
            BattleTape tape,
            BattleTapePlayback playback,
            BattleTapeDispatcher dispatcher,
            IBattleClock clock,
            Presentation.CombatPresenter presenter)
        {
            _simulation = simulation;
            _tape       = tape;
            _playback   = playback;
            _dispatcher = dispatcher;
            _clock      = clock;
            _presenter  = presenter;

            _dispatcher.BattleEnded  += OnBattleEndedOnScreen;
            _simulation.OnBattleReset += OnBattleReset;
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

            // Показ догнал фронт в бою — значит запас потерян и телеграфы работать не могут.
            if (fighting && _playback.IsPlaying && _playback.TargetLead > 0
                && Lead() == 0 && _simulation.Outcome.IsOngoing)
            {
                if (!_warnedNoLead)
                {
                    _warnedNoLead = true;
                    Debug.LogWarning($"[BattleTape] - показ догнал симуляцию (запас 0 из {_playback.TargetLead}): " +
                                     "просчёт не успевает уходить вперёд, знания будущего нет");
                }
            }
            else _warnedNoLead = false;

            // Строку состояния печатаем и ВНЕ боя (реже): «в расстановке никого не видно» тоже надо
            // уметь разобрать — пуст кадр или не созданы виды.
            if (Time.unscaledTime < _nextHeartbeat) return;
            _nextHeartbeat = Time.unscaledTime + (fighting ? HeartbeatSeconds : IdleHeartbeatSeconds);

            int inFrame = _playback.TryGetFrame(out var frame, out var projectiles)
                ? frame.Count
                : -1;
            int projectilesInFrame = projectiles != null ? projectiles.Count : 0;

            string views = _presenter != null
                ? $"видов {_presenter.ViewCount}, паспортов {_presenter.IdentityCount}"
                : "презентера нет";

            Debug.Log($"[BattleTape] - фаза {_clock?.Phase}: показ {_playback.ViewTick} / фронт {_tape.FrontTick} " +
                      $"(окно {_tape.OldestTick}..{_tape.FrontTick}), запас {Lead()} из {_playback.TargetLead}; " +
                      $"в кадре юнитов {inFrame}, снарядов {projectilesInFrame}; {views}; " +
                      $"в симе юнитов {_simulation.Units.Count}; " +
                      $"событий отдано {_dispatcher.DeliveredCount} из {_tape.EventCount} " +
                      $"(курсор на тике {_dispatcher.ShownTick})");
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
