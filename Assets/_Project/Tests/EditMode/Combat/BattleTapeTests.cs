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
                sourceKind: DamageSourceKind.AutoAttack, type: DamageType.Slash);

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
            using var recorder = new BattleTapeRecorder(sim, tape, abilities: null, effects: null);

            var attacker = MakeUnit(id: 1, hp: 100f, team: 0);
            var victim   = MakeUnit(id: 2, hp: 100f, team: 1, pos: new Vector2(2f, 0f));

            sim.DealDamage(new DamageRequest(attacker, victim, 25f, DamageType.Pure, sim.ArmorK,
                sourceKind: DamageSourceKind.AutoAttack));
            sim.Tick(SimConstants.TickDelta);
            recorder.CaptureCurrentState();

            Assert.AreEqual(0, tape.FrontTick, "Записан тик, который только что досчитали");
            Assert.GreaterOrEqual(tape.EventCount, 1, "Урон попал в ленту событием");

            var collected = new List<TapeEvent>();
            tape.CollectEvents(0, 0, collected);
            Assert.AreEqual(TapeEventKind.DamageDealt, collected[0].Kind);
            Assert.AreEqual(1, collected[0].SourceId, "Событие несёт id, а не ссылку на юнита");
            Assert.AreEqual(2, collected[0].TargetId);
            Assert.AreEqual(25f, tape.GetDamage(collected[0].PayloadIndex).HpDamage, 1e-3f);
        }

        // Регресс play-mode 2026-07-29: в расстановке сим стоит на паузе, счётчик тиков не двигается —
        // и лента оставалась ПУСТОЙ, хотя юниты на арене есть. Арена выглядела пустой при семи юнитах.
        [Test]
        public void Recorder_WithoutASingleTick_StillProducesAFrame()
        {
            var sim  = BuildSim();
            var tape = new BattleTape(windowTicks: 16);
            using var recorder = new BattleTapeRecorder(sim, tape, abilities: null, effects: null);

            Assert.AreEqual(BattleTape.NoTick, tape.FrontTick, "Предусловие: лента пуста");
            Assert.AreEqual(0, sim.CurrentTick, "Предусловие: ни одного тика не прошло");

            recorder.CaptureCurrentState();

            Assert.AreEqual(0, tape.FrontTick, "Кадр состояния есть и без тиков — иначе показу нечего показывать");
            Assert.IsTrue(tape.TryGetFrame(0, out _), "И он достаётся из окна");
        }

        // Вне боя состояние меняется БЕЗ тиков (игрок таскает юнита) — кадр обязан обновляться.
        [Test]
        public void Recorder_RepeatedCapture_RefreshesTheSameTick()
        {
            var tape = new BattleTape(windowTicks: 16);
            var unit = MakeUnit(id: 1, hp: 100f);
            var units = new List<RuntimeUnit> { unit };

            tape.CaptureTick(0, units);
            unit.Position = new Vector2(5f, 2f);   // игрок перетащил юнита в расстановке
            tape.CaptureTick(0, units);            // тик тот же, состояние новое

            Assert.IsTrue(tape.TryGetFrame(0, out IReadOnlyList<UnitSnapshot> frame));
            Assert.AreEqual(5f, frame[0].Position.x, 1e-4f, "Кадр покоя перезаписан свежим состоянием");
            Assert.AreEqual(0, tape.FrontTick, "Номер тика при этом не поехал");
        }

        // ===================== Момент показа (Ф2) =====================

        // Показ цепляется за самый свежий кадр: до боя (мир, расстановка) лага быть не должно вовсе,
        // иначе игрок двигал бы юнита и видел его через десять секунд.
        [Test]
        public void Playback_StartsAtTheFront_WithoutWaitingForLead()
        {
            var tape = new BattleTape(windowTicks: 64);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            playback.Advance(SimConstants.TickDelta);
            Assert.IsFalse(playback.IsPlaying, "Лента пуста — показывать нечего");

            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta);

            Assert.IsTrue(playback.IsPlaying, "Появился кадр — показ сразу его берёт");
            Assert.AreEqual(0, playback.ViewTick);
            Assert.AreEqual(0, playback.Lead, "Отставания ещё нет: сим не успел никуда уйти");
        }

        // Запас перед показом набирает продюсер, разгоняя сим. Здесь это эмулируется руками: сим
        // уехал вперёд, а показ идёт тик за тиком реального времени.
        [Test]
        public void Playback_TrailsTheSim_WhenTheSimRunsAhead()
        {
            var tape = new BattleTape(windowTicks: BattleTapePlayback.LookaheadTicks + 60);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks); // идёт бой: лаг включён
            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta); // показ встал на тик 0
            for (int tick = 1; tick <= BattleTapePlayback.LookaheadTicks; tick++) tape.CaptureTick(tick, units);

            Assert.IsTrue(playback.HasFullLead, "Сим ушёл на окно вперёд — запас набран");

            for (int i = 0; i < 10; i++) playback.Advance(SimConstants.TickDelta);

            Assert.AreEqual(10, playback.ViewTick, "Показ идёт в реальном времени, тик за тик");
            Assert.AreEqual(BattleTapePlayback.LookaheadTicks - 10, playback.Lead,
                "И тратит запас ровно настолько, насколько прошёл");
        }

        // Обогнать сим показ не может: он упирается во фронт ленты и ждёт там.
        [Test]
        public void Playback_NeverOvertakesTheSim()
        {
            var tape = new BattleTape(windowTicks: BattleTapePlayback.LookaheadTicks + 60);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);
            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta);
            for (int tick = 1; tick <= BattleTapePlayback.LookaheadTicks; tick++) tape.CaptureTick(tick, units);

            // Сим стоит (например, бой кончился), а время показа течёт дальше.
            for (int i = 0; i < BattleTapePlayback.LookaheadTicks * 2; i++) playback.Advance(SimConstants.TickDelta);

            Assert.AreEqual(tape.FrontTick, playback.ViewTick, "Показ дошёл до фронта и остановился на нём");
            Assert.AreEqual(0, playback.Lead, "Запаса больше нет — дальше показывать нечего");
        }

        // Регресс play-mode 2026-07-29: у фронта ленты доля кадра сбрасывалась в ноль, и позиция
        // прыгала между началом и концом одного тика — юниты дрожали на месте (видно в финальном slowmo).
        [Test]
        public void Playback_AtTheFront_KeepsAlphaSteady()
        {
            var tape = new BattleTape(windowTicks: 32);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta);
            Assert.AreEqual(0, playback.ViewTick, "Предусловие: показ на единственном кадре");

            // Сим больше не тикает (бой досчитан), а кадры рендера продолжают идти.
            for (int i = 0; i < 5; i++)
            {
                playback.Advance(SimConstants.TickDelta);
                Assert.AreEqual(1f, playback.Alpha, 1e-4f,
                    "У фронта доля держится на конце тика — иначе позиция дёргается каждый кадр");
                Assert.AreEqual(0, playback.ViewTick, "И сам тик не двигается");
            }
        }

        // То, за что куплен лаг: показ читает состояние, до которого сам ещё не дошёл.
        [Test]
        public void Playback_ReadsTheFuture_ThatItHasNotShownYet()
        {
            var tape = new BattleTape(windowTicks: BattleTapePlayback.LookaheadTicks + 60);
            var playback = new BattleTapePlayback(tape);
            var unit = MakeUnit(id: 1, hp: 100f);
            var units = new List<RuntimeUnit> { unit };

            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);
            unit.CurrentHP = 100f;
            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta); // показ встал на тик 0
            for (int tick = 1; tick <= BattleTapePlayback.LookaheadTicks; tick++)
            {
                unit.CurrentHP = 100f - tick; // тает на единицу в тик
                tape.CaptureTick(tick, units);
            }

            Assert.IsTrue(playback.TryGetFrame(out IReadOnlyList<UnitSnapshot> now));
            Assert.AreEqual(100f, now[0].CurrentHP, 1e-4f, "Показывается ещё целый юнит");

            Assert.IsTrue(playback.TryGetFrameAhead(30, out IReadOnlyList<UnitSnapshot> soon),
                "Через секунду сим уже посчитан — показ имеет право туда заглянуть");
            Assert.AreEqual(70f, soon[0].CurrentHP, 1e-4f, "И знает, что HP там будет ниже");
        }

        [Test]
        public void Playback_Reset_SendsTheViewBackToWaiting()
        {
            var tape = new BattleTape(windowTicks: BattleTapePlayback.LookaheadTicks + 60);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta);
            Assert.IsTrue(playback.IsPlaying, "Предусловие: показ шёл");

            tape.Clear();
            playback.Reset();

            Assert.IsFalse(playback.IsPlaying, "После рестарта показу нечего показывать до первого кадра");
            Assert.AreEqual(BattleTape.NoTick, playback.ViewTick);
        }

        // Регресс play-mode 2026-07-29: показ стартовал НА ФРОНТЕ, а сим к тому моменту успевал
        // разогнаться на сотни тиков — игрок видел последнюю секунду боя вместо боя.
        [Test]
        public void Playback_StartingInBattle_BeginsAtTheStart_NotAtTheFront()
        {
            var tape = new BattleTape(windowTicks: BattleTapePlayback.LookaheadTicks + 60);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            // Сим уже уехал далеко (разгон), показ ещё ни разу не двигался.
            for (int tick = 0; tick <= 240; tick++) tape.CaptureTick(tick, units);
            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);

            playback.Advance(SimConstants.TickDelta);

            Assert.AreEqual(0, playback.ViewTick,
                "Показ начинает с начала ленты, а не с фронта: иначе бой пролетает мимо игрока");
            Assert.AreEqual(240, playback.Lead, "Всё, что сим успел посчитать, стало запасом");
        }

        // Пока показ не поехал, разгонять сим нельзя — иначе он уедет от начала боя.
        [Test]
        public void Playback_BeforeItStarts_ReportsLeadAsSatisfied()
        {
            var tape = new BattleTape(windowTicks: 64);
            var playback = new BattleTapePlayback(tape);

            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);

            Assert.IsFalse(playback.IsPlaying, "Предусловие: показ ещё не начался");
            Assert.IsTrue(playback.HasFullLead,
                "Продюсеру нечего догонять: разгон впереди несуществующего показа уводит от начала боя");
        }

        // Ф4. Пауза игрока = timeScale 0 → доля кадра нулевая: показ обязан стоять на месте.
        [Test]
        public void Playback_OnPause_DoesNotAdvance()
        {
            var tape = new BattleTape(windowTicks: BattleTapePlayback.LookaheadTicks + 60);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);
            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta);
            for (int tick = 1; tick <= 60; tick++) tape.CaptureTick(tick, units);

            int before = playback.ViewTick;
            for (int i = 0; i < 30; i++) playback.Advance(0f);   // масштабированное время стоит

            Assert.AreEqual(before, playback.ViewTick, "На паузе показ не двигается");
            Assert.AreEqual(60 - before, playback.Lead, "А просчёт остаётся впереди — пауза его не касается");
        }

        // Ф4. Пауза не должна съедать картинку: просчёт, ушедший на всё окно, вытеснил бы показываемый
        // кадр — поэтому у края окна продюсер обязан остановиться.
        [Test]
        public void Playback_NearTheWindowEdge_TellsTheProducerToStop()
        {
            var tape = new BattleTape(windowTicks: 120);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);
            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta);
            Assert.IsFalse(playback.AtWindowLimit, "Пока запас мал, считать можно");

            // Показ стоит (пауза), просчёт уходит вперёд до края окна.
            for (int tick = 1; tick <= 90; tick++) tape.CaptureTick(tick, units);

            Assert.IsTrue(playback.AtWindowLimit,
                "У края окна просчёт обязан встать: следующий кадр вытеснил бы показываемый");
            Assert.IsTrue(tape.TryGetFrame(playback.ViewTick, out _),
                "И показываемый кадр всё ещё на месте — картинка не исчезла");
        }

        // ===================== Доставка событий по показу (Ф3) =====================

        // Главное обещание Ф3: событие отдаётся тогда, когда его тик ПОКАЗАН, а не когда посчитан.
        [Test]
        public void Dispatcher_HoldsEventsUntilTheirTickIsShown()
        {
            var tape = new BattleTape(windowTicks: 64);
            var dispatcher = new BattleTapeDispatcher(tape);

            var shown = new List<int>();
            dispatcher.UnitDied += id => shown.Add(id);

            tape.Record(new TapeEvent(TapeEventKind.UnitDied, 10, sourceId: 1));
            tape.Record(new TapeEvent(TapeEventKind.UnitDied, 20, sourceId: 2));

            dispatcher.PumpTo(5);
            Assert.AreEqual(0, shown.Count, "До тика события ничего не отдаётся — сим посчитал, игрок не видел");

            dispatcher.PumpTo(10);
            Assert.AreEqual(1, shown.Count, "Показали тик 10 — отдали его событие");
            Assert.AreEqual(1, shown[0]);

            dispatcher.PumpTo(25);
            Assert.AreEqual(2, shown.Count, "Догнали тик 20 — отдали и второе");
            Assert.AreEqual(25, dispatcher.ShownTick, "Показанный тик — тот, до которого качнули");

            dispatcher.PumpTo(30);
            Assert.AreEqual(2, shown.Count, "Больше событий в ленте нет — повторов не будет");
        }

        [Test]
        public void Dispatcher_NeverRepeatsAnEvent()
        {
            var tape = new BattleTape(windowTicks: 16);
            var dispatcher = new BattleTapeDispatcher(tape);

            int calls = 0;
            dispatcher.AttackEvaded += _ => calls++;
            tape.Record(new TapeEvent(TapeEventKind.AttackEvaded, 3, targetId: 7));

            dispatcher.PumpTo(3);
            dispatcher.PumpTo(3);
            dispatcher.PumpTo(9);

            Assert.AreEqual(1, calls, "Одно событие — один показ, сколько бы раз ни качали");
        }

        // Конец боя обязан приезжать по показу: иначе экран итогов выскакивает, пока на арене дерутся.
        [Test]
        public void Dispatcher_BattleEnd_ArrivesOnTheShownTick_NotWhenSolved()
        {
            var tape = new BattleTape(windowTicks: 64);
            var dispatcher = new BattleTapeDispatcher(tape);

            BattleOutcome? ended = null;
            dispatcher.BattleEnded += o => ended = o;

            BattleOutcome outcome = BattleOutcome.Win(team: 0);
            tape.RecordBattleEnded(tick: 40, outcome: in outcome);

            dispatcher.PumpTo(39);
            Assert.IsFalse(ended.HasValue, "Сим уже знает исход, но игрок его ещё не увидел");

            dispatcher.PumpTo(40);
            Assert.IsTrue(ended.HasValue, "Показ дошёл до тика исхода — вот теперь бой кончился");
            Assert.IsTrue(ended.Value.IsWinFor(0));
        }

        [Test]
        public void Dispatcher_Reset_ForgetsWhatWasShown()
        {
            var tape = new BattleTape(windowTicks: 16);
            var dispatcher = new BattleTapeDispatcher(tape);

            int calls = 0;
            dispatcher.UnitSpawned += _ => calls++;
            tape.Record(new TapeEvent(TapeEventKind.UnitSpawned, 1, sourceId: 1));
            dispatcher.PumpTo(1);
            Assert.AreEqual(1, calls, "Предусловие: событие показано");

            tape.Clear();
            dispatcher.Reset();
            tape.Record(new TapeEvent(TapeEventKind.UnitSpawned, 0, sourceId: 5));
            dispatcher.PumpTo(0);

            Assert.AreEqual(2, calls, "После рестарта первое событие нового боя не считается показанным");
            Assert.AreEqual(0, dispatcher.ShownTick);
        }

        // Лаг — свойство БОЯ: вне боя показ идёт вплотную, иначе расстановка отвечала бы с задержкой.
        [Test]
        public void Playback_WithoutTargetLead_StaysOnTheFront()
        {
            var tape = new BattleTape(windowTicks: 64);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            playback.SetTargetLead(0);
            for (int tick = 0; tick <= 30; tick++) tape.CaptureTick(tick, units);
            playback.Advance(SimConstants.TickDelta);

            Assert.AreEqual(tape.FrontTick, playback.ViewTick, "Вне боя показ стоит на фронте");
            Assert.AreEqual(0, playback.Lead, "Задержки нет вовсе");
        }

        // Выход из боя в мир не должен оставлять картинку в прошлом на десять секунд.
        [Test]
        public void Playback_DroppingTheLead_CatchesUpImmediately()
        {
            var tape = new BattleTape(windowTicks: BattleTapePlayback.LookaheadTicks + 60);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);
            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta);
            for (int tick = 1; tick <= BattleTapePlayback.LookaheadTicks; tick++) tape.CaptureTick(tick, units);
            Assert.Greater(playback.Lead, 0, "Предусловие: в бою запас есть");

            playback.SetTargetLead(0); // бой кончился, вернулись в мир

            Assert.AreEqual(tape.FrontTick, playback.ViewTick, "Показ подтянулся к фронту сразу");
            Assert.AreEqual(0, playback.Lead);
        }

        // Ф7: dev-оверлеи рисуют кольца и радиусы в МИРОВЫХ координатах, поэтому им нужен показанный
        // кадр, а не живой сим — иначе они висят там, где юнитов на экране ещё нет. Значит статусы
        // обязаны доезжать в снимке.
        [Test]
        public void Frame_CarriesDevOverlayState_ForOverlaysToReadTheShownTick()
        {
            var tape = new BattleTape(windowTicks: 8);
            RuntimeUnit unit = MakeUnit(id: 3, hp: 100f);
            unit.Stats.AddModifiersFrom("range", new[]
            {
                new StatModifier(StatType.AttackRange, ModifierOp.Flat, 4.5f),
            });
            unit.CanAct                 = false;   // выведен контролем — кольцо стана
            unit.DisplacedTicksRemaining = 3;      // и летит от отбрасывания
            unit.EmpowerDamageMult       = 0.5f;   // взведено усиление удара

            tape.CaptureTick(0, new List<RuntimeUnit> { unit });

            // Сим ушёл вперёд и статусы уже сняты — кадр обязан помнить своё состояние.
            unit.CanAct                  = true;
            unit.DisplacedTicksRemaining = 0;
            unit.EmpowerDamageMult       = 0f;

            Assert.IsTrue(tape.TryGetFrame(0, out IReadOnlyList<UnitSnapshot> frame));
            UnitSnapshot s = frame[0];

            Assert.AreEqual(4.5f, s.AttackRange, 1e-4f, "Радиус — состояние: его меняет бафф");
            Assert.IsFalse(s.CanAct,      "Стан показанного тика, а не текущего");
            Assert.IsTrue(s.IsDisplaced,  "Полёт от отбрасывания — вторая половина «стана» оверлея");
            Assert.IsTrue(s.IsEmpowered,  "Усиление хранится признаком: оверлею нужен факт, не величина");
        }

        // ===================== Знание будущего: телеграфы и предчувствие (Ф5, Ф6) =====================

        // Шов Ф5: показ обязан УВИДЕТЬ будущее наложение эффекта раньше, чем до него дойдёт, — иначе
        // подводку («щит поднимается до удара») делать нечем.
        [Test]
        public void Foresight_SeesAnEffectApplication_BeforeShowingIt()
        {
            var tape = new BattleTape(windowTicks: 64);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };
            EffectData shield = TelegraphedShield(telegraphSeconds: 0.3f);

            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);
            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta);
            for (int tick = 1; tick <= 30; tick++) tape.CaptureTick(tick, units);

            // Сим уже посчитал: на тике 20 на юнита ляжет щит. Показ стоит на нуле.
            tape.RecordEffect(20, TapeEventKind.EffectApplied, targetId: 1, def: shield);

            var upcoming = new List<TapeEvent>();
            tape.CollectEvents(playback.ViewTick + 1, playback.ViewTick + 30, upcoming);

            Assert.AreEqual(1, upcoming.Count, "Событие ещё не показано, но уже видно вперёд");
            Assert.AreEqual(TapeEventKind.EffectApplied, upcoming[0].Kind);
            Assert.AreEqual(0.3f, tape.GetEffect(upcoming[0].PayloadIndex).TelegraphSeconds, 1e-4f,
                "И эффект сам говорит, за сколько его анонсировать — число живёт в ассете, не в коде");
            Assert.AreEqual(20 - playback.ViewTick, upcoming[0].Tick - playback.ViewTick,
                "До события известно точное расстояние в тиках — из него и считается момент подводки");
        }

        // Шов Ф6: смертельный удар виден заранее — на этом и стоит slowmo, начинающееся ЧУТЬ РАНЬШЕ смерти.
        [Test]
        public void Foresight_SeesALethalHit_BeforeShowingIt()
        {
            var tape = new BattleTape(windowTicks: 64);
            var playback = new BattleTapePlayback(tape);
            var units = new List<RuntimeUnit> { MakeUnit(id: 1, hp: 100f) };

            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);
            tape.CaptureTick(0, units);
            playback.Advance(SimConstants.TickDelta);
            for (int tick = 1; tick <= 30; tick++) tape.CaptureTick(tick, units);

            var plain = new DamageResult(hpDamage: 10f, shieldDamage: 0f, killedTarget: false,
                sourceKind: DamageSourceKind.AutoAttack, type: DamageType.Slash);
            var lethal = new DamageResult(hpDamage: 90f, shieldDamage: 0f, killedTarget: true,
                sourceKind: DamageSourceKind.AutoAttack, type: DamageType.Slash);

            tape.RecordDamage(tick: 5,  sourceId: 2, targetId: 1, result: in plain);
            tape.RecordDamage(tick: 12, sourceId: 2, targetId: 1, result: in lethal);

            var upcoming = new List<TapeEvent>();
            tape.CollectEvents(playback.ViewTick + 1, playback.ViewTick + 15, upcoming);

            int lethalTick = -1;
            for (int i = 0; i < upcoming.Count; i++)
            {
                if (upcoming[i].Kind != TapeEventKind.DamageDealt) continue;
                if (!tape.GetDamage(upcoming[i].PayloadIndex).KilledTarget) continue;
                lethalTick = upcoming[i].Tick;
            }

            Assert.AreEqual(12, lethalTick, "Смерть видна заранее — за неё и цепляется slowmo «чуть раньше»");
            Assert.Greater(lethalTick, playback.ViewTick, "И она ещё не показана");
        }

        /// <summary>Щит с телеграфом: как `BulwarkShield` в ассетах — короткий, с тегом Shield и подводкой.</summary>
        private static EffectData TelegraphedShield(float telegraphSeconds)
        {
            EffectData shield = TestEffect.Make(
                baseDuration: 0.4f, polarity: EffectPolarity.Buff,
                tags: EffectTag.Shield, stacking: StackRule.Refresh);
            return shield.With("_telegraphSeconds", telegraphSeconds);
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
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
        }
    }
}
