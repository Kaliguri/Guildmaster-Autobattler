using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Core.Net;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Guildmaster.Net.Tape;
using Guildmaster.Net.Transport;
using Guildmaster.Tests.EditMode.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Стык коопа и живого боя: хост считает бой и раздаёт его лентой, гость ничего не считает и
    /// приходит к тому же исходу.
    /// </summary>
    /// <remarks>
    /// <b>Почему тест, а не «проверим на двух машинах».</b> Роль решается в рантайме (боевой скоуп
    /// поднимается на буте, когда сети ещё нет), и ошибка разводки выглядит не отказом, а тихой
    /// разницей в картинке у второго игрока — самой дорогой поломкой из возможных: воспроизводится
    /// она только вдвоём и только иногда.
    /// <para>Здесь же держится и обратная сторона: соло-игрок и гость <b>не раздают</b>. Гость,
    /// раздающий ленту, которую ему прислали, — это петля, а соло-игрок платил бы нарезкой чанков в
    /// пустоту каждый кадр.</para>
    /// </remarks>
    public sealed class CoopBattleWiringTests
    {
        [Test]
        public void HostRunsTheBattle_GuestReachesTheSameOutcome()
        {
            var net   = new LoopbackNetwork();
            var host  = net.CreateNode();
            var guest = net.CreateNode();

            Host h = BuildHost(host, BattleRole.Host);

            var guestTape = new BattleTape(windowTicks: 512);
            var intake    = new TapeIntake(guest, new TapeChunkReader(guestTape, new FakeContent()));

            // Кадр игры целиком: тик — снимок — раздача — доставка. Тот же порядок, что в бою:
            // BattleTapeBroadcast тикает после CombatLoopService, NetPump качает транспорт.
            for (int frame = 0; frame < 40; frame++) Frame(h, net);

            // Добиваем вторую команду — исход посчитает сим сам, на ближайшем тике.
            foreach (RuntimeUnit unit in h.Sim.Units)
                if (unit.Team == 1) unit.CurrentHP = 0f;

            for (int frame = 0; frame < 3; frame++) Frame(h, net);
            Assert.AreNotEqual(BattleOutcome.Ongoing, h.Sim.Outcome, "Бой у хоста кончился");

            // Хвост боя короче чанка и уезжает отдельным Flush — в нём и едет исход.
            Frame(h, net);

            Assert.AreEqual(0, intake.MissingCount, "Дыр в нумерации не осталось");
            Assert.Greater(intake.AppliedChunkCount, 0, "Гость получил ленту");

            Assert.IsTrue(TryFindOutcome(guestTape, out BattleOutcome guestOutcome),
                "У гостя в ленте есть конец боя");
            Assert.AreEqual(h.Sim.Outcome.Kind, guestOutcome.Kind, "Тот же вид исхода");
            Assert.AreEqual(h.Sim.Outcome.WinningTeam, guestOutcome.WinningTeam, "И та же команда-победитель");

            // Показ гостя идёт по ленте своими часами: он обязан дойти до конца боя, а не встать на
            // середине. Держим тот же лаг, что у хоста, — иначе гость увидел бы бой раньше напарника.
            var playback = new BattleTapePlayback(guestTape);
            playback.SetTargetLead(BattleTapePlayback.LookaheadTicks);
            for (int i = 0; i < guestTape.FrontTick + 10; i++) playback.Advance(SimConstants.TickDelta);

            Assert.AreEqual(guestTape.FrontTick, playback.ViewTick, "Показ гостя дошёл до фронта ленты");
            Assert.IsTrue(playback.TryGetFrame(out IReadOnlyList<UnitSnapshot> units), "И кадр на месте");
            Assert.AreEqual(2, units.Count, "С обоими юнитами");
        }

        [Test]
        public void Solo_HandsOutNothing()
        {
            var net  = new LoopbackNetwork();
            Host h   = BuildHost(net.CreateNode(), BattleRole.Solo);

            for (int frame = 0; frame < 40; frame++) Frame(h, net);

            Assert.AreEqual(0, h.Streamer.SentChunkCount,
                "В соло раздавать некому: нарезка чанков в пустоту — это работа каждый кадр ни за что");
        }

        [Test]
        public void Guest_DoesNotHandOutTheTapeItReceived()
        {
            var net = new LoopbackNetwork();
            Host h  = BuildHost(net.CreateNode(), BattleRole.Guest);

            for (int frame = 0; frame < 40; frame++) Frame(h, net);

            Assert.AreEqual(0, h.Streamer.SentChunkCount, "Гость ленту не раздаёт — иначе она пошла бы по кругу");
        }

        // Повтор просит тот, у кого дыры, а дыры бывают только у принимающего. Хост, спрашивающий
        // повтор у самого себя, — это лишний трафик и путаница в метриках канала.
        [Test]
        public void OnlyTheGuest_AsksForMissingChunks()
        {
            var net   = new LoopbackNetwork();
            var host  = new DroppingNode(net.CreateNode());
            var guest = net.CreateNode();

            BattleTape source = FilledTape(ticks: 60);
            var streamer = new TapeStreamer(host, source, ticksPerChunk: 30);

            var intake = new TapeIntake(guest, new TapeChunkReader(new BattleTape(256), new FakeContent()));
            var role   = new FakeAuthority { Role = BattleRole.Host };
            var pump   = new TapeIntakePump(intake, role);

            host.DropNextSends = 1;               // первый чанк не доедет — дыра появилась
            streamer.Flush(readyThroughTick: 59);
            net.PollAll();
            Assert.AreEqual(1, intake.MissingCount, "Дыра у приёмника есть");

            pump.Tick();
            Assert.AreEqual(0, intake.ResendRequestCount, "Хост повтора не просит");

            role.Role = BattleRole.Solo;
            pump.Tick();
            Assert.AreEqual(0, intake.ResendRequestCount, "И соло-игрок тоже");

            role.Role = BattleRole.Guest;
            pump.Tick();
            Assert.AreEqual(1, intake.ResendRequestCount, "А гость — просит");
        }

        // Состав боя в снимках не едет — он за бой не меняется. Значит гость, у которого нет спавнов,
        // узнаёт «кто на арене» отдельным объявлением; без него лента приедет, а рисовать будет нечего.
        [Test]
        public void GuestLearnsWhoIsOnTheArena()
        {
            var net   = new LoopbackNetwork();
            var host  = net.CreateNode();
            var guest = net.CreateNode();

            CombatSimulation hostSim = NewSim();
            var hostRoster = new BattleRosterRelay(host, hostSim, new BattleUnitRegistry(hostSim),
                new FakeContent(), new FakeAuthority { Role = BattleRole.Host });
            hostRoster.Start();

            CombatSimulation guestSim = NewSim();
            var guestRegistry = new BattleUnitRegistry(guestSim);
            var guestRoster = new BattleRosterRelay(guest, guestSim, guestRegistry,
                new FakeContent(), new FakeAuthority { Role = BattleRole.Guest });
            guestRoster.Start();

            hostSim.EnqueueUnitSpawn(Unit(team: 0, x: -3f, id: 1));
            hostSim.EnqueueUnitSpawn(Unit(team: 1, x:  3f, id: 2));
            hostSim.Tick(SimConstants.TickDelta);
            net.PollAll();

            Assert.AreEqual(2, hostRoster.AnnouncedCount, "Хост объявил обоих");
            Assert.AreEqual(2, guestRoster.AnnouncedCount, "И оба доехали до гостя");

            foreach (RuntimeUnit unit in hostSim.Units)
            {
                Assert.IsTrue(guestRegistry.TryGet(unit.Id, out UnitIdentity entry),
                    $"Паспорт юнита {unit.Id} доехал");
                Assert.AreEqual(unit.Team, entry.Team, "И команда та же — по ней показ красит юнита");
            }
        }

        // ── помощники ────────────────────────────────────────────────────────────

        private sealed class Host
        {
            public CombatSimulation    Sim;
            public BattleTapeRecorder  Recorder;
            public TapeStreamer        Streamer;
            public BattleTapeBroadcast Broadcast;
        }

        private static CombatSimulation NewSim() =>
            new CombatSimulation(
                new XorShiftRng(7UL), CombatTestValues.ArmorK, new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(), new AutoAttackSystem(),
                new ProjectileSystem(), new DeathSystem(), new EffectSystem(), new RegenSystem());

        private static Host BuildHost(INetTransport transport, BattleRole role)
        {
            CombatSimulation sim = NewSim();

            sim.EnqueueUnitSpawn(Unit(team: 0, x: -3f, id: 1));
            sim.EnqueueUnitSpawn(Unit(team: 1, x:  3f, id: 2));

            var tape     = new BattleTape(windowTicks: 512);
            var recorder = new BattleTapeRecorder(sim, tape, abilities: null, effects: null);
            var streamer = new TapeStreamer(transport, tape, ticksPerChunk: 30);

            return new Host
            {
                Sim       = sim,
                Recorder  = recorder,
                Streamer  = streamer,
                Broadcast = new BattleTapeBroadcast(sim, streamer, new FakeAuthority { Role = role }),
            };
        }

        private static void Frame(Host h, LoopbackNetwork net)
        {
            h.Sim.Tick(SimConstants.TickDelta);
            h.Recorder.CaptureCurrentState();
            h.Broadcast.Tick();
            net.PollAll();
        }

        private static bool TryFindOutcome(BattleTape tape, out BattleOutcome outcome)
        {
            var events = new List<TapeEvent>();
            tape.CollectEvents(0, tape.FrontTick, events);

            foreach (TapeEvent e in events)
            {
                if (e.Kind != TapeEventKind.BattleEnded) continue;
                outcome = tape.GetOutcome(e.PayloadIndex);
                return true;
            }

            outcome = BattleOutcome.Ongoing;
            return false;
        }

        /// <summary>Лента с готовыми кадрами — для проверок, которым живой бой не нужен.</summary>
        private static BattleTape FilledTape(int ticks)
        {
            var tape = new BattleTape(windowTicks: 512);
            for (int tick = 0; tick < ticks; tick++)
                tape.CaptureSnapshots(tick, new List<UnitSnapshot> { Snapshot(1), Snapshot(2) });
            return tape;
        }

        private static UnitSnapshot Snapshot(int id) =>
            new UnitSnapshot(
                id, team: 0, position: Vector2.zero, previousPosition: Vector2.zero,
                currentHp: 100f, maxHp: 100f, currentShield: 0f, currentResource: 0f, maxResource: 0f,
                size: 1f, phase: AttackPhase.Idle, windupTicks: 0, windupRemaining: 0,
                attackCooldownTicks: 0, targetId: -1, effectTagMask: EffectTag.None, isDead: false,
                attackRange: 1.5f, canAct: true);

        // Id назначает фабрика, а собранному руками юниту его обязан задать тест: два нуля означали бы
        // одну запись в реестре на двоих — и «команда не та» вместо честного «паспорт не доехал».
        private static RuntimeUnit Unit(int team, float x, int id)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 500f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat,  20f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat,   1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat,   1.5f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat,   3f),
            });

            return new RuntimeUnit
            {
                Id                   = id,
                Team                 = team,
                Stats                = stats,
                CurrentHP            = 500f,
                Position             = new Vector2(x, 0f),
                PreviousPosition     = new Vector2(x, 0f),
                AutoAttackDamageType = DamageType.Slash,
            };
        }

        private sealed class FakeAuthority : IBattleAuthority
        {
            public BattleRole Role { get; set; } = BattleRole.Solo;
            public bool SimulatesLocally => Role != BattleRole.Guest;
        }

        /// <summary>Узел, глотающий заданное число ближайших отправок: канал с потерей на разрыве.</summary>
        private sealed class DroppingNode : INetTransport
        {
            private readonly INetTransport _inner;

            public DroppingNode(INetTransport inner) => _inner = inner;

            public int DropNextSends { get; set; }

            public bool IsRunning               => _inner.IsRunning;
            public int  LocalPeerId             => _inner.LocalPeerId;
            public bool IsHost                  => _inner.IsHost;
            public int  MaxReliableMessageBytes => _inner.MaxReliableMessageBytes;

            public event System.Action<int> PeerConnected
            {
                add    => _inner.PeerConnected += value;
                remove => _inner.PeerConnected -= value;
            }

            public event System.Action<int> PeerDisconnected
            {
                add    => _inner.PeerDisconnected += value;
                remove => _inner.PeerDisconnected -= value;
            }

            public event System.Action<int, System.ArraySegment<byte>> MessageReceived
            {
                add    => _inner.MessageReceived += value;
                remove => _inner.MessageReceived -= value;
            }

            public void Send(int peerId, System.ArraySegment<byte> payload, NetDelivery delivery)
            {
                if (Swallow()) return;
                _inner.Send(peerId, payload, delivery);
            }

            public void SendToAll(System.ArraySegment<byte> payload, NetDelivery delivery)
            {
                if (Swallow()) return;
                _inner.SendToAll(payload, delivery);
            }

            public void Poll()     => _inner.Poll();
            public void Shutdown() => _inner.Shutdown();

            private bool Swallow()
            {
                if (DropNextSends <= 0) return false;
                DropNextSends--;
                return true;
            }
        }

        private sealed class FakeContent : IContentDatabase
        {
            public bool TryGet<T>(string id, out T def) where T : ContentDefinition
            {
                def = null;
                return false;
            }

            public IReadOnlyList<T> All<T>() where T : ContentDefinition => System.Array.Empty<T>();
        }
    }
}
