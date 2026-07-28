#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Checks clips against the rig they animate. Every rule here exists because the failure it catches
    /// happened, cost a session, and reported nothing: dead curve paths, a weapon rotating around its own
    /// middle instead of its grip, a curve written on one axis while Unity zeroes the other two, a swing
    /// wrapped across +-180 so it plays the long way round.
    ///
    /// It reports; it does not repair. The one exception belongs to <see cref="RigEulerFilter"/>, which
    /// fixes continuity without changing any pose.
    /// </summary>
    public static class RigValidator
    {
        public enum Severity { Info, Warning, Error }

        public sealed class Finding
        {
            public Severity Severity;
            public string Clip;
            public string Rule;
            public string Message;

            public override string ToString() => $"[{Severity}] {Clip} / {Rule}: {Message}";
        }

        public sealed class Report
        {
            public readonly List<Finding> Findings = new List<Finding>();
            public int Errors, Warnings, Checked;

            public override string ToString()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{Checked} clips checked: {Errors} errors, {Warnings} warnings");
                foreach (var finding in Findings) sb.AppendLine("  " + finding);
                return sb.ToString();
            }
        }

        /// <summary>Checks every clip in the folders the profile's rig lives in, plus the rig itself.</summary>
        public static Report Validate(RigProfile profile, string[] clipFolders, float sampleRate = 30f)
        {
            if (profile == null) throw new System.ArgumentNullException(nameof(profile));
            if (profile.Rig == null) throw new System.ArgumentException("RigProfile.Rig is not set.");

            var report = new Report();
            var clips = RigMigrate.LoadClips(clipFolders);

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            GameObject unit = null;
            try
            {
                unit = (GameObject)PrefabUtility.InstantiatePrefab(profile.Rig);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(unit, scene);
                unit.transform.position = Vector3.zero;

                CheckCalibration(profile, unit.transform, report);

                foreach (var clip in clips)
                {
                    report.Checked++;
                    CheckPaths(clip, unit.transform, report);
                    CheckBindingKinds(clip, report);
                    CheckWholeCurves(clip, report);
                    CheckItemsRotateAtTheGrip(profile, clip, report);
                    CheckContinuity(clip, report);
                    CheckLimits(profile, clip, unit, report, sampleRate);
                }
            }
            finally
            {
                if (unit != null) Object.DestroyImmediate(unit);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }

            foreach (var finding in report.Findings)
            {
                if (finding.Severity == Severity.Error) report.Errors++;
                else if (finding.Severity == Severity.Warning) report.Warnings++;
            }
            return report;
        }

        static void Add(Report report, Severity severity, string clip, string rule, string message) =>
            report.Findings.Add(new Finding { Severity = severity, Clip = clip, Rule = rule, Message = message });

        /// <summary>A curve whose path no longer resolves stops animating in silence.</summary>
        static void CheckPaths(AnimationClip clip, Transform root, Report report)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path.Length == 0) continue;
                if (root.Find(binding.path) != null) continue;
                Add(report, Severity.Error, clip.name, "dead-path",
                    $"'{binding.path}' does not exist in the rig — the curve animates nothing");
            }
        }

        /// <summary>
        /// Unity allows one kind of rotation binding per clip. Mixing raw Euler with quaternion curves
        /// means half the clip takes the shortest path and arcs over 180 degrees collapse.
        /// </summary>
        static void CheckBindingKinds(AnimationClip clip, Report report)
        {
            bool euler = false, quaternion = false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.propertyName.StartsWith("localEulerAnglesRaw", System.StringComparison.Ordinal)) euler = true;
                else if (binding.propertyName.StartsWith("m_LocalRotation", System.StringComparison.Ordinal)) quaternion = true;
            }
            if (euler && quaternion)
                Add(report, Severity.Error, clip.name, "mixed-rotation-bindings",
                    "clip holds both localEulerAnglesRaw and m_LocalRotation curves; arcs over 180 degrees " +
                    "will collapse on the quaternion half");
        }

        /// <summary>
        /// Unity fills an axis with zero rather than leaving it alone, so a rotation curve written on z
        /// only silently flattens x and y — and a position curve written on y only teleports x to zero.
        /// </summary>
        static void CheckWholeCurves(AnimationClip clip, Report report)
        {
            var axes = new Dictionary<string, HashSet<string>>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                int dot = binding.propertyName.LastIndexOf('.');
                if (dot < 0) continue;
                string property = binding.propertyName.Substring(0, dot);
                if (property != "localEulerAnglesRaw" && property != "m_LocalPosition" && property != "m_LocalScale") continue;

                string key = binding.path + "|" + property;
                if (!axes.TryGetValue(key, out var found)) axes[key] = found = new HashSet<string>();
                found.Add(binding.propertyName.Substring(dot + 1));
            }

            foreach (var pair in axes)
            {
                if (pair.Value.Count >= 3) continue;
                var parts = pair.Key.Split('|');
                Add(report, Severity.Error, clip.name, "partial-curve",
                    $"{parts[1]} on '{parts[0]}' has only [{string.Join(",", pair.Value)}] — " +
                    "Unity writes the missing axes as zero");
            }
        }

        /// <summary>
        /// A held item must be turned by its grip, not by its own transform. Sprites pivot at their centre,
        /// so rotating a long item directly swings the hilt out of the fist — for this sword the pommel sits
        /// 0.17 units from the hand, which is invisible on short bones and obvious on a blade.
        /// </summary>
        static void CheckItemsRotateAtTheGrip(RigProfile profile, AnimationClip clip, Report report)
        {
            foreach (var item in profile.Held)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.path != item.ItemPath) continue;
                    if (!binding.propertyName.StartsWith("localEulerAnglesRaw", System.StringComparison.Ordinal) &&
                        !binding.propertyName.StartsWith("m_LocalPosition", System.StringComparison.Ordinal)) continue;

                    Add(report, Severity.Error, clip.name, "item-not-at-grip",
                        $"'{item.Id}' is animated on its own sprite node ({binding.propertyName}); it must be " +
                        $"driven by its grip '{item.GripPath}' or it leaves the hand");
                    break;
                }
            }
        }

        /// <summary>
        /// A rotation curve should read as a continuous series. A step over 180 degrees between neighbouring
        /// keys means the number wrapped, and Unity will play the arc the other way round.
        /// </summary>
        static void CheckContinuity(AnimationClip clip, Report report)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!binding.propertyName.StartsWith("localEulerAnglesRaw", System.StringComparison.Ordinal)) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length < 2) continue;

                var keys = curve.keys;
                for (int i = 1; i < keys.Length; i++)
                {
                    float step = keys[i].value - keys[i - 1].value;
                    if (Mathf.Abs(step) <= 180f) continue;
                    Add(report, Severity.Warning, clip.name, "wrapped-arc",
                        $"'{binding.path}' {binding.propertyName} jumps {step:F0} deg between t={keys[i - 1].time:F2} " +
                        $"and t={keys[i].time:F2} — if that long way round is intended, say so with Arc; " +
                        "otherwise run RigEulerFilter");
                }
            }
        }

        /// <summary>Joint limits are art limits here: past them, square segments come apart at the seam.</summary>
        static void CheckLimits(RigProfile profile, AnimationClip clip, GameObject unit, Report report, float sampleRate)
        {
            int steps = Mathf.Max(2, Mathf.RoundToInt(clip.length * sampleRate));
            var worst = new Dictionary<string, float>();

            for (int i = 0; i <= steps; i++)
            {
                clip.SampleAnimation(unit, clip.length * i / steps);
                foreach (var joint in profile.Joints)
                {
                    if (joint.FlexLimit <= 0f) continue;
                    var node = unit.transform.Find(joint.Path);
                    if (node == null) continue;

                    float delta = Mathf.Abs(Mathf.DeltaAngle(joint.RestZ, node.localEulerAngles.z));
                    if (!worst.TryGetValue(joint.Id, out float previous) || delta > previous) worst[joint.Id] = delta;
                }
            }

            foreach (var pair in worst)
            {
                var joint = profile.FindJoint(pair.Key);
                if (pair.Value <= joint.FlexLimit + 0.5f) continue;
                Add(report, Severity.Warning, clip.name, "past-limit",
                    $"{pair.Key} reaches {pair.Value:F0} deg from rest, limit is {joint.FlexLimit:F0}");
            }
        }

        /// <summary>
        /// The calibration that makes a grip's zero mean something: on this rig, zero puts the blade at a
        /// right angle to the forearm. If it drifts, every angle authored against it is quietly wrong.
        /// </summary>
        static void CheckCalibration(RigProfile profile, Transform root, Report report)
        {
            foreach (var item in profile.Held)
            {
                var grip = root.Find(item.GripPath);
                if (grip == null)
                {
                    Add(report, Severity.Error, profile.Rig.name, "calibration",
                        $"grip path of '{item.Id}' not found: {item.GripPath}");
                    continue;
                }

                var joint = profile.FindJoint(FindGripId(profile, item));
                if (joint == null) continue;

                float orientationWorld = RigProbe.WorldOrientation(grip, item);
                float boneWorld = grip.parent != null
                    ? RigProfileBuilder.NormalizeAngle(grip.parent.eulerAngles.z + BoneAxisOf(profile, grip.parent, root))
                    : 0f;
                float between = Mathf.Abs(Mathf.DeltaAngle(orientationWorld, boneWorld));
                Add(report, Severity.Info, profile.Rig.name, "calibration",
                    $"'{item.Id}' {item.OrientationName} sits {between:F1} deg from its bone at grip zero " +
                    $"(calibration offset {item.CalibrationZ:F2})");
            }
        }

        static float BoneAxisOf(RigProfile profile, Transform node, Transform root)
        {
            string path = AnimationUtility.CalculateTransformPath(node, root);
            foreach (var joint in profile.Joints)
                if (joint.Path == path) return joint.BoneAxisLocal;
            // The item usually hangs off a sprite node rather than a joint; its bone runs down local -Y.
            return -90f;
        }

        static string FindGripId(RigProfile profile, RigProfile.HeldItem item)
        {
            foreach (var joint in profile.Joints)
                if (joint.Path == item.GripPath) return joint.Id;
            return null;
        }

        [MenuItem("Alebardium/Animation/Validate Rig Clips", priority = 620)]
        static void ValidateSelected()
        {
            var profile = Selection.activeObject as RigProfile;
            if (profile == null)
            {
                Debug.LogError("Validate Rig Clips: select a RigProfile asset first.");
                return;
            }

            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(profile.Rig)).Replace('\\', '/');
            var report = Validate(profile, new[] { folder });
            if (report.Errors > 0) Debug.LogError(report.ToString());
            else if (report.Warnings > 0) Debug.LogWarning(report.ToString());
            else Debug.Log(report.ToString());
        }

        [MenuItem("Alebardium/Animation/Validate Rig Clips", validate = true)]
        static bool ValidateSelectedValidate() => Selection.activeObject is RigProfile;
    }
}
#endif
