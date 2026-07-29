using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Лента боя, Ф1 (см. `docs/lookahead-presentation-lag.md`): сим пишет вперёд, показ читает с
    /// лагом. Проверяет главные обещания шва: кадр тика воспроизводит состояние ТОГО тика, окно
    /// вытесняет старое и не растёт, события живут за весь бой и отдаются по диапазону тиков,
    /// dev-рестарт чистит ленту целиком, а запись тика не мусорит.
    /// </summary>
    public sealed class BattleTapeTests
    {
        // Главное обещание: кадр отдаёт состояние своего тика, а не текущее состояние сима.
        [Test]
        public void Frame_HoldsStateOfItsOwnTick_NotTheLatest()
        {
            var tape = new BattleTape(windowTicks: 8);
            var unit = MakeUnit(id: 7, hp: 100f);

            var units = new List<RuntimeUnit> { unit };
            tape.CaptureTick(0, units);

            unit.CurrentHP = 40f;               // сим уехал вперёд
            unit.Position  = new Vector2(3f, 0f);
            tape.CaptureTick(1, units);

            Assert.IsTrue(tape.TryGetFrame(0, out IReadOnlyList<UnitSnapshot> old));
            Assert.AreEqual(100f, old[0].CurrentHP, 1e-4f, "Старый кадр помнит своё HP");
            Assert.AreEqual(0f, old[0].Position.x, 1e-4f, "И свою позицию");

            Assert.IsTrue(tape.TryGetFrame(1, out IReadOnlyList<UnitSnapshot> fresh));
            Assert.AreEqual(40f, fresh[0].CurrentHP, 1e-4f, "Свежий кадр — новое состояние");
            Assert.AreEqual(1, tape.FrontTick, "Фронт ленты — последний записанный тик");
        }

        [Test]
        public void Window_EvictsTicksOlderThanItsDepth()
        {
            var tape = new BattleTape(windowTicks: 4);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            for (int tick = 0; tick < 6; tick++) tape.CaptureTick(tick, units);

            Assert.IsFalse(tape.TryGetFrame(0, out _), "Тик 0 вытеснен — он старше окна");
            Assert.IsFalse(tape.TryGetFrame(1, out _), "Тик 1 тоже");
            Assert.IsTrue(tape.TryGetFrame(2, out _),  "А тик 2 ещё в окне");
            Assert.IsTrue(tape.TryGetFrame(5, out _),  "И фронт, разумеется");
            Assert.AreEqual(2, tape.OldestTick, "Самый старый доступный тик считается по окну");
        }

        // Событий за бой много, снимков — только окно: события не выбрасываются вместе с кадрами.
        [Test]
        public void Events_OutliveTheSnapshotWindow()
        {
            var tape = new BattleTape(windowTicks: 2);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            tape.Record(new TapeEvent(TapeEventKind.UnitSpawned, 0, sourceId: 1));
            for (int tick = 0; tick < 10; tick++) tape.CaptureTick(tick, units);

            Assert.IsFalse(tape.TryGetFrame(0, out _), "Кадр тика 0 давно вытеснен");
            Assert.AreEqual(1, tape.EventCount, "А событие тика 0 на месте — режиссура читает весь бой");
            Assert.AreEqual(0, tape.GetEvent(0).Tick);
        }

        [Test]
        public void CollectEvents_ReturnsOnlyTheAskedTickRange()
        {
            var tape = new BattleTape(windowTicks: 8);
            tape.Record(new TapeEvent(TapeEventKind.AttackStarted, 1, sourceId: 1, targetId: 2));
            tape.Record(new TapeEvent(TapeEventKind.Healed, 4, sourceId: 1, targetId: 1, amount: 30f));
            tape.Record(new TapeEvent(TapeEventKind.UnitDied, 4, sourceId: 2));
            tape.Record(new TapeEvent(TapeEventKind.UnitDied, 9, sourceId: 3));

            var collected = new List<TapeEvent>();
            tape.CollectEvents(fromTick: 2, toTick: 4, into: collected);

            Assert.AreEqual(2, collected.Count, "Взяты только события тиков 2..4");
            Assert.AreEqual(TapeEventKind.Healed, collected[0].Kind, "Порядок записи сохранён");
            Assert.AreEqual(TapeEventKind.UnitDied, collected[1].Kind);

            tape.CollectEvents(fromTick: 5, toTick: 8, into: collected);
            Assert.AreEqual(0, collected.Count, "В пустом диапазоне ничего нет");
        }

        // Порядок урона и смерти в одном тике обязан сохраниться: иначе показ убьёт цель до удара.
        [Test]
        public void DamageAndDeath_KeepTheirOrderWithinATick()
        {
            var tape = new BattleTape(windowTicks: 8);
            var lethal = new DamageResult(
                hpDamage: 50f, shieldDamage: 0f, killedTarget: true,
                sourceKind: DamageSourceKind.AutoAttack, school: DamageSchool.Physical);

            tape.RecordDamage(tick: 3, sourceId: 1, targetId: 2, result: in lethal);
            tape.Record(new TapeEvent(TapeEventKind.UnitDied, 3, sourceId: 2));

            var collected = new List<TapeEvent>();
            tape.CollectEvents(3, 3, collected);

            Assert.AreEqual(TapeEventKind.DamageDealt, collected[0].Kind, "Сначала удар");
            Assert.AreEqual(TapeEventKind.UnitDied, collected[1].Kind, "Потом смерть");
            Assert.IsTrue(tape.GetDamage(collected[0].PayloadIndex).KilledTarget,
                "Подробности удара достаются по ссылке из события");
        }

        [Test]
        public void Clear_WipesFramesAndEvents()
        {
            var tape = new BattleTape(windowTicks: 4);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };
            tape.CaptureTick(0, units);
            tape.Record(new TapeEvent(TapeEventKind.UnitDied, 0, sourceId: 1));

            tape.Clear();

            Assert.AreEqual(BattleTape.NoTick, tape.FrontTick, "Лента пуста — фронта нет");
            Assert.AreEqual(0, tape.EventCount, "События прошлого боя не доигрываются после рестарта");
            Assert.IsFalse(tape.TryGetFrame(0, out _), "Кадров тоже не осталось");
        }

        // Запись тика — горячий путь: кадры окна выделяются раз и переиспользуются.
        [Test]
        public void CaptureTick_DoesNotAllocate_AfterWarmup()
        {
            var tape = new BattleTape(windowTicks: 16);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f), MakeUnit(id: 2, hp: 100f) };

            for (int tick = 0; tick < 32; tick++) tape.CaptureTick(tick, units); // разогрев списков

            long before = System.GC.GetTotalMemory(forceFullCollection: true);
            for (int tick = 32; tick < 132; tick++) tape.CaptureTick(tick, units);
            long after = System.GC.GetTotalMemory(forceFullCollection: false);

            Assert.LessOrEqual(after - before, 0L,
                "Сто тиков записи не должны аллоцировать: кадры окна переиспользуются");
        }

        // Рекордер — единственная проводка sim→лента; проверяем на живой симуляции.
        [Test]
        public void Recorder_CapturesTicksAndEvents_FromLiveSimulation()
        {
            var sim  = BuildSim();
            var tape = new BattleTape(windowTicks: 32);
            using var recorder = new BattleTapeRecorder(sim, tape);

            var attacker = MakeUnit(id: 1, hp: 100f, team: 0);
            var victim   = MakeUnit(id: 2, hp: 100f, team: 1, pos: new Vector2(2f, 0f));

            sim.DealDamage(new DamageRequest(attacker, victim, 25f, DamageSchool.True, sim.ArmorK,
                sourceKind: DamageSourceKind.AutoAttack));
            sim.Tick(SimConstants.TickDelta);
            recorder.CaptureTick();

            Assert.AreEqual(0, tape.FrontTick, "Записан тик, который только что досчитали");
            Assert.GreaterOrEqual(tape.EventCount, 1, "Урон попал в ленту событием");

            var collected = new List<TapeEvent>();
            tape.CollectEvents(0, 0, collected);
            Assert.AreEqual(TapeEventKind.DamageDealt, collected[0].Kind);
            Assert.AreEqual(1, collected[0].SourceId, "Событие несёт id, а не ссылку на юнита");
            Assert.AreEqual(2, collected[0].TargetId);
            Assert.AreEqual(25f, tape.GetDamage(collected[0].PayloadIndex).HpDamage, 1e-3f);
        }

        // ===================== Фабрики =====================

        private static CombatSimulation BuildSim() =>
            new CombatSimulation(
                new XorShiftRng(1UL), CombatTestValues.ArmorK, new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, float hp, int team = 0, Vector2 pos = default)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,       ModifierOp.Flat, hp),
                new StatModifier(StatType.MaxResource, ModifierOp.Flat, 50f),
                new StatModifier(StatType.Size,        ModifierOp.Flat, 1f),
            });
            return new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentHP        = hp,
                Position         = pos,
                PreviousPosition = pos,
            };
        }
    }
}
