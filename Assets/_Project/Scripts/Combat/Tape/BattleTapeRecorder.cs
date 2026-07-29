using System;
using Guildmaster.Core.Simulation;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Пишет ленту: подписывается на события симуляции и снимает состояние каждого досчитанного тика.
    /// Отдельный класс, потому что <see cref="BattleTape"/> — это данные, а проводка sim→лента —
    /// поведение; смешивать их значит запретить тесту писать в ленту руками.
    /// <para>Владелец ритма — тот, кто тикает сим (<c>CombatLoopService</c>): он зовёт
    /// <see cref="CaptureTick"/> сразу после <c>Tick</c>. У симуляции события «тик закончился» нет и
    /// заводить его незачем — сим не должен знать, что его записывают.</para>
    /// </summary>
    public sealed class BattleTapeRecorder : IDisposable
    {
        /// <summary>
        /// Глубина окна снимков: опережение показа плюс запас на подводки (камера смотрит чуть дальше
        /// момента показа). Опережение — 10 секунд по решению Макса 2026-07-29, запас — 2 секунды.
        /// </summary>
        public const int DefaultWindowTicks = (10 + 2) * SimConstants.TickRate;

        private readonly CombatSimulation _simulation;
        private readonly BattleTape       _tape;

        public BattleTapeRecorder(CombatSimulation simulation, BattleTape tape)
        {
            _simulation = simulation;
            _tape       = tape;

            _simulation.OnUnitSpawned       += HandleUnitSpawned;
            _simulation.OnUnitDied          += HandleUnitDied;
            _simulation.OnDamageDealt       += HandleDamageDealt;
            _simulation.OnHealed            += HandleHealed;
            _simulation.OnAttackEvaded      += HandleAttackEvaded;
            _simulation.OnAttackStarted     += HandleAttackStarted;
            _simulation.OnAttackInterrupted += HandleAttackInterrupted;
            _simulation.OnAreaHit           += HandleAreaHit;
            _simulation.OnBattleEnded       += HandleBattleEnded;
            _simulation.OnBattleReset       += HandleBattleReset;
        }

        public void Dispose()
        {
            _simulation.OnUnitSpawned       -= HandleUnitSpawned;
            _simulation.OnUnitDied          -= HandleUnitDied;
            _simulation.OnDamageDealt       -= HandleDamageDealt;
            _simulation.OnHealed            -= HandleHealed;
            _simulation.OnAttackEvaded      -= HandleAttackEvaded;
            _simulation.OnAttackStarted     -= HandleAttackStarted;
            _simulation.OnAttackInterrupted -= HandleAttackInterrupted;
            _simulation.OnAreaHit           -= HandleAreaHit;
            _simulation.OnBattleEnded       -= HandleBattleEnded;
            _simulation.OnBattleReset       -= HandleBattleReset;
        }

        /// <summary>
        /// Снять кадр только что досчитанного тика. Тик берётся у симуляции: она уже увеличила
        /// счётчик, поэтому записываем предыдущий — тот, чьё состояние сейчас на юнитах.
        /// </summary>
        public void CaptureTick()
        {
            int tick = _simulation.CurrentTick - 1;
            if (tick < 0) return;

            _tape.CaptureTick(tick, _simulation.Units);
        }

        private int Tick => _simulation.CurrentTick;

        private void HandleUnitSpawned(RuntimeUnit unit) =>
            _tape.Record(new TapeEvent(TapeEventKind.UnitSpawned, Tick, unit.Id));

        private void HandleUnitDied(RuntimeUnit unit) =>
            _tape.Record(new TapeEvent(TapeEventKind.UnitDied, Tick, unit.Id));

        private void HandleDamageDealt(RuntimeUnit source, RuntimeUnit target, DamageResult result) =>
            _tape.RecordDamage(Tick, source != null ? source.Id : -1, target != null ? target.Id : -1, in result);

        private void HandleHealed(RuntimeUnit source, RuntimeUnit target, float amount) =>
            _tape.Record(new TapeEvent(
                TapeEventKind.Healed, Tick,
                source != null ? source.Id : -1, target != null ? target.Id : -1, amount));

        private void HandleAttackEvaded(RuntimeUnit target) =>
            _tape.Record(new TapeEvent(TapeEventKind.AttackEvaded, Tick, targetId: target.Id));

        private void HandleAttackStarted(RuntimeUnit unit, RuntimeUnit target) =>
            _tape.Record(new TapeEvent(
                TapeEventKind.AttackStarted, Tick, unit.Id, target != null ? target.Id : -1));

        private void HandleAttackInterrupted(RuntimeUnit unit) =>
            _tape.Record(new TapeEvent(TapeEventKind.AttackInterrupted, Tick, unit.Id));

        private void HandleAreaHit(AreaHit hit) => _tape.RecordAreaHit(Tick, in hit);

        private void HandleBattleEnded(BattleOutcome outcome) => _tape.RecordBattleEnded(Tick, in outcome);

        /// <summary>
        /// Dev-рестарт боя на месте: лента чистится целиком, иначе показ доигрывает смерти прошлого боя.
        /// Событие пишется уже в пустую ленту — чтобы читатель увидел причину обрыва, а не пустоту.
        /// </summary>
        private void HandleBattleReset()
        {
            _tape.Clear();
            _tape.Record(new TapeEvent(TapeEventKind.BattleReset, Tick));
        }
    }
}
