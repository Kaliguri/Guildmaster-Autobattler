using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Services;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Пауза приходит из сети, а останавливает бой время: мост — единственное место, где одно
    /// становится другим.
    /// </summary>
    /// <remarks>
    /// Инвариант кросс-файловый и потому живёт тестом: состоянием паузы владеет
    /// <see cref="BattleControlRelay"/>, применяет его <c>TimeScaleService</c>, и второй путь
    /// применения означал бы два источника правды о том, стоит ли бой. Сломать это можно одной
    /// строкой <c>_time.SetPaused(...)</c> в любом обработчике ввода — и в соло ничего не заметно,
    /// а в коопе нажатие напарника перестаёт доходить.
    /// </remarks>
    public sealed class NetPauseBridgeTests
    {
        private float _timeScaleBefore;

        [SetUp]
        public void SetUp() => _timeScaleBefore = Time.timeScale;

        [TearDown]
        public void TearDown() => Time.timeScale = _timeScaleBefore;

        [Test]
        public void IntentFromTheRelay_StopsTheShow()
        {
            Fixture f = Build();

            f.Relay.RequestPause(true);

            Assert.IsTrue(f.Time.IsPaused, "Объявленная пауза дошла до времени");
            Assert.AreEqual(0f, Time.timeScale, 1e-6f, "А из него — до симуляции: она копит deltaTime");

            f.Relay.RequestPause(false);
            Assert.IsFalse(f.Time.IsPaused, "И снимается тем же путём");
        }

        // Пауза, поставленная НАПАРНИКОМ, приходит тем же событием: у неё нет отдельного маршрута, и
        // именно поэтому её не приходится применять второй раз в другом месте.
        [Test]
        public void PauseFromTheHost_ArrivesTheSameWay()
        {
            var net   = new LoopbackNetwork();
            var host  = net.CreateNode();
            var guest = net.CreateNode();

            var hostRelay  = new BattleControlRelay(host);
            var guestRelay = new BattleControlRelay(guest);

            Fixture f = Build(guestRelay);

            hostRelay.RequestPause(true);
            net.PollAll();

            Assert.IsTrue(guestRelay.IsPaused, "Гость услышал объявление хоста");
            Assert.IsTrue(f.Time.IsPaused,     "И показ у него встал");
        }

        // Через границу боя пауза не переносится, и сбрасываются ОБА владельца сразу: релей — чтобы
        // следующий интент не оказался «уже в этом состоянии» и не потерялся молча, время — чтобы
        // новый бой не начался замороженным.
        [Test]
        public void PhaseChange_ClearsBothOwners()
        {
            Fixture f = Build();

            f.Relay.RequestPause(true);
            Assert.IsTrue(f.Time.IsPaused);

            f.Clock.SetPhase(BattlePhase.Fighting);

            Assert.IsFalse(f.Relay.IsPaused, "Релей забыл паузу прошлого боя");
            Assert.IsFalse(f.Time.IsPaused,  "И новый бой начался живым");

            // А тумблер после этого работает с первого нажатия — ровно то, что ломалось бы при
            // сбросе одного владельца из двух.
            f.Relay.RequestPause(!f.Relay.IsPaused);
            Assert.IsTrue(f.Time.IsPaused);
        }

        [Test]
        public void Dispose_LetsGoOfTheRelay()
        {
            Fixture f = Build();

            f.Bridge.Dispose();
            f.Relay.RequestPause(true);

            Assert.IsFalse(f.Time.IsPaused, "Выгруженный бой чужие объявления уже не применяет");
        }

        // ── помощники ────────────────────────────────────────────────────────────

        private sealed class Fixture
        {
            public BattleControlRelay Relay;
            public TimeScaleService   Time;
            public FakeClock          Clock;
            public NetPauseBridge     Bridge;
        }

        private static Fixture Build(BattleControlRelay relay = null)
        {
            relay ??= new BattleControlRelay(new LoopbackNetwork().CreateNode());

            var time   = new TimeScaleService(audio: null);
            var clock  = new FakeClock();
            var bridge = new NetPauseBridge(relay, time, clock, audio: null);
            bridge.Start();

            return new Fixture { Relay = relay, Time = time, Clock = clock, Bridge = bridge };
        }

        private sealed class FakeClock : IBattleClock
        {
            public BattlePhase Phase { get; private set; } = BattlePhase.None;
            public float ElapsedSeconds => 0f;

            public event Action PhaseChanged;

            public void SetPhase(BattlePhase phase)
            {
                if (Phase == phase) return;
                Phase = phase;
                PhaseChanged?.Invoke();
            }

            public void RequestStart() { }
        }
    }
}
