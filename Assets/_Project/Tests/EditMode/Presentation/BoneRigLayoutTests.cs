using System.Collections.Generic;
using System.Text;
using Guildmaster.AnimationLab.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Holds the two rig-layout invariants that no single file can hold, because both halves of each
    /// live on opposite sides of a seam — the prefab and the tools that read it.
    ///
    /// Both fail SILENTLY in the editor, which is the whole reason they are tests. A sprite dropped
    /// straight onto a bone still draws perfectly while the tools measuring that bone quietly report
    /// zeros; a clip whose path no longer resolves simply stops animating, which is how four attack
    /// clips once lost their sword without a single console line.
    /// </summary>
    public class BoneRigLayoutTests
    {
        const string RigPrefab = "Assets/_Project/Prefabs/Bones/BoneUnit_Standart.prefab";
        const string ClipFolder = "Assets/_Project/Prefabs/Bones";
        const string RigProfileAsset = "Assets/_Project/Prefabs/Bones/BoneUnit_Standart_RigProfile.asset";

        static GameObject LoadRig()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            Assert.That(rig, Is.Not.Null, $"Rig prefab missing: {RigPrefab}");
            return rig;
        }

        /// <summary>
        /// Artwork belongs two levels below the bone: bone -> container -> sprite. On the bone it would
        /// take the bone's scale and the joints under it; on the container itself it would be the one
        /// privileged sprite among the several a part is allowed to carry.
        /// </summary>
        [Test]
        public void EverySpriteRendererLivesInsideAVisualPartContainer()
        {
            var rig = LoadRig();
            var offenders = new List<string>();

            foreach (var renderer in rig.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
            {
                var node = renderer.transform;
                if (RigVisualParts.IsContainer(node.parent) && !RigVisualParts.IsContainer(node)) continue;
                offenders.Add(AnimationUtility.CalculateTransformPath(node, rig.transform));
            }

            Assert.That(offenders, Is.Empty,
                "Sprite renderers belong on nodes INSIDE a 'Visual Part (Bone)' container — not on the bone, " +
                "not on the container. Run Alebardium/Animation/Split Rig Visual Parts. Offenders:\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// The profile describes BONES, so nothing it lists may live inside a visual container. Sprite
        /// nodes are named freely by whoever draws them, and a sprite called "Head" was picked up as a
        /// second 'head' joint the first time the containers landed — silently, with both entries valid
        /// on their face.
        /// </summary>
        [Test]
        public void ProfileDescribesBonesOnlyAndHasNoDuplicateIds()
        {
            var profile = AssetDatabase.LoadAssetAtPath<RigProfile>(RigProfileAsset);
            Assert.That(profile, Is.Not.Null, $"Rig profile missing: {RigProfileAsset}");

            var seen = new HashSet<string>();
            var duplicates = new List<string>();
            var insideContainers = new List<string>();

            foreach (var joint in profile.Joints)
            {
                if (!seen.Add(joint.Id)) duplicates.Add(joint.Id);
                if (joint.Path != null && joint.Path.Contains("Visual Part (")) insideContainers.Add($"{joint.Id}: {joint.Path}");
            }

            Assert.That(duplicates, Is.Empty, "Duplicate joint ids in the profile: " + string.Join(", ", duplicates));
            Assert.That(insideContainers, Is.Empty,
                "These profile joints point inside a visual container, i.e. at artwork rather than a bone:\n  " +
                string.Join("\n  ", insideContainers));
        }

        /// <summary>
        /// Every path a clip animates has to exist in the rig. Renaming or re-parenting a node breaks
        /// these bindings without any error — the curve just stops reaching anything.
        /// </summary>
        [Test]
        public void EveryAnimatedPathResolvesInTheRig()
        {
            var rig = LoadRig();
            var missing = new SortedSet<string>();
            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipFolder });
            Assert.That(guids.Length, Is.GreaterThan(0), $"No clips found under {ClipFolder}");

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) continue;

                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (string.IsNullOrEmpty(binding.path)) continue; // the root itself
                    if (rig.transform.Find(binding.path) == null)
                        missing.Add($"{clip.name}: {binding.path}");
                }
            }

            var report = new StringBuilder("Clip bindings point at nodes the rig does not have. ")
                .AppendLine("Nodes are renamed through RigMigrate for exactly this reason:");
            foreach (var line in missing) report.AppendLine("  " + line);
            Assert.That(missing, Is.Empty, report.ToString());
        }
    }
}
