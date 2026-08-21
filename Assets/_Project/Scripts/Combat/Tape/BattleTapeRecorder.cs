using System;
using Guildmaster.Core.Simulation;
using UnityEngine;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Пишет ленту: подписывается на события симуляции и снимает состояние в кадры.
    /// Отдельный класс, потому что <see cref="BattleTape"/> — это данные, а проводка sim→лента —
    /// поведение; смешивать их значит запретить тесту писать в ленту руками.
    /// <para>Владелец ритма — тот, кто тикает сим (<c>CombatLoopService</c>): он зовёт
    /// <see cref="CaptureCurrentState"/> после каждого тика и раз в кадр рендера. У симуляции события
    /// «тик закончился» нет и заводить его незачем — сим не должен знать, что его записывают.</para>
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
        private readonly AbilitySystem _abilities;
        private readonly EffectSystem     _effects;

        public BattleTapeRecorder(
            CombatSimulation simulation, BattleTape tape,
            AbilitySystem abilities, EffectSystem effects)
        {
            _simulation = simulation;
            _tape       = tape;
            _abilities  = abilities;
            _effects    = effects;

            // Каст и статусы тоже обязаны ехать по показу: их звук иначе приходит за окно опережения
            // до того, как игрок увидит сам каст.
            if (_abilities != null)
            {
                _abilities.OnAbilityCast            += HandleAbilityCast;
                _abilities.OnAbilityCastStarted     += HandleAbilityCastStarted;
                _abilities.OnAbilityCastInterrupted += HandleAbilityCastInterrupted;
            }
            if (_effects != null)
            {
                _effects.OnEffectApplied += HandleEffectApplied;
                _effects.OnEffectEnded   += HandleEffectEnded;
            }

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

            if (_abilities != null)
            {
                _abilities.OnAbilityCast            -= HandleAbilityCast;
                _abilities.OnAbilityCastStarted     -= HandleAbilityCastStarted;
                _abilities.OnAbilityCastInterrupted -= HandleAbilityCastInterrupted;
            }
            if (_effects != null)
            {
                _effects.OnEffectApplied -= HandleEffectApplied;
                _effects.OnEffectEnded   -= HandleEffectEnded;
            }
        }

        /// <summary>
        /// Снять кадр состояния «на сейчас» под номером последнего завершённого тика.
        /// <para>Способ ровно один, и это важно: «кадр досчитанного тика» и «кадр покоя» отличались бы
        /// только тем, двигался ли счётчик, — а состояние юнитов меняется и без тиков. В расстановке сим
        /// стоит на паузе, игрок таскает юнитов, счётчик не растёт; продюсер, писавший только «после
        /// тика», оставлял ленту пустой, и арена выглядела пустой при семи юнитах в симуляции.</para>
        /// <para>Зовётся после каждого тика И раз в кадр рендера: пока тиков нет, кадр просто
        /// перезаписывается свежим состоянием — это и есть «вне боя показ идёт в реальном времени».</para>
        /// </summary>
        public void CaptureCurrentState()
        {
            _tape.CaptureTick(
                Mathf.Max(0, _simulation.CurrentTick - 1), _simulation.Units, _simulation.Projectiles);
        }

        private int Tick => _simulation.CurrentTick;

        private void HandleUnitSpawned(RuntimeUnit unit) =>
            _tape.Record(new TapeEvent(TapeEventKind.UnitSpawned, Tick, unit.Id));

        private void HandleUnitDied(RuntimeUnit unit) =>
            _tape.Record(new TapeEvent(TapeEventKind.UnitDied, Tick, unit.Id));

        // Доля тика снимается с носителя удара: её держит там AutoAttackSystem.Land на время нанесения.
        // Событие урона одно на все источники (авто-атака, периодика, реактив, способность), поэтому вне
        // удара поле равно нулю — и горение честно едет по границе тика, где его момент и находится.
        private void HandleDamageDealt(RuntimeUnit source, RuntimeUnit target, DamageResult result) =>
            _tape.RecordDamage(
                Tick, source != null ? source.Id : -1, target != null ? target.Id : -1, in result,
                subTick: source != null ? source.ContactSubTick : 0f);

        private void HandleHealed(RuntimeUnit source, RuntimeUnit target, float amount) =>
            _tape.Record(new TapeEvent(
                TapeEventKind.Healed, Tick,
                source != null ? source.Id : -1, target != null ? target.Id : -1, amount));

        private void HandleAttackEvaded(RuntimeUnit attacker, RuntimeUnit target) =>
            _tape.Record(new TapeEvent(TapeEventKind.AttackEvaded, Tick,
                sourceId: attacker != null ? attacker.Id : -1, targetId: target.Id));

        private void HandleAttackStarted(RuntimeUnit unit, RuntimeUnit target) =>
            _tape.Record(new TapeEvent(
                TapeEventKind.AttackStarted, Tick, unit.Id, target != null ? target.Id : -1));

        private void HandleAttackInterrupted(RuntimeUnit unit) =>
            _tape.Record(new TapeEvent(TapeEventKind.AttackInterrupted, Tick, unit.Id));

        private void HandleAreaHit(AreaHit hit) => _tape.RecordAreaHit(Tick, in hit);

        // Каст едет с ОПРЕДЕЛЕНИЕМ: показ по нему решает, чем светить (CastSource). Ссылкой, как эффекты,
        // — определения неизменны, копировать их в ленту незачем.
        private void HandleAbilityCast(RuntimeUnit caster, Data.Definitions.AbilityData data) =>
            _tape.RecordAbility(Tick, TapeEventKind.AbilityCast, caster != null ? caster.Id : -1, data);

        // Подготовка несёт СВОЮ длительность: показ держит подводку ровно столько, сколько сим будет
        // готовиться, — иначе контур гаснет раньше удара или висит после него.
        private void HandleAbilityCastStarted(RuntimeUnit caster, Data.Definitions.AbilityData data, int castTicks) =>
            _tape.RecordAbility(
                Tick, TapeEventKind.AbilityCastStarted, caster != null ? caster.Id : -1, data,
                amount: castTicks / (float)Core.Simulation.SimConstants.TickRate);

        private void HandleAbilityCastInterrupted(RuntimeUnit caster) =>
            _tape.Record(new TapeEvent(TapeEventKind.AbilityCastInterrupted, Tick, caster != null ? caster.Id : -1));

        // Системные эффекты (sys.airborne и родня) в ленту НЕ пишем: они собраны в коде, в реестре
        // контента их нет, и показ по сети/из файла не смог бы разрешить их id — чанк отвергался бы
        // целиком, и воспроизведение вставало. Их визуал (полёт, оглушение) показ и так берёт из снимка
        // юнита (IsDisplaced, маска тегов), а иконки/телеграфа у системного эффекта нет.
        private void HandleEffectApplied(RuntimeUnit target, Data.Definitions.EffectData def, RuntimeUnit source)
        {
            if (def != null && def.IsRuntime) return;
            _tape.RecordEffect(Tick, TapeEventKind.EffectApplied, target != null ? target.Id : -1, def);
        }

        private void HandleEffectEnded(RuntimeUnit target, Data.Definitions.EffectData def, RuntimeUnit source)
        {
            if (def != null && def.IsRuntime) return;
            _tape.RecordEffect(Tick, TapeEventKind.EffectEnded, target != null ? target.Id : -1, def);
        }

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
