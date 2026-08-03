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
    /// Гнёт каждый сустав по ЕГО РЕАЛЬНОМУ диапазону из клипов и показывает, что происходит со стыком:
    /// строка на сустав, три кадра — край, покой, другой край, — и доля перекрытия соседних кусков
    /// числом под каждым кадром.
    ///
    /// Существует потому, что пригодность арта к вращению не видна ни в позе покоя, ни в замере формы.
    /// Замер по кускам 03.08 обещал брак (запас за суставом 8 px, круглость 0.09), а сгиб показал, что
    /// стык держится: перекрытие давала длина ВЕРХНЕГО куска, на которую метрика не смотрела. Приговор
    /// выносит сгиб.
    /// </summary>
    /// <remarks>
    /// Диапазон берётся из клипов, а не назначается: ±40°, выбранные на глаз, оказались втрое меньше
    /// того, что рига требует игра — плечо ходит на 220°, и именно там стык проверяется по-настоящему.
    /// </remarks>
    public static class RigStress
    {
        public sealed class Options
        {
            public GameObject Rig;
            public int CellSize = 420;
            public float Padding = 1.3f;
            public Color Background = new Color(0.62f, 0.63f, 0.66f, 1f);
            public string OutputPath;

            /// <summary>Только эти суставы; пусто — все, под которыми висит арт с объявленным пивотом.</summary>
            public string[] Joints;

            /// <summary>Чем гнуть сустав, который клипы не анимируют.</summary>
            public float FallbackRange = 30f;

            /// <summary>Перекрытие ниже этого — стык считается разошедшимся.</summary>
            public float MinOverlapPercent = 2f;

            /// <summary>
            /// Сколько кадров на сустав. Три — край, покой, край: этого хватает, чтобы поймать разрыв.
            /// Больше нужно, когда ищут ГРАНИЦУ — угол, на котором деталь перестаёт читаться, — а он
            /// лежит где-то внутри диапазона и на трёх кадрах невидим.
            /// </summary>
            public int Steps = 3;
        }

        public sealed class Result
        {
            public string Path;
            public readonly List<string> Lines = new List<string>();
            public override string ToString() => Path + "\n" + string.Join("\n", Lines);
        }

        struct Range
        {
            public float Lo, Hi;
            public bool FromClips;
        }

        public static Result Render(RigProfile profile, Options options = null)
        {
            if (profile == null) throw new System.ArgumentNullException(nameof(profile));
            options ??= new Options();
            var rig = options.Rig != null ? options.Rig : profile.Rig;
            if (rig == null) throw new System.ArgumentException("Не задан риг.");

            var ranges = CollectRanges(profile, options.FallbackRange);
            var result = new Result();

            var scene = EditorSceneManager.NewPreviewScene();
            GameObject unit = null, camGo = null;
            RenderTexture rt = null;
            try
            {
                unit = (GameObject)PrefabUtility.InstantiatePrefab(rig);
                SceneManager.MoveGameObjectToScene(unit, scene);
                unit.transform.position = Vector3.zero;

                camGo = new GameObject("RigStressCamera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = options.Background;
                cam.aspect = 1f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                rt = new RenderTexture(options.CellSize, options.CellSize, 24, RenderTextureFormat.ARGB32,
                                       RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;

                var anchors = RigAnchors.Collect(unit.transform, profile);
                var targets = ChooseJoints(profile, anchors, options.Joints);
                if (targets.Count == 0) throw new System.InvalidOperationException("Нет суставов с объявленным артом.");

                int cell = options.CellSize;
                int steps = Mathf.Max(3, options.Steps);
                var sheet = new Texture2D(cell * steps, cell * targets.Count, TextureFormat.RGBA32, false);
                int row = 0;

                foreach (var joint in targets)
                {
                    var node = unit.transform.Find(joint.Path);
                    var range = ranges[joint.Id];
                    float rest = joint.RestZ;
                    var angles = new float[steps];
                    if (steps == 3) { angles[0] = range.Lo; angles[1] = rest; angles[2] = range.Hi; }
                    else for (int s = 0; s < steps; s++) angles[s] = Mathf.Lerp(range.Lo, range.Hi, s / (float)(steps - 1));

                    var bounds = MeasureAcross(unit, node, angles, rest, FindPiece(unit, joint, anchors, parent: true));
                    cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * Mathf.Max(1f, options.Padding);
                    cam.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);

                    for (int i = 0; i < angles.Length; i++)
                    {
                        SetZ(node, angles[i]);
                        cam.Render();
                        var frame = ReadBack(rt, cell);

                        float overlap = MeasureOverlap(unit, cam, rt, cell, joint, anchors);
                        var canvas = new RigCanvas(frame, cam, cell);
                        bool split = overlap >= 0f && overlap < options.MinOverlapPercent;
                        var tint = split ? new Color(1f, 0.3f, 0.3f) : new Color(0.15f, 1f, 0.45f);
                        canvas.TextPixels(10, cell - 26, $"{joint.Id} {angles[i]:F0}", new Color(0.1f, 0.1f, 0.12f), 2);
                        canvas.TextPixels(10, 12, overlap < 0f ? "OVERLAP N/A" : $"OVERLAP {overlap:F1}%", tint, 2);
                        frame.Apply();

                        sheet.SetPixels(i * cell, (targets.Count - 1 - row) * cell, cell, cell, frame.GetPixels());
                        Object.DestroyImmediate(frame);

                        if (i == 0) result.Lines.Add($"{joint.Id,-12} диапазон {range.Lo,7:F1} .. {range.Hi,7:F1}" +
                                                     (range.FromClips ? "  (из клипов)" : "  (клипы не гнут — взят запас)"));
                        result.Lines.Add($"    {angles[i],7:F1} deg -> перекрытие " +
                                         (overlap < 0f ? "нечего мерить" : $"{overlap:F1}%" + (split ? "  ШОВ РАЗОШЁЛСЯ" : "")));
                    }

                    SetZ(node, rest);
                    row++;
                }

                sheet.Apply();
                string path = Path.GetFullPath(string.IsNullOrEmpty(options.OutputPath)
                    ? Path.Combine(AnimationLabRenderer.DefaultOutputDir, rig.name + "_stress.png")
                    : options.OutputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
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
        /// Доля пикселей КУСКА ЭТОГО СУСТАВА, накрытых куском родительского: именно она отвечает на
        /// вопрос «не разошёлся ли шов». Меряется рендером, а не геометрией, потому что интересует
        /// нарисованное, а не прямоугольник спрайта.
        /// </summary>
        static float MeasureOverlap(GameObject unit, Camera cam, RenderTexture rt, int size,
                                    RigProfile.Joint joint, List<RigAnchors.Anchor> anchors)
        {
            var child = OwnPiece(anchors, joint.Id);
            if (child == null) return -1f;
            var parent = ParentPiece(unit.transform, anchors, child);
            if (parent == null) return -1f;

            var all = unit.GetComponentsInChildren<SpriteRenderer>(true);
            var wasEnabled = new bool[all.Length];
            for (int i = 0; i < all.Length; i++) { wasEnabled[i] = all[i].enabled; all[i].enabled = false; }

            try
            {
                var childMask = RenderMask(cam, rt, size, all, child);
                var parentMask = RenderMask(cam, rt, size, all, parent);
                int childPixels = 0, both = 0;
                for (int i = 0; i < childMask.Length; i++)
                {
                    if (!childMask[i]) continue;
                    childPixels++;
                    if (parentMask[i]) both++;
                }
                return childPixels == 0 ? -1f : both * 100f / childPixels;
            }
            finally
            {
                for (int i = 0; i < all.Length; i++) all[i].enabled = wasEnabled[i];
            }
        }

        static bool[] RenderMask(Camera cam, RenderTexture rt, int size, SpriteRenderer[] all, SpriteRenderer only)
        {
            var clear = cam.backgroundColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 1f);
            only.enabled = true;
            cam.Render();
            only.enabled = false;
            cam.backgroundColor = clear;

            var tex = ReadBack(rt, size);
            var pixels = tex.GetPixels();
            var mask = new bool[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                mask[i] = pixels[i].r + pixels[i].g + pixels[i].b > 0.08f;   // фон чёрный, рисунок — нет
            Object.DestroyImmediate(tex);
            return mask;
        }

        /// <summary>Кусок, принадлежащий самому суставу.</summary>
        static SpriteRenderer OwnPiece(List<RigAnchors.Anchor> anchors, string jointId)
        {
            foreach (var a in anchors)
                if (a.DeclaresPivot && a.BelongsToBone && a.JointId == jointId) return a.Visual;
            return null;
        }

        /// <summary>Кусок сустава выше по цепочке — вторая половина шва, который проверяется.</summary>
        static SpriteRenderer ParentPiece(Transform root, List<RigAnchors.Anchor> anchors, SpriteRenderer child)
        {
            var parentJoint = FindParentJoint(root, child.transform);
            foreach (var a in anchors)
                if (a.DeclaresPivot && a.BelongsToBone && a.Joint == parentJoint && a.Visual != child)
                    return a.Visual;
            return null;
        }

        static Transform FindPiece(GameObject unit, RigProfile.Joint joint, List<RigAnchors.Anchor> anchors, bool parent)
        {
            var own = OwnPiece(anchors, joint.Id);
            if (own == null) return null;
            if (!parent) return own.transform;
            var up = ParentPiece(unit.transform, anchors, own);
            return up != null ? up.transform : null;
        }

        static Transform FindParentJoint(Transform root, Transform visual)
        {
            for (var node = visual.parent; node != null && node != root; node = node.parent)
                if (Presentation.Body.RigNaming.IsJoint(node))
                    for (var up = node.parent; up != null && up != root; up = up.parent)
                        if (Presentation.Body.RigNaming.IsJoint(up)) return up;
            return null;
        }

        static Dictionary<string, Range> CollectRanges(RigProfile profile, float fallback)
        {
            var byPath = new Dictionary<string, Range>();
            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(profile.Rig)).Replace('\\', '/');
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!b.propertyName.EndsWith("EulerAnglesRaw.z")) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    if (curve == null || curve.length == 0) continue;
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var k in curve.keys) { lo = Mathf.Min(lo, k.value); hi = Mathf.Max(hi, k.value); }
                    if (!byPath.TryGetValue(b.path, out var cur)) byPath[b.path] = new Range { Lo = lo, Hi = hi, FromClips = true };
                    else byPath[b.path] = new Range { Lo = Mathf.Min(cur.Lo, lo), Hi = Mathf.Max(cur.Hi, hi), FromClips = true };
                }
            }

            var result = new Dictionary<string, Range>();
            foreach (var joint in profile.Joints)
                result[joint.Id] = byPath.TryGetValue(joint.Path, out var r)
                    ? r
                    : new Range { Lo = joint.RestZ - fallback, Hi = joint.RestZ + fallback, FromClips = false };
            return result;
        }

        static List<RigProfile.Joint> ChooseJoints(RigProfile profile, List<RigAnchors.Anchor> anchors, string[] wanted)
        {
            var withArt = new HashSet<string>();
            foreach (var a in anchors)
                if (a.DeclaresPivot && a.BelongsToBone) withArt.Add(a.JointId);

            var list = new List<RigProfile.Joint>();
            foreach (var joint in profile.Joints)
            {
                if (wanted != null && wanted.Length > 0 && System.Array.IndexOf(wanted, joint.Id) < 0) continue;
                if ((wanted == null || wanted.Length == 0) && !withArt.Contains(joint.Id)) continue;
                list.Add(joint);
            }
            return list;
        }

        /// <summary>
        /// Рамка вокруг ТОГО, ЧТО ГНЁМ: цепочка под суставом плюс кусок родителя ради шва. Кадр по
        /// всему юниту делает тестируемую руку мелкой деталью, а вопрос теста — как ведёт себя стык.
        /// Считается по КРАЙНИМ позам: рамка по покою срезала бы ровно то, ради чего тест затевался.
        /// </summary>
        static Bounds MeasureAcross(GameObject unit, Transform node, float[] angles, float rest, Transform parentPiece)
        {
            var bounds = new Bounds(node.position, Vector3.one * 0.03f);
            foreach (var angle in angles)
            {
                SetZ(node, angle);
                foreach (var renderer in node.GetComponentsInChildren<Renderer>(false))
                    bounds.Encapsulate(renderer.bounds);
                if (parentPiece != null)
                {
                    var parentRenderer = parentPiece.GetComponent<Renderer>();
                    if (parentRenderer != null) bounds.Encapsulate(parentRenderer.bounds);
                }
            }
            SetZ(node, rest);
            return bounds;
        }

        static void SetZ(Transform node, float z)
        {
            var e = node.localEulerAngles;
            node.localEulerAngles = new Vector3(e.x, e.y, z);
        }

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

        [MenuItem("Alebardium/Animation/Render Joint Stress Test", priority = 629)]
        static void RenderSelected()
        {
            var prefab = Selection.activeObject as GameObject;
            if (prefab == null) { Debug.LogError("Joint Stress Test: выдели префаб рига."); return; }
            var profile = RigProbe.FindProfileFor(prefab);
            if (profile == null) { Debug.LogError("Joint Stress Test: нет RigProfile для " + prefab.name); return; }

            var result = Render(profile, new Options { Rig = prefab });
            Debug.Log("Joint stress: " + result.Path);
            foreach (var line in result.Lines) Debug.Log("  " + line);
            EditorUtility.RevealInFinder(result.Path);
        }

        [MenuItem("Alebardium/Animation/Render Joint Stress Test", validate = true)]
        static bool RenderSelectedValidate() => Selection.activeObject is GameObject;
    }
}
#endif
