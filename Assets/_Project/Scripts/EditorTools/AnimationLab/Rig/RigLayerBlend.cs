#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Composes a base clip with a STACK of masked layers the way the Animator does at runtime.
    ///
    /// <b>Why this exists.</b> Every acceptance picture so far judged ONE clip, and the game never plays one:
    /// the shield rises over a running body, over a swing, over a charge. Whether those read together is a
    /// question about the combination, and nothing could draw it (Max, 30.07). Sampling clips one after
    /// another does not answer it either — the second sample overwrites the whole rig, mask and all.
    ///
    /// So masking is done by hand: remember every local pose, sample the layer clip, then restore the bones
    /// the mask does NOT cover. What is left is exactly what the layer would produce.
    ///
    /// <b>Why a stack and not a pair.</b> Since 30.07 an attack is not a base state any more: the body runs on
    /// the base, the swing rides an Override layer masked to the upper body, and the pelvic drop arrives on an
    /// ADDITIVE layer masked to the hips. A picture drawn from the base plus one overlay would be short by a
    /// whole layer — and short exactly at the pelvis, which is the part the additive layer was introduced for.
    /// </summary>
    public static class RigLayerBlend
    {
        /// <summary>How a layer folds into what the layers below it already produced.</summary>
        public enum Blend
        {
            /// <summary>The clip's pose replaces what is underneath, on masked bones.</summary>
            Override,

            /// <summary>
            /// The clip's DELTA from its own first frame is added to what is underneath. This is what keeps
            /// the run's pelvic bob while the blow's drop lands on top of it — an override layer would have
            /// to pick one of the two.
            /// </summary>
            Additive,
        }

        /// <summary>One layer of the stack: a clip sampled at a time, through a mask, in a blend mode.</summary>
        public readonly struct Layer
        {
            public readonly AnimationClip Clip;
            public readonly float Time;
            public readonly AvatarMask Mask;
            public readonly Blend Mode;

            /// <summary>0..1, exactly the Animator's layer weight — 0 means the layer contributes nothing.</summary>
            public readonly float Weight;

            public Layer(AnimationClip clip, float time, AvatarMask mask,
                         Blend mode = Blend.Override, float weight = 1f)
            {
                Clip   = clip;
                Time   = time;
                Mask   = mask;
                Mode   = mode;
                Weight = Mathf.Clamp01(weight);
            }
        }

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
        /// Sample <paramref name="baseClip"/> at <paramref name="baseTime"/> and fold every layer on top,
        /// in order. Null or weightless layers are skipped; a null base does nothing.
        /// </summary>
        public static void Sample(GameObject unit, AnimationClip baseClip, float baseTime, params Layer[] layers)
        {
            if (unit == null || baseClip == null) return;

            baseClip.SampleAnimation(unit, baseTime);
            Fold(unit, layers);
        }

        /// <summary>
        /// Fold layers onto the pose the rig is ALREADY in, without resampling a base. Needed when the pose
        /// underneath is itself a composition: resampling would wipe it, and the picture would silently lose
        /// a whole layer.
        /// </summary>
        public static void Fold(GameObject unit, params Layer[] layers)
        {
            if (unit == null || layers == null) return;

            for (int i = 0; i < layers.Length; i++)
            {
                Layer layer = layers[i];
                if (layer.Clip == null || layer.Mask == null || layer.Weight <= 0f) continue;

                if (layer.Mode == Blend.Additive) FoldAdditive(unit, layer);
                else                              FoldOverride(unit, layer);
            }
        }

        /// <summary>Base plus a single Override layer — the shape every caller used before the stack existed.</summary>
        public static void Sample(GameObject unit, AnimationClip baseClip, float baseTime,
                                  AnimationClip overlay, float overlayTime, AvatarMask mask)
            => Sample(unit, baseClip, baseTime, new Layer(overlay, overlayTime, mask));

        /// <summary>
        /// How a TRACED clip sits on a body that is doing something else — the arrangement the game plays
        /// since the attack moved onto a layer: the body runs on the base, the swing rides an Override layer,
        /// the pelvic drop arrives Additive.
        /// </summary>
        /// <remarks>
        /// Lives here, next to the blending itself, because both the contact sheet and the sweep gizmo need
        /// it and a second copy would drift: one picture would show the pose the game plays and the other
        /// would not, with nothing to say which.
        /// </remarks>
        public sealed class Composition
        {
            /// <summary>Клип, который в это время играет БАЗА. Null = композиции нет, клип играется один.</summary>
            public AnimationClip BaseClip;

            /// <summary>
            /// Момент базы. Отрицательное = база крутится СИНХРОННО с трассируемым временем (<c>t % длина</c>) —
            /// так и бежит юнит, пока замахивается; неотрицательное = стоп-кадр базы.
            /// </summary>
            public float BaseTime = -1f;

            /// <summary>Маска слоя действия. Без неё композиции нет — оверлей накрыл бы всё тело.</summary>
            public AvatarMask LayerMask;

            /// <summary>Маска аддитивной надстройки (таз). Null = аддитивного слоя в композиции нет.</summary>
            public AvatarMask AdditiveMask;

            public bool Active => BaseClip != null && LayerMask != null;
        }

        /// <summary>
        /// Pose of <paramref name="traced"/> at <paramref name="time"/> as the game would show it: alone when
        /// there is no composition, or as a layer over a moving base when there is.
        /// </summary>
        public static void SampleTraced(GameObject unit, AnimationClip traced, float time, Composition composition)
        {
            if (unit == null || traced == null) return;

            if (composition == null || !composition.Active)
            {
                traced.SampleAnimation(unit, time);
                return;
            }

            // База крутится синхронно с временем слоя: юнит на въезде продолжает бежать, и стоп-кадр бега
            // под движущимся замахом врал бы ровно про то, ради чего картинка и рисуется.
            float baseLength = Mathf.Max(0.0001f, composition.BaseClip.length);
            float baseTime = composition.BaseTime >= 0f ? composition.BaseTime : Mathf.Repeat(time, baseLength);

            Layer[] layers = composition.AdditiveMask != null
                ? new[]
                {
                    new Layer(traced, time, composition.LayerMask),
                    new Layer(traced, time, composition.AdditiveMask, Blend.Additive),
                }
                : new[] { new Layer(traced, time, composition.LayerMask) };

            Sample(unit, composition.BaseClip, baseTime, layers);
        }

        // Маскированные узлы берут позу клипа, остальные возвращаются к тому, что дали слои ниже.
        static void FoldOverride(GameObject unit, in Layer layer)
        {
            HashSet<string> masked = MaskedPaths(layer.Mask);
            List<Pose> below = Snapshot(unit.transform);

            layer.Clip.SampleAnimation(unit, layer.Time);

            for (int i = 0; i < below.Count; i++)
            {
                Pose was = below[i];
                if (!masked.Contains(PathOf(was.Node, unit.transform))) { was.Restore(); continue; }
                if (layer.Weight < 1f) BlendTowards(was, layer.Weight);
            }
        }

        /// <summary>
        /// Аддитивный слой: к позе снизу прибавляется ДЕЛЬТА клипа от его собственного первого кадра.
        /// Ровно так считает Animator, и поэтому нейтраль берётся из самого клипа, а не из позы покоя рига:
        /// у клипа удара с разбега нулевой кадр — это поза бега, и дельта обязана отсчитываться от неё.
        /// </summary>
        /// <remarks>
        /// Клип сэмплится ДВАЖДЫ (нейтраль и поза), потому что <c>SampleAnimation</c> переписывает риг
        /// целиком — вычесть одно из другого можно только по двум снимкам.
        /// </remarks>
        static void FoldAdditive(GameObject unit, in Layer layer)
        {
            HashSet<string> masked = MaskedPaths(layer.Mask);
            List<Pose> below = Snapshot(unit.transform);

            layer.Clip.SampleAnimation(unit, 0f);
            List<Pose> neutral = Snapshot(unit.transform);

            layer.Clip.SampleAnimation(unit, layer.Time);
            List<Pose> posed = Snapshot(unit.transform);

            for (int i = 0; i < below.Count; i++)
            {
                Pose was = below[i];
                if (!masked.Contains(PathOf(was.Node, unit.transform))) { was.Restore(); continue; }

                Vector3 dPos    = posed[i].Position - neutral[i].Position;
                Quaternion dRot = Quaternion.Inverse(neutral[i].Rotation) * posed[i].Rotation;
                Vector3 dScale  = posed[i].Scale - neutral[i].Scale;

                was.Node.localPosition = was.Position + dPos * layer.Weight;
                was.Node.localScale    = was.Scale + dScale * layer.Weight;
                was.Node.localRotation = layer.Weight >= 1f
                    ? was.Rotation * dRot
                    : Quaternion.Slerp(was.Rotation, was.Rotation * dRot, layer.Weight);
            }
        }

        // Частичный вес override-слоя: узел стоит в позе клипа, тянем его обратно к тому, что было снизу.
        static void BlendTowards(in Pose below, float weight)
        {
            Transform n = below.Node;
            n.localPosition = Vector3.Lerp(below.Position, n.localPosition, weight);
            n.localRotation = Quaternion.Slerp(below.Rotation, n.localRotation, weight);
            n.localScale    = Vector3.Lerp(below.Scale, n.localScale, weight);
        }

        // Снимки берутся ОДНИМ обходом в одном порядке, поэтому индексы трёх списков соответствуют
        // друг другу — без этого дельту не с чем было бы сопоставить.
        static List<Pose> Snapshot(Transform root)
        {
            var poses = new List<Pose>();
            Collect(root, poses);
            return poses;
        }

        static void Collect(Transform node, List<Pose> poses)
        {
            foreach (Transform child in node)
            {
                poses.Add(new Pose(child));
                Collect(child, poses);
            }
        }

        /// <summary>Paths the mask ENABLES. Everything else the layer is not allowed to touch.</summary>
        static HashSet<string> MaskedPaths(AvatarMask mask)
        {
            var paths = new HashSet<string>();
            for (int i = 0; i < mask.transformCount; i++)
                if (mask.GetTransformActive(i)) paths.Add(mask.GetTransformPath(i));
            return paths;
        }

        // Обход с накоплением пути от корня: маска адресует узлы путями, а не именами, и одноимённые
        // «Rotation Point (Shoulder)» в двух руках иначе слились бы в один.
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
