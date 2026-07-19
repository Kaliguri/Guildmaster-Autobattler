using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Рукопожатие между <see cref="BattleFlow"/> (RootScope, оркестрирует забег) и боевым скоупом
    /// (<c>CombatLifetimeScope</c>, живёт один бой). Живёт в RootScope, виден обоим: root кладёт «какой бой
    /// грузить» и ждёт исход; дочерний <c>BattleBootstrap</c> забирает запрос, запускает бой и репортит исход.
    /// Единственный шов, пересекающий границу скоупов — держит её тонкой и сетевой-ready (в Фазе 6 хост
    /// шлёт тот же запрос по сети, клиенты ждут исход через ту же абстракцию).
    /// Реализует read-side <see cref="IBattleClock"/> (Data) — так UI читает фазу/время боя без ссылки на Game.
    /// </summary>
    public interface IBattleSession : IBattleClock
    {
        /// <summary>root → child: поставить бой в очередь (перед загрузкой боевой сцены). Взводит ожидание исхода.</summary>
        void SetPending(BattlePresetData preset);

        /// <summary>child → session: забрать запрос (single-shot). false = запуск не из флоу (dev-панель вручную).</summary>
        bool TryConsumePending(out BattlePresetData preset);

        // ── Persist-мир: launch боя в ЖИВОМ боевом скоупе (сцена не перезагружается) ──
        // Заменяет связку SetPending+LoadBattleAsync: боевой скоуп живёт всю сессию, поэтому «запуск боя»
        // — это доспавн врагов + снятие паузы в уже готовом sim, а не создание нового скоупа/сцены.

        /// <summary>child → session: как запустить бой на месте (доспавн врагов + снять паузу). Привязывает боевой скоуп на старте.</summary>
        void BindLaunch(Action<BattlePresetData> launch);

        /// <summary>child → session: снять делегат launch (при выгрузке боевого скоупа, если она когда-то будет).</summary>
        void UnbindLaunch();

        /// <summary>
        /// root → child: запустить бой в живом скоупе (persist-мир). Взводит новое ожидание исхода.
        /// false = некому запускать (боевой скоуп ещё не поднят).
        /// </summary>
        bool RequestLaunch(BattlePresetData preset);

        /// <summary>child → session: как вернуть вне-боевое состояние (враги прочь, отряд к строю, пауза).</summary>
        void BindReset(Action reset);

        /// <summary>child → session: снять делегат сброса (при выгрузке боевого скоупа).</summary>
        void UnbindReset();

        /// <summary>
        /// root → child: после боя вернуть арену во вне-боевое состояние (persist-мир): убрать врагов,
        /// пере-поставить отряд из <c>RunState.Guild</c>, пауза. false = некому (скоуп не поднят).
        /// </summary>
        bool RequestReset();

        /// <summary>root: дождаться исхода текущего боя (следующий <see cref="ReportOutcome"/> после взвода).</summary>
        UniTask<BattleOutcome> WaitOutcomeAsync(CancellationToken ct);

        /// <summary>child → session: сообщить исход (из <c>BattleEndedEvent</c>).</summary>
        void ReportOutcome(BattleOutcome outcome);

        /// <summary>child → session: как перезапустить текущий бой на месте (без перезагрузки сцены; для ретрая).</summary>
        void BindRestart(Action restart);

        /// <summary>child → session: снять делегат рестарта (при выгрузке боевого скоупа).</summary>
        void UnbindRestart();

        /// <summary>root → child: перезапустить бой (ретрай). Взводит новое ожидание. false = некому (нет боя).</summary>
        bool RequestRestart();

        /// <summary>
        /// dev → перезапустить текущий бой НА МЕСТЕ, НЕ трогая ожидание исхода (в отличие от <see cref="RequestRestart"/>):
        /// текущий await флоу остаётся валиден и разрешится концом перезапущенного боя. Для dev-хоткея R.
        /// false = боя нет (некому перезапускать). Не для ретрай-логики флоу — только ручной dev-перезапуск.
        /// </summary>
        bool RestartInPlace();

        // ── Верхняя панель забега: часы + фаза + старт (план 12 Фаза 2) ──────────────
        // Read-side (Phase / ElapsedSeconds / RequestStart) — в IBattleClock (Data). Здесь только write-side,
        // которым боевой скоуп (DeploymentController) публикует состояние.

        /// <summary>child → session: сменить фазу (расстановка → бой). Панель опрашивает <see cref="IBattleClock.Phase"/>.</summary>
        void SetPhase(BattlePhase phase);

        /// <summary>child → session: привязать источник времени боя (<c>CombatSimulation.ElapsedSeconds</c>).</summary>
        void BindClock(Func<float> elapsedSeconds);

        /// <summary>child → session: снять часы при выгрузке боевого скоупа (сбрасывает фазу в <see cref="BattlePhase.None"/>).</summary>
        void UnbindClock();

        /// <summary>child → session: привязать «начать бой из расстановки» (кнопка «Начать» на панели).</summary>
        void BindStart(Action start);

        /// <summary>child → session: снять делегат старта при выгрузке боевого скоупа.</summary>
        void UnbindStart();
    }

    /// <summary>
    /// Соло-реализация <see cref="IBattleSession"/> (план 11 §2). Всё в главном потоке Unity — без блокировок.
    /// </summary>
    public sealed class BattleSession : IBattleSession
    {
        private BattlePresetData _pending;
        private bool             _hasPending;
        private Action           _restart;
        private Action<BattlePresetData> _launch;
        private Action           _reset;
        private UniTaskCompletionSource<BattleOutcome> _outcome;

        private Func<float> _clock;
        private Action      _start;

        public void SetPending(BattlePresetData preset)
        {
            _pending    = preset;
            _hasPending  = preset != null;
            ArmOutcome();
        }

        public bool TryConsumePending(out BattlePresetData preset)
        {
            preset      = _pending;
            bool had    = _hasPending;
            _pending    = null;
            _hasPending = false;
            return had;
        }

        public UniTask<BattleOutcome> WaitOutcomeAsync(CancellationToken ct)
        {
            UniTaskCompletionSource<BattleOutcome> tcs = _outcome ??= new UniTaskCompletionSource<BattleOutcome>();
            if (ct.CanBeCanceled)
                ct.Register(static state => ((UniTaskCompletionSource<BattleOutcome>)state).TrySetCanceled(), tcs);
            return tcs.Task;
        }

        public void ReportOutcome(BattleOutcome outcome) => _outcome?.TrySetResult(outcome);

        public void BindRestart(Action restart) => _restart = restart;

        public void UnbindRestart() => _restart = null;

        public void BindLaunch(Action<BattlePresetData> launch) => _launch = launch;

        public void UnbindLaunch() => _launch = null;

        public bool RequestLaunch(BattlePresetData preset)
        {
            if (_launch == null) return false;
            ArmOutcome();          // ждём исход до фактического запуска (ReportOutcome ловится даже мгновенный)
            _launch.Invoke(preset);
            return true;
        }

        public void BindReset(Action reset) => _reset = reset;

        public void UnbindReset() => _reset = null;

        public bool RequestReset()
        {
            if (_reset == null) return false;
            _reset.Invoke();       // БЕЗ ArmOutcome: бой уже кончился, ожидание не взводим
            return true;
        }

        public bool RequestRestart()
        {
            if (_restart == null) return false;
            ArmOutcome();       // ждём новый исход до фактического перезапуска
            _restart.Invoke();
            return true;
        }

        public bool RestartInPlace()
        {
            if (_restart == null) return false;
            _restart.Invoke();  // БЕЗ ArmOutcome: текущее ожидание флоу цело, разрешится концом нового боя
            return true;
        }

        // Взвести свежее ожидание исхода: гарантирует, что ReportOutcome, пришедший даже мгновенно
        // после запуска боя, будет пойман (TCS переживает до await).
        private void ArmOutcome() => _outcome = new UniTaskCompletionSource<BattleOutcome>();

        // ── Панель: часы + фаза + старт ──────────────────────────────────────────

        public BattlePhase Phase { get; private set; } = BattlePhase.None;

        public float ElapsedSeconds => _clock?.Invoke() ?? 0f;

        /// <inheritdoc/>
        public event Action PhaseChanged;

        public void SetPhase(BattlePhase phase)
        {
            if (Phase == phase) return;   // реальная смена → ровно одно событие (навигатор пересчитает ввод)
            Phase = phase;
            PhaseChanged?.Invoke();
        }

        public void BindClock(Func<float> elapsedSeconds) => _clock = elapsedSeconds;

        public void UnbindClock()
        {
            _clock = null;
            SetPhase(BattlePhase.None); // боевого скоупа больше нет — панель скрыта + событие (снос ручного SetContext, K8)
        }

        public void BindStart(Action start) => _start = start;

        public void UnbindStart() => _start = null;

        public void RequestStart() => _start?.Invoke();
    }
}
