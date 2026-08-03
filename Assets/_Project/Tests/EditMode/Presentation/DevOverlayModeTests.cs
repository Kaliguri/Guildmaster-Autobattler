using Guildmaster.Core.Simulation;
using Guildmaster.Presentation;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Режим dev-оверлеев (Ф7 ленты боя). Смысл тестов не в тексте подписи, а в двух инвариантах:
    /// дефолт — ПОКАЗ (иначе оверлей рисует там, где на экране никого нет), и подпись обязана называть
    /// разъезд числом (без него «сим» не объясняет, почему кольца не на юнитах).
    /// </summary>
    public sealed class DevOverlayModeTests
    {
        [Test]
        public void Default_IsPresentation_NotSimulation()
        {
            var mode = new DevOverlayMode(playback: null);

            Assert.AreEqual(DevOverlaySource.Presentation, mode.Source,
                "Оверлей по умолчанию совпадает с картинкой: сим впереди на окно опережения");
            Assert.IsFalse(mode.ReadsSimulation);
        }

        [Test]
        public void Toggle_SwitchesBothWays()
        {
            var mode = new DevOverlayMode(playback: null);

            Assert.AreEqual(DevOverlaySource.Simulation, mode.Toggle());
            Assert.IsTrue(mode.ReadsSimulation);
            Assert.AreEqual(DevOverlaySource.Presentation, mode.Toggle());
            Assert.IsFalse(mode.ReadsSimulation);
        }

        [Test]
        public void Describe_NamesTheModeAndTheLagInSeconds()
        {
            // Десять секунд опережения — рабочее окно ленты: подпись обязана назвать и режим, и разъезд.
            string sim  = DevOverlayMode.Describe(DevOverlaySource.Simulation,   10 * SimConstants.TickRate);
            string show = DevOverlayMode.Describe(DevOverlaySource.Presentation, 10 * SimConstants.TickRate);

            Assert.That(sim,  Does.Contain("СИМ").And.Contain("10").And.Contain("с"));
            Assert.That(show, Does.Contain("ПОКАЗ").And.Contain("10"));
            Assert.AreNotEqual(sim, show, "Режимы обязаны читаться по-разному одним взглядом");
        }

        [Test]
        public void Describe_WithoutLag_StillReadable()
        {
            // Вне боя запаса нет (лаг — свойство БОЯ), и подпись не должна ломаться на нуле.
            string text = DevOverlayMode.Describe(DevOverlaySource.Presentation, leadTicks: 0);

            Assert.IsNotEmpty(text);
            Assert.That(text, Does.Contain("ПОКАЗ"));
        }
    }
}
