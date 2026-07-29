using Guildmaster.Data.Definitions;
using Guildmaster.Presentation;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Presentation
{
    public sealed class UnitAnimationSelectorTests
    {
        [Test]
        public void Select_Dead_AlwaysDeath_OverridingEverything()
        {
            var s = UnitAnimationSelector.Select(isDead: true, attackPlaying: true, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Death, s);
        }

        [Test]
        public void Select_Attack_OverridesMovement()
        {
            var s = UnitAnimationSelector.Select(isDead: false, attackPlaying: true, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Attack, s);
        }

        [Test]
        public void Select_Moving_IsRun()
        {
            var s = UnitAnimationSelector.Select(isDead: false, attackPlaying: false, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Run, s);
        }

        [Test]
        public void Select_StillAndIdle_IsIdle()
        {
            var s = UnitAnimationSelector.Select(isDead: false, attackPlaying: false, isMoving: false);
            Assert.AreEqual(UnitAnimationState.Idle, s);
        }

        [Test]
        public void Select_Deterministic_SameInputSameOutput()
        {
            var a = UnitAnimationSelector.Select(false, true, true);
            var b = UnitAnimationSelector.Select(false, true, true);
            Assert.AreEqual(a, b);
        }

        // --- AttackClipPlaying: «в атаке» решает сим-фаза, не смещение ---

        [Test]
        public void AttackClip_SimInSwing_PlaysEvenWhileMoving()
        {
            // Баг расталкивания/погони: во время сим-замаха юнит зарутован, толчок сепарации даёт isMoving,
            // но клип атаки ДОЛЖЕН играть (иначе свинг рвётся в Run, пропадают замах/хвост).
            bool playing = UnitAnimationSelector.AttackClipPlaying(
                attackCycleActive: true, simInSwing: true, canAttackWhileMoving: false, isMoving: true);
            Assert.IsTrue(playing, "Свинг + толчок сепарации → всё равно клип атаки");
        }

        [Test]
        public void AttackClip_ChaserInGap_Moving_Runs()
        {
            // Сим НЕ в свинге (пауза между ударами), юнит локомотит к цели → бег, а не машет на бегу.
            bool playing = UnitAnimationSelector.AttackClipPlaying(
                attackCycleActive: true, simInSwing: false, canAttackWhileMoving: false, isMoving: true);
            Assert.IsFalse(playing, "Преследователь в паузе между ударами бежит");
        }

        [Test]
        public void AttackClip_HolderInGap_Still_LoopsAttack()
        {
            // Сим не в свинге, но юнит стоит → бесшовный луп клипа атаки (стойка).
            bool playing = UnitAnimationSelector.AttackClipPlaying(
                attackCycleActive: true, simInSwing: false, canAttackWhileMoving: false, isMoving: false);
            Assert.IsTrue(playing, "Стоящий боец лупит атаку между ударами");
        }

        [Test]
        public void AttackClip_AttackWhileMoving_PlaysWhileMoving()
        {
            // Стрельба на ходу: клип атаки поверх бега даже без сим-свинга-рута.
            bool playing = UnitAnimationSelector.AttackClipPlaying(
                attackCycleActive: true, simInSwing: false, canAttackWhileMoving: true, isMoving: true);
            Assert.IsTrue(playing, "Стрелок «на ходу» лупит атаку двигаясь");
        }

        [Test]
        public void AttackClip_NoCycle_NeverPlays()
        {
            // Цикл атаки не идёт → атаки нет, что бы ни творилось с движением/свингом.
            Assert.IsFalse(UnitAnimationSelector.AttackClipPlaying(false, true,  true,  true));
            Assert.IsFalse(UnitAnimationSelector.AttackClipPlaying(false, false, false, false));
        }

        // --- ScrubProgress: поза течёт по ДРОБНОМУ тику, а не рубится на TickRate шагов ---

        [Test]
        public void Scrub_OnTickBoundaries_MatchesWholeTickProgress()
        {
            // Граница тика (alpha = 1) — контрольные точки старой целочисленной схемы: замах 6 тиков,
            // остаток 6 → начало, остаток 1 → последний тик перед ударом.
            Assert.AreEqual(0f,     UnitAnimationSelector.ScrubProgress(6, 6, 1f), 1e-5f);
            Assert.AreEqual(3f / 6, UnitAnimationSelector.ScrubProgress(3, 6, 1f), 1e-5f);
            Assert.AreEqual(5f / 6, UnitAnimationSelector.ScrubProgress(1, 6, 1f), 1e-5f);
        }

        [Test]
        public void Scrub_WithinOneTick_MovesContinuously()
        {
            // Внутри ОДНОГО сим-тика поза обязана ползти: иначе за замах в 6 тиков клип покажет 6 поз,
            // сколько бы кадров ни рисовал рендер, — это и выглядит «визуалом в 30 Гц».
            float atStart = UnitAnimationSelector.ScrubProgress(3, 6, 0f);
            float atMid   = UnitAnimationSelector.ScrubProgress(3, 6, 0.5f);
            float atEnd   = UnitAnimationSelector.ScrubProgress(3, 6, 1f);

            Assert.Less(atStart, atMid);
            Assert.Less(atMid,   atEnd);
            Assert.AreEqual(1f / 6, atEnd - atStart, 1e-5f, "За тик поза проходит ровно один тик клипа");
        }

        [Test]
        public void Scrub_IsContinuousAcrossTickBoundary()
        {
            // Стык тиков: конец тика с остатком 4 и начало тика с остатком 3 — один и тот же момент
            // времени, значит и одна и та же поза. Разрыв здесь дал бы дёрганье 30 раз в секунду.
            float endOfPrevious = UnitAnimationSelector.ScrubProgress(4, 6, 1f);
            float startOfNext   = UnitAnimationSelector.ScrubProgress(3, 6, 0f);
            Assert.AreEqual(endOfPrevious, startOfNext, 1e-5f);
        }

        [Test]
        public void Scrub_ClampsAndSurvivesZeroWindow()
        {
            // Первый кадр замаха лежит РАНЬШЕ его старта (счётчик известен только с конца тика) — кламп
            // держит позу на нуле вместо отрицательного скраба. Нулевое окно — деление на ноль.
            Assert.AreEqual(0f, UnitAnimationSelector.ScrubProgress(6, 6, 0f), 1e-5f);
            Assert.AreEqual(0f, UnitAnimationSelector.ScrubProgress(0, 0, 0.5f), 1e-5f);
            Assert.AreEqual(1f, UnitAnimationSelector.ScrubProgress(0, 6, 1f), 1e-5f);
        }
    }
}
