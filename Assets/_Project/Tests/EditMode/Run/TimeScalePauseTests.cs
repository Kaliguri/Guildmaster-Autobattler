using Guildmaster.Game.Services;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Пауза ИГРОКА и её владелец. Отдельный факт от <c>CombatSimulation.SetPaused</c> («сим заморожен
    /// сценарием»): у них похожие имена и разный смысл, и аудит 2026-07-26 (T-4) предлагал слить их в один —
    /// чего делать нельзя. Тест фиксирует контракт, чтобы следующий заход не свёл их обратно.
    /// </summary>
    public sealed class TimeScalePauseTests
    {
        private float _timeScaleBefore;

        [SetUp]
        public void SetUp() => _timeScaleBefore = Time.timeScale;

        [TearDown]
        public void TearDown() => Time.timeScale = _timeScaleBefore;

        private static TimeScaleService New() => new TimeScaleService(audio: null);

        [Test]
        public void Paused_ZeroesTheEffectiveScale_AndLiftingRestoresGameSpeed()
        {
            TimeScaleService time = New();
            time.SetGameSpeed(2f);

            time.SetPaused(true);
            Assert.IsTrue(time.IsPaused);
            Assert.AreEqual(0f, time.Effective, 1e-6f);
            Assert.AreEqual(0f, Time.timeScale, 1e-6f, "пауза обязана дойти до Time.timeScale: из него сим берёт deltaTime");

            time.SetPaused(false);
            Assert.IsFalse(time.IsPaused);
            Assert.AreEqual(2f, time.Effective, 1e-6f, "снятие паузы возвращает выбранную игроком скорость, а не 1");
        }

        [Test]
        public void Reset_KeepsThePlayersPause_BecauseItIsHisChoice()
        {
            TimeScaleService time = New();
            time.SetPaused(true);

            time.Reset(); // dev-рестарт боя: снимает cinematic-состояние

            Assert.IsTrue(time.IsPaused,
                "Reset снимает slowmo/секвенции, но не выбор игрока — иначе dev-рестарт молча снимал бы паузу");
        }

        [Test]
        public void Reset_ClearsCinematicSlowmoSoTheNextFightIsNotInSlowMotion()
        {
            TimeScaleService time = New();
            time.SetGameSpeed(1f);
            time.CinematicPulse(0.1f, holdSeconds: 10f, releaseSeconds: 10f);
            Assert.Less(time.Effective, 1f, "пульс должен был замедлить время");

            time.Reset();

            Assert.AreEqual(1f, time.Effective, 1e-6f, "застрявший slowmo перетёк бы в следующий бой");
        }

        [Test]
        public void Dispose_ReturnsTheWorldToNormalSpeed()
        {
            TimeScaleService time = New();
            time.SetPaused(true);

            time.Dispose();

            Assert.AreEqual(1f, Time.timeScale, 1e-6f, "выгрузка боя не должна оставлять мир замороженным");
        }
    }
}
