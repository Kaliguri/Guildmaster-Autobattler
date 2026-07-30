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
    /// Draws what a clip does over TIME instead of what it looks like at one time: the path the weapon
    /// sweeps through, and how much of the body the shield actually covers.
    ///
    /// It exists because two acceptance questions turned out not to be about angles at all. "Does the
    /// swing read" is about the line the blade draws in the air — a pose sheet shows neither the line nor
    /// its direction. "Does the shield cover the torso" is about overlapping silhouettes on screen —
    /// authoring it by joint angles produced a contact frame where the arm sat exactly where it stands in
    /// idle and the shield tipped 40 degrees away. Both are measured here, in pixels and world units, so
    /// the answer stops depending on whose eye is looking.
    ///
    /// Everything is offline: a preview scene, a clip sampled densely, one picture and one table.
    /// </summary>
    public static class RigSweep
    {
        public sealed class Options
        {
            [Tooltip("Чей путь ведём. Пусто = первый предмет профиля.")]
            public string ItemId = "weapon";

            /// <summary>Item whose silhouette is measured against the body. Null = skip the coverage pass.</summary>
            public string CoverItemId = "shield";

            /// <summary>
            /// Items drawn ALONGSIDE the traced one, as a plain outline. A picture with one item on it is
            /// unreadable for the question actually being asked (Max, 29.07): judging a block means seeing
            /// where the sword is, and judging a swing means seeing where the shield is. Null = the other
            /// held items of the profile, which is what you want almost always.
            /// </summary>
            public string[] AlsoTrace;

            /// <summary>Nodes that count as "the body" for coverage. Names, not paths.</summary>
            public string[] CoverBodyNodes = { "Torso", "Head" };

            /// <summary>Extra samples per clip frame. The blade moves fastest exactly where frames are sparse.</summary>
            public int SamplesPerFrame = 3;

            /// <summary>
            /// Pose the backdrop render sits at. Negative = the contact event, or the middle of the clip if
            /// the clip has no event.
            /// </summary>
            public float BackdropTime = -1f;

            /// <summary>
            /// Share of the peak tip speed that still counts as "the strike". Everything before the strike
            /// is the windup, everything after it the recovery — the three zones the picture is made of.
            /// </summary>
            public float StrikeSpeedShare = 0.25f;

            /// <summary>
            /// Как трассируемый клип лежит на теле, занятом другим: база плюс слои. Null = старое поведение,
            /// «клип один на всё тело».
            /// </summary>
            /// <remarks>
            /// Нужна с тех пор, как удар переехал на слой (30.07): плечо теперь едет от локомоции, а дуга
            /// растёт из плеча. Картинка, нарисованная по одному клипу удара, показывала бы траекторию,
            /// которой в игре нет — и ошибалась бы тем сильнее, чем размашистее бег.
            /// </remarks>
            public RigLayerBlend.Composition Composition;

            public int Size = 900;
            public float Padding = 1.15f;
            public int CoverageSize = 256;
            public Color Background = new Color(0.09f, 0.09f, 0.11f, 1f);
            public string OutputPath;
        }

        /// <summary>What the blade is doing at a moment: gathering, cutting, or putting itself back.</summary>
        public enum Phase { Windup, Strike, Recovery }

        /// <summary>
        /// The other item in the picture. Not a second <see cref="Result"/>: it carries no phases and no
        /// contact, because it is here to be SEEN, not judged — the eye needs to know where the sword is
        /// while the shield is being accepted.
        /// </summary>
        public sealed class Companion
        {
            public string Id;
            public readonly List<Vector3> Butt = new List<Vector3>();
            public readonly List<Vector3> Tip = new List<Vector3>();
            /// <summary>Габарит спрайта по сэмплам — им спутник и рисуется (null, если спрайта нет).</summary>
            public readonly List<Vector3[]> Quads = new List<Vector3[]>();
            /// <summary>Travel of the tip, world units — a companion that never moves is worth saying so.</summary>
            public float TipTravel;
        }

        /// <summary>One moment of the clip, as geometry.</summary>
        public sealed class Sample
        {
            public Phase Phase = Phase.Windup;

            public float Time;
            public Vector3 Grip;
            public Vector3 Butt;
            public Vector3 Tip;
            /// <summary>Where the item's orientation marker points, in world degrees, unwrapped along the path.</summary>
            public float WorldAngle;
            /// <summary>Tip speed in world units per second.</summary>
            public float TipSpeed;
            /// <summary>Share of the body silhouette hidden behind the cover item, 0..1. NaN = not measured.</summary>
            public float Coverage = float.NaN;
            /// <summary>True when this sample lands on a key the clip actually authored.</summary>
            public bool IsKey;

            /// <summary>
            /// The four corners of the item's SPRITE at this moment, world space. The zone is built from
            /// these rather than from the grip-to-tip line, because an item is not a line: a shield is a
            /// plane, and the whole question asked of it — how much does it cover — is about its area.
            /// Drawing it as a segment produced a fan that answered nothing (Max, 29.07). Empty = the item
            /// has no renderer, and then the line is all there is.
            /// </summary>
            public Vector3[] Quad;
        }

        public sealed class Result
        {
            public string Path;
            public string ClipName;
            public float ClipLength;
            public List<Sample> Samples = new List<Sample>();

            /// <summary>Paths of the items drawn alongside the traced one: grip-to-tip per sample.</summary>
            public List<Companion> Companions = new List<Companion>();
            /// <summary>Length of the line the tip drew, in world units.</summary>
            public float TipTravel;
            /// <summary>Total turn of the blade along the path, in degrees. Not the difference of endpoints.</summary>
            public float AngleTravel;
            public float PeakTipSpeed;
            public float PeakTipSpeedTime;
            /// <summary>Time of the contact event, or -1 when the clip has none.</summary>
            public float ContactTime = -1f;
            /// <summary>Bounds of the strike zone in seconds — where the blade is fast enough to matter.</summary>
            public float StrikeStart;
            public float StrikeEnd;

            public override string ToString()
            {
                var text = new StringBuilder();
                text.AppendLine($"{ClipName}: {ClipLength:F2}s, {Samples.Count} samples");
                text.AppendLine($"tip travel {TipTravel:F3} units, blade turn {AngleTravel:F1} deg, " +
                                $"peak tip speed {PeakTipSpeed:F2} u/s at {PeakTipSpeedTime:F3}s");
                text.AppendLine(ContactTime >= 0f
                    ? $"contact at {ContactTime:F3}s ({ContactTime / Mathf.Max(ClipLength, 1e-4f):P0} of the clip)"
                    : "contact: no event on this clip");
                text.AppendLine($"zones: windup 0-{StrikeStart:F3}s, strike {StrikeStart:F3}-{StrikeEnd:F3}s " +
                                $"({StrikeEnd - StrikeStart:F3}s, {TurnBetween(StrikeStart, StrikeEnd):F0} deg), " +
                                $"recovery {StrikeEnd:F3}-{ClipLength:F3}s" +
                                (ContactTime >= 0f && (ContactTime < StrikeStart || ContactTime > StrikeEnd)
                                    ? "  <-- CONTACT FALLS OUTSIDE THE STRIKE"
                                    : ""));

                bool hasCoverage = false;
                foreach (var s in Samples) if (!float.IsNaN(s.Coverage)) { hasCoverage = true; break; }
                if (hasCoverage)
                {
                    float min = 1f, max = 0f, atContact = float.NaN;
                    float minTime = 0f;
                    foreach (var s in Samples)
                    {
                        if (float.IsNaN(s.Coverage)) continue;
                        if (s.Coverage < min) { min = s.Coverage; minTime = s.Time; }
                        if (s.Coverage > max) max = s.Coverage;
                        if (ContactTime >= 0f && Mathf.Abs(s.Time - ContactTime) < 0.02f) atContact = s.Coverage;
                    }
                    text.AppendLine($"body covered: min {min:P0} at {minTime:F2}s, max {max:P0}" +
                                    (float.IsNaN(atContact) ? "" : $", at contact {atContact:P0}"));
                }

                text.AppendLine();
                text.AppendLine("t      angle   speed   cover  key");
                foreach (var s in Samples)
                {
                    if (!s.IsKey && Samples.Count > 24) continue;
                    text.AppendLine($"{s.Time,6:F3} {s.WorldAngle,7:F1} {s.TipSpeed,7:F2} " +
                                    $"{(float.IsNaN(s.Coverage) ? "    -" : s.Coverage.ToString("P0").PadLeft(5))}" +
                                    $"  {(s.IsKey ? "key" : "")}");
                }
                foreach (Companion companion in Companions)
                    text.AppendLine($"в кадре также {companion.Id}: путь кончика {companion.TipTravel:F3} ед" +
                                    (companion.TipTravel < 0.01f ? " (неподвижен)" : ""));

                text.AppendLine($"\npicture: {Path}");
                return text.ToString();
            }

            /// <summary>How far the blade turned between two times, along the path.</summary>
            public float TurnBetween(float from, float to)
            {
                float turn = 0f;
                for (int i = 1; i < Samples.Count; i++)
                {
                    if (Samples[i].Time < from || Samples[i].Time > to) continue;
                    turn += Mathf.Abs(Samples[i].WorldAngle - Samples[i - 1].WorldAngle);
                }
                return turn;
            }
        }

        // The three zones. The strike is red because it is the only part that hits anything; the windup is
        // cold and the recovery is grey, so one glance says where the attack comes FROM and goes TO.
        static readonly Color WindupFill = new Color(0.30f, 0.65f, 1.00f, 0.16f);
        static readonly Color StrikeFill = new Color(1.00f, 0.20f, 0.25f, 0.38f);
        static readonly Color RecoveryFill = new Color(0.65f, 0.65f, 0.72f, 0.10f);
        static readonly Color WindupEdge = new Color(0.45f, 0.80f, 1.00f);
        static readonly Color StrikeEdge = new Color(1.00f, 0.45f, 0.35f);
        static readonly Color RecoveryEdge = new Color(0.70f, 0.70f, 0.78f);
        static readonly Color StartColor = new Color(0.35f, 1.00f, 0.55f);
        static readonly Color EndColor = new Color(1.00f, 0.85f, 0.25f);
        static readonly Color ContactColor = new Color(1.00f, 0.10f, 0.20f);
        // Спутник рисуется бледной линией: он в кадре как ориентир, и не должен спорить с ведомым предметом.
        static readonly Color CompanionPath = new Color(0.55f, 0.85f, 0.70f, 0.55f);
        static readonly Color CompanionNow  = new Color(0.75f, 1.00f, 0.85f);

        // Один вход для всех трёх проходов рендера: геометрия, кадрирование и подложка обязаны смотреть на
        // одну и ту же позу, иначе дуга будет посчитана по одной, а нарисована поверх другой.
        static void SamplePose(GameObject unit, AnimationClip clip, float time, Options options)
            => RigLayerBlend.SampleTraced(unit, clip, time, options.Composition);

        public static Result Render(RigProfile profile, AnimationClip clip, Options options = null)
        {
            if (profile == null) throw new System.ArgumentNullException(nameof(profile));
            if (clip == null) throw new System.ArgumentNullException(nameof(clip));
            if (profile.Rig == null) throw new System.ArgumentException("RigProfile.Rig is not set.");

            options ??= new Options();
            var item = string.IsNullOrEmpty(options.ItemId) ? FirstHeld(profile) : profile.FindHeld(options.ItemId);
            if (item == null) throw new System.ArgumentException($"Profile has no held item '{options.ItemId}'.");

            var result = new Result { ClipName = clip.name, ClipLength = clip.length };
            float frameRate = clip.frameRate > 0f ? clip.frameRate : 60f;
            float[] times = DenseTimes(clip, frameRate, Mathf.Max(1, options.SamplesPerFrame));
            var keyTimes = KeyTimes(clip, frameRate);
            result.ContactTime = ContactTimeOf(clip);

            var scene = EditorSceneManager.NewPreviewScene();
            GameObject unit = null, camGo = null;
            RenderTexture rt = null, coverRt = null;
            try
            {
                unit = (GameObject)PrefabUtility.InstantiatePrefab(profile.Rig);
                SceneManager.MoveGameObjectToScene(unit, scene);
                unit.transform.position = Vector3.zero;
                unit.transform.rotation = Quaternion.identity;
                var root = unit.transform;

                var grip = root.Find(item.GripPath);
                if (grip == null) throw new System.InvalidOperationException($"Grip path not found: {item.GripPath}");

                // Спрайт предмета: по нему строится ПЛОЩАДЬ зоны. Без него остаётся только линия хвата.
                Transform itemNode = string.IsNullOrEmpty(item.ItemPath) ? null : root.Find(item.ItemPath);
                var itemSprite = itemNode != null ? itemNode.GetComponent<SpriteRenderer>() : null;

                // The other held items, so the picture answers the question that was actually asked: the
                // shield is accepted against where the SWORD is, and vice versa. Default is "everything else
                // the profile holds" — a rig with one item simply gets no companions.
                var companions = new List<(RigProfile.HeldItem item, Transform grip, Companion trace, SpriteRenderer sprite)>();
                foreach (RigProfile.HeldItem held in profile.Held)
                {
                    if (held == null || held.Id == item.Id) continue;
                    if (options.AlsoTrace != null && System.Array.IndexOf(options.AlsoTrace, held.Id) < 0) continue;

                    Transform heldGrip = root.Find(held.GripPath);
                    if (heldGrip == null) continue;

                    Transform heldNode = string.IsNullOrEmpty(held.ItemPath) ? null : root.Find(held.ItemPath);
                    var trace = new Companion { Id = held.Id };
                    result.Companions.Add(trace);
                    companions.Add((held, heldGrip, trace, heldNode != null ? heldNode.GetComponent<SpriteRenderer>() : null));
                }

                // Pass 1: geometry only. No camera yet — the framing has to know where the tip went.
                var renderers = unit.GetComponentsInChildren<Renderer>(includeInactive: false);
                var bounds = new Bounds(root.position, Vector3.one * 0.05f);
                bool seeded = false;
                float unwrapped = 0f;

                for (int i = 0; i < times.Length; i++)
                {
                    SamplePose(unit, clip, times[i], options);

                    float world = RigProbe.WorldOrientation(grip, item);
                    var dir = new Vector3(Mathf.Cos(world * Mathf.Deg2Rad), Mathf.Sin(world * Mathf.Deg2Rad), 0f);
                    var butt = grip.position - dir * item.GripToButt;

                    // The angle is unwrapped by ACCUMULATING per-sample deltas. Storing the normalised
                    // world angle instead would erase the very thing the picture is for: a swing that goes
                    // the long way round and one that snaps back read identically once folded into +/-180.
                    if (i == 0) unwrapped = world;
                    else unwrapped += Mathf.DeltaAngle(result.Samples[i - 1].WorldAngle, world);

                    var sample = new Sample
                    {
                        Time = times[i],
                        Grip = grip.position,
                        Butt = butt,
                        Tip = butt + dir * item.ItemLength,
                        WorldAngle = unwrapped,
                        // Compared with a tolerance, not by equality: a key rounded to 1/60 and a sample
                        // stepped by 1/180 are the same instant and different floats.
                        IsKey = IsKeyTime(keyTimes, times[i], 0.5f / (frameRate * Mathf.Max(1, options.SamplesPerFrame))),
                        Quad = SpriteQuad(itemSprite)
                    };
                    result.Samples.Add(sample);

                    foreach (var r in renderers)
                    {
                        if (!r.enabled) continue;
                        if (!seeded) { bounds = r.bounds; seeded = true; }
                        else bounds.Encapsulate(r.bounds);
                    }
                    bounds.Encapsulate(sample.Tip);
                    bounds.Encapsulate(sample.Butt);

                    // Companions on the same sample, so the two paths are the same moments in the same frame.
                    foreach (var (held, heldGrip, trace, heldSprite) in companions)
                    {
                        float heldWorld = RigProbe.WorldOrientation(heldGrip, held);
                        var heldDir = new Vector3(Mathf.Cos(heldWorld * Mathf.Deg2Rad), Mathf.Sin(heldWorld * Mathf.Deg2Rad), 0f);
                        Vector3 heldButt = heldGrip.position - heldDir * held.GripToButt;
                        Vector3 heldTip = heldButt + heldDir * held.ItemLength;

                        if (trace.Tip.Count > 0) trace.TipTravel += Vector3.Distance(heldTip, trace.Tip[trace.Tip.Count - 1]);
                        trace.Butt.Add(heldButt);
                        trace.Tip.Add(heldTip);
                        trace.Quads.Add(SpriteQuad(heldSprite));

                        bounds.Encapsulate(heldTip);
                        bounds.Encapsulate(heldButt);
                    }
                }

                for (int i = 1; i < result.Samples.Count; i++)
                {
                    var previous = result.Samples[i - 1];
                    var current = result.Samples[i];
                    float dt = Mathf.Max(current.Time - previous.Time, 1e-5f);
                    float step = Vector3.Distance(current.Tip, previous.Tip);

                    current.TipSpeed = step / dt;
                    result.TipTravel += step;
                    result.AngleTravel += Mathf.Abs(current.WorldAngle - previous.WorldAngle);
                    if (current.TipSpeed > result.PeakTipSpeed)
                    {
                        result.PeakTipSpeed = current.TipSpeed;
                        result.PeakTipSpeedTime = current.Time;
                    }
                }

                ClassifyPhases(result, options.StrikeSpeedShare);

                camGo = new GameObject("RigSweepCamera");
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

                // Pass 2: coverage, one measurement per clip frame.
                var coverItem = string.IsNullOrEmpty(options.CoverItemId) ? null : profile.FindHeld(options.CoverItemId);
                if (coverItem != null)
                {
                    coverRt = new RenderTexture(options.CoverageSize, options.CoverageSize, 24,
                                                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    MeasureCoverage(cam, coverRt, options, unit, root, clip, coverItem, result);
                }

                // Pass 3: the backdrop pose, then the path drawn over it.
                float backdrop = options.BackdropTime >= 0f
                    ? options.BackdropTime
                    : (result.ContactTime >= 0f ? result.ContactTime : clip.length * 0.5f);
                SamplePose(unit, clip, backdrop, options);

                int size = Mathf.Max(256, options.Size);
                rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                cam.Render();

                var tex = ReadBack(rt, size);
                var canvas = new RigCanvas(tex, cam, size);
                Draw(canvas, result, options);
                tex.Apply();

                string path = string.IsNullOrEmpty(options.OutputPath)
                    ? Path.GetFullPath(Path.Combine(AnimationLabRenderer.DefaultOutputDir, clip.name + "_sweep.png"))
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
                if (coverRt != null) { coverRt.Release(); Object.DestroyImmediate(coverRt); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// Splits the clip into windup, strike and recovery by tip speed. Speed is the honest divider: a
        /// strike is the stretch where the blade is actually moving fast enough to mean something, and it
        /// finds the same moment whether the author called it a swing, a thrust or a chop.
        /// </summary>
        static void ClassifyPhases(Result result, float share)
        {
            if (result.Samples.Count == 0 || result.PeakTipSpeed <= 0f) return;

            float threshold = result.PeakTipSpeed * Mathf.Clamp01(share);
            int peak = 0;
            for (int i = 0; i < result.Samples.Count; i++)
                if (result.Samples[i].TipSpeed >= result.PeakTipSpeed) { peak = i; break; }

            int first = peak, last = peak;
            while (first > 0 && result.Samples[first - 1].TipSpeed >= threshold) first--;
            while (last + 1 < result.Samples.Count && result.Samples[last + 1].TipSpeed >= threshold) last++;

            for (int i = 0; i < result.Samples.Count; i++)
                result.Samples[i].Phase = i < first ? Phase.Windup
                                        : i > last ? Phase.Recovery
                                        : Phase.Strike;

            result.StrikeStart = result.Samples[first].Time;
            result.StrikeEnd = result.Samples[last].Time;
        }

        /// <summary>
        /// The picture: three filled zones swept by the blade, their outlines, and where the swing starts
        /// and ends. Not a fan of blade positions — a fan asks the eye to integrate; a zone just shows the
        /// area the attack owns.
        /// </summary>
        static void Draw(RigCanvas canvas, Result result, Options options)
        {
            // Спутники — ПЕРВЫМИ, под зонами: они ориентир, а не предмет разбора. Контуром габарита, а не
            // линией: увидеть надо, где стоит сам предмет, а не куда смотрит его ось.
            foreach (Companion companion in result.Companions)
            {
                for (int i = 0; i + 1 < companion.Tip.Count; i++)
                    canvas.Line(companion.Tip[i], companion.Tip[i + 1], CompanionPath, 1);

                int mid = companion.Tip.Count / 2;
                if (companion.Tip.Count == 0) continue;

                if (companion.Quads.Count > mid && companion.Quads[mid] != null)
                {
                    Vector3[] quad = companion.Quads[mid];
                    for (int c = 0; c < 4; c++) canvas.Line(quad[c], quad[(c + 1) % 4], CompanionNow, 1);
                }
                else
                {
                    canvas.Line(companion.Butt[mid], companion.Tip[mid], CompanionNow, 2);
                }
                canvas.Dot(companion.Tip[mid], 5, CompanionNow);
            }

            // Painted back to front: recovery, then windup, then the strike on top. The zones overlap
            // heavily — a swing sweeps the same air twice — and whichever is painted last is the one the
            // eye reads, so the part that hits has to be last.
            foreach (var phase in new[] { Phase.Recovery, Phase.Windup, Phase.Strike })
            {
                for (int i = 0; i + 1 < result.Samples.Count; i++)
                {
                    var current = result.Samples[i];
                    if (current.Phase != phase) continue;
                    var next = result.Samples[i + 1];

                    // ПЛОЩАДЬ предмета, а не полоса «рукоять-кончик». Для клинка разница невелика, для
                    // щита принципиальна: он плоскость, и «сколько он закрывает» — вопрос про его площадь.
                    // Заливается сам габарит спрайта на каждом сэмпле; наложение соседних и даёт зону.
                    if (current.Quad != null)
                    {
                        canvas.FillTriangle(current.Quad[0], current.Quad[1], current.Quad[2], FillOf(phase));
                        canvas.FillTriangle(current.Quad[0], current.Quad[2], current.Quad[3], FillOf(phase));
                        continue;
                    }

                    canvas.FillTriangle(current.Butt, current.Tip, next.Tip, FillOf(phase));
                    canvas.FillTriangle(current.Butt, next.Tip, next.Butt, FillOf(phase));
                }

                for (int i = 0; i + 1 < result.Samples.Count; i++)
                {
                    if (result.Samples[i].Phase != phase) continue;
                    canvas.Line(result.Samples[i].Tip, result.Samples[i + 1].Tip, EdgeOf(phase), 1);
                }
            }

            var firstSample = result.Samples[0];
            var lastSample = result.Samples[result.Samples.Count - 1];
            canvas.Line(firstSample.Butt, firstSample.Tip, StartColor, 1);
            canvas.Dot(firstSample.Tip, 9, StartColor);
            canvas.Line(lastSample.Butt, lastSample.Tip, EndColor, 1);
            canvas.Dot(lastSample.Tip, 9, EndColor);

            if (result.ContactTime >= 0f)
            {
                var contact = NearestSample(result, result.ContactTime);
                if (contact != null)
                {
                    canvas.Line(contact.Butt, contact.Tip, ContactColor, 2);
                    canvas.Cross(contact.Tip, 18, ContactColor);
                }
            }
        }

        /// <summary>
        /// Четыре угла спрайта в МИРЕ. Берётся локальный габарит спрайта и гоняется через трансформ узла,
        /// поэтому наклон предмета сохраняется: мировой AABB рендерера дал бы прямоугольник по осям экрана,
        /// то есть соврал бы ровно в том, ради чего гизмо и рисуется.
        /// </summary>
        static Vector3[] SpriteQuad(SpriteRenderer sprite)
        {
            if (sprite == null || sprite.sprite == null) return null;

            Bounds local = sprite.sprite.bounds;
            Vector3 min = local.min, max = local.max;
            Transform node = sprite.transform;
            return new[]
            {
                node.TransformPoint(new Vector3(min.x, min.y, 0f)),
                node.TransformPoint(new Vector3(max.x, min.y, 0f)),
                node.TransformPoint(new Vector3(max.x, max.y, 0f)),
                node.TransformPoint(new Vector3(min.x, max.y, 0f)),
            };
        }

        static Color FillOf(Phase phase) => phase == Phase.Strike ? StrikeFill
                                          : phase == Phase.Windup ? WindupFill
                                          : RecoveryFill;

        static Color EdgeOf(Phase phase) => phase == Phase.Strike ? StrikeEdge
                                          : phase == Phase.Windup ? WindupEdge
                                          : RecoveryEdge;

        /// <summary>
        /// How much of the body the cover item hides, measured the way the player sees it: two silhouettes
        /// rendered from the same camera, counted in pixels. Angles cannot answer this — a shield can be
        /// held at a perfectly sensible angle and still sit beside the torso rather than in front of it.
        /// </summary>
        static void MeasureCoverage(Camera cam, RenderTexture rt, Options options, GameObject unit, Transform root,
                                    AnimationClip clip, RigProfile.HeldItem coverItem, Result result)
        {
            var all = unit.GetComponentsInChildren<SpriteRenderer>(includeInactive: false);
            var restore = new Dictionary<SpriteRenderer, bool>(all.Length);
            foreach (var r in all) restore[r] = r.enabled;

            var body = new List<SpriteRenderer>();
            foreach (var r in all)
                foreach (var name in options.CoverBodyNodes)
                    if (r.transform.name == name) { body.Add(r); break; }

            var coverNode = root.Find(coverItem.ItemPath);
            var cover = coverNode != null ? coverNode.GetComponent<SpriteRenderer>() : null;
            if (body.Count == 0 || cover == null)
            {
                foreach (var pair in restore) pair.Key.enabled = pair.Value;
                return;
            }

            var previousTarget = cam.targetTexture;
            var previousBackground = cam.backgroundColor;
            cam.targetTexture = rt;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

            int stride = Mathf.Max(1, options.SamplesPerFrame);
            try
            {
                for (int i = 0; i < result.Samples.Count; i += stride)
                {
                    var sample = result.Samples[i];
                    SamplePose(unit, clip, sample.Time, options);

                    var bodyMask = RenderMask(cam, rt, options.CoverageSize, all, body);
                    var coverMask = RenderMask(cam, rt, options.CoverageSize, all, new List<SpriteRenderer> { cover });

                    int bodyPixels = 0, hidden = 0;
                    for (int p = 0; p < bodyMask.Length; p++)
                    {
                        if (!bodyMask[p]) continue;
                        bodyPixels++;
                        if (coverMask[p]) hidden++;
                    }
                    sample.Coverage = bodyPixels > 0 ? hidden / (float)bodyPixels : 0f;
                }
            }
            finally
            {
                foreach (var pair in restore) pair.Key.enabled = pair.Value;
                cam.targetTexture = previousTarget;
                cam.backgroundColor = previousBackground;
            }
        }

        static bool[] RenderMask(Camera cam, RenderTexture rt, int size, SpriteRenderer[] all, List<SpriteRenderer> visible)
        {
            foreach (var r in all) r.enabled = false;
            foreach (var r in visible) r.enabled = true;
            cam.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            var pixels = tex.GetPixels32();
            var mask = new bool[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) mask[i] = pixels[i].a > 127;
            Object.DestroyImmediate(tex);
            return mask;
        }

        static Sample NearestSample(Result result, float time)
        {
            Sample best = null;
            float bestDistance = float.MaxValue;
            foreach (var s in result.Samples)
            {
                float d = Mathf.Abs(s.Time - time);
                if (d < bestDistance) { bestDistance = d; best = s; }
            }
            return best;
        }

        static RigProfile.HeldItem FirstHeld(RigProfile profile) =>
            profile.Held.Count > 0 ? profile.Held[0] : null;

        /// <summary>Times sampled evenly and densely: the path is a curve, not a set of poses.</summary>
        static float[] DenseTimes(AnimationClip clip, float frameRate, int samplesPerFrame)
        {
            float step = 1f / (frameRate * samplesPerFrame);
            int count = Mathf.Max(2, Mathf.CeilToInt(clip.length / step) + 1);
            var times = new float[count];
            for (int i = 0; i < count; i++) times[i] = Mathf.Min(clip.length, i * step);
            return times;
        }

        static HashSet<float> KeyTimes(AnimationClip clip, float frameRate)
        {
            var keys = new HashSet<float>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                foreach (var key in curve.keys) keys.Add(Mathf.Round(key.time * frameRate) / frameRate);
            }
            return keys;
        }

        static bool IsKeyTime(HashSet<float> keyTimes, float time, float tolerance)
        {
            foreach (float key in keyTimes)
                if (Mathf.Abs(key - time) <= tolerance) return true;
            return false;
        }

        static float ContactTimeOf(AnimationClip clip)
        {
            var events = AnimationUtility.GetAnimationEvents(clip);
            return events != null && events.Length > 0 ? events[0].time : -1f;
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

        [MenuItem("Alebardium/Animation/Render Slash Gizmo", priority = 630)]
        static void RenderSelected()
        {
            var clip = Selection.activeObject as AnimationClip;
            if (clip == null)
            {
                Debug.LogError("Render Slash Gizmo: select an AnimationClip first.");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:RigProfile");
            if (guids.Length != 1)
            {
                Debug.LogError($"Render Slash Gizmo: expected exactly one RigProfile in the project, found {guids.Length}.");
                return;
            }

            var profile = AssetDatabase.LoadAssetAtPath<RigProfile>(AssetDatabase.GUIDToAssetPath(guids[0]));
            Debug.Log(Render(profile, clip).ToString());
        }

        [MenuItem("Alebardium/Animation/Render Slash Gizmo", validate = true)]
        static bool RenderSelectedValidate() => Selection.activeObject is AnimationClip;
    }
}
#endif
