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

        // THE GATHERED BLADE (Max, 29.07). A knight closing distance runs with the sword already up — half
        // a wind-up carried in the stride — so the charge that follows is one more pull rather than a swing
        // built from nothing. One pose, one place: Sprint holds it and AttackCharge starts from it, because
        // the same pose written twice is two owners of one fact and they drift apart on the first edit.
        // The blade sits just past vertical, angled back over the shoulder — far enough to read as "loaded"
        // at a glance, not so far that the run looks like a permanent wind-up.
        const float BladeGatheredShoulder = 84f;   // degrees from rest: upper arm up and slightly back
        const float BladeGatheredElbow    = 16f;   // a soft bend — a locked elbow reads as a mannequin
        const float BladeGatheredAim      = 105f;  // world degrees: 90 is straight up, so this leans back 15

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
        /// Overhead vertical cut — the knight's bread and butter. Timing and the wrist are Max's hand pass
        /// of 29.07: the wind-up takes a full quarter second, the strike is stretched to 0.21s so the blade
        /// draws its arc instead of snapping through it, and the wrist carries 18 degrees further at the
        /// end, which is what gives the cut its follow-through without a separate key after the impact.
        /// </summary>
        public static string Attack()
        {
            using (var w = new RigWriter(Profile()))
            {
                // Кадр ноль — БОЕВАЯ СТОЙКА, а не поза покоя (Макс, 30.07). Юнит входит в удар из
                // CombatIdle, где клинок уже поднят; стартуя из покоя, он сперва ронял меч вниз на 38
                // градусов и только потом замахивался. Тот же приём, что у AttackCharge с позой бега.
                Braced(w, 0f, Lin);

                // wind-up: the whole body arrives on one key
                w.At(0.250f).Bend("torso", -4f, Near, Out).Bend("shoulder.R", 158f, Near, Out)
                            .Bend("shoulder.L", 27f, Near, Out).Bend("knee.R", 8f, Near, Out)
                            .Bend("head", 17f, Near, Out).Bend("elbow.R", 10f, Near, Out).Bend("elbow.L", 13f, Near, Out)
                            .Aim("weapon", 175f, Ccw, Out).Aim("shield", 75f, Near, Out);
                HoldUntil(w, 0.333f);   // 5 frames of a genuinely frozen wind-up

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
                // ROUND ARC: the blade keeps a constant angle to the forearm through the whole strike, so
                // arm and sword turn as one rigid lever and the tip is forced onto a circle. The low
                // finish is bought with the TORSO instead — leaning drops the shoulder, and the whole
                // circle drops with it. Buying it with the wrist is what bent the arc into a spiral: the
                // radius grew 30% across the swing.
                w.At(0.583f).Bend("torso", -26f, Near, Out).Bend("shoulder.R", -36f, Cw, Out)
                            .Bend("shoulder.L", 3f, Near, Out).Bend("knee.R", 12f, Near, Out)
                            .Bend("knee.L", 6f, Near, Out).Bend("hip.L", -4f, Near, Out)
                            .Bend("head", -18f, Near, Out).Bend("elbow.R", 10f, Near, Out)
                            .Aim("weapon", -46f, Cw, Out).Aim("shield", 50f, Near, Out);
                HoldUntil(w, 0.667f);   // 5 frames of frozen impact

                Stance(w, 1.167f);
                // Contact where the blade crosses the torso band, not where the arm finishes.
                w.Event("Marker", 0.467f);
                return w.Write(Folder + "Attack.anim", 60f).ToString();
            }
        }

        /// <summary>
        /// Rising cut, travelling up the FRONT of the unit.
        ///
        /// The first version gathered the blade back and down behind the leg (-150) and then rose through
        /// the bottom of the circle. Every such path crosses "straight down", and with a blade this long
        /// that put the tip at y=-0.44 while the feet stand at -0.34: the knight ploughed the ground and
        /// swept his own shins on the way through. Measured, not guessed.
        ///
        /// Now the blade starts LOW IN FRONT — which is exactly where a downward blow leaves it, so the two
        /// attacks chain — and climbs through the horizontal to above the shoulder, 180 degrees of front
        /// arc that never goes near the floor. The anticipation stays a crouch rather than a pull-back:
        /// the power of a rising cut comes from the legs extending, so the legs must fold first.
        /// </summary>
        public static string Attack2()
        {
            using (var w = new RigWriter(Profile()))
            {
                Braced(w, 0f, Lin);   // вход из боевой стойки — см. Attack

                // anticipation: fold the knees, drop the pelvis, blade low and forward
                w.At(0.250f).Bend("torso", -12f, Near, Out).Bend("knee.L", 25f, Near, Out).Bend("knee.R", 27f, Near, Out)
                            .Bend("hip.L", -5f, Near, Out).Bend("hip.R", 6f, Near, Out)
                            .Bend("shoulder.R", -30f, Near, Out).Bend("shoulder.L", 8f, Near, Out)
                            .Bend("head", 6f, Near, Out).Bend("elbow.R", 10f, Near, Out).Bend("elbow.L", -5f, Near, Out)
                            .Aim("weapon", -50f, Cw, Out).Aim("shield", 82f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.008f));
                HoldUntil(w, 0.333f);

                // The legs extend and throw the blade up the front — one acceleration. Max raised the
                // shoulder to 137 and turned the wrist right through, so the cut finishes with the blade
                // upright over the head rather than tipped back behind it.
                w.At(0.333f).Aim("weapon", -50f, Near, In);
                w.At(0.583f).Bend("torso", 9f, Near, Out).Bend("knee.L", 4f, Near, Out).Bend("knee.R", 5f, Near, Out)
                            .Bend("hip.L", -9f, Near, Out).Bend("hip.R", 12f, Near, Out)
                            .Bend("shoulder.R", 137f, Near, Out).Bend("shoulder.L", -12f, Near, Out)
                            .Bend("head", -6f, Near, Out).Bend("elbow.R", 0f, Near, Out).Bend("elbow.L", 9f, Near, Out)
                            .Aim("weapon", 99f, Ccw, Out).Aim("shield", 94f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.038f));
                HoldUntil(w, 0.667f);

                Stance(w, 1.000f);
                // Rising cuts pass the torso on the way UP, so contact is mid-arc, not at the top of it.
                w.Event("Marker", 0.450f);
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
                Braced(w, 0f, Lin);   // вход из боевой стойки — см. Attack

                // wind-up: the blade goes further back than in the vertical cut, and the weight loads onto
                // the back leg — the deeper gather is what tells the two overheads apart at a glance
                w.At(0.250f).Bend("torso", 7f, Near, Out).Bend("shoulder.R", 172f, Near, Out)
                            .Bend("shoulder.L", 24f, Near, Out).Bend("knee.R", 10f, Near, Out)
                            .Bend("knee.L", 6f, Near, Out).Bend("hip.R", 7f, Near, Out)
                            .Bend("head", 14f, Near, Out).Bend("elbow.R", 14f, Near, Out).Bend("elbow.L", 12f, Near, Out)
                            .Aim("weapon", 205f, Ccw, Out).Aim("shield", 80f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.030f));
                HoldUntil(w, 0.333f);   // 5 frames frozen at the top

                // one continuous acceleration through the front, no mid-swing key
                w.At(0.333f).Bend("torso", 7f, Near, In).Bend("shoulder.R", 172f, Near, In);
                // Cw for the same reason as the vertical cut: from behind the back the short way home runs
                // backwards over his own head.
                w.At(0.583f).Bend("torso", -14f, Near, Out).Bend("shoulder.R", -22f, Cw, Out)
                            .Bend("shoulder.L", 5f, Near, Out).Bend("knee.R", 14f, Near, Out)
                            .Bend("knee.L", 8f, Near, Out).Bend("hip.L", -6f, Near, Out)
                            .Bend("head", -15f, Near, Out).Bend("elbow.R", 6f, Near, Out).Bend("elbow.L", 8f, Near, Out)
                            .Aim("weapon", -60f, Cw, Out).Aim("shield", 55f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.010f));
                HoldUntil(w, 0.667f);   // 5 frames of frozen impact

                Stance(w, 1.000f);
                // Contact where the blade CROSSES the torso band, not where the arm finishes.
                w.Event("Marker", 0.483f);
                return w.Write(Folder + "Attack3.anim", 60f).ToString();
            }
        }

        /// <summary>
        /// The charge attack: the second overhead's swing, thrown with everything the knight has.
        ///
        /// It is deliberately built on <see cref="Attack3"/> rather than invented separately — same gather
        /// behind the back, same finish forward — because a charge should read as "that blow, but he meant
        /// it", not as a fourth handwriting nobody recognises. What is bigger: the gather goes 10 degrees
        /// further, the arc is the widest of the four at ~260 degrees, and the whole body commits — the
        /// torso leans 26 degrees into it (against 14), the legs land in a lunge, and the pelvis DROPS
        /// through the blow instead of holding its height.
        ///
        /// Frame zero is a sprint pose — trailing leg still up, body already leaning — because entering
        /// from a stance would read as a stumble. The landing and the wind-up are one beat: the feet plant
        /// while the blade is still climbing.
        /// </summary>
        public static string AttackCharge()
        {
            using (var w = new RigWriter(Profile()))
            {
                // Frame zero IS the sprint pose, blade included: the run already carries it gathered, so the
                // charge picks the arm up exactly where the stride left it. Entering from a stance — or from
                // a lowered blade — would read as a stumble followed by a wind-up.
                w.At(0f).Bend("torso", -11f, Near, Lin).Bend("hip.R", 30f, Near, Lin).Bend("hip.L", -8f, Near, Lin)
                        .Bend("knee.L", 40f, Near, Lin).Bend("knee.R", 10f, Near, Lin).Bend("head", 5f, Near, Lin)
                        .Bend("shoulder.R", BladeGatheredShoulder, Near, Lin).Bend("elbow.R", BladeGatheredElbow, Near, Lin)
                        .Aim("weapon", BladeGatheredAim, Near, Lin)
                        .Move("hips", new Vector2(RestHips.x, 0.015f));

                // The feet plant while the blade holds where the run had it — the pull comes next, and it is
                // one continuous gather from here to the top rather than two.
                w.At(0.117f).Bend("shoulder.R", BladeGatheredShoulder, Near, Soft).Bend("elbow.R", BladeGatheredElbow, Near, Soft)
                            .Bend("hip.L", 30f, Near, Soft).Bend("hip.R", -8f, Near, Soft)
                            .Bend("knee.L", 10f, Near, Soft).Bend("knee.R", 45f, Near, Soft)
                            .Aim("weapon", BladeGatheredAim, Near, Soft)
                            .Move("hips", new Vector2(RestHips.x, 0.036f));

                // gathered further behind the back than the standing version, weight loaded on the back leg
                w.At(0.300f).Bend("torso", 14f, Near, Out).Bend("shoulder.R", 184f, Near, Out)
                            .Bend("shoulder.L", 26f, Near, Out).Bend("hip.L", -16f, Near, Out).Bend("hip.R", 24f, Near, Out)
                            .Bend("knee.L", 32f, Near, Out).Bend("knee.R", 12f, Near, Out)
                            .Bend("head", 18f, Near, Out).Bend("elbow.R", 16f, Near, Out).Bend("elbow.L", 12f, Near, Out)
                            .Aim("weapon", 218f, Ccw, Out).Aim("shield", 78f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.044f));
                HoldUntil(w, 0.433f);   // 8 frames — the charge hangs at the top longer than a standing cut

                // The whole body goes with it: lunge, deep lean, pelvis dropping through the blow. This is
                // the one attack that does NOT share the common grid: the strike takes 0.30s against 0.21,
                // and both holds are longer. A charge that moves at the same rate as a normal cut is just a
                // normal cut with a run-up — the extra weight has to be in the TIME, not only in the pose.
                w.At(0.433f).Bend("torso", 14f, Near, In).Bend("shoulder.R", 184f, Near, In);
                w.At(0.733f).Bend("torso", -30f, Near, Out).Bend("shoulder.R", -34f, Cw, Out)
                            .Bend("shoulder.L", -20f, Near, Out).Bend("hip.L", -36f, Near, Out).Bend("hip.R", 44f, Near, Out)
                            .Bend("knee.L", 44f, Near, Out).Bend("knee.R", 16f, Near, Out)
                            .Bend("head", -18f, Near, Out).Bend("elbow.R", 4f, Near, Out).Bend("elbow.L", 15f, Near, Out)
                            .Aim("weapon", -80f, Cw, Out).Aim("shield", 58f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, -0.022f));
                HoldUntil(w, 0.867f);   // 8 frames of frozen impact: the ground takes the blow with him

                Stance(w, 1.350f);
                w.Event("Marker", 0.600f);
                return w.Write(Folder + "AttackCharge.anim", 60f).ToString();
            }
        }

        /// <summary>
        /// Block behind the shield — the telegraph for Bulwark, and it lives on a masked layer, so it only
        /// writes the shield arm. Snaps up in seven frames (a guard that eases up is a guard that arrives
        /// late), holds, then settles a couple of degrees so the pose is not frozen.
        /// </summary>
        public static string Block()
        {
            using (var w = new RigWriter(Profile()))
            {
                // ЩИТ ДОЛЖЕН ЗАКРЫВАТЬ КОРПУС, и это измеримо: RigSweep мерит долю силуэта тела за щитом.
                // Первая версия давала 6-10% — щит стоял СБОКУ от торса, потому что локоть РАЗГИБАЛСЯ
                // (−27°) и выпрямленная рука уводила щит от тела. Локоть теперь гнётся внутрь, предплечье
                // идёт поперёк корпуса, а плечо выносится вперёд, а не вверх.
                // ЩИТ ЗАКРЫВАЕТ СТОРОНУ ВРАГА, а не просто корпус (Макс, 30.07): противник всегда со
                // стороны взгляда, поэтому рука обязана вынести щит ПОПЕРЁК тела, к нему.
                //
                // Выносит ПЛЕЧО, и только оно: перебор по сетке показал, что локоть двигает центр щита на
                // сотые доли (0.008 против 0.011 на тридцати градусах), то есть на вынос не работает вовсе.
                // Прежние +50 на локте не помогали, а РАЗГИБАЛИ его: у левой руки положительный сгиб идёт
                // в переразгиб, и валидатор ловил это как hinge-inverted на 48 градусов. Локоть теперь
                // держит мягкий сгиб в анатомическую сторону — чтобы рука не читалась палкой.
                w.At(0.117f).Bend("shoulder.L", 54f, Near, Out).Bend("elbow.L", -15f, Near, Out)
                            .Bend("torso", -3f, Near, Out)
                            .Aim("shield", 96f, Ccw, Out);
                HoldUntil(w, 0.334f);

                // Оседание, а НЕ возврат в стойку: прошлая версия уводила щит обратно к 90° к концу клипа,
                // и рука уезжала раньше, чем кончался барьер. Опускает её вес слоя (см. UnitView.RaiseGuard),
                // клип же обязан держать позу столько, сколько его держат.
                w.At(0.450f).Bend("shoulder.L", 52f, Near, Soft).Bend("elbow.L", -13f, Near, Soft)
                            .Bend("torso", -3f, Near, Soft)
                            .Aim("shield", 94f, Near, Soft);
                w.At(0.600f).Bend("shoulder.L", 52f, Near, Hold).Bend("elbow.L", -13f, Near, Hold)
                            .Bend("torso", -3f, Near, Hold)
                            .Aim("shield", 94f, Near, Hold);
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
                // ОТДЫХ, а не «поза рига»: меч и щит ОПУЩЕНЫ (Макс, 30.07). Три состояния обязаны читаться
                // с одного взгляда — отдых опущен, наготове поднято, удар бьёт. Пока Idle показывал позу
                // покоя, клинок висел под 37 мировых градусов, и «вне боя» отличалось от «наготове» только
                // двадцатью градусами, то есть ничем.
                Resting(w, 0f, Lin);
                w.At(1.100f).Bend("torso", -1.5f, Near, Out).Bend("shoulder.L", -13.5f, Near, Out)
                            .Bend("shoulder.R", -19f, Near, Out).Bend("head", 1.5f, Near, Out)
                            .Bend("elbow.L", -4f, Near, Out).Bend("elbow.R", -10f, Near, Out)
                            .Aim("weapon", -22f, Near, Out).Aim("shield", 67f, Near, Out)
                            .Move("hips", new Vector2(RestHips.x, 0.026f));
                Resting(w, 2.600f, Out);
                return w.Write(Folder + "Idle.anim", 60f, loopTime: true).ToString();
            }
        }

        /// <summary>
        /// The resting pose: weapon hand hanging, blade angled down and forward, shield lowered off the
        /// body. Written at both ends of the loop so the cycle closes on itself rather than drifting.
        /// </summary>
        static void Resting(RigWriter w, float time, RigWriter.Ease ease)
        {
            w.At(time).Bend("torso", 0f, Near, ease).Bend("head", 0f, Near, ease)
                      .Bend("shoulder.R", -21f, Near, ease).Bend("shoulder.L", -12f, Near, ease)
                      .Bend("elbow.R", -8f, Near, ease).Bend("elbow.L", -6f, Near, ease)
                      .Aim("weapon", -25f, Near, ease).Aim("shield", 65f, Near, ease)
                      .Move("hips", RestHips);
        }

        /// <summary>
        /// Combat idle: the fourth state of the attack loop — the fighter holds his target and waits for his
        /// window. Not <see cref="Idle"/>, which now means "outside the attack loop" and shows a knight at
        /// rest with his blade down.
        ///
        /// The pose sits deliberately BETWEEN rest and the wind-up: blade already up but not yet gathered,
        /// weight settled, feet apart, knees soft. That placement is the whole point — an attack starting
        /// from here costs nothing and cannot pop, while the difference from Idle is legible at a glance.
        ///
        /// Breathing is shorter and shallower than at rest (0.9s against 1.1s, half the amplitude): a man
        /// braced to strike breathes tighter than a man standing about. Same asymmetry though — a symmetric
        /// loop reads as a machine.
        /// </summary>
        public static string CombatIdle()
        {
            using (var w = new RigWriter(Profile()))
            {
                Braced(w, 0f, Lin);
                // Вдох: грудь и плечи поднимаются, таз идёт вверх — амплитуда вдвое меньше покойной.
                w.At(0.900f).Bend("torso", -6.5f, Near, Soft).Bend("head", 5.5f, Near, Soft)
                            .Bend("shoulder.R", 40f, Near, Soft).Bend("shoulder.L", 24f, Near, Soft)
                            .Bend("elbow.R", 18f, Near, Soft).Bend("elbow.L", 26f, Near, Soft)
                            .Bend("hip.L", -6f, Near, Soft).Bend("hip.R", 8f, Near, Soft)
                            .Bend("knee.L", 10f, Near, Soft).Bend("knee.R", 12f, Near, Soft)
                            .Aim("weapon", 60f, Near, Soft).Aim("shield", 90f, Near, Soft)
                            .Move("hips", new Vector2(RestHips.x, 0.014f));
                Braced(w, 2.000f, Soft);
                return w.Write(Folder + "CombatIdle.anim", 60f, loopTime: true).ToString();
            }
        }

        /// <summary>
        /// The braced pose itself, written twice — at the top of the loop and at its end, so the cycle
        /// closes on the same values instead of drifting. Blade forward-up at 58 world degrees: past rest,
        /// short of the 105 the gathered blade holds.
        /// </summary>
        static void Braced(RigWriter w, float time, RigWriter.Ease ease)
        {
            w.At(time).Bend("torso", -5f, Near, ease).Bend("head", 4f, Near, ease)
                      .Bend("shoulder.R", 38f, Near, ease).Bend("shoulder.L", 22f, Near, ease)
                      .Bend("elbow.R", 18f, Near, ease).Bend("elbow.L", 26f, Near, ease)
                      .Bend("hip.L", -6f, Near, ease).Bend("hip.R", 8f, Near, ease)
                      .Bend("knee.L", 10f, Near, ease).Bend("knee.R", 12f, Near, ease)
                      .Aim("weapon", 58f, Near, ease).Aim("shield", 88f, Near, ease)
                      .Move("hips", new Vector2(RestHips.x, 0.008f));
        }

        /// <summary>
        /// Walk: two strides in 0.7s, four poses each — contact, down, passing, up.
        ///
        /// The hip swing is 34 degrees against the 22 it carried before, and that is a fix rather than a
        /// taste: the view paces the clip by ground covered, so a short stride forces the clip to spin fast
        /// to avoid sliding, and the knight ended up taking 5.6 steps a second. 34 degrees puts the stride
        /// near 0.85 world units, which at the Defender's speed is about three steps a second — a walk.
        /// The arms swing for the same reason: legs covering that much ground with still arms read as a doll
        /// being slid along.
        /// </summary>
        public static string Walk()
        {
            using (var w = new RigWriter(Profile()))
            {
                Stride(w, 0f, 0.7f, hipSwing: 34f, kneeLift: 34f, bobLow: 0.028f, bobHigh: 0.042f,
                       lean: -4f, armSwing: 12f);
                return w.Write(Folder + "Walk.anim", 60f, loopTime: true).ToString();
            }
        }

        /// <summary>
        /// Sprint: the same four-pose skeleton in 0.5s, but everything is bigger — longer stride, deeper
        /// knee, more lean, twice the bob. Same number of steps per cycle and the same phase as Walk, so
        /// the two blend without the legs popping.
        ///
        /// The blade rides UP through the whole run (decision by Max, 29.07): a knight closing distance
        /// carries it already half-gathered, so the charge that follows is one more pull rather than a
        /// wind-up from scratch. The pose is deliberately the one <see cref="AttackCharge"/> holds at
        /// 0.117s — shoulder 84, elbow 16, blade at 105 degrees — so entering the charge costs nothing and
        /// cannot pop. This is also why the raised arm keeps its own tiny swing instead of the locomotion
        /// one: an arm that pumps like the other would throw the blade around and read as flailing.
        /// </summary>
        public static string Sprint()
        {
            using (var w = new RigWriter(Profile()))
            {
                // kneeLift держится под лимитом сгиба колена из профиля (60): 62 давало переразгиб на
                // пролёте, и валидатор ловил его на обеих ногах.
                Stride(w, 0f, 0.5f, hipSwing: 39f, kneeLift: 58f, bobLow: 0.024f, bobHigh: 0.052f,
                       lean: -10f, armSwing: 14f, bladeGathered: true);
                return w.Write(Folder + "Sprint.anim", 60f, loopTime: true).ToString();
            }
        }

        /// <summary>
        /// Two strides of a locomotion cycle, on the canonical FOUR poses per step: contact, down, passing,
        /// up. The two-pose version this replaces (contact and passing only) is what made the knight mince:
        /// with no down and no up, the only place a stride could grow was the hip angle, and the pelvis had
        /// to dip at contact — where a real one is still falling — so every step read as short and busy.
        ///
        /// What each pose is FOR, because that is what makes a walk read as weight (Max, 29.07):
        /// <list type="bullet">
        /// <item><b>contact</b> — the heel lands. The front leg is nearly straight and the toe is up: a knee
        /// folded at contact is a leg that has already given way, and it costs the stride its length.</item>
        /// <item><b>down</b> — the weight arrives. The supporting knee folds to absorb it and the pelvis is
        /// at its LOWEST here, one beat after contact rather than on it.</item>
        /// <item><b>passing</b> — the support straightens under the body while the free leg folds through
        /// its highest knee. This is the pose the stride's height comes from.</item>
        /// <item><b>up</b> — the push-off. The rear leg extends behind with the toe pointed, the free leg
        /// reaches forward, and the pelvis is at its HIGHEST — the body is briefly thrown upward.</item>
        /// </list>
        /// The ankles carry the roll (toe up at contact, toe down at push-off). Without them the foot is a
        /// plank hinged at the knee, which is visible at any stride length and unmissable at a long one.
        /// </summary>
        static void Stride(RigWriter w, float start, float length, float hipSwing, float kneeLift,
                           float bobLow, float bobHigh, float lean, float armSwing, bool bladeGathered = false)
        {
            float half = length / 2f;      // one step
            float bobMid = (bobLow + bobHigh) * 0.5f;

            // How far the trailing leg goes back, as a share of the leading one's reach forward. Close to
            // one: a rear leg that barely extends is the other half of a short stride.
            const float RearShare = 0.85f;
            const float StraightKnee = 5f;   // "almost straight" — a locked leg reads as a stilt
            // Знаки переката ПРОВЕРЕНЫ гизмо, а не выведены из конвенции: на этом риге сгиб голеностопа
            // «в плюс» опускает нижнюю кромку сапога, то есть тянет носок ВНИЗ. Подошва при контакте
            // уходила под пол на 0.016 ровно из-за перевёрнутого знака.
            const float ToeUp = -10f;        // heel strike: носок вверх
            const float ToeDown = 20f;       // toe-off: носок вниз, отталкивание

            for (int step = 0; step < 2; step++)
            {
                // step 0 leads with the left leg, step 1 mirrors it
                float sign = step == 0 ? 1f : -1f;
                float contact = start + step * half;
                float down    = contact + half * 0.25f;
                float passing = contact + half * 0.5f;
                float up      = contact + half * 0.75f;

                // Front leg / rear leg for THIS step, by name, so the four poses read as choreography
                // rather than as a table of ternaries.
                string front = sign > 0f ? "L" : "R";
                string rear  = sign > 0f ? "R" : "L";

                w.At(contact).Bend($"hip.{front}", hipSwing, Near, Out).Bend($"hip.{rear}", -hipSwing * RearShare, Near, Out)
                             .Bend($"knee.{front}", StraightKnee, Near, Out).Bend($"knee.{rear}", kneeLift * 0.35f, Near, Out)
                             .Bend($"ankle.{front}", ToeUp, Near, Out).Bend($"ankle.{rear}", ToeDown * 0.5f, Near, Out)
                             .Bend("torso", lean, Near, Soft)
                             .Bend("head", -lean * 0.4f, Near, Soft)
                             .Move("hips", new Vector2(RestHips.x, bobMid));

                // The dip is PAID FOR by the knee. Lowering the pelvis without folding the supporting leg
                // pushes the sole through the floor — measured by RigStride at 0.021 rig units under the
                // ground line, right here at the down pose.
                w.At(down).Bend($"hip.{front}", hipSwing * 0.65f, Near, Soft).Bend($"hip.{rear}", -hipSwing * RearShare * 0.8f, Near, Soft)
                          .Bend($"knee.{front}", kneeLift * 0.75f, Near, Soft).Bend($"knee.{rear}", kneeLift * 0.5f, Near, Soft)
                          .Bend($"ankle.{front}", 0f, Near, Soft).Bend($"ankle.{rear}", ToeDown * 0.7f, Near, Soft)
                          .Bend("torso", lean * 1.15f, Near, Soft)
                          .Move("hips", new Vector2(RestHips.x, bobLow));

                w.At(passing).Bend($"hip.{front}", hipSwing * 0.1f, Near, Lin).Bend($"hip.{rear}", -hipSwing * 0.3f, Near, Lin)
                             .Bend($"knee.{front}", StraightKnee, Near, Lin).Bend($"knee.{rear}", kneeLift, Near, Lin)
                             .Bend($"ankle.{front}", ToeDown * 0.25f, Near, Lin).Bend($"ankle.{rear}", ToeUp * 0.5f, Near, Lin)
                             .Bend("torso", lean, Near, Soft)
                             .Move("hips", new Vector2(RestHips.x, bobMid));

                w.At(up).Bend($"hip.{front}", -hipSwing * RearShare * 0.55f, Near, Soft).Bend($"hip.{rear}", hipSwing * 0.6f, Near, Soft)
                        .Bend($"knee.{front}", StraightKnee * 1.4f, Near, Soft).Bend($"knee.{rear}", kneeLift * 0.55f, Near, Soft)
                        .Bend($"ankle.{front}", ToeDown, Near, Soft).Bend($"ankle.{rear}", ToeUp * 0.6f, Near, Soft)
                        .Bend("torso", lean * 0.9f, Near, Soft)
                        .Move("hips", new Vector2(RestHips.x, bobHigh));

                // Arms swing AGAINST the legs, and they swing properly: a knight whose arms hang still
                // while his legs cover a metre reads as a doll being slid along the ground. The shield arm
                // is free, so it carries the full swing.
                w.At(contact).Bend("shoulder.L", -armSwing * sign, Near, Soft);
                w.At(passing).Bend("shoulder.L", 0f, Near, Soft);

                // The weapon arm either pumps with the run or holds the blade gathered. Held, it keeps a
                // small counter-swing (a third of the other arm, against it) so the pose breathes with the
                // stride instead of looking welded to the shoulder.
                if (bladeGathered)
                    w.At(contact).Bend("shoulder.R", BladeGatheredShoulder - armSwing * 0.35f * sign, Near, Soft)
                                 .Bend("elbow.R", BladeGatheredElbow, Near, Soft)
                                 .Aim("weapon", BladeGatheredAim, Near, Soft);
                else
                    w.At(contact).Bend("shoulder.R", armSwing * sign, Near, Soft);
            }

            // Close the loop on the first pose so the cycle does not jump.
            w.At(start + length).Bend("hip.L", hipSwing, Near, Out).Bend("hip.R", -hipSwing * RearShare, Near, Out)
                                .Bend("knee.L", StraightKnee, Near, Out).Bend("knee.R", kneeLift * 0.35f, Near, Out)
                                .Bend("ankle.L", ToeUp, Near, Out).Bend("ankle.R", ToeDown * 0.5f, Near, Out)
                                .Bend("torso", lean, Near, Soft)
                                .Bend("shoulder.L", -armSwing, Near, Soft)
                                .Bend("head", -lean * 0.4f, Near, Soft)
                                .Move("hips", new Vector2(RestHips.x, bobMid));

            if (bladeGathered)
                w.At(start + length).Bend("shoulder.R", BladeGatheredShoulder - armSwing * 0.35f, Near, Soft)
                                    .Bend("elbow.R", BladeGatheredElbow, Near, Soft)
                                    .Aim("weapon", BladeGatheredAim, Near, Soft);
            else
                w.At(start + length).Bend("shoulder.R", armSwing, Near, Soft);
        }

        /// <summary>
        /// Stunned: the guard is gone. Sword and shield hang, the knees give a little, the torso folds
        /// forward — a body that has stopped holding itself up. The head rolls a slow circle, which is the
        /// only part that keeps moving, so the pose reads as dazed rather than as dead.
        ///
        /// It loops on 1.2s because control lasts as long as it lasts, and the head must arrive back where
        /// it started or the loop clicks. Arms hang a touch out of phase with the head (the sword lags by a
        /// quarter of the cycle) — a body swaying all in one piece looks like a puppet on one string.
        /// </summary>
        public static string Stun()
        {
            using (var w = new RigWriter(Profile()))
            {
                // Collapse into the daze: everything drops in the first fifth of a second, fast, because a
                // stun that eases in reads as a stumble the unit chose.
                w.At(0.2f).Bend("torso", 12f, Near, Out).Bend("head", 16f, Near, Out)
                          .Bend("shoulder.R", -34f, Near, Out).Bend("elbow.R", 10f, Near, Out)
                          .Bend("shoulder.L", -28f, Near, Out).Bend("elbow.L", 8f, Near, Out)
                          .Bend("knee.L", 14f, Near, Out).Bend("knee.R", 10f, Near, Out)
                          .Aim("weapon", -40f, Near, Out).Aim("shield", -28f, Near, Out)
                          .Move("hips", new Vector2(RestHips.x, 0.004f));

                // The head rolls: right, down-forward, left, back. Four poses, so the circle is a circle
                // and not a wiper blade.
                w.At(0.45f).Bend("head", 2f, Near, Soft).Bend("torso", 15f, Near, Soft)
                           .Aim("weapon", -46f, Near, Soft).Aim("shield", -22f, Near, Soft);
                w.At(0.70f).Bend("head", -14f, Near, Soft).Bend("torso", 12f, Near, Soft)
                           .Aim("weapon", -34f, Near, Soft).Aim("shield", -34f, Near, Soft);
                w.At(0.95f).Bend("head", 2f, Near, Soft).Bend("torso", 9f, Near, Soft)
                           .Aim("weapon", -40f, Near, Soft).Aim("shield", -26f, Near, Soft);

                // Close on the entry pose so the loop does not click.
                w.At(1.2f).Bend("torso", 12f, Near, Soft).Bend("head", 16f, Near, Soft)
                          .Bend("shoulder.R", -34f, Near, Soft).Bend("elbow.R", 10f, Near, Soft)
                          .Bend("shoulder.L", -28f, Near, Soft).Bend("elbow.L", 8f, Near, Soft)
                          .Bend("knee.L", 14f, Near, Soft).Bend("knee.R", 10f, Near, Soft)
                          .Aim("weapon", -40f, Near, Soft).Aim("shield", -28f, Near, Soft)
                          .Move("hips", new Vector2(RestHips.x, 0.004f));

                return w.Write(Folder + "Stun.anim", 60f, loopTime: true).ToString();
            }
        }

        /// <summary>
        /// Freezes the whole rig until <paramref name="until"/>. One end for every bone, because a pause
        /// where bones leave at different times is not a pause.
        /// </summary>
        static void HoldUntil(RigWriter w, float until) => w.HoldUntil(until);

        /// <summary>Bring both controllers in line with the clips that now exist. See BoneUnitControllerBuilder.</summary>
        static string[] RebuildControllers() => new[]
        {
            BoneUnitControllerBuilder.Rebuild("BoneUnit_Combat"),
            BoneUnitControllerBuilder.Rebuild("BoneUnit_Standart"),
        };

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
            log.AppendLine(CombatIdle());
            log.AppendLine(Walk());
            log.AppendLine(Sprint());
            log.AppendLine(Stun());

            // A clip nobody can reach is the same as a clip that does not exist — that is how the sprint,
            // the charge and the stun each went missing in turn. Rebuilding the recipes therefore rebuilds
            // the controllers too, in BOTH of them: the combat one and the stand Max actually looks at.
            foreach (string line in RebuildControllers()) log.AppendLine(line);
            Debug.Log(log.ToString());
        }
    }
}
#endif
