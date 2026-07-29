using Guildmaster.Core.Simulation;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Овертайм — правило анти-затягивания (ГДД «Боевая система», числа утверждены 2026-07-28).
    /// Наносимый урон растёт со временем боя, ЛЕЧЕНИЕ И ЩИТЫ — НЕТ: именно на этой асимметрии
    /// разваливается клинч «танк+хил против танк+хил», где иначе время не решает ничего.
    /// </summary>
    /// <remarks>
    /// Тесты держат не реализацию, а замысел: до порога бой обязан идти без надбавки (иначе овертайм
    /// незаметно подкручивал бы КАЖДЫЙ бой — при медиане 20-29 секунд это переписало бы весь баланс),
    /// а после порога рост обязан быть заметным, иначе предохранитель не сработает до жёсткой ничьей.
    /// </remarks>
    public sealed class OvertimeRampTests
    {
        private static SimTuning Tuning(float start, float perSecond) => new SimTuning(
            bodyRadiusPerSize:         SimTuning.Default.BodyRadiusPerSize,
            separationStrength:        SimTuning.Default.SeparationStrength,
            separationIterations:      SimTuning.Default.SeparationIterations,
            separationSameTeamScale:   SimTuning.Default.SeparationSameTeamScale,
            projectileHitRadiusFactor: SimTuning.Default.ProjectileHitRadiusFactor,
            projectileDespawnMargin:   SimTuning.Default.ProjectileDespawnMargin,
            kiteFleeFactor:            SimTuning.Default.KiteFleeFactor,
            globalSearchRadius:        SimTuning.Default.GlobalSearchRadius,
            fleeThreatWeight:          SimTuning.Default.FleeThreatWeight,
            fleeHomeWeight:            SimTuning.Default.FleeHomeWeight,
            fleeWallWeight:            SimTuning.Default.FleeWallWeight,
            fleeWallMargin:            SimTuning.Default.FleeWallMargin,
            fleeThreatRadius:          SimTuning.Default.FleeThreatRadius,
            kiteStrafeWeight:          SimTuning.Default.KiteStrafeWeight,
            displaceSpeedPerSecond:    SimTuning.Default.DisplaceSpeedPerSecond,
            cannonballWidthMult:       SimTuning.Default.CannonballWidthMult,
            wallImpactDamageMult:      SimTuning.Default.WallImpactDamageMult,
            wallImpactStunSeconds:     SimTuning.Default.WallImpactStunSeconds,
            overtimeStartSeconds:      start,
            overtimeDamagePerSecond:   perSecond,
            sprintSpeedMult:           SimTuning.Default.SprintSpeedMult,
            sprintEnterGap:            SimTuning.Default.SprintEnterGap,
            sprintExitGap:             SimTuning.Default.SprintExitGap);

        [Test]
        public void BeforeThreshold_NoRamp()
        {
            SimTuning t = Tuning(start: 90f, perSecond: 0.05f);

            Assert.AreEqual(1f, t.OvertimeDamageMultiplier(0f),    1e-5f, "Старт боя — надбавки нет");
            Assert.AreEqual(1f, t.OvertimeDamageMultiplier(29f),   1e-5f, "Медианный бой овертайма не видит вовсе");
            Assert.AreEqual(1f, t.OvertimeDamageMultiplier(89.9f), 1e-5f, "До самого порога — ровно единица");
            Assert.AreEqual(1f, t.OvertimeDamageMultiplier(90f),   1e-5f, "На пороге надбавка ещё нулевая");
        }

        [Test]
        public void AfterThreshold_RampsLinearly()
        {
            SimTuning t = Tuning(start: 90f, perSecond: 0.05f);

            Assert.AreEqual(1.5f, t.OvertimeDamageMultiplier(100f), 1e-5f, "10 с сверх порога = +50%");
            Assert.AreEqual(2.0f, t.OvertimeDamageMultiplier(110f), 1e-5f, "20 с = удвоенный урон");
            Assert.AreEqual(4.0f, t.OvertimeDamageMultiplier(150f), 1e-5f, "К жёсткой ничьей (150 с) урон вчетверо");
        }

        /// <summary>
        /// Ноль в рампе — рабочее состояние «овертайм выключен», а не забытое поле: числа правятся в
        /// <c>SimTuningConfig</c>, и обнуление должно давать в точности прежний бой без надбавок.
        /// </summary>
        [Test]
        public void ZeroRamp_DisablesOvertimeEntirely()
        {
            SimTuning t = Tuning(start: 90f, perSecond: 0f);

            Assert.AreEqual(1f, t.OvertimeDamageMultiplier(240f), 1e-5f, "Выключенный овертайм не трогает даже потолок");
        }

        /// <summary>
        /// Клинч ломается арифметикой, а не таймером: пока урон ниже лечения, время не решает ничего.
        /// Проверяем, что при вдвое меньшем уроне развязка наступает ДО жёсткой ничьей на 150-й секунде —
        /// иначе предохранитель бесполезен ровно в том случае, ради которого заведён.
        /// </summary>
        [Test]
        public void SustainClinch_BreaksBeforeHardDraw()
        {
            SimTuning t = Tuning(start: 90f, perSecond: 0.05f);

            const float damagePerSecond = 50f;
            const float healPerSecond   = 100f; // вдвое сильнее урона — вечный клинч без овертайма

            float breakSecond = -1f;
            for (float s = 90f; s <= 150f; s += 0.5f)
            {
                if (damagePerSecond * t.OvertimeDamageMultiplier(s) > healPerSecond) { breakSecond = s; break; }
            }

            Assert.Greater(breakSecond, 0f, "Клинч обязан развалиться до жёсткой ничьей");
            Assert.Less(breakSecond, 150f, $"Развязка на {breakSecond:0.0} с — раньше ничьей на 150-й");
        }

        /// <summary>Дефолты кода — контракт баланса: их правка обязана быть осознанной, а не побочной.</summary>
        [Test]
        public void Defaults_MatchApprovedNumbers()
        {
            Assert.AreEqual(90f,   SimTuning.Default.OvertimeStartSeconds,    1e-5f, "Порог овертайма — 90 с");
            Assert.AreEqual(0.05f, SimTuning.Default.OvertimeDamagePerSecond, 1e-5f, "Рампа — +5% урона за секунду");
        }
    }
}
