using System.Collections.Generic;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using Guildmaster.Net;
using Guildmaster.Net.Tape;
using Guildmaster.Net.Transport;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Подключение посреди боя: бой ждёт напарника, но не бесконечно.
    /// </summary>
    /// <remarks>
    /// Три обещания, и все три живут между файлами: паузу поднимает просьба о ленте (стример), снимает
    /// её опустевшая очередь плюс отсчёт, а таймаут защищает от гостя, который отвалился ровно в момент
    /// подключения. Проверять это руками значило бы каждый раз звать второго человека и ждать по
    /// тридцать секунд.
    /// </remarks>
    public sealed class MidBattleJoinHoldTests
    {
        [Test]
        public void FreshBattle_DoesNotHold()
        {
            var fix = new Fixture();

            // Гость вошёл вместе с началом боя: истории нет, догружать нечего.
            fix.Guest.RequestWholeBattle();
            fix.Net.PollAll();

            Assert.IsFalse(fix.Hold.Holding, "держать некого — пауза на штатном входе не нужна");
            Assert.IsFalse(fix.Relay.IsPaused);
        }

        [Test]
        public void JoinMidBattle_HoldsUntilTapeIsSent_ThenCountsDown()
        {
            var fix = new Fixture();
            fix.PlayBattle(ticks: 150); // пять чанков истории

            fix.Guest.RequestWholeBattle();
            fix.Net.PollAll();

            Assert.IsTrue(fix.Hold.Holding, "кто-то догружается — бой ждёт");
            Assert.IsTrue(fix.Relay.IsPaused, "и пауза общая, её видят оба");

            // Лента ещё в очереди — сколько ни жди, отсчёт не начнётся.
            fix.Hold.Advance(1f);
            Assert.AreEqual(0f, fix.Hold.CountdownLeft, "отсчёт не начинается, пока лента не ушла");
            Assert.IsTrue(fix.Relay.IsPaused);

            fix.Streamer.PumpBackfill();
            fix.Net.PollAll();
            Assert.AreEqual(0, fix.Streamer.BackfillRemaining, "вся история отправлена");

            fix.Hold.Advance(0.1f);
            Assert.Greater(fix.Hold.CountdownLeft, 0f, "теперь идёт отсчёт");
            Assert.IsTrue(fix.Relay.IsPaused, "и до его конца бой всё ещё стоит");

            fix.Hold.Advance(MidBattleJoinHold.CountdownSeconds);
            Assert.IsFalse(fix.Hold.Holding);
            Assert.IsFalse(fix.Relay.IsPaused, "отсчёт кончился — играем дальше");
        }

        [Test]
        public void GuestVanishesWhileLoading_BattleResumesAnyway()
        {
            var fix = new Fixture();
            fix.PlayBattle(ticks: 150);

            fix.Guest.RequestWholeBattle();
            fix.Net.PollAll();
            Assert.IsTrue(fix.Relay.IsPaused);

            // Напарник исчез: очередь не двигается, потому что качать её больше некому.
            fix.Hold.Advance(MidBattleJoinHold.TimeoutSeconds + 1f);

            Assert.IsFalse(fix.Hold.Holding, "ждать вечно нельзя — человека уже нет");
            Assert.IsFalse(fix.Relay.IsPaused, "бой продолжается без него");
        }

        // ── Обвязка ──────────────────────────────────────────────────────────
        private sealed class Fixture
        {
            public readonly LoopbackNetwork    Net = new LoopbackNetwork();
            public readonly TapeStreamer       Streamer;
            public readonly TapeIntake         Guest;
            public readonly BattleControlRelay Relay;
            public readonly MidBattleJoinHold  Hold;

            private readonly BattleTape _tape = new BattleTape(windowTicks: 512);

            public Fixture()
            {
                INetTransport host  = Net.CreateNode();
                INetTransport guest = Net.CreateNode();

                Streamer = new TapeStreamer(host, _tape, ticksPerChunk: 30);
                Relay    = new BattleControlRelay(host);
                Hold     = new MidBattleJoinHold(Streamer, Relay);

                Guest = new TapeIntake(guest, new TapeChunkReader(new BattleTape(512), new EmptyContent()));
            }

            /// <summary>Контента в этих тестах нет: проверяем ожидание, а не разбор паспортов.</summary>
            private sealed class EmptyContent : IContentDatabase
            {
                public bool TryGet<T>(string id, out T def) where T : ContentDefinition
                {
                    def = null;
                    return false;
                }

                public IReadOnlyList<T> All<T>() where T : ContentDefinition => System.Array.Empty<T>();
            }

            /// <summary>Прожить бой: записать кадры и раздать их (гостя ещё нет — уходит в пустоту).</summary>
            public void PlayBattle(int ticks)
            {
                for (int tick = 0; tick < ticks; tick++)
                    _tape.CaptureTick(tick, System.Array.Empty<Guildmaster.Combat.RuntimeUnit>(), null);

                Streamer.Pump(readyThroughTick: ticks - 1);
                Net.PollAll();
            }
        }
    }
}
