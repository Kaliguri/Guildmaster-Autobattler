using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Чистая арифметика тайминга авто-атаки (вики «14»): целочисленность, кламп,
    /// полы MinWindupTicks/intervalTicks≥1, явная политика округления (не banker's).
    /// </summary>
    public sealed class AttackTimingTests
    {
        // ===================== IntervalTicks =====================

        [Test]
        public void IntervalTicks_BaseSpeed_EqualsTickRate()
        {
            Assert.AreEqual(30, AttackTiming.IntervalTicks(1f));   // 30 / 1
            Assert.AreEqual(15, AttackTiming.IntervalTicks(2f));   // 30 / 2
            Assert.AreEqual(60, AttackTiming.IntervalTicks(0.5f)); // 30 / 0.5
        }

        [Test]
        public void IntervalTicks_RoundsAwayFromZero_NotBankers()
        {
            // 30 / 12 = 2.5 ровно. AwayFromZero → 3; banker's (к чётному) дал бы 2.
            // Этот тест ловит дефолтное округление .NET как источник рассинхрона.
            Assert.AreEqual(3, AttackTiming.IntervalTicks(12f));
        }

        [Test]
        public void IntervalTicks_VeryFast_FlooredToOne()
        {
            Assert.AreEqual(1, AttackTiming.IntervalTicks(100f)); // 0.3 → round 0 → пол 1
        }

        [Test]
        public void IntervalTicks_NonPositiveSpeed_IsEffectivelyInfinite()
        {
            Assert.AreEqual(int.MaxValue, AttackTiming.IntervalTicks(0f));
            Assert.AreEqual(int.MaxValue, AttackTiming.IntervalTicks(-1f));
        }

        // ===================== WindupTicks: основные режимы =====================

        [Test]
        public void WindupTicks_SlowUnit_SwingCappedAtMaxAnim()
        {
            // interval 90 (atkSpeed 1/3), duration = min(30, 90) = 30, hit 5/7 → 150/7 = 21.
            int interval = AttackTiming.IntervalTicks(1f / 3f);
            Assert.AreEqual(90, interval);
            Assert.AreEqual(21, AttackTiming.WindupTicks(5, 7, interval));
        }

        [Test]
        public void WindupTicks_BaseSpeed_UsesFullAnim()
        {
            // interval 30, duration 30, hit 5/7 → 21, upper 29.
            Assert.AreEqual(21, AttackTiming.WindupTicks(5, 7, 30));
        }

        [Test]
        public void WindupTicks_FastUnit_SwingCompressedToInterval()
        {
            // interval 15, duration = min(30,15) = 15, hit 5/7 → 75/7 = 10, upper 14.
            Assert.AreEqual(10, AttackTiming.WindupTicks(5, 7, 15));
        }

        // ===================== WindupTicks: полы и краевые =====================

        [Test]
        public void WindupTicks_TinyHitFraction_FlooredToMinWindup()
        {
            // interval 30, duration 30, hit 1/20 → 30/20 = 1 < MinWindupTicks(3) → 3.
            Assert.AreEqual(SimConstants.MinWindupTicks, AttackTiming.WindupTicks(1, 20, 30));
        }

        [Test]
        public void WindupTicks_HitFrameZero_FlooredToMinWindup()
        {
            Assert.AreEqual(SimConstants.MinWindupTicks, AttackTiming.WindupTicks(0, 7, 30));
        }

        [Test]
        public void WindupTicks_EmptyClip_FlooredToMinWindup()
        {
            Assert.AreEqual(SimConstants.MinWindupTicks, AttackTiming.WindupTicks(3, 0, 30));
        }

        [Test]
        public void WindupTicks_NeverReachesIntervalStart()
        {
            // Последний кадр при базовой скорости: hit 7/7 → 30, но upper = interval-1 = 29.
            Assert.AreEqual(29, AttackTiming.WindupTicks(7, 7, 30));
        }

        [Test]
        public void WindupTicks_ShortInterval_LowerClampYieldsToUpper()
        {
            // interval 2 → upper 1; пол MinWindupTicks(3) не может превысить upper → результат ≤ 1.
            int w = AttackTiming.WindupTicks(5, 7, 2);
            Assert.LessOrEqual(w, 1);
            Assert.GreaterOrEqual(w, 0);
        }

        [Test]
        public void WindupTicks_IntervalOne_IsInstant()
        {
            // interval 1 → upper 0 → удар в тот же тик (windup 0).
            Assert.AreEqual(0, AttackTiming.WindupTicks(5, 7, 1));
        }

        [Test]
        public void WindupTicks_IsDeterministic()
        {
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(21, AttackTiming.WindupTicks(5, 7, 30));
        }
    }
}
