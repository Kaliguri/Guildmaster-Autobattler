using Guildmaster.Core.Settings;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Core
{
    /// <summary>
    /// Пасовка кадров: что применяется при каждом сочетании «синхронизация + потолок».
    /// Чистый headless — без сцены, без <c>QualitySettings</c> и без редактора.
    /// </summary>
    /// <remarks>
    /// Эти инварианты живут в тесте, а не в комментарии, потому что нарушить их можно из другого файла:
    /// UI настроек, читающий <c>FrameRateCap</c>, и <c>DisplayService</c>, применяющий его в
    /// <c>Application.targetFrameRate</c>, стоят по разные стороны шва.
    /// </remarks>
    public sealed class FramePacingTests
    {
        [Test]
        public void Default_IsVSyncOn()
        {
            FramePacing pacing = FramePacing.Resolve(null, null);

            Assert.IsTrue(pacing.VSync, "не выбирал — синхронизация включена: игра без потолка кадров " +
                                        "греет железо ради кадров, которых никто не видит");
            Assert.AreEqual(FramePacing.Unlimited, pacing.FrameRateCap);
        }

        [Test]
        public void VSyncOn_DropsFrameRateCap()
        {
            FramePacing pacing = FramePacing.Resolve(true, 60);

            Assert.AreEqual(FramePacing.Unlimited, pacing.FrameRateCap,
                "при vSyncCount > 0 Unity игнорирует targetFrameRate — хранить и показывать число, " +
                "на которое игра не смотрит, значит выдавать «поставил 60, идёт 165» за баг");
            Assert.IsFalse(pacing.FrameRateCapSelectable, "выбор потолка в UI гасится, а не врёт");
        }

        [Test]
        public void VSyncOff_KeepsChosenCap()
        {
            FramePacing pacing = FramePacing.Resolve(false, 60);

            Assert.IsFalse(pacing.VSync);
            Assert.AreEqual(60, pacing.FrameRateCap);
            Assert.IsTrue(pacing.FrameRateCapSelectable);
        }

        [Test]
        public void VSyncOff_WithoutCap_StaysUnlimited()
        {
            FramePacing pacing = FramePacing.Resolve(false, null);

            Assert.AreEqual(FramePacing.Unlimited, pacing.FrameRateCap,
                "выключить синхронизацию — осознанный выбор «столько, сколько получится»");
        }

        [Test]
        public void CapBelowFloor_IsRaisedToMinimum()
        {
            FramePacing pacing = FramePacing.Resolve(false, 5);

            Assert.AreEqual(FramePacing.MinCap, pacing.FrameRateCap,
                "слайд-шоу игрок не заказывал — потолок ниже порога поднимается, а не применяется");
        }

        [Test]
        public void ZeroCap_MeansUnlimited_NotFloor()
        {
            FramePacing pacing = FramePacing.Resolve(false, FramePacing.Unlimited);

            Assert.AreEqual(FramePacing.Unlimited, pacing.FrameRateCap,
                "ноль — это «снять потолок», а не «поставить минимальный»");
        }
    }
}
