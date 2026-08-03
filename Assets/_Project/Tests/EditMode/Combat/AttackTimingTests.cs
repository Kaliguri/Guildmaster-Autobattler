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

        // ===================== Свой потолок свинга у юнита (override, 2026-07-30) =====================

        [Test]
        public void AttackDuration_UnitOverride_ReplacesGlobalCap()
        {
            // Медленный кит: интервал 55 тиков. Глобально свинг упёрся бы в 30; со своим потолком 45 —
            // тяжёлый занос в 1.5 сек. Это и есть ручка «редкий тяжёлый удар».
            Assert.AreEqual(SimConstants.MaxAttackAnimTicks, AttackTiming.AttackDurationTicks(55));
            Assert.AreEqual(45, AttackTiming.AttackDurationTicks(55, 45));
        }

        [Test]
        public void AttackDuration_ZeroOverride_FallsBackToGlobalCap()
        {
            // 0 — не «мгновенный свинг», а «значение не задано»: та же идиома, что у WindupShare.
            Assert.AreEqual(AttackTiming.AttackDurationTicks(55), AttackTiming.AttackDurationTicks(55, 0));
        }

        [Test]
        public void AttackDuration_OverrideLongerThanInterval_StillClampedToInterval()
        {
            // Кламп к интервалу сильнее любого потолка: свинг не имеет права наехать на следующую атаку,
            // иначе удар совпадёт с тиком её старта. Override 60 при интервале 40 → 40.
            Assert.AreEqual(40, AttackTiming.AttackDurationTicks(40, 60));
        }

        [Test]
        public void Windup_UnitOverride_WidensTelegraph()
        {
            // Тот же клип (5/7) и тот же интервал: замах растёт вместе с потолком, то есть окно
            // прерывания и парирования становится шире — ради этого override и заведён.
            int windupGlobal   = AttackTiming.WindupTicks(5, 7, 55);
            int windupOverride = AttackTiming.WindupTicks(5, 7, 55, 1f, 45);

            Assert.AreEqual(21, windupGlobal);                          // 5×30/7
            Assert.AreEqual(32, windupOverride);                        // 5×45/7 = 32.14 → floor 32
            Assert.Greater(windupOverride, windupGlobal, "Свой потолок должен удлинять замах");
        }

        [Test]
        public void Swing_UnitOverride_EatsTheWaitingWindow()
        {
            // Ловушка, названная в докстринге UnitData.AttackSwingTicks: пауза = интервал − свинг.
            // Поднять потолок, не снизив скорость атаки, значит сократить окно ожидания — тест держит
            // это арифметическим фактом, чтобы правку «сделаем занос длиннее» не приняли за рост паузы.
            const int interval = 55;
            int pauseGlobal   = interval - AttackTiming.AttackDurationTicks(interval);
            int pauseOverride = interval - AttackTiming.AttackDurationTicks(interval, 45);

            Assert.AreEqual(25, pauseGlobal);
            Assert.AreEqual(10, pauseOverride);
            Assert.Less(pauseOverride, pauseGlobal, "Длинный свинг съедает паузу при том же интервале");
        }

        [Test]
        public void FollowThrough_UnitOverride_TailGrowsWithSwing()
        {
            // windup + доигрыш = длительность свинга ровно, каким бы потолком она ни была задана.
            int windup = AttackTiming.WindupTicks(5, 7, 55, 1f, 45);
            int tail   = AttackTiming.FollowThroughTicks(5, 7, 55, windup, 45);

            Assert.AreEqual(45, windup + tail, "Замах и доигрыш складываются в длительность свинга");
        }

        // ===================== FollowThroughTicks (доигрыш клипа после кадра контакта) =====================

        [Test]
        public void FollowThrough_BaseSpeed_FillsRestOfClip()
        {
            // interval 30, duration 30, windup 21 → доигрыш 30 − 21 = 9. windup + доигрыш = весь клип.
            int windup = AttackTiming.WindupTicks(5, 7, 30);
            Assert.AreEqual(9, AttackTiming.FollowThroughTicks(5, 7, 30, windup));
            Assert.AreEqual(30, windup + AttackTiming.FollowThroughTicks(5, 7, 30, windup),
                "windup + доигрыш ровно покрывают длительность клипа (без фантомного зазора)");
        }

        [Test]
        public void FollowThrough_NoClip_IsZero()
        {
            // Без клипа windup был чистым телеграфом (пол MinWindupTicks) → доигрывать нечего.
            Assert.AreEqual(0, AttackTiming.FollowThroughTicks(0, 7, 30, SimConstants.MinWindupTicks));
            Assert.AreEqual(0, AttackTiming.FollowThroughTicks(5, 0, 30, SimConstants.MinWindupTicks));
        }

        [Test]
        public void FollowThrough_LastFrameHit_LeavesMinimalTail()
        {
            // hit 7/7 → windup клампится к interval−1 = 29; доигрыш = 30 − 29 = 1 (удар почти в конце клипа).
            int windup = AttackTiming.WindupTicks(7, 7, 30);
            Assert.AreEqual(1, AttackTiming.FollowThroughTicks(7, 7, 30, windup));
        }
    }
}
