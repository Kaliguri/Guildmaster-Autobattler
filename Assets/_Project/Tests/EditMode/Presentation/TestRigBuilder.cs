using System.Collections.Generic;
using Guildmaster.Presentation.Body;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Builds rigs by hand for part-registry tests: bone -> "Visual Part (Bone)" -> sprite, arms with
    /// grips, declared items. The prefab carries one arrangement (sword plus shield) while the code has
    /// to hold for arrangements that have no art yet — two daggers, a two-handed spear, bare fists.
    /// </summary>
    public sealed class TestRigBuilder
    {
        readonly List<GameObject> _spawned = new List<GameObject>();

        public GameObject Root { get; }

        public TestRigBuilder(string name = "Body")
        {
            Root = new GameObject(name);
            _spawned.Add(Root);
        }

        public void Dispose()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        public static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            return go.transform;
        }

        /// <summary>Bone plus its art container plus the sprite node — the three levels the rig demands.</summary>
        public static SpriteRenderer Bone(Transform parent, string boneName)
        {
            Transform bone = Child(parent, boneName);
            Transform container = Child(bone, RigNaming.ContainerName(boneName));
            return Child(container, boneName).gameObject.AddComponent<SpriteRenderer>();
        }

        public SpriteRenderer BodyPart(string boneName) => Bone(Root.transform, boneName);

        /// <summary>An arm that can hold something: limb (side) -> elbow -> hand -> grip.</summary>
        public Transform Arm(BodySide side, out SpriteRenderer hand)
        {
            Transform limb = Child(Root.transform, side == BodySide.Left ? "Arm (Left)" : "Arm (Right)");
            Transform elbow = Child(limb, RigNaming.JointPrefix + "Elbow)");
            hand = Bone(elbow, side == BodySide.Left ? "Arm_Down_L" : "Arm_Down_R");
            return Child(hand.transform.parent.parent, RigNaming.JointPrefix + RigNaming.GripLabel + ")");
        }

        /// <summary>An item in a grip, declared the way the game demands: kind on the item's bone.</summary>
        public static SpriteRenderer Held(Transform grip, string boneName, HeldKind kind, bool twoHanded = false)
        {
            SpriteRenderer renderer = Bone(grip, boneName);
            var mark = renderer.transform.parent.parent.gameObject.AddComponent<UnitHeldItem>();
            var so = new SerializedObject(mark);
            so.FindProperty("_kind").enumValueIndex = (int)kind;
            so.FindProperty("_twoHanded").boolValue = twoHanded;
            so.ApplyModifiedPropertiesWithoutUndo();
            return renderer;
        }

        public UnitPartRegistry Registry(params SpriteRenderer[] parts) =>
            UnitPartRegistry.FromBody(parts, Root.transform);
    }
}
