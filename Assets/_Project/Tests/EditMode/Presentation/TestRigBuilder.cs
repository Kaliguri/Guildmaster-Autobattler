using System.Collections.Generic;
using Guildmaster.Presentation.Body;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Builds rigs by hand for part-registry tests: bone -> "Bone_Art" sprite node, arms with grips,
    /// declared items. The prefab carries one arrangement (sword plus shield) while the code has to hold
    /// for arrangements that have no art yet — two daggers, a two-handed spear, bare fists.
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

        /// <summary>Bone plus its art node — the two levels the rig demands since the restructure.</summary>
        public static SpriteRenderer Bone(Transform parent, string boneName)
        {
            Transform bone = Child(parent, boneName);
            return Child(bone, RigNaming.ArtName(boneName)).gameObject.AddComponent<SpriteRenderer>();
        }

        public SpriteRenderer BodyPart(string boneName) => Bone(Root.transform, boneName);

        /// <summary>
        /// An arm that can hold something: shoulder -> forearm -> hand -> grip. The side is a suffix on
        /// every bone, not a container above them, so the chain reads the same on both sides.
        /// </summary>
        public Transform Arm(BodySide side, out SpriteRenderer hand)
        {
            string s = RigNaming.SideSuffix(side);
            Transform shoulder = Child(Root.transform, "Shoulder" + s);
            Transform forearm = Child(shoulder, "LowerArm" + s);
            hand = Bone(forearm, "Hand" + s);
            return Child(hand.transform.parent, RigNaming.GripPrefix.TrimEnd('_') + s);
        }

        /// <summary>An item in a grip, declared the way the game demands: kind on the grip bone itself.</summary>
        /// <summary>
        /// Предмет в хвате. <paramref name="declareReach"/> = false собирает его БЕЗ рабочей части — так
        /// выглядит неразведённый контент, и запрос «чем бьют» обязан ответить «нечем», а не угадать кусок.
        /// </summary>
        public static SpriteRenderer Held(Transform grip, string artName, HeldKind kind, bool twoHanded = false,
            bool declareReach = true)
        {
            var renderer = Child(grip, artName + RigNaming.ArtSuffix).gameObject.AddComponent<SpriteRenderer>();
            var mark = grip.gameObject.AddComponent<UnitHeldItem>();
            var so = new SerializedObject(mark);
            so.FindProperty("_kind").enumValueIndex = (int)kind;
            so.FindProperty("_twoHanded").boolValue = twoHanded;
            // Рабочая часть объявляется ЯВНО — в игре тоже (04.08.2026). У предмета из одного куска это он сам.
            if (declareReach) so.FindProperty("_reachPart").objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            return renderer;
        }

        public UnitPartRegistry Registry(params SpriteRenderer[] parts) =>
            UnitPartRegistry.FromBody(parts, Root.transform);
    }
}
