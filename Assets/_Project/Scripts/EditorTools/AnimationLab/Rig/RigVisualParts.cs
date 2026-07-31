#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Lays the rig out in three levels: bone -> "Visual Part (Bone)" -> sprite nodes.
    ///
    /// Scale is the reason for the middle level. A bone used to carry both its own artwork and the
    /// joints below it — the forearm sprite literally parented the grip, and the grip the sword — so
    /// scaling a part to fit redrawn art dragged the skeleton under it along for the ride. Scale now
    /// lives on the container and reaches the artwork alone.
    ///
    /// The container stays an EMPTY node and the renderers hang below it, because a part is allowed
    /// more than one sprite (armour over a torso, a plume on a helm). Putting the first renderer on
    /// the container itself would make sprite number one privileged and the rest its children.
    ///
    /// Bone names, and therefore every animated path, are untouched: clips, avatar masks and the
    /// generic avatar address bones, and no bone moves. Only tools that grabbed the renderer with
    /// GetComponent on the bone had to learn to look below it.
    /// </summary>
    public static class RigVisualParts
    {
        // Сами имена и предикаты живут в рантайм-конвенции: их читает не только этот инструмент, но и
        // реестр частей в игре, а конвенция с двумя копиями расходится молча.
        public static string ContainerName(string boneName) => Presentation.Body.RigNaming.ContainerName(boneName);

        public static bool IsContainer(Transform node) => Presentation.Body.RigNaming.IsContainer(node);

        /// <summary>"Visual Part (Head)" -> "Head".</summary>
        public static string BoneNameFromContainer(string containerName) =>
            Presentation.Body.RigNaming.BoneNameFromContainer(containerName);

        /// <summary>
        /// The renderer that draws THIS bone: the first one inside its own visual container, never a
        /// child joint's. GetComponentInChildren would happily return the sword hanging off the forearm.
        /// </summary>
        public static SpriteRenderer FindVisual(Transform bone)
        {
            if (bone == null) return null;

            var own = bone.GetComponent<SpriteRenderer>();
            if (own != null) return own; // not split yet

            var container = FindContainer(bone);
            if (container == null) return null;

            var onContainer = container.GetComponent<SpriteRenderer>();
            if (onContainer != null) return onContainer;

            for (int i = 0; i < container.childCount; i++)
            {
                var renderer = container.GetChild(i).GetComponent<SpriteRenderer>();
                if (renderer != null) return renderer;
            }
            return null;
        }

        /// <summary>Every renderer drawing this bone — a part may carry several sprites.</summary>
        public static List<SpriteRenderer> FindVisuals(Transform bone)
        {
            var found = new List<SpriteRenderer>();
            if (bone == null) return found;

            var container = FindContainer(bone);
            if (container == null)
            {
                var own = bone.GetComponent<SpriteRenderer>();
                if (own != null) found.Add(own);
                return found;
            }
            container.GetComponentsInChildren(true, found);
            return found;
        }

        /// <summary>
        /// True for a container and anything inside it. Names in there belong to the artist — a sprite
        /// node may be called "Head", "Hair" or "Armor Plate" — so rig code that recognises bones BY
        /// NAME has to stop at the container or it will invent joints out of artwork.
        /// </summary>
        public static bool IsUnderContainer(Transform node) => Presentation.Body.RigNaming.IsUnderContainer(node);

        public static Transform FindContainer(Transform bone) => Presentation.Body.RigNaming.FindContainer(bone);

        /// <summary>
        /// The bone a renderer draws for, walking back up through its container. Callers that address
        /// rig parts by bone name ("Body", "Head") must not start missing them once the artwork moved
        /// two levels down.
        /// </summary>
        public static string BoneNameOf(Transform rendererNode) =>
            Presentation.Body.RigNaming.BoneNameOf(rendererNode);

        public sealed class Report
        {
            public readonly List<string> Lines = new List<string>();
            public int Moved;
            public int AlreadySplit;

            public override string ToString()
            {
                var sb = new StringBuilder($"visual parts split: {Moved} moved, {AlreadySplit} already in place\n");
                foreach (var line in Lines) sb.AppendLine("  " + line);
                return sb.ToString();
            }
        }

        /// <summary>
        /// Splits the prefab at <paramref name="prefabPath"/> into bone -> container -> sprite.
        /// Idempotent, and it also finishes a half-done layout: a renderer left sitting ON a container
        /// is pushed one level further down.
        /// </summary>
        public static Report Split(string prefabPath, bool dryRun = false)
        {
            var report = new Report();
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                // Collected up front: nodes created mid-walk would otherwise be revisited.
                var renderers = new List<SpriteRenderer>(root.GetComponentsInChildren<SpriteRenderer>(includeInactive: true));

                foreach (var renderer in renderers)
                {
                    var node = renderer.transform;

                    // Already in place: a sprite node whose parent is the container.
                    if (IsContainer(node.parent))
                    {
                        report.AlreadySplit++;
                        continue;
                    }

                    bool onContainer = IsContainer(node);
                    string boneName = onContainer ? BoneNameFromContainer(node.name) : node.name;
                    string nodePath = AnimationUtility.CalculateTransformPath(node, root.transform);

                    if (dryRun)
                    {
                        report.Moved++;
                        report.Lines.Add(onContainer
                            ? $"{nodePath} — renderer moves off the container into '{boneName}' (dry run)"
                            : $"{nodePath} -> {ContainerName(boneName)}/{boneName} (dry run)");
                        continue;
                    }

                    Transform container;
                    if (onContainer)
                    {
                        container = node;
                    }
                    else
                    {
                        var created = new GameObject(ContainerName(boneName));
                        container = created.transform;
                        container.SetParent(node, worldPositionStays: false);
                        Reset(container);
                        // First child: the artwork reads above any joint hanging off this bone.
                        container.SetAsFirstSibling();
                    }

                    var spriteNode = new GameObject(boneName);
                    spriteNode.transform.SetParent(container, worldPositionStays: false);
                    Reset(spriteNode.transform);

                    UnityEditorInternal.ComponentUtility.CopyComponent(renderer);
                    UnityEditorInternal.ComponentUtility.PasteComponentAsNew(spriteNode);
                    Object.DestroyImmediate(renderer);

                    report.Moved++;
                    report.Lines.Add($"{nodePath} -> {ContainerName(boneName)}/{boneName}");
                }

                if (!dryRun && report.Moved > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return report;
        }

        static void Reset(Transform node)
        {
            node.localPosition = Vector3.zero;
            node.localRotation = Quaternion.identity;
            node.localScale = Vector3.one;
        }

        [MenuItem("Alebardium/Animation/Split Rig Visual Parts", priority = 615)]
        static void SplitSelected()
        {
            var prefab = Selection.activeObject as GameObject;
            string path = AssetDatabase.GetAssetPath(prefab);
            var report = Split(path);
            Debug.Log($"[RigVisualParts] {path}\n{report}");
        }

        [MenuItem("Alebardium/Animation/Split Rig Visual Parts", validate = true)]
        static bool SplitSelectedValidate() =>
            Selection.activeObject is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go);
    }
}
#endif
