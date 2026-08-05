using Guildmaster.Data.Definitions;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Core
{
    /// <summary>
    /// Кем игрок вправе распоряжаться в каждом виде мероприятия.
    /// </summary>
    /// <remarks>
    /// <b>Инвариант живёт в тесте, потому что нарушается умолчанием.</b> «Только своих» — обычный
    /// <c>bool</c> со значением по умолчанию <c>false</c>, и пресет, заведённый без него, молча
    /// разрешает трогать чужую сторону. Именно так и вышло с кампанией: её пресет появился раньше
    /// самого флага, и на живом прогоне 05.08.2026 врагов можно было таскать наравне со своим
    /// отрядом. Ни компилятор, ни ревью такого не видят — видно только в игре.
    /// </remarks>
    [TestFixture]
    public class ActivityPresetRightsTests
    {
        [Test]
        public void В_кампании_чужую_сторону_трогать_нельзя()
        {
            Assert.IsTrue(ActivitySetup.Campaign.OwnUnitsOnly,
                "в кампании врагов приносит энкаунтер — они не наши, и расставлять их игрок не должен");
        }

        [Test]
        public void В_матче_чужой_строй_скрыт_и_неприкосновенен()
        {
            Assert.IsTrue(ActivitySetup.Pvp.OwnUnitsOnly, "в PvP чужим строем распоряжается второй игрок");
            Assert.IsTrue(ActivitySetup.Pvp.HideOpponent, "в PvP подглядывать в чужой строй нельзя");
        }

        [Test]
        public void На_Ристалище_обе_стороны_наши_и_это_НАМЕРЕННО()
        {
            // Единственное место, где «чужих» нет вовсе: там противник — такие же киты игрока, и
            // расставляет их он сам. Если этот тест однажды покраснеет, чинить надо НЕ его.
            Assert.IsFalse(ActivitySetup.ProvingGrounds.OwnUnitsOnly,
                "на площадке игрок собирает обе стороны — запрет здесь сделал бы её бесполезной");
            Assert.IsFalse(ActivitySetup.ProvingGrounds.HideOpponent,
                "на площадке оба состава видны: она для того и есть, чтобы сравнивать");
        }
    }
}
