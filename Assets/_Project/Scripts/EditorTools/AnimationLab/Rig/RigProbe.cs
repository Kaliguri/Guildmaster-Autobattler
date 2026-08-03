#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Draws the rig's invisible parts on top of a render of it: joints, bone axes, the grip and where
    /// the item points when the grip sits at zero.
    ///
    /// This exists because the questions that cost the most time are not visible in the art — "is the
    /// grip where the hand is", "which way does zero face", "did the blade leave the fist" — and they
    /// were being answered by three rounds of "still broken" instead of by one look.
    /// </summary>
    public static class RigProbe
    {
        public sealed class Options
        {
            /// <summary>Pose the rig from this clip before drawing. Null = the prefab's rest pose.</summary>
            public AnimationClip Clip;
            public float Time;
            /// <summary>Frame the shot around this joint id (with its bone and item) instead of the whole unit.</summary>
            public string FocusJoint;
            public int Size = 900;
            public float Padding = 1.25f;
            public Color Background = new Color(0.09f, 0.09f, 0.11f, 1f);
            public string OutputPath;

            /// <summary>
            /// Draw this rig instead of <see cref="RigProfile.Rig"/> — a variant wearing different art,
            /// judged against the profile of the rig it came from.
            /// </summary>
            public GameObject Rig;

            /// <summary>Name every joint on the picture itself, so the dots stop needing a legend.</summary>
            public bool Labels = true;

            /// <summary>
            /// Draw where each sprite's pivot actually sits, and the gap to the joint it hangs under.
            /// This is the one thing that cannot be seen in the art and breaks rotation silently.
            /// </summary>
            public bool Anchors = true;

            /// <summary>Gap, in art pixels, above which a piece is drawn as sitting on the wrong point.</summary>
            public float AnchorTolerancePixels = RigAnchors.DefaultTolerancePixels;

            public int LabelScale = 2;
        }

        public sealed class Result
        {
            public string Path;
            /// <summary>Joint id -> the colour it was drawn in, so the picture can be read.</summary>
            public readonly List<string> Legend = new List<string>();

            public override string ToString() => Path + "\n" + string.Join("\n", Legend);
        }

        static readonly Color BoneColor = new Color(1f, 1f, 1f, 1f);
        static readonly Color OrientationColor = new Color(1f, 0.60f, 0.15f);
        static readonly Color ZeroColor = new Color(0.30f, 0.85f, 1f);
        static readonly Color ButtColor = new Color(1f, 0.35f, 0.90f);
        static readonly Color BoneEndColor = new Color(1f, 0.90f, 0.25f);
        static readonly Color AnchorOkColor = new Color(0.45f, 1f, 0.55f);
        static readonly Color AnchorBadColor = new Color(1f, 0.25f, 0.25f);
        static readonly Color AnchorLegacyColor = new Color(0.45f, 0.45f, 0.50f);

        public static Result Render(RigProfile profile, Options options = null)
        {
            if (profile == null) throw new System.ArgumentNullException(nameof(profile));
            if (profile.Rig == null) throw new System.ArgumentException("RigProfile.Rig is not set.");

            options ??= new Options();
            int size = Mathf.Max(256, options.Size);
            var result = new Result();

            var scene = EditorSceneManager.NewPreviewScene();
            GameObject unit = null, camGo = null;
            RenderTexture rt = null;
            try
            {
                unit = (GameObject)PrefabUtility.InstantiatePrefab(options.Rig != null ? options.Rig : profile.Rig);
                SceneManager.MoveGameObjectToScene(unit, scene);
                unit.transform.position = Vector3.zero;
                unit.transform.rotation = Quaternion.identity;
                if (options.Clip != null) options.Clip.SampleAnimation(unit, options.Time);

                var root = unit.transform;
                var bounds = MeasureBounds(unit, profile, root, options.FocusJoint);

                camGo = new GameObject("RigProbeCamera");
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
                rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                cam.Render();

                var tex = ReadBack(rt, size);
                var canvas = new RigCanvas(tex, cam, size);

                foreach (var joint in profile.Joints)
                {
                    var node = root.Find(joint.Path);
                    if (node == null)
                    {
                        result.Legend.Add($"{joint.Id}: PATH NOT FOUND ({joint.Path})");
                        continue;
                    }

                    var color = JointColor(joint.Id);
                    var boneEnd = node.TransformPoint(PolarLocal(joint.BoneAxisLocal, joint.BoneLength));
                    canvas.Line(node.position, boneEnd, BoneColor, 0);
                    canvas.Dot(node.position, 7, color);
                    if (options.Labels) canvas.Label(node.position, joint.Id, color, options.LabelScale);
                    result.Legend.Add($"{joint.Id}: {ColorName(color)}  rest={joint.RestZ:F2}  " +
                                      $"boneAxis={joint.BoneAxisLocal:F1}  len={joint.BoneLength:F4}");
                }

                if (options.Anchors) DrawAnchors(canvas, root, profile, options, result);

                foreach (var item in profile.Held)
                {
                    var grip = root.Find(item.GripPath);
                    var sprite = root.Find(item.ItemPath);
                    if (grip == null || sprite == null)
                    {
                        result.Legend.Add($"[{item.Id}]: PATH NOT FOUND");
                        continue;
                    }

                    // Where the item points now, and where it would point if the grip were at zero.
                    float liveWorld = WorldOrientation(grip, item);
                    float zeroWorld = liveWorld - NormalizeAngle(grip.localEulerAngles.z);
                    canvas.Line(grip.position, grip.position + PolarWorld(liveWorld, item.ItemLength), OrientationColor, 1);
                    canvas.Line(grip.position, grip.position + PolarWorld(zeroWorld, item.ItemLength * 0.55f), ZeroColor, 0);
                    canvas.Cross(grip.position, 14, new Color(1f, 0.25f, 0.25f));
                    canvas.Dot(grip.position + PolarWorld(liveWorld + 180f, item.GripToButt), 6, ButtColor);

                    if (grip.parent != null)
                    {
                        var boneRenderer = RigVisualParts.FindVisual(grip.parent);
                        if (boneRenderer != null && boneRenderer.sprite != null)
                        {
                            float half = boneRenderer.sprite.bounds.extents.y;
                            canvas.Dot(boneRenderer.transform.TransformPoint(new Vector3(0f, -half, 0f)), 6, BoneEndColor);
                        }
                    }

                    result.Legend.Add($"[{item.Id}] {item.OrientationName} now={liveWorld:F1} deg world, " +
                                      $"at grip zero={zeroWorld:F1} deg (orange = now, cyan = zero, " +
                                      $"magenta = butt, yellow = bone end)");
                }

                tex.Apply();
                string path = string.IsNullOrEmpty(options.OutputPath)
                    ? Path.GetFullPath(Path.Combine(AnimationLabRenderer.DefaultOutputDir, profile.Rig.name + "_probe.png"))
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

        /// <summary>
        /// Draws the second opinion about where every joint is: the pivot of each sprite hanging under it.
        ///
        /// A piece whose pivot left its joint keeps turning — around the joint, while the drawing sits
        /// somewhere else, so it swings on an arc that has nothing to do with the limb. That is invisible
        /// in a still pose and reads as "the rotation points moved", which is why the gap is drawn as a
        /// line with its length in art pixels written next to it.
        /// </summary>
        static void DrawAnchors(RigCanvas canvas, Transform root, RigProfile profile, Options options, Result result)
        {
            var anchors = RigAnchors.Collect(root, profile);
            int declared = 0;
            foreach (var anchor in anchors)
            {
                // Кусок с центральным пивотом ничего про свою точку крепления не заявлял — он держится
                // смещением узла, и мерить ему «пивот против сустава» значит обвинять работающее.
                if (!anchor.DeclaresPivot)
                {
                    canvas.Dot(anchor.PivotWorld, 3, AnchorLegacyColor);
                    continue;
                }

                declared++;
                bool off = anchor.OffsetPixels > options.AnchorTolerancePixels;
                var color = off ? AnchorBadColor : AnchorOkColor;
                canvas.Dot(anchor.PivotWorld, 4, color);
                if (!off) continue;

                canvas.Line(anchor.Joint.position, anchor.PivotWorld, color, 1);
                canvas.Label(anchor.PivotWorld, $"{anchor.SpriteName} {anchor.OffsetPixels:F0}PX",
                             color, options.LabelScale, 10, -20);
            }

            var offenders = RigAnchors.Offenders(anchors, options.AnchorTolerancePixels);
            var scales = RigAnchors.ScaleHistogram(anchors);
            result.Legend.Add(offenders.Count == 0
                ? $"anchors: all {declared} pieces with a declared pivot sit on their joint " +
                  $"({anchors.Count - declared} placeholders skipped — centre pivot claims nothing)"
                : $"anchors: {offenders.Count} of {declared} pieces turn around the wrong point " +
                  $"(red line = the gap; {anchors.Count - declared} placeholders skipped)");
            foreach (var bad in offenders)
                result.Legend.Add($"  {bad.JointId} <- {bad.SpriteName}: {bad.OffsetPixels:F0} px off, scale {bad.Scale:F3}");
            if (scales.Count > 1)
            {
                var parts = new List<string>();
                foreach (var s in scales) parts.Add($"{s.Key:F3}x{s.Value}");
                result.Legend.Add("scales: " + string.Join(", ", parts) +
                                  " — pieces live in different world sizes, proportions are no longer the drawn ones");
            }
        }

        /// <summary>
        /// Where the item's orientation marker points in world degrees, right now.
        /// <see cref="RigProfile.HeldItem.OrientationLocal"/> is the marker's angle inside the sprite's
        /// own frame (90 = along local +Y), so the world angle is simply the sprite's world rotation
        /// plus that — no extra term. Subtracting 90 here once drew the blade pointing backwards.
        /// </summary>
        public static float WorldOrientation(Transform grip, RigProfile.HeldItem item)
        {
            var itemTransform = item.Resolve(grip);
            if (itemTransform == null) return NormalizeAngle(grip.eulerAngles.z);
            return NormalizeAngle(itemTransform.eulerAngles.z + item.OrientationLocal);
        }

        static Bounds MeasureBounds(GameObject unit, RigProfile profile, Transform root, string focusJoint)
        {
            if (!string.IsNullOrEmpty(focusJoint))
            {
                var joint = profile.FindJoint(focusJoint);
                var node = joint != null ? root.Find(joint.Path) : null;
                if (node != null)
                {
                    var bounds = new Bounds(node.position, Vector3.one * 0.05f);
                    foreach (var renderer in node.GetComponentsInChildren<Renderer>(false))
                        bounds.Encapsulate(renderer.bounds);
                    if (node.parent != null)
                    {
                        var parentRenderer = node.parent.GetComponent<Renderer>();
                        if (parentRenderer != null) bounds.Encapsulate(parentRenderer.bounds);
                    }
                    return bounds;
                }
            }

            var all = new Bounds(unit.transform.position, Vector3.one * 0.1f);
            foreach (var renderer in unit.GetComponentsInChildren<Renderer>(false))
                all.Encapsulate(renderer.bounds);
            return all;
        }

        static Vector3 PolarLocal(float degrees, float length) =>
            new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad) * length, Mathf.Sin(degrees * Mathf.Deg2Rad) * length, 0f);

        static Vector3 PolarWorld(float degrees, float length) => PolarLocal(degrees, length);

        static float NormalizeAngle(float degrees) => RigProfileBuilder.NormalizeAngle(degrees);

        static Texture2D ReadBack(RenderTexture rt, int size)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            return tex;
        }

        static readonly (string name, Color color)[] Palette =
        {
            ("cyan",    new Color(0.30f, 0.85f, 1.00f)),
            ("green",   new Color(0.35f, 0.95f, 0.45f)),
            ("violet",  new Color(0.70f, 0.50f, 1.00f)),
            ("amber",   new Color(1.00f, 0.75f, 0.20f)),
            ("teal",    new Color(0.20f, 0.80f, 0.70f)),
            // No pink or yellow in here: magenta marks the item's butt and yellow the bone end, and a
            // joint drawn in a near-identical colour reads as one of those markers.
            ("indigo",  new Color(0.45f, 0.55f, 1.00f)),
        };

        [MenuItem("Alebardium/Animation/Render Rig Anchors Gizmo", priority = 625)]
        static void RenderSelected()
        {
            var prefab = Selection.activeObject as GameObject;
            if (prefab == null)
            {
                Debug.LogError("Render Rig Anchors Gizmo: select a rig prefab first.");
                return;
            }

            var profile = FindProfileFor(prefab);
            if (profile == null)
            {
                Debug.LogError($"Render Rig Anchors Gizmo: no RigProfile points at {prefab.name} " +
                               "or at any prefab it is a variant of.");
                return;
            }

            var result = Render(profile, new Options
            {
                Rig = prefab,
                Size = 1400,
                Padding = 1.12f,
                Background = new Color(0.62f, 0.63f, 0.66f, 1f),
                OutputPath = Path.Combine(AnimationLabRenderer.DefaultOutputDir, prefab.name + "_anchors.png"),
            });
            // Построчно: консоль через MCP отдаёт только первую строку многострочного лога, и легенда,
            // ради которой картинку и смотрят, до читателя не доезжает.
            Debug.Log("Rig anchors: " + result.Path);
            foreach (var line in result.Legend) Debug.Log("  " + line);
            EditorUtility.RevealInFinder(result.Path);
        }

        [MenuItem("Alebardium/Animation/Render Rig Anchors Gizmo", validate = true)]
        static bool RenderSelectedValidate() => Selection.activeObject is GameObject;

        /// <summary>
        /// Профиль этого рига, либо профиль префаба, вариантом которого он является. Вариант с другим
        /// артом судится профилем родителя намеренно: свой профиль у него появится только когда его
        /// геометрию признают отдельной, а до тех пор вопрос к нему ровно один — «насколько он от
        /// родителя уехал».
        /// </summary>
        static RigProfile FindProfileFor(GameObject prefab)
        {
            var profiles = new List<RigProfile>();
            foreach (var guid in AssetDatabase.FindAssets("t:RigProfile"))
            {
                var p = AssetDatabase.LoadAssetAtPath<RigProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null) profiles.Add(p);
            }

            for (var candidate = prefab; candidate != null;
                 candidate = PrefabUtility.GetCorrespondingObjectFromSource(candidate))
            {
                foreach (var p in profiles)
                    if (p.Rig == candidate) return p;
            }
            return null;
        }

        static Color JointColor(string id)
        {
            int hash = 0;
            foreach (char c in id) hash = hash * 31 + c;
            return Palette[Mathf.Abs(hash) % Palette.Length].color;
        }

        static string ColorName(Color color)
        {
            foreach (var entry in Palette)
                if (entry.color == color) return entry.name;
            return "white";
        }
    }
}
#endif
