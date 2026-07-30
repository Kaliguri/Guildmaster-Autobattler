using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Разбег на дальнем подходе. Тесты держат ЗАМЫСЕЛ, а не реализацию: ускорение обязано жить в
    /// симуляции (позиция за тик реально больше), порог обязан считаться от зазора сверх досягаемости
    /// (иначе стрелок бежит вечно), полоса гистерезиса — гасить мигание на границе, а сам разгон —
    /// занимать время: юнит сперва идёт шагом и только потом переходит на бег.
    /// </summary>
    public sealed class SprintTests
    {
        private const float Reach = 2f;   // досягаемость обоих: дальше неё и начинается зазор

        // Сколько тиков нужно, чтобы разгон только начался и чтобы он дошёл до полного.
        private static int TicksToRampStart(in SimTuning t)
            => Mathf.CeilToInt(t.SprintWalkSeconds * SimConstants.TickRate) + 1;
        private static int TicksToFullRamp(in SimTuning t)
            => Mathf.CeilToInt((t.SprintWalkSeconds + t.SprintRampSeconds) * SimConstants.TickRate) + 1;

        private static RuntimeUnit Make(Vector2 pos, float moveSpeed, float attackRange = Reach)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.AttackRange, ModifierOp.Flat, attackRange),
                new StatModifier(StatType.MoveSpeed,   ModifierOp.Flat, moveSpeed),
                new StatModifier(StatType.Size,        ModifierOp.Flat, 1f),
                new StatModifier(StatType.MaxHP,       ModifierOp.Flat, 100f),
            });
            return new RuntimeUnit
            {
                Stats = stats, Position = pos, PreviousPosition = pos,
                Positioning = PositioningIntent.Approach, CurrentHP = 100f,
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
        }

        // Один тик движения пары «преследователь → цель». Возвращает пройденное преследователем расстояние.
        private static float StepDistance(RuntimeUnit chaser, RuntimeUnit target, in SimTuning tuning)
        {
            var units = new List<RuntimeUnit> { chaser, target };
            Vector2 before = chaser.Position;
            new MovementSystem().Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);
            return (chaser.Position - before).magnitude;
        }

        [Test]
        public void FarTarget_SprintRaisesActualStep()
        {
            SimTuning tuning = SimTuning.Default;
            var chaser = Make(new Vector2(-20f, 0f), moveSpeed: 3f);
            var target = Make(new Vector2(20f, 0f),  moveSpeed: 0f);
            chaser.CurrentTarget = target;

            // Разгон занимает время — меряем шаг, когда он уже набран полностью.
            for (int i = 0; i < TicksToFullRamp(in tuning); i++) StepDistance(chaser, target, in tuning);
            float step = StepDistance(chaser, target, in tuning);

            Assert.That(chaser.SprintRamp, Is.EqualTo(1f).Within(1e-4f), "Цель далеко — разгон обязан дойти до полного.");
            float walk = 3f * SimConstants.TickDelta;
            Assert.That(step, Is.EqualTo(walk * tuning.SprintSpeedMult).Within(1e-4f),
                "Ускорение должно двигать ПОЗИЦИЮ: разбег, которого нет в симуляции, анимация обгонит.");
        }

        [Test]
        public void FirstSecond_RunsAtPlainWalkingSpeed()
        {
            SimTuning tuning = SimTuning.Default;
            var chaser = Make(new Vector2(-20f, 0f), moveSpeed: 3f);
            var target = Make(new Vector2(20f, 0f),  moveSpeed: 0f);
            chaser.CurrentTarget = target;

            float walk = 3f * SimConstants.TickDelta;
            // Вся «прогулочная» часть: юнит уже хочет бежать, но идёт обычным шагом.
            for (int i = 0; i < (int)(tuning.SprintWalkSeconds * SimConstants.TickRate); i++)
            {
                float step = StepDistance(chaser, target, in tuning);
                Assert.That(step, Is.EqualTo(walk).Within(1e-4f),
                    "Первые секунды юнит идёт обычным шагом: прибавка, включённая щелчком, читается как телепорт.");
                Assert.That(chaser.IsSprinting, Is.False, "И показ всё это время обязан видеть шаг, а не бег.");
            }
        }

        [Test]
        public void Ramp_ClimbsToFullInsteadOfSnapping()
        {
            SimTuning tuning = SimTuning.Default;
            var chaser = Make(new Vector2(-40f, 0f), moveSpeed: 3f);
            var target = Make(new Vector2(40f, 0f),  moveSpeed: 0f);
            chaser.CurrentTarget = target;

            for (int i = 0; i < TicksToRampStart(in tuning); i++) StepDistance(chaser, target, in tuning);
            float began = chaser.SprintRamp;

            Assert.That(began, Is.GreaterThan(0f), "После прогулочной части разгон обязан начаться.");
            Assert.That(began, Is.LessThan(1f), "И начаться ЧАСТИЧНО — иначе это тот же щелчок, просто позже.");

            float previous = began;
            for (int i = 0; i < 5; i++)
            {
                StepDistance(chaser, target, in tuning);
                Assert.That(chaser.SprintRamp, Is.GreaterThan(previous), "Разгон обязан расти каждый тик.");
                previous = chaser.SprintRamp;
            }

            for (int i = 0; i < TicksToFullRamp(in tuning); i++) StepDistance(chaser, target, in tuning);
            Assert.That(chaser.SprintRamp, Is.EqualTo(1f).Within(1e-4f), "И дойти до полной прибавки.");
        }

        [Test]
        public void InterruptedApproach_RestartsTheRampFromScratch()
        {
            SimTuning tuning = SimTuning.Default;
            var chaser = Make(new Vector2(-40f, 0f), moveSpeed: 3f);
            var target = Make(new Vector2(40f, 0f),  moveSpeed: 0f);
            chaser.CurrentTarget = target;

            for (int i = 0; i < TicksToFullRamp(in tuning); i++) StepDistance(chaser, target, in tuning);
            Assert.That(chaser.SprintRamp, Is.EqualTo(1f).Within(1e-4f));

            // Юнита выбило из подхода (замах, контроль, потеря цели — здесь корень).
            chaser.CanMove = false;
            StepDistance(chaser, target, in tuning);
            Assert.That(chaser.SprintRamp, Is.EqualTo(0f), "Любая причина не бежать обнуляет разгон.");

            chaser.CanMove = true;
            StepDistance(chaser, target, in tuning);
            Assert.That(chaser.SprintRamp, Is.EqualTo(0f),
                "И следующий разбег начинается заново: иначе юнит копил бы разгон, пока стоял в замахе.");
        }

        [Test]
        public void ArrivedAtReach_SprintOff()
        {
            SimTuning tuning = SimTuning.Default;
            var chaser = Make(new Vector2(-20f, 0f), moveSpeed: 3f);
            var target = Make(new Vector2(20f, 0f),  moveSpeed: 0f);
            chaser.CurrentTarget = target;

            // Гоним до упора: в конце подхода юнит обязан выйти из разбега сам.
            for (int i = 0; i < 600; i++) StepDistance(chaser, target, in tuning);

            Assert.That(chaser.IsSprinting, Is.False, "Добежал — разбег кончился.");
        }

        [Test]
        public void InsideHysteresisBand_KeepsPreviousDecision()
        {
            SimTuning tuning = SimTuning.Default;
            // Зазор ровно между порогами выхода и входа: решение не меняется ни в одну сторону.
            float gap = (tuning.SprintEnterGap + tuning.SprintExitGap) * 0.5f;
            var target = Make(new Vector2(0f, 0f), moveSpeed: 0f);
            var chaser = Make(new Vector2(-1f, 0f), moveSpeed: 0f); // скорость 0 — позиция не плывёт
            chaser.CurrentTarget = target;

            // Досягаемость body-aware (радиус атаки + оба тела), поэтому ставим по ней, а не по AttackRange:
            // иначе «зазор» теста разошёлся бы с зазором, который считает движение.
            float reach = CombatPositioning.AttackReachCenter(chaser, target, in tuning);
            chaser.Position = chaser.PreviousPosition = new Vector2(-(reach + gap), 0f);

            // Гистерезис живёт на НАМЕРЕНИИ бежать, а не на набранной скорости: разгон только следствие.
            chaser.SprintWantTicks = 10;
            StepDistance(chaser, target, in tuning);
            Assert.That(chaser.SprintWantTicks, Is.GreaterThan(10), "В полосе гистерезиса бежавший продолжает бежать.");

            chaser.StopSprint();
            StepDistance(chaser, target, in tuning);
            Assert.That(chaser.SprintWantTicks, Is.Zero, "В той же полосе стоявший не срывается в разбег.");
        }

        [Test]
        public void LongRangeUnit_DoesNotSprintAtItsOwnFiringDistance()
        {
            SimTuning tuning = SimTuning.Default;
            // Стрелок на своей рабочей дистанции: сырое расстояние огромно, а зазор нулевой.
            var target = Make(new Vector2(0f, 0f), moveSpeed: 0f);
            var chaser = Make(new Vector2(-8f, 0f), moveSpeed: 3f, attackRange: 8f);
            chaser.CurrentTarget = target;

            for (int i = 0; i < TicksToFullRamp(in tuning); i++) StepDistance(chaser, target, in tuning);

            Assert.That(chaser.SprintWantTicks, Is.Zero,
                "Порог считается от зазора сверх досягаемости: по сырой дистанции стрелок бежал бы всегда.");
        }

        [Test]
        public void ArrivingFromSprint_ArmsChargedAttack()
        {
            SimTuning tuning = SimTuning.Default;
            var chaser = Make(new Vector2(-20f, 0f), moveSpeed: 3f);
            var target = Make(new Vector2(20f, 0f),  moveSpeed: 0f);
            chaser.CurrentTarget = target;

            for (int i = 0; i < 600; i++) StepDistance(chaser, target, in tuning);

            Assert.That(chaser.IsSprinting, Is.False);
            Assert.That(chaser.ChargedAttackReady, Is.True,
                "Разбег, кончившийся прибытием, обязан оставить заряд на первый удар.");
        }

        [Test]
        public void LeavingForAnotherRun_DisarmsChargedAttack()
        {
            SimTuning tuning = SimTuning.Default;
            var target = Make(new Vector2(0f, 0f), moveSpeed: 0f);
            var chaser = Make(new Vector2(-1f, 0f), moveSpeed: 0f);
            chaser.CurrentTarget = target;
            chaser.ChargedAttackReady = true;   // добежал в прошлый раз

            // Цель ушла далеко — юнит снова в пути, и заряд прошлого сближения недействителен.
            float reach = CombatPositioning.AttackReachCenter(chaser, target, in tuning);
            chaser.Position = chaser.PreviousPosition = new Vector2(-(reach + tuning.SprintEnterGap + 5f), 0f);
            StepDistance(chaser, target, in tuning);

            Assert.That(chaser.SprintWantTicks, Is.GreaterThan(0));
            Assert.That(chaser.ChargedAttackReady, Is.False,
                "Удар с разбега принадлежит тому сближению, которым добыт.");
        }

        [Test]
        public void ShortDash_DoesNotBuyAChargedAttack()
        {
            SimTuning tuning = SimTuning.Default;
            var target = Make(new Vector2(0f, 0f), moveSpeed: 0f);
            var chaser = Make(new Vector2(-1f, 0f), moveSpeed: 3f);
            chaser.CurrentTarget = target;

            // Цель чуть дальше порога: юнит сорвётся с места, но упрётся в неё раньше, чем разгонится.
            float reach = CombatPositioning.AttackReachCenter(chaser, target, in tuning);
            chaser.Position = chaser.PreviousPosition = new Vector2(-(reach + tuning.SprintEnterGap + 0.2f), 0f);

            for (int i = 0; i < 60; i++) StepDistance(chaser, target, in tuning);

            Assert.That(chaser.SprintRamp, Is.EqualTo(0f), "Добежал раньше, чем разогнался.");
            Assert.That(chaser.ChargedAttackReady, Is.False,
                "Особый удар покупается РАЗГОНОМ, а не тем, что до цели было чуть далеко.");
        }

        [Test]
        public void ChargedSwing_SurvivesInTheSnapshotForTheWholeSwing()
        {
            // Regress: заряд гаснет в ТОМ ЖЕ тике, в котором взводится (движение ставит при прибытии,
            // авто-атака тем же тиком входит в замах), а снимок ленты снимается ПОСЛЕ тика. Показ читал
            // погасший заряд и играл разбег обычной атакой — «удар в рывке не срабатывает».
            var unit = Make(Vector2.zero, moveSpeed: 3f);
            unit.ChargedAttackReady = true;

            // То, что делает EnterWindup: заряд переезжает на свинг и тратится.
            unit.ChargedSwing       = unit.ChargedAttackReady;
            unit.ChargedAttackReady = false;
            unit.Phase = AttackPhase.Windup;

            var shot = Guildmaster.Combat.Tape.UnitSnapshot.From(unit);
            Assert.That(shot.ChargedSwing, Is.True,
                "Снимок обязан нести признак СВИНГА: заряд к моменту съёмки уже потрачен всегда.");
        }

        [Test]
        public void ChargedAttack_UsesItsOwnWindupLength()
        {
            // Разные множители дают разную длину замаха — и обычный удар остаётся эталоном.
            const int hitFrame = 5, frames = 10, interval = 30;
            int normal = AttackTiming.WindupTicks(hitFrame, frames, interval);
            int longer = AttackTiming.WindupTicks(hitFrame, frames, interval, 1.5f);
            int shorter = AttackTiming.WindupTicks(hitFrame, frames, interval, 0.6f);

            Assert.That(longer,  Is.GreaterThan(normal), "Множитель > 1 удлиняет замах (телеграф дольше).");
            Assert.That(shorter, Is.LessThan(normal),    "Множитель < 1 укорачивает замах (выпад на скорости).");
            Assert.That(AttackTiming.WindupTicks(hitFrame, frames, interval, 1f), Is.EqualTo(normal),
                "Множитель 1 обязан не менять ничего: у юнитов без разбег-атаки тайминг не должен ехать.");
        }

        [Test]
        public void WindupShare_GivesSkeletalUnitsARealWindup()
        {
            const int interval = 55;   // ≈ 0.55 атак/сек: интервал 1.8 с

            // Без кадров и без доли расчёт падает на телеграф-пол: замах 3 тика при интервале 55.
            int floorOnly = AttackTiming.WindupTicks(0, 0, interval);
            Assert.That(floorOnly, Is.EqualTo(SimConstants.MinWindupTicks),
                "Юнит без кадров сейчас получает минимальный замах — это и есть «удар прилетает мгновенно».");

            int fromShare = AttackTiming.WindupTicksFromShare(0.45f, interval);
            Assert.That(fromShare, Is.GreaterThan(floorOnly * 3),
                "Доля из данных обязана дать замах, который видно: иначе клип скрабится в десятую долю секунды.");
        }

        [Test]
        public void WindupShare_ObeysTheSameClampsAsFrames()
        {
            const int interval = 6;
            Assert.That(AttackTiming.WindupTicksFromShare(1f, interval), Is.LessThanOrEqualTo(interval - 1),
                "Замах из доли не имеет права налезть на следующий удар.");
            Assert.That(AttackTiming.WindupTicksFromShare(0.001f, interval),
                Is.GreaterThanOrEqualTo(Mathf.Min(SimConstants.MinWindupTicks, interval - 1)),
                "И не имеет права опуститься ниже телеграф-пола.");
        }

        [Test]
        public void ChargedWindup_StaysWithinItsClamps()
        {
            const int hitFrame = 9, frames = 10, interval = 12;
            int huge = AttackTiming.WindupTicks(hitFrame, frames, interval, 10f);
            int tiny = AttackTiming.WindupTicks(hitFrame, frames, interval, 0.01f);

            Assert.That(huge, Is.LessThanOrEqualTo(interval - 1),
                "Замах с разбега не имеет права налезть на следующий удар.");
            Assert.That(tiny, Is.GreaterThanOrEqualTo(SimConstants.MinWindupTicks),
                "И не имеет права опуститься ниже телеграф-пола: удар без телеграфа нечитаем.");
        }

        [Test]
        public void DeadUnit_ClearsSprint()
        {
            SimTuning tuning = SimTuning.Default;
            var target = Make(new Vector2(20f, 0f), moveSpeed: 0f);
            var chaser = Make(new Vector2(-20f, 0f), moveSpeed: 3f);
            chaser.CurrentTarget = target;

            for (int i = 0; i < TicksToFullRamp(in tuning); i++) StepDistance(chaser, target, in tuning);
            Assert.That(chaser.IsSprinting, Is.True);

            chaser.CurrentHP = 0f;
            chaser.IsDead    = true;   // умер посреди разбега
            StepDistance(chaser, target, in tuning);

            Assert.That(chaser.IsSprinting, Is.False, "Разбег не должен переживать смерть — показ поверит.");
        }
    }
}
