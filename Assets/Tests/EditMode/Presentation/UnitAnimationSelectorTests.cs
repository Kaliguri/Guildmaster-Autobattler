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
    }
}
