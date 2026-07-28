#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Writes clips in the rig's own language: "bend the elbow 40 degrees", "point the blade there",
    /// "swing it the long way round" — instead of local Euler numbers that have to be derived by hand.
    ///
    /// Three things it takes off the author's hands, because all three cost this project whole clips:
    ///
    /// - <b>Aiming.</b> A blade angle is a WORLD direction, but a curve stores a LOCAL one, so the chain
    ///   above it (shoulder, elbow, the calibration offset on the sprite) has to be subtracted. Doing
    ///   that by hand is how a 200-degree cut came out as 19: the shoulder was cancelling the wrist.
    /// - <b>Arc direction.</b> Unity interpolates the NUMBER in the curve, not the rotation: 135 -> -3
    ///   travels through zero, 135 -> 357 travels through 180. The number written therefore has to be
    ///   unwrapped into a continuous series — the job Maya and Blender call an Euler filter, and the way
    ///   Spine encodes direction by letting local angles leave 0-360. Our clips are already in the mode
    ///   that supports it (localEulerAnglesRaw = Unity's "Euler Angles"), which keeps the full range
    ///   instead of taking the short way round.
    /// - <b>Order independence.</b> Aims are resolved at <see cref="Write"/>, once the whole pose is
    ///   known — not when they are called. Resolving them eagerly means a later Bend further up the arm
    ///   silently moves the blade: measured, a Bend of 40 on the elbow dragged an aimed blade 25 degrees
    ///   off target.
    ///
    /// Usage:
    /// <code>
    /// using (var writer = new RigWriter(profile))
    /// {
    ///     writer.At(0f).Bend("elbow.R", 20f).Aim("weapon", 135f);
    ///     writer.At(0.25f).Aim("weapon", -25f, RigWriter.Arc.Ccw);   // long way round, 200 degrees
    ///     var report = writer.Write("Assets/_Project/Prefabs/Bones/Attack2.anim");
    /// }
    /// </code>
    /// The report lists what was written and warns about anything that did not land: an aim the clip does
    /// not actually play, a bend past a joint's limit.
    /// </summary>
    public sealed class RigWriter : System.IDisposable
    {
        /// <summary>
        /// Which way round the joint travels to the new angle.
        ///
        /// Cw and Ccw are stated as seen on screen WITH THE UNIT FACING RIGHT. Facing is flipped by a
        /// negative scale.x on the facing root, and a mirrored transform reverses apparent rotation — so
        /// a swing authored as clockwise plays counter-clockwise on the flipped side. That is a property
        /// of the rig, not of this tool: author for the unflipped facing.
        /// </summary>
        public enum Arc
        {
            /// <summary>Whatever is closer — the safe default for small moves.</summary>
            Shortest,
            /// <summary>Clockwise on screen: the local angle decreases.</summary>
            Cw,
            /// <summary>Counter-clockwise on screen: the local angle increases.</summary>
            Ccw
        }

        public sealed class Report
        {
            public readonly List<string> Lines = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public int Curves;
            public int Keys;
            public string Path;

            public override string ToString()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{Path}: {Curves} curves, {Keys} keys");
                foreach (var line in Lines) sb.AppendLine("  " + line);
                foreach (var warning in Warnings) sb.AppendLine("  WARNING: " + warning);
                return sb.ToString();
            }
        }

        /// <summary>
        /// How the curve behaves AT a key — the speed graph, which is what makes a swing read as a swing
        /// rather than as a slide. A tangent is speed: zero means the motion is at rest there.
        /// </summary>
        public enum Ease
        {
            /// <summary>Automatic smoothing. Fine for breathing and drift, wrong for impacts.</summary>
            Smooth,
            /// <summary>At rest on both sides — an extreme pose the motion settles into and leaves slowly.</summary>
            Hold,
            /// <summary>Constant speed through the key: no braking, no dwelling.</summary>
            Linear,
            /// <summary>Arrives at full speed and stops dead. This is the contact frame of a strike.</summary>
            EaseOut,
            /// <summary>Starts from rest and leaves at full speed. This is the break from a wind-up hold.</summary>
            EaseIn
        }

        /// <summary>
        /// One authored value on a joint's track. An aim keeps its WORLD intent instead of a local angle,
        /// so it can be solved after the rest of the pose exists.
        /// </summary>
        struct Order
        {
            public bool IsAim;
            public float LocalZ;
            public string ItemId;
            public float WorldDegrees;
            public Arc Arc;
            public int ExtraTurns;
            public Ease Ease;
        }

        readonly RigProfile _profile;
        readonly UnityEngine.SceneManagement.Scene _scene;
        readonly GameObject _instance;
        readonly Report _report = new Report();

        readonly Dictionary<string, SortedList<float, Order>> _orders = new Dictionary<string, SortedList<float, Order>>();
        readonly Dictionary<string, SortedList<float, Vector2>> _positions = new Dictionary<string, SortedList<float, Vector2>>();
        /// <summary>Filled by <see cref="Resolve"/>: the actual local angles that go into the curves.</summary>
        readonly Dictionary<string, SortedList<float, float>> _resolved = new Dictionary<string, SortedList<float, float>>();
        /// <summary>joint id -> time -> speed graph at that key.</summary>
        readonly Dictionary<string, Dictionary<float, Ease>> _eases = new Dictionary<string, Dictionary<float, Ease>>();
        readonly List<AnimationEvent> _events = new List<AnimationEvent>();

        float _time;

        public RigWriter(RigProfile profile)
        {
            _profile = profile ?? throw new System.ArgumentNullException(nameof(profile));
            if (profile.Rig == null) throw new System.ArgumentException("RigProfile.Rig is not set.");

            _scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(profile.Rig);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_instance, _scene);
            _instance.transform.position = Vector3.zero;
            _instance.transform.rotation = Quaternion.identity;
        }

        /// <summary>Sets the time the following commands write to.</summary>
        public RigWriter At(float seconds)
        {
            _time = Mathf.Max(0f, seconds);
            return this;
        }

        /// <summary>
        /// Bends a joint by degrees FROM ITS REST POSE, in the direction the joint actually bends —
        /// positive always means "folds", whatever sign the rig happens to use.
        /// </summary>
        public RigWriter Bend(string jointId, float degreesFromRest, Arc arc = Arc.Shortest, Ease ease = Ease.Smooth)
        {
            var joint = RequireJoint(jointId);
            if (joint.FlexLimit > 0f && Mathf.Abs(degreesFromRest) > joint.FlexLimit)
                _report.Warnings.Add($"{jointId} bent {degreesFromRest:F1} deg past its limit of {joint.FlexLimit:F0} at t={_time:F2}");

            return Set(jointId, joint.RestZ + joint.FlexSign * degreesFromRest, arc, ease);
        }

        /// <summary>Sets a joint's local angle outright. Prefer <see cref="Bend"/> or <see cref="Aim"/>.</summary>
        public RigWriter Set(string jointId, float localZ, Arc arc = Arc.Shortest, Ease ease = Ease.Smooth)
        {
            RequireJoint(jointId);
            Track(jointId)[_time] = new Order { LocalZ = localZ, Arc = arc, Ease = ease };
            return this;
        }

        /// <summary>
        /// Holds everything written at the current time for <paramref name="seconds"/>, then moves the
        /// clock to the end of that hold. This is the pause that gives a strike its weight: the wind-up
        /// settles and waits, the contact lands and stops. Both ends of the plateau are pinned at rest so
        /// smoothing cannot bow the curve through the pause.
        /// </summary>
        public RigWriter Hold(float seconds)
        {
            if (seconds <= 0f) return this;
            float from = _time, to = _time + seconds;

            foreach (var pair in _orders)
            {
                if (!pair.Value.TryGetValue(from, out var order)) continue;
                var pinned = order;
                pinned.Ease = Ease.Hold;
                pair.Value[from] = pinned;

                var copy = order;
                copy.Ease = Ease.Hold;
                copy.ExtraTurns = 0;      // the turn already happened on the way in
                copy.Arc = Arc.Shortest;  // a plateau must not travel anywhere
                pair.Value[to] = copy;
            }

            foreach (var pair in _positions)
                if (pair.Value.TryGetValue(from, out var position))
                    pair.Value[to] = position;

            _time = to;
            return this;
        }

        /// <summary>
        /// Adds an animation event. Attack clips carry a "Marker" event at the contact frame and the
        /// combat code reads the hit timing from it, so rewriting a clip without re-adding its marker
        /// moves the hit to the last frame.
        /// </summary>
        public RigWriter Event(string functionName, float time)
        {
            _events.Add(new AnimationEvent { functionName = functionName, time = Mathf.Max(0f, time) });
            return this;
        }

        /// <summary>
        /// Points a held item's orientation marker at a WORLD angle: 0 = to the right, 90 = up. The chain
        /// above the grip and the sprite's calibration offset are solved for at <see cref="Write"/>, so
        /// the order of commands does not matter.
        /// </summary>
        public RigWriter Aim(string itemId, float worldDegrees, Arc arc = Arc.Shortest, Ease ease = Ease.Smooth)
        {
            var item = RequireHeld(itemId);
            Track(GripJointId(item))[_time] = new Order
            {
                IsAim = true,
                ItemId = itemId,
                WorldDegrees = worldDegrees,
                Arc = arc,
                Ease = ease
            };
            return this;
        }

        /// <summary>
        /// Aims the long way round: the arc to <paramref name="worldDegrees"/> is forced to travel in
        /// <paramref name="arc"/>, plus whole extra turns on top.
        /// </summary>
        public RigWriter Sweep(string itemId, float worldDegrees, Arc arc, int extraTurns = 0, Ease ease = Ease.Smooth)
        {
            var item = RequireHeld(itemId);
            Track(GripJointId(item))[_time] = new Order
            {
                IsAim = true,
                ItemId = itemId,
                WorldDegrees = worldDegrees,
                Arc = arc,
                ExtraTurns = Mathf.Abs(extraTurns),
                Ease = ease
            };
            return this;
        }

        /// <summary>Moves a node. Position curves are written on all three axes — see <see cref="Write"/>.</summary>
        public RigWriter Move(string jointId, Vector2 localPosition)
        {
            RequireJoint(jointId);
            if (!_positions.TryGetValue(jointId, out var track))
                _positions[jointId] = track = new SortedList<float, Vector2>();
            track[_time] = localPosition;
            return this;
        }

        /// <summary>
        /// Where the item's marker points in world degrees at <see cref="At"/>, given the EXPLICIT angles
        /// written so far. Aims are not resolved yet at this point, so a grip driven only by aims still
        /// reads from its rest pose.
        /// </summary>
        public float ReadWorldOrientation(string itemId)
        {
            var item = RequireHeld(itemId);
            ApplyPose(_time);
            return RigProbe.WorldOrientation(_instance.transform.Find(item.GripPath), item);
        }

        /// <summary>
        /// Resolves every aim, then writes the keys into a clip asset. Rotation goes to
        /// localEulerAnglesRaw on all three axes and position to m_LocalPosition on all three: Unity
        /// fills a missing axis with zero rather than leaving it alone, so a half-written curve silently
        /// flattens the pose.
        /// </summary>
        public Report Write(string assetPath, float frameRate = 30f, bool loopTime = false, bool seedRestAtZero = true)
        {
            Resolve(seedRestAtZero);

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            bool created = clip == null;
            if (created) clip = new AnimationClip();
            else clip.ClearCurves();

            clip.frameRate = frameRate;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loopTime;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            foreach (var pair in _resolved)
            {
                var joint = _profile.FindJoint(pair.Key);
                var curveZ = new AnimationCurve();
                var flat = new AnimationCurve();
                foreach (var key in pair.Value)
                {
                    curveZ.AddKey(key.Key, key.Value);
                    flat.AddKey(key.Key, 0f);
                }
                SmoothAll(curveZ);
                ApplyEases(curveZ, pair.Key);

                SetCurve(clip, joint.Path, "localEulerAnglesRaw.x", flat);
                SetCurve(clip, joint.Path, "localEulerAnglesRaw.y", new AnimationCurve(flat.keys));
                SetCurve(clip, joint.Path, "localEulerAnglesRaw.z", curveZ);
                _report.Curves += 3;
                _report.Keys += curveZ.length;
            }

            foreach (var pair in _positions)
            {
                var joint = _profile.FindJoint(pair.Key);
                var x = new AnimationCurve();
                var y = new AnimationCurve();
                var z = new AnimationCurve();
                foreach (var key in pair.Value)
                {
                    x.AddKey(key.Key, key.Value.x);
                    y.AddKey(key.Key, key.Value.y);
                    z.AddKey(key.Key, 0f);
                }
                SmoothAll(x); SmoothAll(y);

                SetCurve(clip, joint.Path, "m_LocalPosition.x", x);
                SetCurve(clip, joint.Path, "m_LocalPosition.y", y);
                SetCurve(clip, joint.Path, "m_LocalPosition.z", z);
                _report.Curves += 3;
                _report.Keys += x.length;
            }

            // Events survive ClearCurves, so replacing them wholesale is the only way to keep a rewritten
            // clip honest: a stale marker points at a frame the new choreography does not have.
            AnimationUtility.SetAnimationEvents(clip, _events.ToArray());
            if (_events.Count > 0)
                _report.Lines.Add($"events: {string.Join(", ", _events.ConvertAll(e => $"{e.functionName}@{e.time:F3}"))}");

            if (created) AssetDatabase.CreateAsset(clip, assetPath);
            else { EditorUtility.SetDirty(clip); AssetDatabase.SaveAssetIfDirty(clip); }

            _report.Path = assetPath;
            VerifyAims(clip);
            return _report;
        }

        /// <summary>
        /// Turns orders into local angles, in an order that matters:
        ///
        /// 1. seed t=0 with the rest angle on every track that starts later, because a curve holds its
        ///    first key's value backwards in time — a lone key at 0.8s bends the joint from frame zero;
        /// 2. lay down the explicit values;
        /// 3. only then solve the aims, against a chain that is now final.
        ///
        /// Seeding after solving is what made an aimed blade land 40 degrees off: the seed changed the
        /// elbow's contribution AFTER the aim had already been computed against the unseeded chain.
        /// </summary>
        void Resolve(bool seedRestAtZero)
        {
            _resolved.Clear();
            foreach (var pair in _orders)
            {
                var track = new SortedList<float, float>();
                if (seedRestAtZero && pair.Value.Count > 0 && pair.Value.Keys[0] > 1e-4f)
                {
                    var seeded = _profile.FindJoint(pair.Key);
                    if (seeded != null)
                    {
                        track[0f] = seeded.RestZ;
                        _report.Lines.Add($"seeded {pair.Key} at t=0 with its rest angle {seeded.RestZ:F1}");
                    }
                }
                foreach (var order in pair.Value)
                    if (!order.Value.IsAim)
                        track[order.Key] = order.Value.LocalZ;
                _resolved[pair.Key] = track;
            }

            // Unwrapping needs the previous value on the track, so walk each track forwards in time.
            foreach (var pair in _orders)
            {
                var joint = _profile.FindJoint(pair.Key);
                foreach (var entry in pair.Value)
                {
                    float time = entry.Key;
                    var order = entry.Value;
                    float value;

                    float previous = PreviousResolved(pair.Key, time, joint.RestZ, out float previousTime);

                    if (order.IsAim)
                    {
                        var item = _profile.FindHeld(order.ItemId);
                        var grip = _instance.transform.Find(item.GripPath);

                        // The arc has to be unwrapped in WORLD space, because that is where the author sees
                        // it. Unwrapping the grip's LOCAL angle instead sent the blade the wrong way round:
                        // the hand travelled -26 degrees while the shoulder supplied +138, so "counter-
                        // clockwise" on the local number meant a full extra turn on screen.
                        ApplyPose(previousTime);
                        float parentBefore = ParentWorld(grip);
                        float previousWorld = previous + item.OrientationLocal + item.CalibrationZ + parentBefore;

                        ApplyPose(time);
                        float parentNow = ParentWorld(grip);
                        float targetWorld = Unwrap(previousWorld, order.WorldDegrees, order.Arc);
                        if (order.ExtraTurns > 0)
                            targetWorld += (order.Arc == Arc.Cw ? -360f : 360f) * order.ExtraTurns;

                        // In DELTAS, not absolutes: how much world the blade must travel, minus how much the
                        // chain already supplies. Solving for the absolute local angle instead produced an
                        // equivalent value 360 out (measured: 333.8 where -26.2 was meant) — the end pose
                        // matched, but the wrist took a full turn to get there.
                        float chainDelta = Mathf.DeltaAngle(parentBefore, parentNow);
                        value = previous + (targetWorld - previousWorld) - chainDelta;
                        _report.Lines.Add($"t={time:F2} aim {order.ItemId} at {order.WorldDegrees:F1} world " +
                                          $"(from {previousWorld:F0}, {order.Arc}) -> {pair.Key} local {value:F1}");
                    }
                    else
                    {
                        value = Unwrap(previous, order.LocalZ, order.Arc);
                        if (order.ExtraTurns > 0)
                            value += (order.Arc == Arc.Cw ? -360f : 360f) * order.ExtraTurns;
                    }

                    _resolved[pair.Key][time] = value;
                    if (!_eases.TryGetValue(pair.Key, out var modes))
                        _eases[pair.Key] = modes = new Dictionary<float, Ease>();
                    modes[time] = order.Ease;
                }
            }
        }

        /// <summary>
        /// The last value on this track before <paramref name="time"/>, plus the time it sits at — the aim
        /// solver needs both, because the world angle it unwraps from depends on the chain's pose back then.
        /// </summary>
        float PreviousResolved(string jointId, float time, float fallback, out float previousTime)
        {
            var track = _resolved[jointId];
            float value = fallback;
            previousTime = 0f;
            foreach (var key in track)
            {
                if (key.Key >= time - 1e-4f) break;
                value = key.Value;
                previousTime = key.Key;
            }
            return value;
        }

        static float ParentWorld(Transform grip) =>
            grip.parent != null ? RigProfileBuilder.NormalizeAngle(grip.parent.eulerAngles.z) : 0f;

        static void SetCurve(AnimationClip clip, string path, string property, AnimationCurve curve)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        static void SmoothAll(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        }

        /// <summary>
        /// Overrides the automatic tangents where the author asked for a specific speed graph. A tangent is
        /// speed at the key, so zero means "at rest here": that is what separates a wind-up that settles
        /// and breaks from one that drifts through its own extreme.
        /// </summary>
        void ApplyEases(AnimationCurve curve, string jointId)
        {
            if (!_eases.TryGetValue(jointId, out var modes)) return;

            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (!modes.TryGetValue(keys[i].time, out var mode) || mode == Ease.Smooth) continue;

                float slopeIn = i > 0
                    ? (keys[i].value - keys[i - 1].value) / Mathf.Max(1e-4f, keys[i].time - keys[i - 1].time)
                    : 0f;
                float slopeOut = i < keys.Length - 1
                    ? (keys[i + 1].value - keys[i].value) / Mathf.Max(1e-4f, keys[i + 1].time - keys[i].time)
                    : 0f;

                switch (mode)
                {
                    case Ease.Hold:    keys[i].inTangent = 0f;      keys[i].outTangent = 0f;        break;
                    case Ease.Linear:  keys[i].inTangent = slopeIn; keys[i].outTangent = slopeOut;  break;
                    case Ease.EaseOut: keys[i].inTangent = slopeIn; keys[i].outTangent = 0f;        break;
                    case Ease.EaseIn:  keys[i].inTangent = 0f;      keys[i].outTangent = slopeOut;  break;
                }
                keys[i].weightedMode = WeightedMode.None;
            }
            curve.keys = keys;
        }

        /// <summary>
        /// Poses the working instance from the resolved keys, interpolating between them the way a curve
        /// will. Holding the previous value instead put the chain in a pose Unity never plays, and every
        /// aim solved against it came out wrong.
        /// </summary>
        void ApplyPose(float time)
        {
            foreach (var joint in _profile.Joints)
            {
                var node = _instance.transform.Find(joint.Path);
                if (node == null) continue;
                node.localEulerAngles = new Vector3(0f, 0f, SampleTrack(joint, time));
            }
        }

        /// <summary>
        /// The value a written curve would hold at <paramref name="time"/>. Before the first key and after
        /// the last, a curve holds that key's value — NOT the rest pose. That is exactly how a single key
        /// late in the clip bends a joint from frame zero.
        /// </summary>
        float SampleTrack(RigProfile.Joint joint, float time)
        {
            if (!_resolved.TryGetValue(joint.Id, out var track) || track.Count == 0) return joint.RestZ;
            if (time <= track.Keys[0]) return track.Values[0];
            if (time >= track.Keys[track.Count - 1]) return track.Values[track.Count - 1];

            for (int i = 1; i < track.Count; i++)
            {
                if (track.Keys[i] < time) continue;
                float span = track.Keys[i] - track.Keys[i - 1];
                float k = span <= 1e-6f ? 1f : (time - track.Keys[i - 1]) / span;
                return Mathf.Lerp(track.Values[i - 1], track.Values[i], k);
            }
            return track.Values[track.Count - 1];
        }

        /// <summary>
        /// Samples the finished clip and checks every aim actually landed. This is the check that catches
        /// the writer's own model drifting from what Unity plays — the failure that turned a 200-degree
        /// cut into 19 degrees and was invisible until someone looked at the frames.
        /// </summary>
        void VerifyAims(AnimationClip clip)
        {
            foreach (var pair in _orders)
            {
                foreach (var entry in pair.Value)
                {
                    if (!entry.Value.IsAim) continue;
                    var item = _profile.FindHeld(entry.Value.ItemId);
                    var grip = _instance.transform.Find(item.GripPath);
                    if (grip == null) continue;

                    clip.SampleAnimation(_instance, entry.Key);
                    float actual = RigProbe.WorldOrientation(grip, item);
                    float error = Mathf.Abs(Mathf.DeltaAngle(actual, entry.Value.WorldDegrees));
                    // A few degrees of drift is the overlap doing its job: the hand lags the shoulder by a
                    // frame or two, so on a plateau the world angle keeps creeping while the local one holds.
                    // Only flag what is too large to be lag.
                    if (error <= 8f) continue;
                    _report.Warnings.Add(
                        $"aim {entry.Value.ItemId} at t={entry.Key:F2} asked for {entry.Value.WorldDegrees:F1} world " +
                        $"but the clip plays {actual:F1} (off by {error:F1}) — check whether the chain is still " +
                        "moving through that key");
                }
            }
        }

        string GripJointId(RigProfile.HeldItem item)
        {
            foreach (var joint in _profile.Joints)
                if (joint.Path == item.GripPath) return joint.Id;
            throw new System.InvalidOperationException(
                $"Grip path '{item.GripPath}' of held item '{item.Id}' is not a joint in the profile — rebuild it.");
        }

        RigProfile.Joint RequireJoint(string jointId)
        {
            var joint = _profile.FindJoint(jointId);
            if (joint == null)
                throw new System.ArgumentException($"No joint '{jointId}' in {_profile.name}. Rebuild the profile or check the id.");
            return joint;
        }

        RigProfile.HeldItem RequireHeld(string itemId)
        {
            var item = _profile.FindHeld(itemId);
            if (item == null)
                throw new System.ArgumentException($"No held item '{itemId}' in {_profile.name}.");
            return item;
        }

        SortedList<float, Order> Track(string jointId)
        {
            if (!_orders.TryGetValue(jointId, out var track))
                _orders[jointId] = track = new SortedList<float, Order>();
            return track;
        }

        /// <summary>
        /// Rewrites <paramref name="target"/> as the value nearest <paramref name="previous"/> that travels
        /// in the requested direction. The result may leave -180..180 on purpose: that is what carries the
        /// arc into the curve, and normalising it later collapses the swing.
        /// </summary>
        public static float Unwrap(float previous, float target, Arc arc)
        {
            float delta = Mathf.DeltaAngle(previous, target);
            switch (arc)
            {
                case Arc.Cw:
                    if (delta > 0f) delta -= 360f;
                    break;
                case Arc.Ccw:
                    if (delta < 0f) delta += 360f;
                    break;
            }
            return previous + delta;
        }

        public void Dispose()
        {
            if (_instance != null) Object.DestroyImmediate(_instance);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(_scene);
        }
    }

    /// <summary>
    /// The Euler filter, for curves that already exist: walks each rotation curve and rewrites every key
    /// as the equivalent angle (+-360k) closest to its predecessor, so the series is continuous.
    ///
    /// This changes no pose — every key still describes the same orientation — but it removes the jumps
    /// where a curve crosses +-180 and Unity plays the long way round by accident. Measured on a curve
    /// wrapped the way Record mode wraps one: 170 then -170 sent the blade 340 degrees backwards; after
    /// the filter, 170 then 190, and it travels the 20 degrees that were meant.
    ///
    /// What it CANNOT do is restore a deliberate long swing. Once 297.8 has been normalised to -62.2, the
    /// intent is gone and the shorter arc is the honest reading of what is left — a 200-degree cut has to
    /// be authored as one (<see cref="RigWriter.Arc"/>), not recovered afterwards. <see cref="RigWriter"/>
    /// never produces wrapped keys itself, so this is for clips that came from Record mode or hand edits.
    /// </summary>
    public static class RigEulerFilter
    {
        /// <summary>Returns how many keys were rewritten.</summary>
        public static int Apply(AnimationClip clip)
        {
            int rewritten = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!binding.propertyName.StartsWith("localEulerAnglesRaw", System.StringComparison.Ordinal)) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length < 2) continue;

                var keys = curve.keys;
                bool changed = false;
                for (int i = 1; i < keys.Length; i++)
                {
                    float continuous = keys[i - 1].value + Mathf.DeltaAngle(keys[i - 1].value, keys[i].value);
                    if (Mathf.Abs(continuous - keys[i].value) < 1e-3f) continue;
                    keys[i].value = continuous;
                    changed = true;
                    rewritten++;
                }
                if (!changed) continue;

                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            if (rewritten > 0)
            {
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssetIfDirty(clip);
            }
            return rewritten;
        }
    }
}
#endif
