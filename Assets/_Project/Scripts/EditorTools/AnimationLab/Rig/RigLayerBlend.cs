#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Composes two clips through an avatar mask the way the Animator does at runtime: a base clip on the
    /// whole body, an overlay clip on the masked bones only.
    ///
    /// <b>Why this exists.</b> Every acceptance picture so far judged ONE clip, and the game never plays one:
    /// the shield rises over a running body, over a swing, over a charge. Whether those read together is a
    /// question about the combination, and nothing could draw it (Max, 30.07). Sampling the two clips one
    /// after another does not answer it either — the second sample overwrites the whole rig, mask and all.
    ///
    /// So the mask is applied by hand: sample the base, remember every local pose, sample the overlay, and
    /// restore the bones the mask does NOT cover. What is left is exactly what the layer would produce.
    /// </summary>
    public static class RigLayerBlend
    {
        /// <summary>One node's local pose — everything a clip can write on this rig.</summary>
        readonly struct Pose
        {
            public readonly Transform Node;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public Pose(Transform node)
            {
                Node     = node;
                Position = node.localPosition;
                Rotation = node.localRotation;
                Scale    = node.localScale;
            }

            public void Restore()
            {
                Node.localPosition = Position;
                Node.localRotation = Rotation;
                Node.localScale    = Scale;
            }
        }

        /// <summary>
        /// Sample <paramref name="baseClip"/> at <paramref name="baseTime"/> and lay
        /// <paramref name="overlay"/> at <paramref name="overlayTime"/> on top, but only on the bones
        /// <paramref name="mask"/> enables. Null overlay or null mask = plain base sample.
        /// </summary>
        public static void Sample(GameObject unit, AnimationClip baseClip, float baseTime,
                                  AnimationClip overlay, float overlayTime, AvatarMask mask)
        {
            if (unit == null || baseClip == null) return;

            baseClip.SampleAnimation(unit, baseTime);
            if (overlay == null || mask == null) return;

            // Позы ДО оверлея: восстанавливать придётся всё, что маска не пускает.
            var kept = new List<Pose>();
            HashSet<string> masked = MaskedPaths(mask);
            CollectPoses(unit.transform, unit.transform, masked, kept);

            overlay.SampleAnimation(unit, overlayTime);

            for (int i = 0; i < kept.Count; i++) kept[i].Restore();
        }

        /// <summary>Paths the mask ENABLES. Everything else the overlay is not allowed to touch.</summary>
        static HashSet<string> MaskedPaths(AvatarMask mask)
        {
            var paths = new HashSet<string>();
            for (int i = 0; i < mask.transformCount; i++)
                if (mask.GetTransformActive(i)) paths.Add(mask.GetTransformPath(i));
            return paths;
        }

        // Обход с накоплением пути от корня: маска адресует узлы путями, а не именами, и одноимённые
        // «Rotation Point (Shoulder)» в двух руках иначе слились бы в один.
        static void CollectPoses(Transform node, Transform root, HashSet<string> masked, List<Pose> kept)
        {
            foreach (Transform child in node)
            {
                if (!masked.Contains(PathOf(child, root))) kept.Add(new Pose(child));
                CollectPoses(child, root, masked, kept);
            }
        }

        static string PathOf(Transform node, Transform root)
        {
            var stack = new List<string>();
            for (Transform t = node; t != null && t != root; t = t.parent) stack.Add(t.name);
            stack.Reverse();
            return string.Join("/", stack);
        }
    }
}
#endif
