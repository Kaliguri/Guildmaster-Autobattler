#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Builds the knight's animator controller from the clips on disk, so that a clip which exists on disk
    /// exists as a state, and adding one means writing its recipe and nothing else.
    ///
    /// <b>Why this exists.</b> The clip list used to be kept in step by hand, which made it a fact with two
    /// owners — and it drifted exactly as such facts do: a new clip would be added to one controller and
    /// silently miss the other. "Where is the stun?" is the question that costs the drift.
    ///
    /// <b>There used to be two controllers here.</b> A "combat" one and an R&amp;D stand — and their names
    /// lied backwards: the stand was what actually played in battle for the whole roster, while the one
    /// called combat sat on a dev duellist alone. Both the duellist and that controller were removed on
    /// 2026-08-06; what is left is the single controller the game plays.
    ///
    /// <b>What stays authored.</b> Only the structure: which layers exist, which mask each wears, and which
    /// clip belongs to an overlay rather than to the base layer. That list is short, it is the design, and
    /// it lives right here.
    /// </summary>
    public static class BoneUnitControllerBuilder
    {
        const string Folder = "Assets/_Project/Prefabs/Bones/";

        static readonly string[] Controllers = { "BoneUnit_SwordShield" };

        /// <summary>
        /// Clips that belong to an overlay layer instead of the base one, and the mask that layer wears.
        /// An overlay without a mask would cover the whole body and kill the legs mid-stride.
        /// </summary>
        static readonly (string layer, string mask, string clip)[] Overlays =
        {
            ("Arms",  "Mask_Arms",      "Attack"),   // a swing over a running body, for part of the bestiary
            ("Block", "Mask_ShieldArm", "Block"),    // the Bulwark telegraph, shield arm only
        };

        /// <summary>
        /// States whose name differs from their clip. The code plays state NAMES, so renaming a state to
        /// match its clip would silently stop the animation the day someone renames the clip instead.
        /// </summary>
        static readonly Dictionary<string, string> StateNameByClip = new Dictionary<string, string>
        {
            { "Walk", "Run" },   // locomotion state is "Run" in code; the clip behind it is the walk cycle
        };

        [MenuItem("Alebardium/Animation/Rebuild Bone Unit Controllers", priority = 605)]
        public static void RebuildAll()
        {
            var log = new System.Text.StringBuilder("Rebuilt bone unit controllers from the clips on disk:\n");
            foreach (string name in Controllers) log.AppendLine(Rebuild(name));
            Debug.Log(log.ToString());
        }

        /// <summary>
        /// Brings one controller in line with the clips on disk: every clip gets a state, overlays get their
        /// masked layer at weight zero. Existing states keep their position and their transitions — this
        /// adds what is missing rather than rewriting what is there, because the graph is hand-arranged.
        /// </summary>
        public static string Rebuild(string controllerName)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + controllerName + ".controller");
            if (ctrl == null) return controllerName + ": controller not found";

            var added = new List<string>();

            // Overlay layers first: their clips must not also land on the base layer.
            var overlayClips = new HashSet<string>();
            foreach (var (layer, mask, clip) in Overlays)
            {
                overlayClips.Add(clip);
                EnsureLayer(ctrl, layer, mask, clip, added);
            }

            AnimatorStateMachine baseLayer = ctrl.layers[0].stateMachine;
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { Folder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null || overlayClips.Contains(clip.name)) continue;

                string stateName = StateNameByClip.TryGetValue(clip.name, out string mapped) ? mapped : clip.name;
                if (HasState(baseLayer, stateName)) continue;

                var state = baseLayer.AddState(stateName);
                state.motion = clip;
                // WriteDefaults ON across the base layer: overlays live by layer weight rather than by an
                // Empty state, and that whole class of gotcha disappears with it.
                state.writeDefaultValues = true;
                added.Add(stateName);
            }

            if (added.Count > 0)
            {
                EditorUtility.SetDirty(ctrl);
                AssetDatabase.SaveAssetIfDirty(ctrl);
            }

            return added.Count > 0
                ? $"{controllerName}: added {string.Join(", ", added)}"
                : $"{controllerName}: already complete";
        }

        /// <summary>
        /// Layers that used to carry a different name in one of the controllers. Renamed rather than
        /// re-created: adding the new name beside the old one leaves two layers driving the same bones
        /// through the same mask, and the second one silently wins.
        /// </summary>
        static readonly Dictionary<string, string> LayerRenames = new Dictionary<string, string>
        {
            { "Brace", "Block" },   // the layer is named after its clip, as the stand had it from the start
        };

        static void EnsureLayer(AnimatorController ctrl, string layerName, string maskName, string clipName,
                                List<string> added)
        {
            bool hasWanted = false, hasLegacy = false;
            foreach (var existing in ctrl.layers)
            {
                if (existing.name == layerName) hasWanted = true;
                if (IsLegacyNameOf(existing.name, layerName)) hasLegacy = true;
            }

            // Both names present — a previous run added the layer instead of renaming it. Two layers on the
            // same mask drive the same bones, and the later one silently wins: drop the stray.
            if (hasWanted && hasLegacy)
            {
                var kept = new List<AnimatorControllerLayer>();
                foreach (var existing in ctrl.layers)
                    if (!IsLegacyNameOf(existing.name, layerName)) kept.Add(existing);
                ctrl.layers = kept.ToArray();
                added.Add($"dropped stray layer duplicating {layerName}");
                return;
            }

            if (hasWanted) return;

            // Only the older name is there — rename in place and keep its states.
            if (hasLegacy)
            {
                var renamed = ctrl.layers;
                for (int i = 0; i < renamed.Length; i++)
                {
                    if (!IsLegacyNameOf(renamed[i].name, layerName)) continue;
                    added.Add($"layer {renamed[i].name} renamed to {layerName}");
                    renamed[i].name = layerName;
                    ctrl.layers = renamed;
                    return;
                }
            }

            ctrl.AddLayer(layerName);
            var layers = ctrl.layers;
            var layer = layers[layers.Length - 1];
            layer.avatarMask    = AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder + maskName + ".mask");
            layer.defaultWeight = 0f;   // silent until code raises it
            layer.blendingMode  = AnimatorLayerBlendingMode.Override;

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + clipName + ".anim");
            if (clip != null)
            {
                var state = layer.stateMachine.AddState(clipName);
                state.motion = clip;
                state.writeDefaultValues = true;
            }

            layers[layers.Length - 1] = layer;
            ctrl.layers = layers;   // ctrl.layers hands out a COPY; without assigning back the edit is lost
            added.Add("layer " + layerName);
        }

        static bool IsLegacyNameOf(string candidate, string layerName)
            => LayerRenames.TryGetValue(candidate, out string current) && current == layerName;

        static bool HasState(AnimatorStateMachine machine, string name)
        {
            foreach (var child in machine.states)
                if (child.state.name == name) return true;
            return false;
        }
    }
}
#endif
