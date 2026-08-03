using Guildmaster.Data.Definitions;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// «Где мы» по сети: вид мероприятия, границы арены и фаза боя, которым гость следует.
    /// <para><b>Что здесь проверяется и чего здесь НЕТ.</b> Проверяется формат и правило отказа —
    /// то, что ломается молча. Сам цикл «хост открыл место → гость открыл такое же» тестом не
    /// закрыт: обе половины работают через <c>ActivityHost</c>, который рождает скоупы от сеанса, а
    /// это уже живая игра. Проверять его надо вдвоём — и до второго Steam-аккаунта эта проверка
    /// не делается ничем, кроме глаз.</para>
    /// </summary>
    public sealed class ActivityStateTests
    {
        [Test]
        public void State_SurvivesTheRoundTrip()
        {
            var sent = new ActivityState(ActivityKind.ProvingGrounds, hideOpponent: true,
                ownUnitsOnly: true, battleOpen: true, phase: BattlePhase.Fighting);

            var writer = new NetByteWriter(16);
            Assert.IsTrue(ActivityStateCodec.TryRead(ActivityStateCodec.Write(in sent, writer),
                out ActivityState got));

            Assert.AreEqual(sent, got, "Все пять полей пережили дорогу");
        }

        /// <summary>
        /// Неизвестный вид мероприятия — это расхождение сборок. Встать «примерно там же» нельзя: гость
        /// оказался бы в месте, которого у хоста нет, и разошёлся бы с ним молча.
        /// </summary>
        [Test]
        public void UnknownKind_IsRefused()
        {
            var writer = new NetByteWriter(16);
            writer.WriteByte(200);        // вида с таким номером в этой сборке нет
            writer.WriteBool(false);
            writer.WriteBool(false);
            writer.WriteBool(true);
            writer.WriteByte((byte)BattlePhase.Fighting);

            Assert.IsFalse(ActivityStateCodec.TryRead(writer.WrittenSegment, out _));
        }

        [Test]
        public void TruncatedMessage_IsRefused()
        {
            var writer = new NetByteWriter(16);
            writer.WriteByte((byte)ActivityKind.Campaign);
            writer.WriteBool(false);

            Assert.IsFalse(ActivityStateCodec.TryRead(writer.WrittenSegment, out _));
        }

        /// <summary>
        /// Состояние сравнивается целиком: на этом стоит и «не слать одно и то же», и «не открывать
        /// заново уже открытое». Забытое поле в сравнении выглядело бы как замерший гость.
        /// </summary>
        [Test]
        public void EveryFieldCounts_WhenComparing()
        {
            var baseline = new ActivityState(ActivityKind.Campaign, false, false, true, BattlePhase.Fighting);

            Assert.AreNotEqual(baseline,
                new ActivityState(ActivityKind.ProvingGrounds, false, false, true, BattlePhase.Fighting));
            Assert.AreNotEqual(baseline,
                new ActivityState(ActivityKind.Campaign, true, false, true, BattlePhase.Fighting));
            Assert.AreNotEqual(baseline,
                new ActivityState(ActivityKind.Campaign, false, true, true, BattlePhase.Fighting));
            Assert.AreNotEqual(baseline,
                new ActivityState(ActivityKind.Campaign, false, false, false, BattlePhase.Fighting));
            Assert.AreNotEqual(baseline,
                new ActivityState(ActivityKind.Campaign, false, false, true, BattlePhase.Deployment));
        }

        [Test]
        public void Nowhere_MeansNoActivityAndNoArena()
        {
            ActivityState nowhere = ActivityState.Nowhere;

            Assert.AreEqual(ActivityKind.None, nowhere.Kind);
            Assert.IsFalse(nowhere.BattleOpen);
            Assert.AreEqual(BattlePhase.None, nowhere.Phase);
        }
    }
}
