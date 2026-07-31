using System.Collections.Generic;
using Guildmaster.Presentation.Body;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Holds the contract of the part registry: what the rig convention is expected to yield, and the one
    /// invariant that spans a seam — a part's index in the body's renderer list IS its bit in
    /// <see cref="PartMask"/>. The body writes the property block by index while the registry answers
    /// queries by index, and neither side can see the other break it.
    ///
    /// The rig is built here by hand rather than loaded from the prefab on purpose: the prefab carries one
    /// arrangement (sword plus shield), and the registry has to hold for arrangements that have no art yet —
    /// two daggers, a two-handed spear, bare fists.
    /// </summary>
    public class UnitPartRegistryTests
    {
        TestRigBuilder _rig;
        readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        // Риг собирается общим билдером (TestRigBuilder): те же три уровня, что требует конвенция.
        GameObject NewRoot()
        {
            _rig = new TestRigBuilder();
            return _rig.Root;
        }

        static SpriteRenderer Bone(Transform parent, string boneName) => TestRigBuilder.Bone(parent, boneName);

        static Transform Child(Transform parent, string name) => TestRigBuilder.Child(parent, name);

        Transform Arm(Transform root, BodySide side, out SpriteRenderer hand) => _rig.Arm(side, out hand);

        static SpriteRenderer Held(Transform grip, string boneName, HeldKind kind, bool twoHanded = false) =>
            TestRigBuilder.Held(grip, boneName, kind, twoHanded);

        // --- Held items -----------------------------------------------------------------------------

        [Test]
        public void SwordAndShield_AreAddressedByHandAndByKind()
        {
            GameObject root = NewRoot();
            Transform rightGrip = Arm(root.transform, BodySide.Right, out _);
            Transform leftGrip = Arm(root.transform, BodySide.Left, out _);
            SpriteRenderer sword = Held(rightGrip, "Sword", HeldKind.Weapon);
            SpriteRenderer shield = Held(leftGrip, "Shield", HeldKind.Shield);

            var registry = UnitPartRegistry.FromBody(new[] { sword, shield }, root.transform);

            Assert.That(registry.TryGetHeld(HandSlot.Right, out UnitPart inRight), Is.True);
            Assert.That(inRight.Bone, Is.EqualTo("Sword"));
            Assert.That(registry.TryGetHeld(HandSlot.Left, out UnitPart inLeft), Is.True);
            Assert.That(inLeft.Bone, Is.EqualTo("Shield"));

            Assert.That(registry.TryGetHeld(HeldKind.Shield, out UnitPart byKind), Is.True);
            Assert.That(byKind.Bone, Is.EqualTo("Shield"), "«дай щит» обязан работать без знания руки");
        }

        /// <summary>
        /// A two-handed item is ONE part answering both hands. Two entries would light the same shaft twice
        /// and make "give me the weapon" return two weapons.
        /// </summary>
        [Test]
        public void TwoHandedItem_AnswersEitherHand_AsASinglePart()
        {
            GameObject root = NewRoot();
            Transform grip = Arm(root.transform, BodySide.Right, out _);
            Arm(root.transform, BodySide.Left, out SpriteRenderer leftHand);
            SpriteRenderer spear = Held(grip, "Spear", HeldKind.Weapon, twoHanded: true);

            var registry = UnitPartRegistry.FromBody(new[] { spear, leftHand }, root.transform);

            Assert.That(registry.TryGetHeld(HandSlot.Left, out UnitPart byLeft), Is.True);
            Assert.That(registry.TryGetHeld(HandSlot.Right, out UnitPart byRight), Is.True);
            Assert.That(byLeft.Index, Is.EqualTo(byRight.Index), "двуручное копьё — один предмет, не два");
            Assert.That(byLeft.Slot, Is.EqualTo(HandSlot.Both));
        }

        [Test]
        public void TwoDaggers_AreToldApartByHand_ThoughTheirKindIsTheSame()
        {
            GameObject root = NewRoot();
            Transform rightGrip = Arm(root.transform, BodySide.Right, out _);
            Transform leftGrip = Arm(root.transform, BodySide.Left, out _);
            SpriteRenderer right = Held(rightGrip, "Dagger_R", HeldKind.Weapon);
            SpriteRenderer left = Held(leftGrip, "Dagger_L", HeldKind.Weapon);

            var registry = UnitPartRegistry.FromBody(new[] { right, left }, root.transform);

            Assert.That(registry.TryGetHeld(HandSlot.Right, out UnitPart inRight), Is.True);
            Assert.That(registry.TryGetHeld(HandSlot.Left, out UnitPart inLeft), Is.True);
            Assert.That(inRight.Mask, Is.Not.EqualTo(inLeft.Mask),
                "приём может зажечь ОДИН из двух кинжалов — маски обязаны различаться");
        }

        [Test]
        public void HeldItemWithoutDeclaration_FailsLoudly()
        {
            GameObject root = NewRoot();
            Transform grip = Arm(root.transform, BodySide.Right, out _);
            SpriteRenderer unnamed = Bone(grip, "Torch");   // без UnitHeldItem

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("не объявлен"));
            var registry = UnitPartRegistry.FromBody(new[] { unnamed }, root.transform);

            Assert.That(registry.TryGetHeld(HeldKind.Weapon, out _), Is.False,
                "необъявленный предмет не должен молча становиться оружием");
        }

        // --- Body parts -----------------------------------------------------------------------------

        [Test]
        public void BodyPart_IsAddressedByBoneAndSide()
        {
            GameObject root = NewRoot();
            SpriteRenderer head = Bone(root.transform, "Head");
            Transform legLeft = Child(root.transform, "Leg (Left)");
            Transform legRight = Child(root.transform, "Leg (Right)");
            SpriteRenderer bootLeft = Bone(legLeft, "Leg_Boots");
            SpriteRenderer bootRight = Bone(legRight, "Leg_Boots");

            var registry = UnitPartRegistry.FromBody(new[] { head, bootLeft, bootRight }, root.transform);

            Assert.That(registry.TryGetBone("Head", BodySide.None, out UnitPart headPart), Is.True);
            Assert.That(headPart.Side, Is.EqualTo(BodySide.None), "голова непарная");

            Assert.That(registry.TryGetBone("Leg_Boots", BodySide.Right, out UnitPart rightBoot), Is.True);
            Assert.That(rightBoot.Index, Is.EqualTo(2), "правая нога — третья часть тела, не первая совпавшая");
        }

        /// <summary>
        /// Bare fists are a weapon too (Max, 31.07): a unit with empty hands must still have something to
        /// light up, or the whole unarmed archetype loses its cast telegraph.
        /// </summary>
        [Test]
        public void StrikeSource_FallsBackToTheHand_WhenTheGripIsEmpty()
        {
            GameObject root = NewRoot();
            Arm(root.transform, BodySide.Right, out SpriteRenderer rightHand);
            SpriteRenderer head = Bone(root.transform, "Head");

            var registry = UnitPartRegistry.FromBody(new[] { rightHand, head }, root.transform);

            Assert.That(registry.TryGetStrikeSource(HandSlot.Right, out UnitPart source), Is.True);
            Assert.That(source.Bone, Is.EqualTo("Arm_Down_R"));
            Assert.That(source.IsHand, Is.True);
        }

        [Test]
        public void StrikeSource_PrefersTheWeaponInTheAskedHand()
        {
            GameObject root = NewRoot();
            Transform rightGrip = Arm(root.transform, BodySide.Right, out _);
            Transform leftGrip = Arm(root.transform, BodySide.Left, out _);
            SpriteRenderer sword = Held(rightGrip, "Sword", HeldKind.Weapon);
            SpriteRenderer shield = Held(leftGrip, "Shield", HeldKind.Shield);

            var registry = UnitPartRegistry.FromBody(new[] { shield, sword }, root.transform);

            Assert.That(registry.TryGetStrikeSource(HandSlot.Right, out UnitPart source), Is.True);
            Assert.That(source.Bone, Is.EqualTo("Sword"), "щит из другой руки — не источник удара правой");
        }

        [Test]
        public void SingleSpriteBody_AnswersEveryQueryWithItself()
        {
            var go = new GameObject("Frame");
            _spawned.Add(go);
            var sprite = go.AddComponent<SpriteRenderer>();

            var registry = UnitPartRegistry.ForSingleSprite(sprite);

            Assert.That(registry.TryGetStrikeSource(HandSlot.Right, out UnitPart source), Is.True);
            Assert.That(source.Index, Is.EqualTo(0));
            Assert.That(registry.Everything, Is.EqualTo(PartMask.Single(0)),
                "покадровое тело светится целиком — его «всё» это единственная часть");
        }

        // --- The cross-seam invariant ---------------------------------------------------------------

        /// <summary>
        /// The index carried by a part is its slot in the BODY's renderer list, not its position among the
        /// registry entries: the body applies the glow with <c>GlowParts.Has(i)</c> while walking that very
        /// list. A lost reference leaves a hole in it, and the parts after the hole must not shift — they
        /// would light up the wrong limb.
        /// </summary>
        [Test]
        public void PartIndex_MatchesTheSlotInTheBodyList_EvenWithLostReferences()
        {
            GameObject root = NewRoot();
            SpriteRenderer head = Bone(root.transform, "Head");
            SpriteRenderer torso = Bone(root.transform, "Body");

            var withHole = new List<SpriteRenderer> { head, null, torso };
            var registry = UnitPartRegistry.FromBody(withHole, root.transform);

            Assert.That(registry.Parts.Count, Is.EqualTo(2), "потерянная ссылка записи не получает");
            Assert.That(registry.TryGetBone("Body", BodySide.None, out UnitPart torsoPart), Is.True);
            Assert.That(torsoPart.Index, Is.EqualTo(2), "дыра в списке тела не сдвигает индексы за ней");
            Assert.That(torsoPart.Mask.Has(2), Is.True);
        }

        [Test]
        public void PartMask_AddressesEveryPartOfABody_AndCombines()
        {
            Assert.That(PartMask.Empty.IsEmpty, Is.True);
            Assert.That(PartMask.All(3).Count, Is.EqualTo(3));
            Assert.That((PartMask.Single(0) | PartMask.Single(5)).Count, Is.EqualTo(2),
                "приём щитом И клинком — две части в одной маске");
            Assert.That(PartMask.Single(PartMask.MaxParts).IsEmpty, Is.True,
                "часть за пределом маски не адресуется, и молча чужой бит занимать не должна");
        }
    }
}
