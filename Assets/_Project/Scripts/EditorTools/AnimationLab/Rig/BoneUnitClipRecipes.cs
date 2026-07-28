#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// The eight clips of the skeletal knight, written as recipes rather than dragged in the Animation
    /// window. Each method reads as the choreography it produces, so a change to the timing of a swing is
    /// a change to one line instead of forty keyframes.
    ///
    /// <b>Ownership.</b> These recipes are the FIRST pass — a preview for Max to judge and then adjust by
    /// hand. Once he edits a clip in the Animation window, that clip is the truth and rerunning its recipe
    /// would overwrite his work. Rerun deliberately, per clip, not as a habit.
    ///
    /// Angles here are the vocabulary of the rig profile: Bend is degrees from the rest pose in the
    /// direction a joint folds, Aim is where the blade points in WORLD degrees (0 right, 90 up), Arc says
    /// which way round it travels, Hold is the pause that gives a blow its weight.
    /// </summary>
    public static class BoneUnitClipRecipes
    {
        const string Folder = "Assets/_Project/Prefabs/Bones/";
        const string ProfilePath = Folder + "BoneUnit_Standart_RigProfile.asset";

        /// <summary>Rest position of the pelvis. Vertical travel from here is the weight shift.</summary>
        static readonly Vector2 RestHips = new Vector2(0.037f, 0.020f);

        // Every attack shares one timing skeleton, measured off Max's first attack and kept:
        // wind-up in ~10 frames, HOLD the extreme ~8-10 frames, strike in 5-8, HOLD the impact ~6-7,
        // then a long settle. The holds are what make it read as a blow instead of a wave.
        //
        // WHOLE POSES ON WHOLE KEYS (Max, 28.07). Bones used to arrive a frame or two apart — head and
        // elbow trailing the torso, the hand trailing them. On paper that is overlapping action; at 60 fps
        // on a rig this simple it read as lag, and it made every note ("that pose is wrong") ambiguous
        // about WHICH of three keys to fix. Overlap may come back later on the settle alone, where the
        // movement is long enough for a trail to read as weight instead of as a stutter.
        //
        // A SLASH IS TWO KEYS (Max, 28.07). Where the swing starts and where it ends — nothing between
        // them and nothing after them. A key inside the arc splits one acceleration into two and the blade
        // measurably stutters; a follow-through key after the impact re-starts the blade once it has
        // already stopped, which reads as the animation lagging rather than as weight. Both were in every
        // attack and both are gone. Weight after the blow lives in the HOLD and in the settle back to
        // stance, not in another few degrees of blade.

        const RigWriter.Ease Hold = RigWriter.Ease.Hold;
        const RigWriter.Ease Out = RigWriter.Ease.EaseOut;      // arrives at speed, stops dead
        const RigWriter.Ease In = RigWriter.Ease.EaseIn;        // breaks from rest
        const RigWriter.Ease Lin = RigWriter.Ease.Linear;       // no braking mid-swing
        const RigWriter.Ease Soft = RigWriter.Ease.Smooth;
        const RigWriter.Arc Cw = RigWriter.Arc.Cw;
        const RigWriter.Arc Ccw = RigWriter.Arc.Ccw;
        const RigWriter.Arc Near = RigWriter.Arc.Shortest;

        static RigProfile Profile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<RigProfile>(ProfilePath);
            if (profile == null) throw new System.InvalidOperationException($"No rig profile at {ProfilePath}");
            return profile;
        }

        /// <summary>
        /// Overhead vertical cut — the knight's bread and butter. Max's timing frame for frame; what changed
        /// is where the blade ENDS: it used to finish pointing back across the body, because the shoulder
        /// and the wrist cancelled each other, so the cut never crossed the front of the unit. Now it
        /// travels 245 degrees through the front and finishes low, blade down.
        /// </summary>
        public static string Attack()
        {
            using (var w = new RigWriter(Profile()))
            {
                // wind-up: the whole body arrives on one key
                w.At(0.167f).Bend("torso", -4f, Near, Out).Bend("shoulder.R", 158f, Near, Out)
                            .Bend("shoulder.L", 27f, Near, Out).Bend("knee.R", 8f, Near, Out)
                            .Bend("head", 17f, Near, Out).Bend("elbow.R", 10f, Near, Out).Bend("elbow.L", 13f, Near, Out)
                            .Aim("weapon", 175f, Ccw, Out).Aim("shield", 75f, Near, Out);
                HoldUntil(w, 0.333f);   // 8 frames of a genuinely frozen wind-up

                // Break from the hold and swing through the FRONT in ONE go. No mid-swing keys: a key
                // between the hold and the contact splits the arc into two accelerations, and the blade
                // measurably stutters — 49 deg/frame, then 25, then 46 again.
                w.At(0.333f).Bend("torso", -4f, Near, In).Bend("shoulder.R", 158f, Near, In);
                // The shoulder needs its side stated too, and this is the one joint on the rig where the
                // short way is the WRONG way: from 168 to -26 the short path is +166, i.e. onward through
                // 180 and around the back. The cut has to come down through 90 and 0, which is -194 — a
                // long arc, so Cw or the arm swings backwards over his own head.
                // Elbow stops just short of straight. It used to land at -64, inherited from the old clip,
                // and that put the angle between upper arm and forearm at -44 — the joint bent inside out.
                // A hinge only folds one way; the blade's reach comes from the shoulder and the wrist.
                w.At(0.450f).Bend("torso", -10f, Near, Out).Bend("shoulder.R", -36f, Cw, Out)
                            .Bend("shoulder.L", 3f, Near, Out).Bend("knee.R", 12f, Near, Out)
                            .Bend("knee.L", 6f, Near, Out).Bend("hip.L", -4f, Near, Out)
                            .Bend("head", -18f, Near, Out).Bend("elbow.R", 2f, Near, Out)
                            .Aim("weapon", -70f, Cw, Out).Aim("shield", 50f, Near, Out);
                HoldUntil(w, 0.583f);   // 6 frames of frozen impact

                Stance(w, 1.167f);
                // Contact where the blade crosses the torso band, not where the arm finishes: at 0.45 the
                // swing is already over and the blade is below the target.
                w.Event("Marker", 0.400f);
                return w.Write(Folder + "Attack.anim", 60f).ToString();
            }
        }

        /// <summary>
        /// Rising cut. The anticipation is a CROUCH rather than a pull-back: knees fold, the pelvis drops,
        /// the blade sinks behind the leg — then the legs extend and drive the blade up through the bottom
        /// of the arc, 205 degrees. A downward blow ends low, which is exactly where this one starts, so the
        /// two chain.
        /// </summary>
        public static string Attack2()
        {
            using (var w = new RigWriter(Profile()))
            {
                w.At(0.160f).Bend("torso", -12f, Near, Out).Bend("knee.L", 25f, Near, Out).Bend("knee.R", 27f, Near, Out)
                            .Bend("hip.L", -5f, Near, Out).Bend("hip.R", 6f, Near, Out)
                            .Bend("shoulder.R", -27f, Near, Out).Bend("shoulder.L", 8f, Near, Out)
                            .Bend("head", 6f, Near, Out).Bend("elbow.R", 6f, Near, Out).Bend("elbow.L", -5f, Near, Out)
                            .Aim("weapon", -150f, Cw, Out).Aim("shield", 82f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.008f));
                HoldUntil(w, 0.300f);

                w.At(0.300f).Aim("weapon", -150f, Near, In);
                w.At(0.380f).Bend("torso", 7f, Near, Out).Bend("knee.L", 5f, Near, Out).Bend("knee.R", 6f, Near, Out)
                            .Bend("hip.L", -9f, Near, Out).Bend("hip.R", 12f, Near, Out)
                            .Bend("shoulder.R", 57f, Near, Out).Bend("shoulder.L", -12f, Near, Out)
                            .Bend("head", -4f, Near, Out).Bend("elbow.R", 31f, Near, Out).Bend("elbow.L", 9f, Near, Out)
                            .Aim("weapon", 55f, Ccw, Out).Aim("shield", 94f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.035f));
                HoldUntil(w, 0.513f);

                Stance(w, 1.000f);
                // Rising cuts pass the torso on the way UP, so contact is mid-arc, not at the top of it.
                w.Event("Marker", 0.350f);
                return w.Write(Folder + "Attack2.anim", 60f).ToString();
            }
        }

        /// <summary>
        /// The second overhead — a shoulder cut that carries THROUGH instead of down into the ground.
        ///
        /// The diagonal that used to live here was cut on Max's verdict: in our flat 2D view a diagonal
        /// reads as neither an overhead nor a horizontal, and the gizmo confirmed it — its contact landed as
        /// an almost horizontal poke while the path had started high on the other side. This is a variation
        /// of the first attack rather than a third handwriting: the same wide arc through the front, but the
        /// wind-up goes deeper behind the back (205 instead of 175) and the blade stops FORWARD at chest
        /// height (-25) instead of pointing at the floor (-70). Same family, different ending — which is
        /// what a variation is.
        ///
        /// The strike is deliberately slower than the first attack's: 8 frames instead of 6 for a comparable
        /// arc. A 230-degree sweep crossed in three frames leaves a trail no slash effect can sit on.
        /// </summary>
        public static string Attack3()
        {
            using (var w = new RigWriter(Profile()))
            {
                // wind-up: the blade goes further back than in the vertical cut, and the weight loads onto
                // the back leg — the deeper gather is what tells the two overheads apart at a glance
                w.At(0.150f).Bend("torso", 7f, Near, Out).Bend("shoulder.R", 172f, Near, Out)
                            .Bend("shoulder.L", 24f, Near, Out).Bend("knee.R", 10f, Near, Out)
                            .Bend("knee.L", 6f, Near, Out).Bend("hip.R", 7f, Near, Out)
                            .Bend("head", 14f, Near, Out).Bend("elbow.R", 14f, Near, Out).Bend("elbow.L", 12f, Near, Out)
                            .Aim("weapon", 205f, Ccw, Out).Aim("shield", 80f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.030f));
                HoldUntil(w, 0.300f);   // 7 frames frozen at the top

                // one continuous acceleration through the front, no mid-swing key
                w.At(0.300f).Bend("torso", 7f, Near, In).Bend("shoulder.R", 172f, Near, In);
                // Cw for the same reason as the vertical cut: from behind the back the short way home runs
                // backwards over his own head.
                w.At(0.440f).Bend("torso", -14f, Near, Out).Bend("shoulder.R", -22f, Cw, Out)
                            .Bend("shoulder.L", 5f, Near, Out).Bend("knee.R", 14f, Near, Out)
                            .Bend("knee.L", 8f, Near, Out).Bend("hip.L", -6f, Near, Out)
                            .Bend("head", -15f, Near, Out).Bend("elbow.R", 6f, Near, Out).Bend("elbow.L", 8f, Near, Out)
                            .Aim("weapon", -25f, Cw, Out).Aim("shield", 55f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.010f));
                HoldUntil(w, 0.573f);   // 6 frames of frozen impact

                Stance(w, 1.000f);
                // Contact where the blade CROSSES chest height (measured: tip at y=0.05 inside the torso
                // band -0.07..0.19, still travelling 12.5 u/s), not where the arm finishes. Put it at the
                // end of the arc and the hit lands on a blade that has already stopped, below the target.
                w.Event("Marker", 0.400f);
                return w.Write(Folder + "Attack3.anim", 60f).ToString();
            }
        }

        /// <summary>
        /// The charge attack: a heavy vertical blow that only enters from Sprint, so frame zero is a sprint
        /// pose — trailing leg still up, body already leaning — rather than the stance. The blade goes high
        /// while the legs land, then comes down 185 degrees with the whole torso behind it.
        /// </summary>
        public static string AttackCharge()
        {
            using (var w = new RigWriter(Profile()))
            {
                // frame zero IS the sprint pose: entering from a stance would read as a stumble
                w.At(0f).Bend("torso", -11f, Near, Lin).Bend("hip.R", 30f, Near, Lin).Bend("hip.L", -8f, Near, Lin)
                        .Bend("knee.L", 40f, Near, Lin).Bend("knee.R", 10f, Near, Lin).Bend("head", 5f, Near, Lin)
                        .Move("hips", new Vector2(RestHips.x, 0.015f));

                w.At(0.117f).Bend("shoulder.R", -21f, Near, Soft).Bend("elbow.R", 19f, Near, Soft)
                            .Bend("hip.L", 30f, Near, Soft).Bend("hip.R", -8f, Near, Soft)
                            .Bend("knee.L", 10f, Near, Soft).Bend("knee.R", 45f, Near, Soft)
                            .Aim("weapon", 60f, Ccw, Soft)
                            .Move("hips", new Vector2(RestHips.x, 0.036f));

                // blade at its highest while the feet plant — the wind-up and the landing are one beat
                w.At(0.267f).Bend("torso", 7f, Near, Out).Bend("shoulder.R", -63f, Near, Out)
                            .Bend("shoulder.L", 10f, Near, Out).Bend("hip.L", -19f, Near, Out).Bend("hip.R", 28f, Near, Out)
                            .Bend("knee.L", 47f, Near, Out).Bend("knee.R", 13f, Near, Out)
                            .Bend("head", -2f, Near, Out).Bend("elbow.R", 47f, Near, Out).Bend("elbow.L", -7f, Near, Out)
                            .Aim("weapon", 120f, Ccw, Out).Aim("shield", 100f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.028f));
                HoldUntil(w, 0.383f);

                w.At(0.383f).Aim("weapon", 120f, Near, In);
                // -36 put this elbow 16 degrees past straight the wrong way; the blow's reach lives in the
                // shoulder and the torso lean, not in an inverted joint.
                w.At(0.467f).Bend("torso", -22f, Near, Out).Bend("shoulder.R", 53f, Near, Out)
                            .Bend("shoulder.L", -21f, Near, Out).Bend("hip.L", -28f, Near, Out).Bend("hip.R", 36f, Near, Out)
                            .Bend("knee.L", 36f, Near, Out).Bend("knee.R", 12f, Near, Out)
                            .Bend("head", -9f, Near, Out).Bend("elbow.R", 2f, Near, Out).Bend("elbow.L", 15f, Near, Out)
                            .Aim("weapon", -80f, Cw, Out).Aim("shield", 62f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, -0.009f));
                HoldUntil(w, 0.616f);

                Stance(w, 1.100f);
                w.Event("Marker", 0.417f);
                return w.Write(Folder + "AttackCharge.anim", 60f).ToString();
            }
        }

        /// <summary>
        /// Brace behind the shield — the telegraph for Bulwark, and it lives on a masked layer, so it only
        /// writes the shield arm. Snaps up in seven frames (a guard that eases up is a guard that arrives
        /// late), holds, then settles a couple of degrees so the pose is not frozen.
        /// </summary>
        public static string Block()
        {
            using (var w = new RigWriter(Profile()))
            {
                w.At(0.117f).Bend("shoulder.L", 33f, Near, Out).Bend("elbow.L", -27f, Near, Out)
                            .Aim("shield", 122f, Ccw, Out);
                HoldUntil(w, 0.334f);
                w.At(0.450f).Bend("shoulder.L", 31f, Near, Soft).Bend("elbow.L", -25f, Near, Soft)
                            .Aim("shield", 118f, Near, Soft);
                w.At(0.600f).Bend("shoulder.L", 31f, Near, Hold).Bend("elbow.L", -25f, Near, Hold)
                            .Aim("shield", 118f, Near, Hold);
                return w.Write(Folder + "Block.anim", 60f, loopTime: false).ToString();
            }
        }

        /// <summary>
        /// Breathing. The chest rises on a 1.1s inhale and falls on a 1.5s exhale — never symmetrical,
        /// that is what stops a loop reading as a machine. The shoulders lift with the ribcage; the weapon
        /// arm barely moves, because a knight at rest holds his blade still.
        /// </summary>
        public static string Idle()
        {
            using (var w = new RigWriter(Profile()))
            {
                w.At(1.100f).Bend("torso", -1.5f, Near, Out).Bend("shoulder.L", -3f, Near, Out)
                            .Bend("shoulder.R", 3f, Near, Out).Bend("head", 1.5f, Near, Out)
                            .Bend("elbow.L", 2f, Near, Out).Bend("elbow.R", -2f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.026f));
                w.At(2.600f).Bend("torso", 0f, Near, Out).Bend("shoulder.L", 0f, Near, Out)
                            .Bend("shoulder.R", 0f, Near, Out).Bend("head", 0f, Near, Out)
                            .Bend("elbow.L", 0f, Near, Out).Bend("elbow.R", 0f, Near, Out)
                            .Move("hips", RestHips);
                return w.Write(Folder + "Idle.anim", 60f, loopTime: true).ToString();
            }
        }

        /// <summary>
        /// Walk: two strides in 0.7s, four poses each — contact, passing, contact, passing. The pelvis is
        /// LOWEST at contact and highest at passing, which is the whole reason a walk reads as weight
        /// rather than as sliding. Arms stay quiet: this is a knight carrying a sword, not swinging it.
        /// </summary>
        public static string Walk()
        {
            using (var w = new RigWriter(Profile()))
            {
                Stride(w, 0f, 0.7f, hipSwing: 22f, kneeLift: 32f, bobLow: 0.016f, bobHigh: 0.024f,
                       lean: -3f, armSwing: 3f);
                return w.Write(Folder + "Walk.anim", 60f, loopTime: true).ToString();
            }
        }

        /// <summary>
        /// Sprint: the same four-pose skeleton in 0.5s, but everything is bigger — longer stride, deeper
        /// knee, more lean, twice the bob. Same number of steps per cycle and the same phase as Walk, so
        /// the two blend without the legs popping.
        /// </summary>
        public static string Sprint()
        {
            using (var w = new RigWriter(Profile()))
            {
                Stride(w, 0f, 0.5f, hipSwing: 32f, kneeLift: 50f, bobLow: 0.012f, bobHigh: 0.030f,
                       lean: -8f, armSwing: 5f);
                return w.Write(Folder + "Sprint.anim", 60f, loopTime: true).ToString();
            }
        }

        /// <summary>
        /// Two strides of a locomotion cycle. Poses land at 0, 1/4, 1/2, 3/4 and the loop closes on the
        /// original pose; the pelvis dips at both contacts and lifts at both passings.
        /// </summary>
        static void Stride(RigWriter w, float start, float length, float hipSwing, float kneeLift,
                           float bobLow, float bobHigh, float lean, float armSwing)
        {
            float quarter = length / 4f;
            for (int step = 0; step < 2; step++)
            {
                // step 0 leads with the left leg, step 1 mirrors it
                float sign = step == 0 ? 1f : -1f;
                float contact = start + step * quarter * 2f;
                float passing = contact + quarter;

                w.At(contact).Bend("hip.L", hipSwing * sign, Near, Out).Bend("hip.R", -hipSwing * 0.8f * sign, Near, Out)
                             .Bend("knee.L", step == 0 ? 22f : kneeLift * 0.6f, Near, Out)
                             .Bend("knee.R", step == 0 ? kneeLift * 0.6f : 22f, Near, Out)
                             .Bend("torso", lean, Near, Soft)
                             .Bend("shoulder.L", -armSwing * sign, Near, Soft).Bend("shoulder.R", armSwing * sign, Near, Soft)
                             .Bend("head", -lean * 0.4f, Near, Soft)
                             .Move("hips", new Vector2(RestHips.x, bobLow));

                w.At(passing).Bend("hip.L", hipSwing * 0.15f * sign, Near, Lin)
                             .Bend("hip.R", hipSwing * 0.1f * -sign, Near, Lin)
                             .Bend("knee.L", step == 0 ? 14f : kneeLift, Near, Lin)
                             .Bend("knee.R", step == 0 ? kneeLift : 14f, Near, Lin)
                             .Move("hips", new Vector2(RestHips.x, bobHigh));
            }

            // close the loop on the first pose so the cycle does not jump
            w.At(start + length).Bend("hip.L", hipSwing, Near, Out).Bend("hip.R", -hipSwing * 0.8f, Near, Out)
                                .Bend("knee.L", 22f, Near, Out).Bend("knee.R", kneeLift * 0.6f, Near, Out)
                                .Bend("torso", lean, Near, Soft)
                                .Bend("shoulder.L", -armSwing, Near, Soft).Bend("shoulder.R", armSwing, Near, Soft)
                                .Bend("head", -lean * 0.4f, Near, Soft)
                                .Move("hips", new Vector2(RestHips.x, bobLow));
        }

        /// <summary>
        /// Freezes the whole rig until <paramref name="until"/>. One end for every bone, because a pause
        /// where bones leave at different times is not a pause.
        /// </summary>
        static void HoldUntil(RigWriter w, float until) => w.HoldUntil(until);

        /// <summary>Back to the stance every attack starts and ends in.</summary>
        static void Stance(RigWriter w, float time)
        {
            w.At(time).Bend("torso", 0f).Bend("head", 0f)
                      .Bend("shoulder.R", 0f).Bend("elbow.R", 0f)
                      .Bend("shoulder.L", 0f).Bend("elbow.L", 0f)
                      .Bend("hip.L", 0f).Bend("hip.R", 0f).Bend("knee.L", 0f).Bend("knee.R", 0f)
                      .Aim("weapon", 37f).Aim("shield", 90f)
                      .Move("hips", RestHips);
        }

        [MenuItem("Alebardium/Animation/Rebuild Bone Unit Clips", priority = 600)]
        static void RebuildAll()
        {
            var log = new System.Text.StringBuilder("Rebuilt bone unit clips from recipes:\n");
            log.AppendLine(Attack());
            log.AppendLine(Attack2());
            log.AppendLine(Attack3());
            log.AppendLine(AttackCharge());
            log.AppendLine(Block());
            log.AppendLine(Idle());
            log.AppendLine(Walk());
            log.AppendLine(Sprint());
            Debug.Log(log.ToString());
        }
    }
}
#endif
