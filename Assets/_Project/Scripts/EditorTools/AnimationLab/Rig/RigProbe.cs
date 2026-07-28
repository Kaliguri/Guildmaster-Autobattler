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
                unit = (GameObject)PrefabUtility.InstantiatePrefab(profile.Rig);
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
                var canvas = new Canvas(tex, cam, size);

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
                    result.Legend.Add($"{joint.Id}: {ColorName(color)}  rest={joint.RestZ:F2}  " +
                                      $"boneAxis={joint.BoneAxisLocal:F1}  len={joint.BoneLength:F4}");
                }

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
                        var boneRenderer = grip.parent.GetComponent<SpriteRenderer>();
                        if (boneRenderer != null && boneRenderer.sprite != null)
                        {
                            float half = boneRenderer.sprite.bounds.extents.y;
                            canvas.Dot(grip.parent.TransformPoint(new Vector3(0f, -half, 0f)), 6, BoneEndColor);
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
        /// Where the item's orientation marker points in world degrees, right now.
        /// <see cref="RigProfile.HeldItem.OrientationLocal"/> is the marker's angle inside the sprite's
        /// own frame (90 = along local +Y), so the world angle is simply the sprite's world rotation
        /// plus that — no extra term. Subtracting 90 here once drew the blade pointing backwards.
        /// </summary>
        public static float WorldOrientation(Transform grip, RigProfile.HeldItem item)
        {
            var sprite = grip.Find(System.IO.Path.GetFileName(item.ItemPath));
            var itemTransform = sprite != null ? sprite : grip.GetComponentInChildren<SpriteRenderer>()?.transform;
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

        /// <summary>Pixel drawing over a rendered frame, in world coordinates.</summary>
        sealed class Canvas
        {
            readonly Texture2D _tex;
            readonly Camera _cam;
            readonly int _size;

            public Canvas(Texture2D tex, Camera cam, int size) { _tex = tex; _cam = cam; _size = size; }

            Vector2 ToPixels(Vector3 world)
            {
                var viewport = _cam.WorldToViewportPoint(world);
                return new Vector2(viewport.x * _size, viewport.y * _size);
            }

            public void Dot(Vector3 world, int radius, Color color)
            {
                var p = ToPixels(world);
                for (int dx = -radius; dx <= radius; dx++)
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (dx * dx + dy * dy > radius * radius) continue;
                        Plot((int)p.x + dx, (int)p.y + dy, color);
                    }
            }

            public void Line(Vector3 worldA, Vector3 worldB, Color color, int thickness)
            {
                var a = ToPixels(worldA);
                var b = ToPixels(worldB);
                int steps = (int)Vector2.Distance(a, b) * 2 + 2;
                for (int i = 0; i <= steps; i++)
                {
                    var p = Vector2.Lerp(a, b, i / (float)steps);
                    for (int dx = -thickness; dx <= thickness; dx++)
                        for (int dy = -thickness; dy <= thickness; dy++)
                            Plot((int)p.x + dx, (int)p.y + dy, color);
                }
            }

            public void Cross(Vector3 world, int radius, Color color)
            {
                var p = ToPixels(world);
                for (int i = -radius; i <= radius; i++)
                {
                    Plot((int)p.x + i, (int)p.y + i, color);
                    Plot((int)p.x + i, (int)p.y - i, color);
                }
            }

            void Plot(int x, int y, Color color)
            {
                if (x < 0 || y < 0 || x >= _size || y >= _size) return;
                _tex.SetPixel(x, y, color);
            }
        }
    }
}
#endif
