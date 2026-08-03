using Guildmaster.Data.Definitions;
using Guildmaster.Presentation.Body;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// The seam between what a skill DECLARES (CastSource: a role) and what a body OWNS (parts): the same
    /// declaration has to land on different art. "OffHand" is a second dagger for a duellist, a shield for
    /// a shield-bearer and a bare fist for a monk, and no side of that translation can see the other break.
    /// </summary>
    public class CastGlowMaskTests
    {
        TestRigBuilder _rig;

        [TearDown]
        public void TearDown() => _rig?.Dispose();

        [Test]
        public void SwordAndShield_LightTheDeclaredItem()
        {
            _rig = new TestRigBuilder();
            Transform right = _rig.Arm(BodySide.Right, out _);
            Transform left = _rig.Arm(BodySide.Left, out _);
            SpriteRenderer sword = TestRigBuilder.Held(right, "Sword", HeldKind.Weapon);
            SpriteRenderer shield = TestRigBuilder.Held(left, "Shield", HeldKind.Shield);
            var parts = _rig.Registry(sword, shield);

            Assert.That(parts.TryGetHeld(HeldKind.Weapon, out UnitPart blade), Is.True);
            Assert.That(parts.TryGetHeld(HeldKind.Shield, out UnitPart guard), Is.True);

            Assert.That(CastGlowMask.Resolve(parts, CastSource.Auto), Is.EqualTo(blade.Mask));
            Assert.That(CastGlowMask.Resolve(parts, CastSource.Shield), Is.EqualTo(guard.Mask),
                "щит-бэш обязан светить щитом, а не клинком в другой руке");
            Assert.That(CastGlowMask.Resolve(parts, CastSource.OffHand), Is.EqualTo(guard.Mask),
                "у щитовика вторая рука — это щит");
        }

        [Test]
        public void TwoDaggers_LightOneOrBoth_ByDeclaration()
        {
            _rig = new TestRigBuilder();
            Transform right = _rig.Arm(BodySide.Right, out _);
            Transform left = _rig.Arm(BodySide.Left, out _);
            SpriteRenderer main = TestRigBuilder.Held(right, "Dagger_R", HeldKind.Weapon);
            SpriteRenderer off = TestRigBuilder.Held(left, "Dagger_L", HeldKind.Weapon);
            var parts = _rig.Registry(main, off);

            parts.TryGetHeld(HandSlot.Right, out UnitPart rightDagger);
            parts.TryGetHeld(HandSlot.Left, out UnitPart leftDagger);

            Assert.That(CastGlowMask.Resolve(parts, CastSource.Auto), Is.EqualTo(rightDagger.Mask));
            Assert.That(CastGlowMask.Resolve(parts, CastSource.OffHand), Is.EqualTo(leftDagger.Mask),
                "один из двух одинаковых кинжалов — то, ради чего маска частей заменила маску ролей");
            Assert.That(CastGlowMask.Resolve(parts, CastSource.BothHands),
                Is.EqualTo(rightDagger.Mask | leftDagger.Mask));
        }

        /// <summary>A two-handed item is one part, so "both hands" must not double it into something else.</summary>
        [Test]
        public void TwoHandedSpear_LightsTheSameSinglePart_ForEveryHandSource()
        {
            _rig = new TestRigBuilder();
            Transform grip = _rig.Arm(BodySide.Right, out _);
            _rig.Arm(BodySide.Left, out SpriteRenderer leftHand);
            SpriteRenderer spear = TestRigBuilder.Held(grip, "Spear", HeldKind.Weapon, twoHanded: true);
            var parts = _rig.Registry(spear, leftHand);

            parts.TryGetHeld(HandSlot.Both, out UnitPart shaft);

            Assert.That(CastGlowMask.Resolve(parts, CastSource.Auto), Is.EqualTo(shaft.Mask));
            Assert.That(CastGlowMask.Resolve(parts, CastSource.OffHand), Is.EqualTo(shaft.Mask));
            Assert.That(CastGlowMask.Resolve(parts, CastSource.BothHands), Is.EqualTo(shaft.Mask),
                "древко одно — маска обязана остаться одной частью, иначе свет ляжет дважды");
        }

        [Test]
        public void BareFists_LightTheHands()
        {
            _rig = new TestRigBuilder();
            _rig.Arm(BodySide.Right, out SpriteRenderer rightHand);
            _rig.Arm(BodySide.Left, out SpriteRenderer leftHand);
            var parts = _rig.Registry(rightHand, leftHand);

            parts.TryGetStrikeSource(HandSlot.Right, out UnitPart right);
            parts.TryGetStrikeSource(HandSlot.Left, out UnitPart left);

            Assert.That(right.IsHand, Is.True);
            Assert.That(CastGlowMask.Resolve(parts, CastSource.Auto), Is.EqualTo(right.Mask));
            Assert.That(CastGlowMask.Resolve(parts, CastSource.BothHands), Is.EqualTo(right.Mask | left.Mask));
        }

        [Test]
        public void SustainedBuff_LightsTheWholeBody_AndNoneLightsNothing()
        {
            _rig = new TestRigBuilder();
            Transform grip = _rig.Arm(BodySide.Right, out SpriteRenderer hand);
            SpriteRenderer drum = TestRigBuilder.Held(grip, "Drum", HeldKind.Weapon);
            SpriteRenderer head = _rig.BodyPart("Head");
            var parts = _rig.Registry(drum, hand, head);

            Assert.That(CastGlowMask.Resolve(parts, CastSource.WholeBody).Count, Is.EqualTo(3),
                "марш светит юнитом, а не барабаном");
            Assert.That(CastGlowMask.Resolve(parts, CastSource.None).IsEmpty, Is.True);
        }

        [Test]
        public void ShieldSource_OnAUnitWithoutAShield_LightsNothing()
        {
            _rig = new TestRigBuilder();
            Transform grip = _rig.Arm(BodySide.Right, out _);
            SpriteRenderer sword = TestRigBuilder.Held(grip, "Sword", HeldKind.Weapon);
            var parts = _rig.Registry(sword);

            Assert.That(CastGlowMask.Resolve(parts, CastSource.Shield).IsEmpty, Is.True,
                "нет щита — нечему светиться; подменять его клинком значило бы соврать о приёме");
        }
    }
}
