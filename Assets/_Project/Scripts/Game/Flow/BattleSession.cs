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
    /// </summary>
    public interface IBattleSession
    {
        /// <summary>root → child: поставить бой в очередь (перед загрузкой боевой сцены). Взводит ожидание исхода.</summary>
        void SetPending(BattlePresetData preset);

        /// <summary>child → session: забрать запрос (single-shot). false = запуск не из флоу (dev-панель вручную).</summary>
        bool TryConsumePending(out BattlePresetData preset);

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
    }

    /// <summary>
    /// Соло-реализация <see cref="IBattleSession"/> (план 11 §2). Всё в главном потоке Unity — без блокировок.
    /// </summary>
    public sealed class BattleSession : IBattleSession
    {
        private BattlePresetData _pending;
        private bool             _hasPending;
        private Action           _restart;
        private UniTaskCompletionSource<BattleOutcome> _outcome;

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

        public bool RequestRestart()
        {
            if (_restart == null) return false;
            ArmOutcome();       // ждём новый исход до фактического перезапуска
            _restart.Invoke();
            return true;
        }

        // Взвести свежее ожидание исхода: гарантирует, что ReportOutcome, пришедший даже мгновенно
        // после запуска боя, будет пойман (TCS переживает до await).
        private void ArmOutcome() => _outcome = new UniTaskCompletionSource<BattleOutcome>();
    }
}
