using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using Guildmaster.Net;
using Guildmaster.Net.Tape;
using Guildmaster.Net.Transport;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Раздача боевой ленты (ТЗ кооп-вертикали §5.3): хост режет готовые тики на чанки и шлёт их, гость
    /// складывает их в свою ленту и просит повторить то, что не доехало.
    /// <para>Обещание раздачи ровно одно и проверяется здесь целиком: <b>у гостя тот же бой</b> — те же
    /// кадры и те же события, — и оно держится при переупорядочивании, дублях и потере чанка. Если бы
    /// оно держалось только на идеальном канале, кооп ломался бы ровно там, где его нельзя отладить: на
    /// чужом интернете.</para>
    /// </summary>
    public sealed class TapeDeliveryTests
    {
        [Test]
        public void GuestSeesTheSameBattle_FramesAndEvents()
        {
            var net   = new LoopbackNetwork();
            var host  = net.CreateNode();
            var guest = net.CreateNode();

            BattleTape source = HostTape(ticks: 70);
            var streamer = new TapeStreamer(host, source, ticksPerChunk: 30);

            var target = new BattleTape(windowTicks: 256);
            var intake = new TapeIntake(guest, new TapeChunkReader(target, new FakeContent()));

            streamer.Flush(readyThroughTick: 69);
            net.PollAll();

            Assert.AreEqual(3, streamer.SentChunkCount, "Два полных чанка по 30 тиков и хвост из десяти");
            Assert.AreEqual(3, intake.AppliedChunkCount, "Все три уложены");
            Assert.AreEqual(0, intake.MissingCount);

            for (int tick = 0; tick < 70; tick++)
            {
                Assert.IsTrue(target.TryGetFrame(tick, out IReadOnlyList<UnitSnapshot> units),
                    $"Кадр тика {tick} доехал");
                Assert.AreEqual(2, units.Count, $"И в нём оба юнита (тик {tick})");
            }

            Assert.AreEqual(source.EventCount, target.EventCount, "События доехали все");
        }

        // Хвост короче чанка ждёт Flush: иначе конец боя дробился бы на однотиковые посылки, а с ним и
        // событие исхода — самое важное в ленте.
        [Test]
        public void PartialTail_WaitsForFlush()
        {
            var net   = new LoopbackNetwork();
            var host  = net.CreateNode();
            var guest = net.CreateNode();

            BattleTape source = HostTape(ticks: 40);
            var streamer = new TapeStreamer(host, source, ticksPerChunk: 30);

            var target = new BattleTape(windowTicks: 128);
            var intake = new TapeIntake(guest, new TapeChunkReader(target, new FakeContent()));

            streamer.Pump(readyThroughTick: 39);
            net.PollAll();

            Assert.AreEqual(1, intake.AppliedChunkCount, "Уехал только полный чанк");
            Assert.IsFalse(target.TryGetFrame(35, out _), "Хвост ещё у хоста");

            streamer.Flush(readyThroughTick: 39);
            net.PollAll();

            Assert.AreEqual(2, intake.AppliedChunkCount);
            Assert.IsTrue(target.TryGetFrame(39, out _), "После Flush доехал и хвост");
        }

        // Переупорядочивание и дубли — норма стыка реконнекта, а не поломка: чанк самодостаточен, и
        // порядок применения на картинку не влияет.
        [Test]
        public void ReorderAndDuplicates_ChangeNothing()
        {
            var net       = new LoopbackNetwork();
            var hostInner = net.CreateNode();
            var guestInner = net.CreateNode();

            var profile = new ChaosProfile
            {
                MinDelaySteps   = 1,
                MaxDelaySteps   = 4,   // разброс и даёт переупорядочивание
                DuplicateChance = 1f,  // каждый чанк приходит дважды
            };
            var host  = new ChaosTransport(hostInner, profile, seed: 20260801UL);
            var guest = new ChaosTransport(guestInner, profile, seed: 4242UL);

            BattleTape source = HostTape(ticks: 90);
            var streamer = new TapeStreamer(host, source, ticksPerChunk: 30);

            var target = new BattleTape(windowTicks: 256);
            var intake = new TapeIntake(guest, new TapeChunkReader(target, new FakeContent()));

            streamer.Flush(readyThroughTick: 89);
            for (int step = 0; step < 16; step++) { host.Poll(); guest.Poll(); }

            Assert.AreEqual(3, intake.AppliedChunkCount, "Дубли применились ровно по одному разу");
            Assert.AreEqual(0, intake.MissingCount, "И ни одна дыра не осталась");
            Assert.AreEqual(source.EventCount, target.EventCount);
            Assert.IsTrue(target.TryGetFrame(89, out _), "Последний тик на месте");
        }

        [Test]
        public void LostChunk_IsAskedFor_AndResent()
        {
            var net   = new LoopbackNetwork();
            var host  = new DroppingTransport(net.CreateNode());
            var guest = net.CreateNode();

            BattleTape source = HostTape(ticks: 90);
            var streamer = new TapeStreamer(host, source, ticksPerChunk: 30);

            var target = new BattleTape(windowTicks: 256);
            var intake = new TapeIntake(guest, new TapeChunkReader(target, new FakeContent()));

            host.DropNextSends = 1; // первый чанк не доедет
            streamer.Flush(readyThroughTick: 89);
            net.PollAll();

            Assert.AreEqual(2, intake.AppliedChunkCount, "Первый чанк потерян");
            Assert.AreEqual(1, intake.MissingCount, "И дыра видна по номеру, а не по картинке");
            Assert.IsFalse(target.TryGetFrame(0, out _));

            intake.RequestMissing(now: 1f);
            net.PollAll();  // запрос доехал до хоста
            net.PollAll();  // повтор доехал до гостя

            Assert.AreEqual(1, streamer.ResentChunkCount, "Хост повторил ровно один чанк");
            Assert.AreEqual(0, intake.MissingCount, "Дыра закрылась");
            Assert.IsTrue(target.TryGetFrame(0, out _), "И первые тики появились у гостя");
        }

        // На просевшем канале запросы повтора сами становятся нагрузкой, поэтому один и тот же номер
        // спрашивается не чаще, чем раз в интервал.
        [Test]
        public void ResendRequests_AreRateLimited()
        {
            var net   = new LoopbackNetwork();
            var host  = new DroppingTransport(net.CreateNode());
            var guest = net.CreateNode();

            BattleTape source = HostTape(ticks: 60);
            var streamer = new TapeStreamer(host, source, ticksPerChunk: 30);

            var target = new BattleTape(windowTicks: 128);
            var intake = new TapeIntake(guest, new TapeChunkReader(target, new FakeContent()));

            host.DropNextSends = 1;
            streamer.Flush(readyThroughTick: 59);
            net.PollAll();

            intake.RequestMissing(now: 0f, retrySeconds: 0.5f);
            intake.RequestMissing(now: 0.2f, retrySeconds: 0.5f);
            Assert.AreEqual(1, intake.ResendRequestCount, "Второй запрос в пределах интервала не ушёл");

            intake.RequestMissing(now: 0.7f, retrySeconds: 0.5f);
            Assert.AreEqual(2, intake.ResendRequestCount, "А после интервала — ушёл");
        }

        [Test]
        public void ForeignChannel_IsIgnored_NotMisread()
        {
            var net   = new LoopbackNetwork();
            var host  = net.CreateNode();
            var guest = net.CreateNode();

            var target = new BattleTape(windowTicks: 32);
            var intake = new TapeIntake(guest, new TapeChunkReader(target, new FakeContent()));

            TapeChunkStatus rejected = TapeChunkStatus.Ok;
            intake.ChunkRejected += (status, _) => rejected = status;

            byte[] envelope = null;
            host.SendToAll(
                NetEnvelope.Wrap(NetChannel.Presence, new ArraySegment<byte>(new byte[] { 1, 2, 3 }), ref envelope),
                NetDelivery.Unreliable);
            net.PollAll();

            Assert.AreEqual(0, intake.AppliedChunkCount, "Присутствие не пытались прочитать как чанк");
            Assert.AreEqual(TapeChunkStatus.Ok, rejected, "И отказа не случилось — чужой канал просто не наш");
        }

        // ── помощники ────────────────────────────────────────────────────────────

        /// <summary>Лента хоста: два юнита ходят навстречу, между делом бьют друг друга.</summary>
        private static BattleTape HostTape(int ticks)
        {
            var tape = new BattleTape(windowTicks: 512);

            for (int tick = 0; tick < ticks; tick++)
            {
                var units = new List<UnitSnapshot>
                {
                    Unit(1, hp: 100f - tick * 0.5f, position: new Vector2(-4f + tick * 0.05f, 0f)),
                    Unit(2, hp: 90f  - tick * 0.4f, position: new Vector2( 4f - tick * 0.05f, 0f), team: 1),
                };
                tape.CaptureSnapshots(tick, units);

                if (tick % 15 != 0) continue;

                var damage = new DamageResult(hpDamage: 7.5f, shieldDamage: 0f, killedTarget: false,
                    sourceKind: DamageSourceKind.AutoAttack, type: DamageType.Slash,
                    vulnerability: 1f, mitigated: 1.25f);
                tape.RecordDamage(tick, sourceId: 1, targetId: 2, in damage);
            }

            return tape;
        }

        private static UnitSnapshot Unit(int id, float hp, Vector2 position = default, int team = 0) =>
            new UnitSnapshot(
                id, team, position, position,
                currentHp: hp, maxHp: 150f, currentShield: 0f, currentResource: 25f, maxResource: 50f,
                size: 1f, phase: AttackPhase.Idle, windupTicks: 0, windupRemaining: 0,
                attackCooldownTicks: 12, targetId: -1, effectTagMask: EffectTag.None, isDead: false,
                attackRange: 1.5f, canAct: true);

        /// <summary>
        /// Транспорт, глотающий заданное число ближайших отправок. Chaos надёжные сообщения не теряет
        /// намеренно (их доставку обеспечивает транспорт), а нам нужен именно случай «чанк не доехал» —
        /// он бывает на разрыве соединения, и повтор по номеру существует ровно ради него.
        /// </summary>
        private sealed class DroppingTransport : INetTransport
        {
            private readonly INetTransport _inner;

            public DroppingTransport(INetTransport inner) => _inner = inner;

            /// <summary>Сколько ближайших отправок проглотить.</summary>
            public int DropNextSends { get; set; }

            public bool IsRunning               => _inner.IsRunning;
            public int  LocalPeerId             => _inner.LocalPeerId;
            public bool IsHost                  => _inner.IsHost;
            public int  MaxReliableMessageBytes => _inner.MaxReliableMessageBytes;

            public event Action<int> PeerConnected
            {
                add    => _inner.PeerConnected += value;
                remove => _inner.PeerConnected -= value;
            }

            public event Action<int> PeerDisconnected
            {
                add    => _inner.PeerDisconnected += value;
                remove => _inner.PeerDisconnected -= value;
            }

            public event Action<int, ArraySegment<byte>> MessageReceived
            {
                add    => _inner.MessageReceived += value;
                remove => _inner.MessageReceived -= value;
            }

            public void Send(int peerId, ArraySegment<byte> payload, NetDelivery delivery)
            {
                if (Swallow()) return;
                _inner.Send(peerId, payload, delivery);
            }

            public void SendToAll(ArraySegment<byte> payload, NetDelivery delivery)
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
            private readonly Dictionary<string, ContentDefinition> _byId = new Dictionary<string, ContentDefinition>();

            public void Add(ContentDefinition def) => _byId[def.Id] = def;

            public bool TryGet<T>(string id, out T def) where T : ContentDefinition
            {
                def = null;
                if (!_byId.TryGetValue(id, out ContentDefinition found)) return false;
                def = found as T;
                return def != null;
            }

            public IReadOnlyList<T> All<T>() where T : ContentDefinition => Array.Empty<T>();
        }
    }
}
