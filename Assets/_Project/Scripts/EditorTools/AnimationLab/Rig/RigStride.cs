#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Draws what a locomotion clip does over TIME: the path each foot travels, against the line of the
    /// ground it is supposed to stand on.
    ///
    /// <b>Why this exists.</b> <see cref="RigSweep"/> answers the acceptance questions of an attack, and a
    /// walk has entirely different ones, none of which a pose sheet can answer. How LONG is the stride —
    /// the number the whole pacing of the clip is divided by. Does the foot go THROUGH the floor. Does it
    /// stay planted while the body passes over it, or does it skate. All three are about a path over time,
    /// so all three are measured here, in world units, and drawn.
    ///
    /// The ground is not a guess: it is where the foot rests in the rig's own rest pose. A foot dipping
    /// below that line is the sole clipping into the floor, and it shows up as path below the line rather
    /// than as an argument about whose eye is right.
    /// </summary>
    public static class RigStride
    {
        public sealed class Options
        {
            /// <summary>
            /// Renderers whose SOLE is traced, matched by name. The sole, not the ankle joint: the ground is
            /// touched by the bottom of the boot sprite, and the joint sits well above it by an offset that
            /// differs between the two legs. Tracing joints made the shorter leg look permanently airborne.
            /// </summary>
            public string[] FootRenderers = { "Leg_Boots" };

            /// <summary>Extra samples per clip frame: the foot moves fastest exactly where frames are sparse.</summary>
            public int SamplesPerFrame = 3;

            /// <summary>How close to the ground still counts as "planted", in world units.</summary>
            public float PlantedTolerance = 0.02f;

            public int Size = 900;
            public float Padding = 1.2f;
            public Color Background = new Color(0.09f, 0.09f, 0.11f, 1f);
            public string OutputPath;
        }

        /// <summary>One foot's whole journey through the clip.</summary>
        public sealed class Foot
        {
            public string Name;
            public readonly List<Vector3> Path = new List<Vector3>();
            public readonly List<bool> IsKey = new List<bool>();

            /// <summary>
            /// Where THIS foot rests in the rig's own rest pose. Per foot rather than one line for both,
            /// because the two legs of this rig are not the same length (right shin 0.105 against left
            /// 0.117): measured against a single floor the short leg reads as never touching the ground at
            /// all, which is a fact about the rig and not about the clip being judged.
            /// </summary>
            public float RestY;

            /// <summary>Clip time of the lowest point — the answer to "where do I fix the sinking".</summary>
            public float LowestTime;

            /// <summary>Horizontal travel — this IS the step: while planted, the body passes over it by this much.</summary>
            public float Stride;
            /// <summary>How deep the foot goes under the ground line. 0 = never.</summary>
            public float BelowGround;
            /// <summary>Highest lift above the ground line.</summary>
            public float Lift;
            /// <summary>Share of the clip the foot spends planted.</summary>
            public float PlantedShare;
        }

        public sealed class Result
        {
            public string Path;
            public string ClipName;
            public float ClipLength;
            public float GroundY;
            public readonly List<Foot> Feet = new List<Foot>();

            /// <summary>Ground covered per second by the clip itself — the number the view paces by.</summary>
            public float UnitsPerSecond;

            public override string ToString()
            {
                var text = new StringBuilder();
                text.AppendLine($"{ClipName}: {ClipLength:F2}s, ground at y={GroundY:F3}");
                foreach (var foot in Feet)
                    text.AppendLine($"  {foot.Name}: шаг {foot.Stride:F3} ед, подъём {foot.Lift:F3}, " +
                                    $"на земле {foot.PlantedShare:P0}, свой покой y={foot.RestY:F3}" +
                                    (foot.BelowGround > 0.001f
                                        ? $"  <-- УХОДИТ ПОД ЗЕМЛЮ на {foot.BelowGround:F3} при t={foot.LowestTime:F3}"
                                        : ""));
                text.AppendLine($"  темп клипа: {UnitsPerSecond:F2} ед/с (два шага за цикл)");
                text.AppendLine($"picture: {Path}");
                return text.ToString();
            }
        }

        // Left foot warm, right foot cool: two paths over one body are unreadable in one colour, and the
        // pair has to be told apart to see that the legs are out of phase rather than doubled.
        static readonly Color LeftPath   = new Color(1.00f, 0.62f, 0.25f);
        static readonly Color RightPath  = new Color(0.35f, 0.75f, 1.00f);
        static readonly Color GroundLine = new Color(0.45f, 1.00f, 0.55f, 0.85f);
        static readonly Color UnderGround = new Color(1.00f, 0.15f, 0.20f);
        static readonly Color StrideMark = new Color(1.00f, 0.90f, 0.35f);

        public static Result Render(RigProfile profile, AnimationClip clip, Options options = null)
        {
            if (profile == null) throw new System.ArgumentNullException(nameof(profile));
            if (clip == null) throw new System.ArgumentNullException(nameof(clip));
            if (profile.Rig == null) throw new System.ArgumentException("RigProfile.Rig is not set.");

            options ??= new Options();
            var result = new Result { ClipName = clip.name, ClipLength = clip.length };

            float frameRate = clip.frameRate > 0f ? clip.frameRate : 60f;
            int perFrame = Mathf.Max(1, options.SamplesPerFrame);
            int samples = Mathf.Max(4, Mathf.RoundToInt(clip.length * frameRate * perFrame));

            var scene = EditorSceneManager.NewPreviewScene();
            GameObject unit = null, camGo = null;
            RenderTexture rt = null;
            try
            {
                unit = (GameObject)PrefabUtility.InstantiatePrefab(profile.Rig);
                SceneManager.MoveGameObjectToScene(unit, scene);
                unit.transform.position = Vector3.zero;
                unit.transform.rotation = Quaternion.identity;

                List<Renderer> feet = FindFeet(unit.transform, options.FootRenderers);
                if (feet.Count == 0)
                    throw new System.InvalidOperationException("На риге не нашлось спрайтов стоп: " +
                                                               string.Join(", ", options.FootRenderers));

                // The ground is the rest pose, sampled BEFORE the clip touches anything: the rig is authored
                // standing on the floor, so that is where the floor is. Recorded per foot as well — the two
                // legs of this rig differ in length, and the shared line is only what gets DRAWN.
                float ground = float.MaxValue;
                foreach (Renderer foot in feet) ground = Mathf.Min(ground, Sole(foot).y);
                result.GroundY = ground;

                foreach (Renderer foot in feet)
                    result.Feet.Add(new Foot { Name = FootLabel(foot.transform), RestY = Sole(foot).y });

                var bounds = new Bounds(unit.transform.position, Vector3.one * 0.05f);
                var renderers = unit.GetComponentsInChildren<Renderer>(includeInactive: false);
                float keyEpsilon = 0.5f / (frameRate * perFrame);
                var keyTimes = KeyTimes(clip, frameRate);

                for (int s = 0; s <= samples; s++)
                {
                    float t = clip.length * s / samples;
                    clip.SampleAnimation(unit, t);

                    bool isKey = IsKeyTime(keyTimes, t, keyEpsilon);
                    for (int i = 0; i < feet.Count; i++)
                    {
                        result.Feet[i].Path.Add(Sole(feet[i]));
                        result.Feet[i].IsKey.Add(isKey);
                    }

                    foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                }

                Summarise(result, options.PlantedTolerance);

                camGo = new GameObject("RigStrideCamera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = options.Background;
                cam.aspect = 1f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * Mathf.Max(1f, options.Padding);
                cam.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);

                // Backdrop: the contact pose (frame zero of a locomotion cycle), so the paths are read
                // against a body standing in the stride rather than against nothing.
                clip.SampleAnimation(unit, 0f);

                int size = Mathf.Max(256, options.Size);
                rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                cam.Render();

                Texture2D tex = ReadBack(rt, size);
                Draw(new RigCanvas(tex, cam, size), result, cam);
                tex.Apply();

                string path = string.IsNullOrEmpty(options.OutputPath)
                    ? Path.GetFullPath(Path.Combine(AnimationLabRenderer.DefaultOutputDir, clip.name + "_stride.png"))
                    : Path.GetFullPath(options.OutputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);

                result.Path = path;
                return result;
            }
            finally
            {
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (unit != null) Object.DestroyImmediate(unit);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // Numbers, from the paths. Everything here is a property of the CLIP, not of any unit that plays it.
        static void Summarise(Result result, float tolerance)
        {
            float strideSum = 0f;
            foreach (Foot foot in result.Feet)
            {
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                int planted = 0, lowestIndex = 0;
                for (int i = 0; i < foot.Path.Count; i++)
                {
                    Vector3 p = foot.Path[i];
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) { minY = p.y; lowestIndex = i; }
                    if (p.y > maxY) maxY = p.y;
                    // Планка «на земле» — СВОЙ покой ноги, а не общая линия: короткая нога до общей не
                    // достаёт никогда и показывала бы 0% всегда.
                    if (p.y - foot.RestY <= tolerance) planted++;
                }

                foot.Stride       = maxX - minX;
                foot.BelowGround  = Mathf.Max(0f, foot.RestY - minY);
                foot.Lift         = Mathf.Max(0f, maxY - foot.RestY);
                foot.PlantedShare = foot.Path.Count > 0 ? (float)planted / foot.Path.Count : 0f;
                foot.LowestTime   = foot.Path.Count > 1
                    ? result.ClipLength * lowestIndex / (foot.Path.Count - 1)
                    : 0f;
                strideSum += foot.Stride;
            }

            // Two steps to a cycle, so the clip's own pace is the average stride twice over its length.
            float averageStride = result.Feet.Count > 0 ? strideSum / result.Feet.Count : 0f;
            result.UnitsPerSecond = result.ClipLength > 0f ? averageStride * 2f / result.ClipLength : 0f;
        }

        /// <summary>
        /// The picture: the ground line across the frame, each foot's path over it, key samples marked, and
        /// the horizontal extent of the stride called out at the bottom. Anything below the ground line is
        /// redrawn in red — that is the one failure the eye should not have to hunt for.
        /// </summary>
        static void Draw(RigCanvas canvas, Result result, Camera cam)
        {
            float halfWidth = cam.orthographicSize;
            var groundLeft  = new Vector3(cam.transform.position.x - halfWidth, result.GroundY, 0f);
            var groundRight = new Vector3(cam.transform.position.x + halfWidth, result.GroundY, 0f);
            canvas.Line(groundLeft, groundRight, GroundLine, 1);

            for (int f = 0; f < result.Feet.Count; f++)
            {
                Foot foot = result.Feet[f];
                Color color = f == 0 ? LeftPath : RightPath;

                for (int i = 0; i + 1 < foot.Path.Count; i++)
                {
                    bool under = foot.Path[i].y < foot.RestY - 0.001f;
                    canvas.Line(foot.Path[i], foot.Path[i + 1], under ? UnderGround : color, under ? 3 : 2);
                }

                for (int i = 0; i < foot.Path.Count; i++)
                    if (foot.IsKey[i]) canvas.Dot(foot.Path[i], 4, color);

                // The extent, drawn where it is measured: two uprights at the ends of the travel, joined
                // along the ground. A stride length that has to be taken on trust is a stride length nobody
                // checks.
                float minX = float.MaxValue, maxX = float.MinValue;
                foreach (Vector3 p in foot.Path) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }

                float rail = result.GroundY - 0.04f * (f + 1);
                canvas.Line(new Vector3(minX, rail, 0f), new Vector3(maxX, rail, 0f), StrideMark, 1);
                canvas.Line(new Vector3(minX, rail - 0.02f, 0f), new Vector3(minX, rail + 0.02f, 0f), StrideMark, 1);
                canvas.Line(new Vector3(maxX, rail - 0.02f, 0f), new Vector3(maxX, rail + 0.02f, 0f), StrideMark, 1);
            }
        }

        static string FootLabel(Transform foot)
        {
            // "Hips/Leg (Left)/.../Rotation Point (Ankle)" -> "Leg (Left)": the side is what the reader needs.
            Transform node = foot;
            while (node != null && !node.name.StartsWith("Leg (")) node = node.parent;
            return node != null ? node.name : foot.name;
        }

        // Где подошва: низ спрайта по центру его ширины. Именно эта точка стоит на земле — и она же
        // единственная, чей путь имеет смысл сравнивать с линией пола.
        static Vector3 Sole(Renderer foot)
        {
            Bounds b = foot.bounds;
            return new Vector3(b.center.x, b.min.y, 0f);
        }

        static List<Renderer> FindFeet(Transform root, string[] names)
        {
            var found = new List<Renderer>();
            foreach (Renderer node in root.GetComponentsInChildren<Renderer>(true))
                foreach (string name in names)
                    if (node.name == name) found.Add(node);
            return found;
        }

        static List<float> KeyTimes(AnimationClip clip, float frameRate)
        {
            var times = new List<float>();
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                foreach (Keyframe key in curve.keys)
                {
                    float rounded = Mathf.Round(key.time * frameRate) / frameRate;
                    if (!times.Contains(rounded)) times.Add(rounded);
                }
            }
            times.Sort();
            return times;
        }

        static bool IsKeyTime(List<float> keyTimes, float time, float epsilon)
        {
            for (int i = 0; i < keyTimes.Count; i++)
                if (Mathf.Abs(keyTimes[i] - time) <= epsilon) return true;
            return false;
        }

        static Texture2D ReadBack(RenderTexture rt, int size)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = previous;
            return tex;
        }
    }
}
#endif
