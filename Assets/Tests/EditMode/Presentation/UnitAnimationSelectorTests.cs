using Guildmaster.Presentation;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Presentation
{
    public sealed class UnitAnimationSelectorTests
    {
        [Test]
        public void Select_Dead_AlwaysDeath_OverridingEverything()
        {
            var s = UnitAnimationSelector.Select(isDead: true, attackRemaining: 0.5f, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Death, s);
        }

        [Test]
        public void Select_Attack_OverridesMovement()
        {
            var s = UnitAnimationSelector.Select(isDead: false, attackRemaining: 0.3f, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Attack, s);
        }

        [Test]
        public void Select_Moving_IsRun()
        {
            var s = UnitAnimationSelector.Select(isDead: false, attackRemaining: 0f, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Run, s);
        }

        [Test]
        public void Select_StillAndIdle_IsIdle()
        {
            var s = UnitAnimationSelector.Select(isDead: false, attackRemaining: 0f, isMoving: false);
            Assert.AreEqual(UnitAnimationState.Idle, s);
        }

        [Test]
        public void Select_Deterministic_SameInputSameOutput()
        {
            var a = UnitAnimationSelector.Select(false, 0.1f, true);
            var b = UnitAnimationSelector.Select(false, 0.1f, true);
            Assert.AreEqual(a, b);
        }
    }
}
