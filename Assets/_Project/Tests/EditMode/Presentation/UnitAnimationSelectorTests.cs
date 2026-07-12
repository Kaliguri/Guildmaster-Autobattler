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
    }
}
