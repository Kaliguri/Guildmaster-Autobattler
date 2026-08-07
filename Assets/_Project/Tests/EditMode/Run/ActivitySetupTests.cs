using Guildmaster.Data.Definitions;
using Guildmaster.Game.Activity;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Контракт «где мы»: вид мероприятия — факт, названный при входе, и вне мероприятия его нет.
    /// Инвариант живёт между владельцем мероприятия и интерфейсом, который по нему решает, показывать
    /// ли панель забега и кнопку «Начать», — поэтому он в тесте.
    /// </summary>
    public sealed class ActivitySetupTests
    {
        /// <summary>
        /// Мероприятия нет — вид <c>None</c>, и это тот самый ответ, по которому UI прячет панель.
        /// Прежде «где мы» выводилось из наличия забега и состояния арены, и стоило владельцу второго
        /// признака уехать в боевой скоуп, панель пропадала целиком (наход. Макса 02.08.2026).
        /// </summary>
        [Test]
        public void Host_HasNoKindUntilSomethingIsOpened()
        {
            // Паблишер не нужен: мероприятия нет, а значит и говорить о его смене нечего.
            var host = new ActivityHost(new Guildmaster.Game.Session.SessionHost(), null);

            Assert.IsFalse(host.IsOpen);
            Assert.AreEqual(ActivityKind.None, host.Current.Kind);
            Assert.IsFalse(host.Current.IsOpen);
        }

        /// <summary>
        /// PvP — это Ристалище с двумя ограничениями, а не свой вид (решение Макса 02.08.2026). Тест
        /// держит именно это: код, который спросит «мы на площадке?», обязан отвечать «да» и в матче.
        /// </summary>
        [Test]
        public void Pvp_IsTheGroundsWithTwoRestrictions()
        {
            Assert.AreEqual(ActivityKind.ProvingGrounds, ActivitySetup.Pvp.Kind);
            Assert.IsTrue(ActivitySetup.Pvp.HideOpponent, "в матче чужой строй скрыт до начала боя");
            Assert.AreEqual(OpposingSide.Player, ActivitySetup.Pvp.Opposition,
                "в матче вторую сторону держит другой игрок");

            Assert.IsFalse(ActivitySetup.ProvingGrounds.HideOpponent);
            Assert.AreEqual(OpposingSide.Unclaimed, ActivitySetup.ProvingGrounds.Opposition);
            Assert.AreEqual(ActivityKind.Campaign, ActivitySetup.Campaign.Kind);
        }
    }
}
