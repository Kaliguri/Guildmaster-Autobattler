using System.Collections.Generic;
using Guildmaster.Core.Players;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Состав сеанса по проводу: таблица от хозяина и представление от участника.
    /// </summary>
    /// <remarks>
    /// <b>Эти тесты появились после поломки, которую нечем было поймать (08.08.2026).</b> Формат канала
    /// жил в двух файлах — писатель у хозяина, читатель у гостя, — и правка, добавившая участнику место,
    /// тронула только хозяйскую половину. Хозяин стал писать шесть полей, гость читать пять; гость слал
    /// три, хозяин читал четыре. Обе стороны глотали разбор молча, и состав перестал доезжать вовсе:
    /// у гостя список участников пуст, у хозяина ник и цвет гостя не применяются никогда. Компиляция,
    /// рукопожатие и весь EditMode при этом были зелёными.
    /// <para>Поэтому первый тест здесь сверяет <b>каждое поле</b>, а не факт «что-то доехало»: именно
    /// поле, выпавшее из одной половины, и было дефектом.</para>
    /// </remarks>
    public sealed class SessionRosterCodecTests
    {
        [Test]
        public void Table_SurvivesTheRoundTrip_WithEveryField()
        {
            var sent = new List<SessionPlayer>
            {
                new SessionPlayer(0, "Хозяин", team: 0, colorIndex: 2, cursorSkinId: "cursor.classic",
                                  where: PlayerWhere.Arena),
                new SessionPlayer(1, "Гость",  team: 1, colorIndex: 5, cursorSkinId: "cursor.paw",
                                  where: PlayerWhere.Menu),
            };

            var got = new List<SessionPlayer>();
            Assert.IsTrue(SessionRosterCodec.TryReadTable(
                SessionRosterCodec.WriteTable(sent, new NetByteWriter(64)), got));

            Assert.AreEqual(sent.Count, got.Count, "участники доехали все");

            for (int i = 0; i < sent.Count; i++)
            {
                Assert.AreEqual(sent[i].Id,           got[i].Id,           $"номер участника {i}");
                Assert.AreEqual(sent[i].Name,         got[i].Name,         $"имя участника {i}");
                Assert.AreEqual(sent[i].Team,         got[i].Team,         $"сторона участника {i}");
                Assert.AreEqual(sent[i].ColorIndex,   got[i].ColorIndex,   $"цвет участника {i}");
                Assert.AreEqual(sent[i].CursorSkinId, got[i].CursorSkinId, $"скин курсора участника {i}");
                Assert.AreEqual(sent[i].Where,        got[i].Where,        $"место участника {i}");
            }
        }

        [Test]
        public void Intro_SurvivesTheRoundTrip_WithEveryField()
        {
            var sent = new SessionIntro("Гость", wantedColorIndex: 7, cursorSkinId: "cursor.paw",
                                        where: PlayerWhere.Map);

            Assert.IsTrue(SessionRosterCodec.TryReadIntro(
                SessionRosterCodec.WriteIntro(in sent, new NetByteWriter(64)), out SessionIntro got));

            Assert.AreEqual(sent.Name,             got.Name,             "ник из профиля");
            Assert.AreEqual(sent.WantedColorIndex, got.WantedColorIndex, "пожелание по цвету");
            Assert.AreEqual(sent.CursorSkinId,     got.CursorSkinId,     "скин курсора");
            Assert.AreEqual(sent.Where,            got.Where,            "место");
        }

        /// <summary>Пустой состав — законное состояние: сеанс поднят, никто ещё не вошёл.</summary>
        [Test]
        public void EmptyTable_IsValid()
        {
            var got = new List<SessionPlayer> { new SessionPlayer(9, "мусор", 0, 0) };

            Assert.IsTrue(SessionRosterCodec.TryReadTable(
                SessionRosterCodec.WriteTable(new List<SessionPlayer>(), new NetByteWriter(16)), got));
            Assert.AreEqual(0, got.Count, "старая таблица заменена целиком, а не дополнена");
        }

        /// <summary>
        /// Обрезанный пакет не должен оставить половину состава: по половине мы решили бы, что кто-то
        /// вышел, и перестали бы показывать его курсор.
        /// </summary>
        [Test]
        public void TruncatedTable_LeavesTheOldOneAlone()
        {
            var writer = new NetByteWriter(16);
            writer.WriteByte(2);              // обещано двое
            writer.WriteByte(0);              // а дальше пакет кончился
            writer.WriteByte(0);

            var kept = new List<SessionPlayer> { new SessionPlayer(0, "Хозяин", 0, 0) };

            Assert.IsFalse(SessionRosterCodec.TryReadTable(writer.WrittenSegment, kept));
            Assert.AreEqual(1, kept.Count, "прежняя таблица честнее половины новой");
            Assert.AreEqual("Хозяин", kept[0].Name);
        }

        /// <summary>
        /// Лишний хвост значит, что писали не этим кодеком, — ровно тот случай, что нас и сломал.
        /// Молча взять начало значило бы снова «встать примерно там же».
        /// </summary>
        [Test]
        public void ExtraBytes_AreRefused()
        {
            var writer = new NetByteWriter(64);
            var one = new List<SessionPlayer> { new SessionPlayer(0, "Хозяин", 0, 0, "", PlayerWhere.Menu) };
            SessionRosterCodec.WriteTable(one, writer);
            writer.WriteByte(42); // поле из другой версии формата

            Assert.IsFalse(SessionRosterCodec.TryReadTable(writer.WrittenSegment, new List<SessionPlayer>()));
        }

        /// <summary>Место с неизвестным номером — расхождение сборок: вставать «примерно там» нельзя.</summary>
        [Test]
        public void UnknownWhere_IsRefused()
        {
            var writer = new NetByteWriter(64);
            writer.WriteByte(1);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteString("Хозяин");
            writer.WriteString("");
            writer.WriteByte(200);            // места с таким номером в этой сборке нет

            Assert.IsFalse(SessionRosterCodec.TryReadTable(writer.WrittenSegment, new List<SessionPlayer>()));
        }

        [Test]
        public void TruncatedIntro_IsRefused()
        {
            var writer = new NetByteWriter(16);
            writer.WriteString("Гость");      // а цвета, скина и места нет

            Assert.IsFalse(SessionRosterCodec.TryReadIntro(writer.WrittenSegment, out _));
        }
    }
}
