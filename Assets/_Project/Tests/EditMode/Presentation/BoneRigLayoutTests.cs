using System.Collections.Generic;
using System.Text;
using Guildmaster.AnimationLab.Editor;
using Guildmaster.Presentation.Body;
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
        /// Artwork lives on its own node under the bone, named "Bone_Art". Straight on the bone it would
        /// take the bone's scale and drag every joint below it; and a bone is allowed several drawings
        /// (face and hair, blade and guard and hilt), so no single renderer may sit on the bone itself.
        /// </summary>
        [Test]
        public void EverySpriteRendererLivesOnAnArtNodeUnderItsBone()
        {
            var rig = LoadRig();
            var offenders = new List<string>();

            foreach (var renderer in rig.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
            {
                var node = renderer.transform;
                if (RigNaming.IsArt(node) && node.parent != null && RigNaming.IsBone(node.parent)) continue;
                offenders.Add(AnimationUtility.CalculateTransformPath(node, rig.transform));
            }

            Assert.That(offenders, Is.Empty,
                "Sprite renderers belong on '<Bone>_Art' nodes hanging off a bone — never on the bone " +
                "itself, never on another art node. Offenders:\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// The profile describes BONES, so nothing it lists may be an art node. A sprite called "Head"
        /// was once picked up as a second 'head' joint — silently, with both entries valid on their face;
        /// the "_Art" suffix is what keeps drawings out of the skeleton now.
        /// </summary>
        [Test]
        public void ProfileDescribesBonesOnlyAndHasNoDuplicateIds()
        {
            var profile = AssetDatabase.LoadAssetAtPath<RigProfile>(RigProfileAsset);
            Assert.That(profile, Is.Not.Null, $"Rig profile missing: {RigProfileAsset}");

            var seen = new HashSet<string>();
            var duplicates = new List<string>();
            var artwork = new List<string>();

            foreach (var joint in profile.Joints)
            {
                if (!seen.Add(joint.Id)) duplicates.Add(joint.Id);
                if (joint.Path != null && joint.Path.Contains(RigNaming.ArtSuffix)) artwork.Add($"{joint.Id}: {joint.Path}");
            }

            Assert.That(duplicates, Is.Empty, "Duplicate joint ids in the profile: " + string.Join(", ", duplicates));
            Assert.That(artwork, Is.Empty,
                "These profile joints point at an art node rather than a bone:\n  " +
                string.Join("\n  ", artwork));
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

        /// <summary>
        /// The body seam holds every part by reference, and those references die when the sprite nodes are
        /// recreated — which is exactly what moving the artwork into containers did on 31.07.2026. All
        /// sixteen went null at once and nothing said a word: tint, hit flash, silhouette and death shards
        /// all run through this list, and an empty answer looks the same as "no effect right now".
        /// <para>Counted against the renderers actually in the rig, so a part added to the hierarchy and
        /// never wired shows up here too.</para>
        /// </summary>
        [Test]
        public void EveryBodyPartReferenceStillPointsAtArtwork()
        {
            var rig = LoadRig();
            var body = rig.GetComponentInChildren<SkeletalBodyVisual>(includeInactive: true);
            Assert.That(body, Is.Not.Null, "The rig carries the body seam — SkeletalBodyVisual is missing.");

            int lost = 0, spriteless = 0;
            foreach (var part in body.Renderers)
            {
                if (part == null) { lost++; continue; }
                if (part.sprite == null) spriteless++;
            }

            int inHierarchy = 0;
            foreach (var renderer in rig.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
                if (renderer.sprite != null) inHierarchy++;

            Assert.That(lost, Is.Zero,
                $"{lost} of {body.Renderers.Count} body parts are lost references. Rebuild the list " +
                "(SkeletalBodyVisual inspector, «Собрать заново») — until then the unit cannot be tinted, " +
                "flashed or shattered.");
            Assert.That(spriteless, Is.Zero, $"{spriteless} wired body parts carry no sprite.");
            Assert.That(body.Renderers.Count, Is.EqualTo(inHierarchy),
                $"The list holds {body.Renderers.Count} parts while the rig draws {inHierarchy}. A part outside " +
                "the list is a part that stays its own colour when the rest of the body flashes.");
        }
    }
}
