using System.Collections.Generic;
using Guildmaster.Core.Net;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Общее согласие по проводу: голос участника и объявленный счёт.
    /// </summary>
    /// <remarks>
    /// Заведены вместе с <see cref="SessionRosterCodecTests"/> и по той же причине: формат этого канала
    /// жил в трёх местах — писатель у хозяина, читатель у гостя, третий разбор в тесте, — то есть
    /// держался ничем, кроме внимательности. Соседний канал с такой же раскладкой разъехался и унёс
    /// весь состав сеанса; здесь просто не успело.
    /// </remarks>
    public sealed class DecisionCodecTests
    {
        [Test]
        public void Vote_SurvivesTheRoundTrip()
        {
            var payload = DecisionCodec.WriteVote("relic.trash_hexer", new NetByteWriter(32));

            Assert.IsTrue(DecisionCodec.IsVote(payload), "первый байт объявляет, чьё это сообщение");
            Assert.IsFalse(DecisionCodec.IsTally(payload));
            Assert.IsTrue(DecisionCodec.TryReadVote(payload, out string option));
            Assert.AreEqual("relic.trash_hexer", option);
        }

        /// <summary>Снятый голос — пустая строка, и она обязана доехать пустой, а не потеряться.</summary>
        [Test]
        public void EmptyVote_SurvivesTheRoundTrip()
        {
            Assert.IsTrue(DecisionCodec.TryReadVote(
                DecisionCodec.WriteVote(DecisionOptions.None, new NetByteWriter(32)), out string option));
            Assert.AreEqual(DecisionOptions.None, option);
        }

        [Test]
        public void Tally_SurvivesTheRoundTrip_WithEveryField()
        {
            var sent = new List<PlayerChoice>
            {
                new PlayerChoice(0, "agree"),
                new PlayerChoice(1, "relic.necromancer"),
            };

            var payload = DecisionCodec.WriteTally("reward.pick", required: 2, fired: true, sent,
                                                   new NetByteWriter(64));

            Assert.IsTrue(DecisionCodec.IsTally(payload));
            Assert.IsFalse(DecisionCodec.IsVote(payload));

            var got = new List<PlayerChoice>();
            Assert.IsTrue(DecisionCodec.TryReadTally(payload, got, out string key, out int required,
                out bool fired));

            Assert.AreEqual("reward.pick", key,      "ключ едет строкой — он и есть смысл действия");
            Assert.AreEqual(2,             required, "сколько голосов нужно");
            Assert.IsTrue(fired,                     "признак срабатывания — по нему гость закрывает экран");

            Assert.AreEqual(sent.Count, got.Count, "голоса доехали все");
            for (int i = 0; i < sent.Count; i++)
            {
                Assert.AreEqual(sent[i].PlayerId, got[i].PlayerId, $"чей голос {i}");
                Assert.AreEqual(sent[i].Option,   got[i].Option,   $"за что голос {i}");
            }
        }

        /// <summary>Голос не должен читаться как счёт, и наоборот: раньше стороны различались по ДЛИНЕ.</summary>
        [Test]
        public void VoteAndTally_AreNotConfusedWithEachOther()
        {
            var vote  = DecisionCodec.WriteVote("agree", new NetByteWriter(32));
            Assert.IsFalse(DecisionCodec.TryReadTally(vote, new List<PlayerChoice>(), out _, out _, out _));

            var tally = DecisionCodec.WriteTally("battle.start", 2, false, new List<PlayerChoice>(),
                                                 new NetByteWriter(32));
            Assert.IsFalse(DecisionCodec.TryReadVote(tally, out _));
        }

        /// <summary>
        /// Обрезанное объявление не должно оставить половину счёта: прежний счёт честнее половины нового.
        /// </summary>
        [Test]
        public void TruncatedTally_LeavesTheOldOneAlone()
        {
            var writer = new NetByteWriter(32);
            writer.WriteByte(DecisionWire.Tally);
            writer.WriteByte(2);
            writer.WriteBool(false);
            writer.WriteString("battle.start");
            writer.WriteByte(2);              // обещано два голоса, а пакет кончился
            writer.WriteByte(0);

            var kept = new List<PlayerChoice> { new PlayerChoice(0, "agree") };

            Assert.IsFalse(DecisionCodec.TryReadTally(writer.WrittenSegment, kept, out _, out _, out _));
            Assert.AreEqual(1, kept.Count);
            Assert.AreEqual("agree", kept[0].Option);
        }

        [Test]
        public void ExtraBytes_AreRefused()
        {
            var writer = new NetByteWriter(32);
            DecisionCodec.WriteVote("agree", writer);
            writer.WriteByte(42);             // поле из другой версии формата

            Assert.IsFalse(DecisionCodec.TryReadVote(writer.WrittenSegment, out _));
        }

        [Test]
        public void EmptyPayload_IsNeitherVoteNorTally()
        {
            var empty = new System.ArraySegment<byte>(new byte[0]);

            Assert.IsFalse(DecisionCodec.IsVote(empty));
            Assert.IsFalse(DecisionCodec.IsTally(empty));
        }
    }
}
