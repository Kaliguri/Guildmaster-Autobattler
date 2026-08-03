#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Mechanical passes over an AnimationClip's curves — the part of animation that is craft
    /// rather than taste: overlap, tangents by phase, dead constant curves.
    ///
    /// Always writes a new clip beside the source; the original is never modified.
    /// Timing rules follow docs/wiki/gdd/10-vision/character-animation.md.
    /// </summary>
    public static class AnimationLabProcessor
    {
        public sealed class Options
        {
            /// <summary>Frames a child lags behind its parent. Canon: forearm 1, weapon 2 (= two levels).</summary>
            public float LagFramesPerLevel = 1f;
            /// <summary>Extra lag for specific transform paths, e.g. head trailing the body.</summary>
            public Dictionary<string, float> ExtraLagFrames = new();
            /// <summary>Impact time in seconds. Negative = read from an AnimationEvent, else skip the tangent pass.</summary>
            public float ImpactTime = -1f;

            public bool ApplyOverlap = true;
            public bool ApplyTangents = true;
            public bool CleanDeadCurves = true;

            /// <summary>Suffix for the generated clip. Empty = overwrite guard trips.</summary>
            public string OutputSuffix = "_processed";
        }

        public sealed class Report
        {
            public string ClipPath;
            public int CurvesTotal;
            public int CurvesShifted;
            public int CurvesRetangented;
            public int DeadCurvesRemoved;
            public readonly List<string> Skipped = new();
            public float ImpactTime = -1f;

            public override string ToString()
            {
                var line = $"{ClipPath}: {CurvesTotal} curves, shifted {CurvesShifted}, retangented {CurvesRetangented}, dead removed {DeadCurvesRemoved}";
                if (ImpactTime >= 0f) line += $", impact at {ImpactTime:F3}s";
                if (Skipped.Count > 0) line += $"\nskipped: {string.Join("; ", Skipped)}";
                return line;
            }
        }

        public static Report Process(GameObject rig, AnimationClip source, Options options = null)
        {
            if (source == null) throw new System.ArgumentNullException(nameof(source));
            options ??= new Options();

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
                throw new System.InvalidOperationException("The clip must be a saved asset.");
            if (string.IsNullOrEmpty(options.OutputSuffix))
                throw new System.InvalidOperationException("OutputSuffix must be set — this pass never overwrites the source.");

            float frameRate = source.frameRate > 0f ? source.frameRate : 60f;
            var report = new Report();

            // Instantiate() carries curves, events and clip settings across; we then edit the copy.
            var working = Object.Instantiate(source);
            working.name = source.name + options.OutputSuffix;

            var bindings = AnimationUtility.GetCurveBindings(working);
            report.CurvesTotal = bindings.Length;

            if (options.CleanDeadCurves)
                RemoveDeadCurves(rig, working, bindings, report);

            // Re-read: the dead pass may have dropped bindings.
            bindings = AnimationUtility.GetCurveBindings(working);

            if (options.ApplyOverlap)
                ApplyOverlap(working, bindings, frameRate, options, report);

            if (options.ApplyTangents)
            {
                float impact = ResolveImpactTime(source, options);
                report.ImpactTime = impact;
                if (impact >= 0f) ApplyTangents(working, AnimationUtility.GetCurveBindings(working), impact, frameRate, report);
                else report.Skipped.Add("tangent pass: no impact time (set it manually or add an AnimationEvent)");
            }

            string directory = System.IO.Path.GetDirectoryName(sourcePath).Replace('\\', '/');
            string outputPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{working.name}.anim");
            AssetDatabase.CreateAsset(working, outputPath);
            // Точечно: SaveAssets() пишет ВСЕ грязные ассеты проекта, включая чужую несохранённую
            // работу в инспекторе. Здесь сохранять надо ровно созданный клип.
            AssetDatabase.SaveAssetIfDirty(working);

            report.ClipPath = outputPath;
            return report;
        }

        /// <summary>
        /// Parts must not move in lockstep: a child starts a frame or two after its parent, so the
        /// weapon chases the arm instead of riding glued to it.
        /// First and last keys stay put — moving them would break looping and clip stitching.
        /// </summary>
        static void ApplyOverlap(AnimationClip clip, EditorCurveBinding[] bindings, float frameRate, Options options, Report report)
        {
            int minDepth = bindings.Length == 0 ? 0 : bindings.Min(b => Depth(b.path));

            foreach (var binding in bindings)
            {
                float lagFrames = (Depth(binding.path) - minDepth) * options.LagFramesPerLevel;
                if (options.ExtraLagFrames != null && options.ExtraLagFrames.TryGetValue(binding.path, out float extra))
                    lagFrames += extra;
                if (Mathf.Approximately(lagFrames, 0f)) continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length < 3) continue;

                float lag = lagFrames / frameRate;
                var keys = curve.keys;
                float first = keys[0].time;
                float last = keys[keys.Length - 1].time;
                float minGap = 0.5f / frameRate;
                bool changed = false;

                for (int i = 1; i < keys.Length - 1; i++)
                {
                    float shifted = Mathf.Clamp(keys[i].time + lag, first + minGap, last - minGap);
                    // Never let a shifted key pass its neighbour — that would reorder the pose sequence.
                    shifted = Mathf.Max(shifted, keys[i - 1].time + minGap);
                    if (Mathf.Approximately(shifted, keys[i].time)) continue;
                    keys[i].time = shifted;
                    changed = true;
                }

                if (!changed) continue;
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                report.CurvesShifted++;
            }
        }

        /// <summary>
        /// Flat tangents make every key a micro-stop: the strike starts from zero speed and ramps up
        /// instead of exploding. Smooth everything, then make the approach to impact linear so the
        /// motion accelerates into contact without braking.
        /// </summary>
        static void ApplyTangents(AnimationClip clip, EditorCurveBinding[] bindings, float impactTime, float frameRate, Report report)
        {
            float window = 1.5f / frameRate;

            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length < 2) continue;

                for (int i = 0; i < curve.length; i++)
                {
                    bool isImpact = Mathf.Abs(curve[i].time - impactTime) <= window;
                    bool leadsIntoImpact = i + 1 < curve.length && Mathf.Abs(curve[i + 1].time - impactTime) <= window;

                    if (isImpact)
                    {
                        // Accelerate in, settle out: the hold after contact is what makes the hit read.
                        AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                    }
                    else if (leadsIntoImpact)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    }
                    else
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                    }
                }

                AnimationUtility.SetEditorCurve(clip, binding, curve);
                report.CurvesRetangented++;
            }
        }

        /// <summary>
        /// Record mode leaves constant curves behind. They keep writing their value and will drown
        /// another layer if they fall under its mask — but a constant that disagrees with the prefab
        /// pose is load-bearing: deleting it snaps that body part. Those are reported, not removed.
        /// </summary>
        static void RemoveDeadCurves(GameObject rig, AnimationClip clip, EditorCurveBinding[] bindings, Report report)
        {
            if (rig == null)
            {
                report.Skipped.Add("dead-curve pass: no rig prefab to compare against");
                return;
            }

            var scene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(rig);
                SceneManager.MoveGameObjectToScene(instance, scene);

                foreach (var binding in bindings)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.length == 0) continue;

                    float value = curve[0].value;
                    bool constant = curve.keys.All(k => Mathf.Approximately(k.value, value));
                    if (!constant) continue;

                    if (!TryReadPrefabValue(instance, binding, out float prefabValue, out bool isAngle))
                    {
                        report.Skipped.Add($"{binding.path}:{binding.propertyName} (constant, unreadable property — left alone)");
                        continue;
                    }

                    // Euler curves and the prefab can hold the same pose 360 apart (-16.34 vs 343.66),
                    // and for a constant curve that is the same pose, not a disagreement.
                    float delta = isAngle ? Mathf.Abs(Mathf.DeltaAngle(value, prefabValue)) : Mathf.Abs(prefabValue - value);
                    if (delta > 0.0001f)
                    {
                        report.Skipped.Add($"{binding.path}:{binding.propertyName} (constant {value:F3} != prefab {prefabValue:F3} — load-bearing, left alone)");
                        continue;
                    }

                    AnimationUtility.SetEditorCurve(clip, binding, null);
                    report.DeadCurvesRemoved++;
                }
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static bool TryReadPrefabValue(GameObject root, EditorCurveBinding binding, out float value, out bool isAngle)
        {
            value = 0f;
            isAngle = false;
            if (binding.type != typeof(Transform)) return false;

            var target = string.IsNullOrEmpty(binding.path) ? root.transform : root.transform.Find(binding.path);
            if (target == null) return false;

            switch (binding.propertyName)
            {
                case "m_LocalPosition.x": value = target.localPosition.x; return true;
                case "m_LocalPosition.y": value = target.localPosition.y; return true;
                case "m_LocalPosition.z": value = target.localPosition.z; return true;
                case "m_LocalScale.x": value = target.localScale.x; return true;
                case "m_LocalScale.y": value = target.localScale.y; return true;
                case "m_LocalScale.z": value = target.localScale.z; return true;
                case "localEulerAnglesRaw.x": value = target.localEulerAngles.x; isAngle = true; return true;
                case "localEulerAnglesRaw.y": value = target.localEulerAngles.y; isAngle = true; return true;
                case "localEulerAnglesRaw.z": value = target.localEulerAngles.z; isAngle = true; return true;
                default: return false;
            }
        }

        static float ResolveImpactTime(AnimationClip source, Options options)
        {
            if (options.ImpactTime >= 0f) return options.ImpactTime;
            var events = AnimationUtility.GetAnimationEvents(source);
            return events is { Length: > 0 } ? events[0].time : -1f;
        }

        static int Depth(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            return path.Count(c => c == '/') + 1;
        }

        /// <summary>
        /// Where the clip moves fastest — a starting guess for the impact frame, not a verdict.
        /// The author confirms it; the timing of a hit is a design call, not a measurement.
        /// </summary>
        public static float SuggestImpactTime(AnimationClip clip)
        {
            if (clip == null) return -1f;
            float frameRate = clip.frameRate > 0f ? clip.frameRate : 60f;
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * frameRate));
            var bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length == 0) return -1f;

            var curves = bindings
                .Select(b => AnimationUtility.GetEditorCurve(clip, b))
                .Where(c => c != null && c.length > 1)
                .ToArray();
            if (curves.Length == 0) return -1f;

            float step = 1f / frameRate;
            float bestTime = -1f, bestSpeed = 0f;

            for (int f = 1; f <= frames; f++)
            {
                float t = f * step;
                float speed = curves.Sum(c => Mathf.Abs(c.Evaluate(t) - c.Evaluate(t - step)));
                if (speed > bestSpeed) { bestSpeed = speed; bestTime = t; }
            }

            return bestTime;
        }
    }
}
#endif
