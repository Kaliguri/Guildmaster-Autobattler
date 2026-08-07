using Guildmaster.Data.Definitions;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Core
{
    /// <summary>
    /// Кем игрок вправе распоряжаться в каждом виде мероприятия — и по скольким сторонам сеанс
    /// рассаживает участников.
    /// </summary>
    /// <remarks>
    /// <b>Инвариант живёт в тесте, потому что нарушается ТИХО и в другом файле.</b> Сперва «только
    /// своих» было булем с умолчанием <c>false</c>, и кампания, чей пресет появился раньше самого
    /// флага, молча разрешала таскать врагов (наход. Макса 05.08.2026). Потом флаг ей проставили — и
    /// он же оказался источником числа сторон, по которым сеанс раскладывает игроков: гость уехал на
    /// сторону врага и стал двигать монстров вместо своего отряда (наход. Макса 07.08.2026). Оба раза
    /// ломалось на живом прогоне вдвоём, и ни компилятор, ни ревью этого не видели.
    /// <para>Поэтому здесь проверяются ОБА следствия владения стороной, а не одно: право
    /// (<see cref="ActivitySetup.MayCommandSide"/>) и раскладка
    /// (<see cref="ActivitySetup.SidesAreDealt"/>).</para>
    /// </remarks>
    [TestFixture]
    public class ActivityPresetRightsTests
    {
        private const int Mine    = 0;
        private const int TheirsB = 1;

        [Test]
        public void В_кампании_чужую_сторону_трогать_нельзя()
        {
            ActivitySetup campaign = ActivitySetup.Campaign;

            Assert.AreEqual(OpposingSide.Encounter, campaign.Opposition,
                "в кампании врагов приносит энкаунтер — они не наши ни в каком смысле");
            Assert.IsTrue(campaign.MayCommandSide(Mine, Mine), "свой отряд игрок расставляет сам");
            Assert.IsFalse(campaign.MayCommandSide(TheirsB, Mine),
                "врагов расставлять нельзя: их привёл узел, а не игрок");
        }

        [Test]
        public void В_кампании_напарники_играют_за_ОДНУ_сторону()
        {
            // Тот самый инвариант, которого не было 07.08.2026. Раздели тут участников — и второй
            // игрок получит во владение монстров, а свой отряд трогать не сможет.
            Assert.IsFalse(ActivitySetup.Campaign.SidesAreDealt,
                "кооп в кампании — это союзники на одной стороне, делить их не по чему");
        }

        [Test]
        public void В_матче_чужой_строй_скрыт_и_неприкосновенен()
        {
            ActivitySetup pvp = ActivitySetup.Pvp;

            Assert.AreEqual(OpposingSide.Player, pvp.Opposition, "в матче вторую сторону держит игрок");
            Assert.IsTrue(pvp.SidesAreDealt, "в матче участников разводят по сторонам — иначе не с кем играть");
            Assert.IsFalse(pvp.MayCommandSide(TheirsB, Mine), "чужим строем распоряжается второй игрок");
            Assert.IsTrue(pvp.HideOpponent, "в PvP подглядывать в чужой строй нельзя");
        }

        [Test]
        public void На_Ристалище_обе_стороны_наши_и_это_НАМЕРЕННО()
        {
            // Единственное место, где «чужих» нет вовсе: вторая сторона ничья, и собирает её тот же
            // игрок. Если этот тест однажды покраснеет, чинить надо НЕ его.
            ActivitySetup grounds = ActivitySetup.ProvingGrounds;

            Assert.AreEqual(OpposingSide.Unclaimed, grounds.Opposition,
                "на площадке вторую сторону не держит никто — забирать её не у кого");
            Assert.IsTrue(grounds.MayCommandSide(TheirsB, Mine),
                "на площадке игрок собирает обе стороны — запрет здесь сделал бы её бесполезной");
            Assert.IsFalse(grounds.SidesAreDealt,
                "на площадке играют заодно: делить участников по сторонам не по чему");
            Assert.IsFalse(grounds.HideOpponent,
                "на площадке оба состава видны: она для того и есть, чтобы сравнивать");
        }

        [Test]
        public void Площадка_с_заказанным_составом_остаётся_площадкой()
        {
            // Дев-срез входит сюда же, но своим конструктором — и однажды уже разъехался бы с
            // пресетом, будь у владельца стороны умолчание.
            ActivitySetup ordered = ActivitySetup.GroundsWith(default);

            Assert.AreEqual(OpposingSide.Unclaimed, ordered.Opposition,
                "заказ состава не меняет того, чья вторая сторона");
            Assert.IsTrue(ordered.MayCommandSide(TheirsB, Mine));
        }
    }
}
