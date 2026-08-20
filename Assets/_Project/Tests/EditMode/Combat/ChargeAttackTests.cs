using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Удар с разбега как ВЪЕЗД: замах начинается ЗА границей досягаемости, чтобы кадр контакта пришёлся
    /// на момент, когда дистанции хватает для удара. Инвариант живёт между тремя файлами — гейт
    /// (<c>AutoAttackSystem</c>), предсказание (<c>CombatPositioning</c>) и рут (<c>MovementSystem</c>), —
    /// поэтому его держит тест, а не комментарий: нарушить его можно из любого из трёх, и две другие
    /// стороны шва об этом не узнают.
    /// <para>
    /// Что здесь проверяется по существу: замах СТАРТУЕТ вне досягаемости, юнит на нём НЕ зарутован, удар
    /// к концу замаха ЗАСЧИТЫВАЕТСЯ, а обычная атака и стрелок остаются с прежним поведением. Всё это
    /// вместе и есть «удар с разбега», а не «добежал, встал, ударил».
    /// </para>
    /// </summary>
    public sealed class ChargeAttackTests
    {
        // Скорость атаки 1 → интервал 30 тиков; доля замаха 0.45 → 14 тиков; разбег ×1.3 → 18 тиков.
        private const float WindupShare = 0.45f;
        private const float ChargeMult  = 1.3f;
        private const float MoveSpeed   = 3f;
        private const float Range       = 1f;   // мили: reach центров = 1 + 0.3 + 0.3 = 1.6

        // ===================== Предикат въезда =====================

        [Test]
        public void CanCloseIntoReach_TrueOnlyWhenTheRunActuallyArrives()
        {
            SimTuning tuning = SimTuning.Default;
            var target  = MakeUnit(pos: Vector2.zero, team: 1);
            var chaser  = MakeUnit(pos: new Vector2(-4f, 0f), team: 0);
            chaser.CurrentTarget = target;

            // Досягаемость СЧИТАЕМ, а не предполагаем: она body-aware (радиус атаки + оба тела), и своя
            // арифметика в тесте разошлась бы с той, по которой решает гейт.
            float reach = CombatPositioning.AttackReachCenter(chaser, target, in tuning);
            const int windup = 18;
            const float step = 0.13f;               // ед./тик на скорости разбега
            float exact = reach + step * windup;    // дистанция, которую замах закрывает ровно

            // Дальше, чем замах способен закрыть: доехать не успеет — значит и начинать нельзя.
            Place(chaser, exact + 0.5f, step);
            Assert.IsFalse(CombatPositioning.CanCloseIntoReach(chaser, target, windup, in tuning),
                "Слишком далеко: за замах не доедет — замах начинать нельзя, иначе удар вхолостую.");

            // Та же скорость, но дистанция уже на «тормозном пути» замаха.
            Place(chaser, exact - 0.01f, step);
            Assert.IsTrue(CombatPositioning.CanCloseIntoReach(chaser, target, windup, in tuning),
                "Дистанция ровно в замах — это и есть момент начать удар с разбега.");

            // Стоит на месте — въезжать нечем, сколько бы ни было места.
            Place(chaser, exact - 0.01f, stepPerTick: 0f);
            Assert.IsFalse(CombatPositioning.CanCloseIntoReach(chaser, target, windup, in tuning),
                "Стоящий юнит не въезжает: закрывать остаток дистанции нечем.");

            // Уже в досягаемости — это обычный гейт, второго пути к нему быть не должно.
            Place(chaser, reach * 0.9f, step);
            Assert.IsFalse(CombatPositioning.CanCloseIntoReach(chaser, target, windup, in tuning),
                "Цель в досягаемости — решает InAttackRange, а не въезд.");
        }

        [Test]
        public void CanCloseIntoReach_DoesNotCountTheTargetRunningIn()
        {
            // Пессимизм расчёта: полная относительная скорость удваивала бы дистанцию старта в сшибке
            // двух линий, и стоило цели затормозить (её собственный замах рутует её), юнит не доезжал
            // половину пути и бил по воздуху. Своё движение — да, чужое навстречу — нет.
            SimTuning tuning = SimTuning.Default;
            var target = MakeUnit(pos: new Vector2(4f, 0f), team: 1);
            var chaser = MakeUnit(pos: Vector2.zero, team: 0);
            chaser.CurrentTarget = target;

            chaser.PreviousPosition = chaser.Position;                 // сам стоит
            target.PreviousPosition = new Vector2(4.5f, 0f);           // цель несётся навстречу

            Assert.IsFalse(CombatPositioning.CanCloseIntoReach(chaser, target, 18, in tuning),
                "Въезд считается по СВОЕМУ ходу: чужое сближение не гарантирует, что цель не встанет.");
        }

        // ===================== Заряд =====================

        [Test]
        public void FullRamp_ArmsTheChargeWhileStillRunning()
        {
            SimTuning tuning = SimTuning.Default;
            var (chaser, target, units) = Scene(chaserX: -20f);
            var move = new MovementSystem();

            // Гоним ровно до полного разгона — юнит ещё далеко от цели.
            int ticks = Mathf.CeilToInt((tuning.SprintWalkSeconds + tuning.SprintRampSeconds) * SimConstants.TickRate) + 1;
            for (int i = 0; i < ticks; i++) move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);

            float reach = CombatPositioning.AttackReachCenter(chaser, target, in tuning);
            Assert.That((target.Position - chaser.Position).magnitude, Is.GreaterThan(reach + 1f),
                "Предусловие: юнит ещё в пути, а не у цели.");
            Assert.That(chaser.SprintRamp, Is.EqualTo(1f).Within(1e-4f));
            Assert.IsTrue(chaser.ChargedAttackReady,
                "Заряд покупается РАЗГОНОМ и обязан существовать ещё в беге: въездной замах стартует до прибытия.");
        }

        /// <summary>
        /// Прибытие ГАСИТ заряд (решение Макса 31.07.2026). Удар с разбега — это удар на ходу: он
        /// покупается въездом в досягаемость, а не тем, что разгон когда-то был полным. Пока заряд
        /// переживал остановку, юнит, добежавший и вставший, играл клип рывка стоя на месте и получал
        /// укороченный замах — подача спорила с тем, что видно глазами.
        /// </summary>
        [Test]
        public void ArrivingFromSprint_SpendsTheCharge()
        {
            SimTuning tuning = SimTuning.Default;
            var (chaser, target, units) = Scene(chaserX: -20f);
            var move = new MovementSystem();

            for (int i = 0; i < 600; i++) move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);

            Assert.IsFalse(chaser.IsSprinting, "Добежал — разбег кончился.");
            Assert.IsFalse(chaser.ChargedAttackReady, "И заряд кончился вместе с ним: стоящий бьёт обычным ударом.");
        }

        [Test]
        public void SprintBrokenByControl_LosesTheCharge()
        {
            SimTuning tuning = SimTuning.Default;
            var (chaser, target, units) = Scene(chaserX: -20f);
            var move = new MovementSystem();

            int ticks = Mathf.CeilToInt((tuning.SprintWalkSeconds + tuning.SprintRampSeconds) * SimConstants.TickRate) + 1;
            for (int i = 0; i < ticks; i++) move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);
            Assert.IsTrue(chaser.ChargedAttackReady, "Предусловие: заряд набран.");

            chaser.CanMove = false;   // сбили с разбега
            move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);

            Assert.IsFalse(chaser.ChargedAttackReady,
                "Особый удар покупается доведённым до конца сближением: сбитый с разбега его не получает.");
        }

        // ===================== Въезд в бою =====================

        [Test]
        public void ChargedWindup_StartsOutsideReach_AndLandsTheHit()
        {
            SimTuning tuning = SimTuning.Default;
            var (chaser, target, units) = Scene(chaserX: -20f);
            var move = new MovementSystem();
            var attack = new AutoAttackSystem();
            var ctx = new MockCombatContext();

            float reach = CombatPositioning.AttackReachCenter(chaser, target, in tuning);
            float distAtWindupStart = -1f;

            for (int i = 0; i < 400 && ctx.DamageCalls.Count == 0; i++)
            {
                move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);
                attack.Tick(units, ctx, SimConstants.TickDelta);

                if (distAtWindupStart < 0f && chaser.IsWindingUp)
                    distAtWindupStart = (target.Position - chaser.Position).magnitude;
            }

            Assert.That(distAtWindupStart, Is.GreaterThan(reach),
                "Замах удара с разбега обязан стартовать ЗА границей досягаемости — иначе это «добежал, встал, ударил».");
            Assert.That(ctx.DamageCalls.Count, Is.EqualTo(1),
                "И к кадру контакта удар обязан ЗАСЧИТАТЬСЯ: въезд, который не доезжает, — это промах.");
            Assert.That((target.Position - chaser.Position).magnitude,
                Is.LessThanOrEqualTo(reach + SimConstants.AttackReachTolerance),
                "К моменту удара юнит обязан быть в досягаемости: кадр удара и есть момент, когда её хватило.");
        }

        [Test]
        public void ChargedWindup_IsNotRooted()
        {
            SimTuning tuning = SimTuning.Default;
            var (chaser, target, units) = Scene(chaserX: -20f);
            var move = new MovementSystem();
            var attack = new AutoAttackSystem();
            var ctx = new MockCombatContext();

            // Доводим до старта заряженного замаха.
            for (int i = 0; i < 400 && !chaser.IsWindingUp; i++)
            {
                move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);
                attack.Tick(units, ctx, SimConstants.TickDelta);
            }
            Assert.IsTrue(chaser.IsWindingUp, "Предусловие: замах начат.");
            Assert.IsTrue(chaser.ChargedSwing, "И это именно свинг с разбега.");

            Vector2 before = chaser.Position;
            move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);

            Assert.That((chaser.Position - before).magnitude, Is.GreaterThan(1e-4f),
                "На заряженном замахе рута нет: остаток дистанции закрывается ходом, иначе замах уйдёт в пустоту.");
            Assert.That(chaser.SprintRamp, Is.EqualTo(1f).Within(1e-4f),
                "И разбег на нём заморожен: упавшая скорость увела бы удар мимо расчёта гейта.");
        }

        /// <summary>
        /// Въезд полагается КАЖДОМУ подбегающему мили, а не только разогнавшемуся (решение Макса
        /// 31.07.2026: «атака должна случаться сразу при достижении нужной дистанции»). Юнит, который не
        /// успел набрать разбег, всё равно начинает замах за границей досягаемости — иначе он добегает,
        /// тормозит и бьёт заметно позже того момента, в который удар выглядит заслуженным.
        /// </summary>
        [Test]
        public void PlainRunner_AlsoStartsItsWindupOutsideReach()
        {
            SimTuning tuning = SimTuning.Default;
            var (chaser, target, units) = Scene(chaserX: -3.5f);
            var move = new MovementSystem();
            var attack = new AutoAttackSystem();
            var ctx = new MockCombatContext();

            float reach = CombatPositioning.AttackReachCenter(chaser, target, in tuning);
            float distAtWindupStart = -1f;

            for (int i = 0; i < 400 && distAtWindupStart < 0f; i++)
            {
                move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);
                attack.Tick(units, ctx, SimConstants.TickDelta);
                if (chaser.IsWindingUp) distAtWindupStart = (target.Position - chaser.Position).magnitude;
            }

            Assert.That(distAtWindupStart, Is.GreaterThan(0f), "Предусловие: юнит дошёл и начал замах.");
            Assert.IsFalse(chaser.ChargedSwing, "Предусловие: разбега он не набрал — это обычный удар.");
            Assert.IsTrue(chaser.ChargingIn, "Обычный замах подбегающего — тоже въезд.");
            Assert.That(distAtWindupStart, Is.GreaterThan(reach),
                "Замах обязан стартовать ЗА границей досягаемости, чтобы удар пришёлся на вход в неё.");
        }

        [Test]
        public void StandingUnit_StaysRooted()
        {
            // Контроль: свинг с места по-прежнему рутует. Въезд достаётся тому, кто ЕДЕТ, а не всякому.
            SimTuning tuning = SimTuning.Default;
            var (chaser, target, units) = Scene(chaserX: -1.2f);
            var move = new MovementSystem();
            var attack = new AutoAttackSystem();
            var ctx = new MockCombatContext();

            // Цель уже в досягаемости: ехать некуда, значит и въезда нет — обычный свинг с места.
            for (int i = 0; i < 120 && !chaser.IsWindingUp; i++)
            {
                move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);
                attack.Tick(units, ctx, SimConstants.TickDelta);
            }
            Assert.IsTrue(chaser.IsWindingUp, "Предусловие: замах начат.");
            Assert.IsFalse(chaser.ChargedSwing, "И он обычный: разгона юнит не набрал.");
            Assert.IsFalse(chaser.ChargingIn, "И не въездной: юнит уже стоял в досягаемости.");

            Vector2 before = chaser.Position;
            move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);

            Assert.That((chaser.Position - before).magnitude, Is.LessThan(1e-4f),
                "Свинг с места по-прежнему рутует: это базовое поведение мили.");
        }

        [Test]
        public void RangedUnit_DoesNotChargeIntoReach()
        {
            // Стрелку въезд не положен: его порог разбега считается от зазора сверх досягаемости именно
            // потому, что по сырому расстоянию он «бежал бы всегда», — и замах в движении на рабочей
            // дистанции разбегом не является.
            SimTuning tuning = SimTuning.Default;
            var (chaser, target, units) = Scene(chaserX: -20f, attackType: AttackType.Ranged, range: 8f);
            var move = new MovementSystem();
            var attack = new AutoAttackSystem();
            var ctx = new MockCombatContext();

            float reach = CombatPositioning.AttackReachCenter(chaser, target, in tuning);
            float distAtWindupStart = -1f;

            for (int i = 0; i < 400 && distAtWindupStart < 0f; i++)
            {
                move.Tick(units, SimConstants.TickDelta, ArenaBounds.Unbounded, in tuning);
                attack.Tick(units, ctx, SimConstants.TickDelta);
                if (chaser.IsWindingUp) distAtWindupStart = (target.Position - chaser.Position).magnitude;
            }

            Assert.That(distAtWindupStart, Is.GreaterThan(0f), "Предусловие: стрелок дошёл и начал стрелять.");
            Assert.That(distAtWindupStart, Is.LessThanOrEqualTo(reach),
                "Стрелок начинает замах только из своей досягаемости — въезд ему не выдаётся.");
        }

        // ===================== Хелперы =====================

        /// <summary>Поставить юнита на <paramref name="distance"/> левее цели (та в нуле), задав ему шаг
        /// прошлого тика: предикат въезда читает скорость именно из него.</summary>
        private static void Place(RuntimeUnit unit, float distance, float stepPerTick)
        {
            unit.Position         = new Vector2(-distance, 0f);
            unit.PreviousPosition = new Vector2(-distance - stepPerTick, 0f);
        }

        /// <summary>Преследователь с ударом с разбега + неподвижная цель. Обе сцены боя и движения — одна.</summary>
        private static (RuntimeUnit chaser, RuntimeUnit target, List<RuntimeUnit> units) Scene(
            float chaserX, AttackType attackType = AttackType.Melee, float range = Range)
        {
            // Доля замаха живёт на АРХЕТИПЕ с 06.08.2026: «время замаха должно быть у всех одинаковым»,
            // поэтому своего поля у юнита нет, и тест обязан задавать её там же, где прод.
            RelicData relic = TestRelic.Make(attackType: attackType, visual: TestVisual.WithShare(WindupShare))
                .With("_chargeAttackWindupMult", ChargeMult);

            var chaser = MakeUnit(new Vector2(chaserX, 0f), team: 0, relic: relic, range: range, moveSpeed: MoveSpeed);
            var target = MakeUnit(Vector2.zero, team: 1);
            chaser.CurrentTarget = target;

            return (chaser, target, new List<RuntimeUnit> { chaser, target });
        }

        private static RuntimeUnit MakeUnit(
            Vector2 pos, int team, RelicData relic = null, float range = Range, float moveSpeed = 0f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 1000f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 10f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, range),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, moveSpeed),
                new StatModifier(StatType.Size,             ModifierOp.Flat, 1f),
            });

            var u = new RuntimeUnit
            {
                Team = team, Stats = stats, CurrentHP = 1000f,
                Position = pos, PreviousPosition = pos,
                Positioning = PositioningIntent.Approach,
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
            u.AdoptKit(relic);   // доставка и on-hit — из снимка кита, как в фабрике
            return u;
        }
    }
}
