using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// SeparationSystem: перекрытия расходятся, непересекающиеся не трогаются, результат детерминирован,
    /// итог держится в границах арены, юнит в полёте — неподвижный толкатель.
    /// Тела: Size 1 (натуральный дефолт) → радиус 0.25, минимальная дистанция между центрами = 0.5.
    /// </summary>
    public sealed class SeparationSystemTests
    {
        // Минимальная дистанция между центрами = сумма радиусов двух Size-1 тел; тянем из SimConstants,
        // чтобы тест не разъезжался при смене дефолтного радиуса.
        private static readonly float MinDist = 2f * SimConstants.BodyRadiusPerSize;

        private static RuntimeUnit MakeUnit(int id, float x, float y, int displaced = 0, int team = 0) =>
            new RuntimeUnit
            {
                Id       = id,
                Team     = team,
                Stats    = new Stats(null), // Size = 1 → радиус из SimConstants
                Position = new Vector2(x, y),
                DisplacedTicksRemaining = displaced,
            };

        private static void Step(SeparationSystem sys, List<RuntimeUnit> units, SpatialHash hash, ArenaBounds bounds, int times = 1)
        {
            for (int t = 0; t < times; t++)
            {
                hash.Rebuild(units);          // в тесте хэш актуален на момент прохода
                sys.Tick(units, hash, in bounds);
            }
        }

        [Test]
        public void Overlapping_PushesApart()
        {
            var a = MakeUnit(0, 0f, 0f, team: 0);
            var b = MakeUnit(1, 0.2f, 0f, team: 1); // враги (полная сила), перекрытие
            var units = new List<RuntimeUnit> { a, b };
            var sys = new SeparationSystem();
            var hash = new SpatialHash(2f);

            float before = (a.Position - b.Position).magnitude;
            Step(sys, units, hash, ArenaBounds.Unbounded);
            float after = (a.Position - b.Position).magnitude;

            Assert.Greater(after, before, "перекрытие должно раздвигаться");
        }

        [Test]
        public void Overlapping_ConvergesToContact_OverTicks()
        {
            var a = MakeUnit(0, 0f, 0f, team: 0);
            var b = MakeUnit(1, 0.1f, 0f, team: 1); // враги — полная сила, сходятся до касания
            var units = new List<RuntimeUnit> { a, b };
            var sys = new SeparationSystem();
            var hash = new SpatialHash(2f);

            Step(sys, units, hash, ArenaBounds.Unbounded, times: 30);

            float dist = (a.Position - b.Position).magnitude;
            Assert.GreaterOrEqual(dist, MinDist - 1e-3f, "за много тиков тела должны разойтись до касания");
        }

        [Test]
        public void NonOverlapping_Untouched()
        {
            var a = MakeUnit(0, 0f, 0f);
            var b = MakeUnit(1, 5f, 0f); // далеко — не пересекаются
            var units = new List<RuntimeUnit> { a, b };
            var sys = new SeparationSystem();
            var hash = new SpatialHash(2f);

            Step(sys, units, hash, ArenaBounds.Unbounded);

            Assert.AreEqual(0f, a.Position.x, 1e-6f);
            Assert.AreEqual(5f, b.Position.x, 1e-6f);
        }

        [Test]
        public void Deterministic_TwoRunsIdentical()
        {
            List<RuntimeUnit> Build() => new List<RuntimeUnit>
            {
                MakeUnit(0, 0f, 0f), MakeUnit(1, 0.15f, 0.05f), MakeUnit(2, -0.1f, 0.12f), MakeUnit(3, 0.05f, -0.1f),
            };

            var run1 = Build();
            var run2 = Build();
            Step(new SeparationSystem(), run1, new SpatialHash(2f), ArenaBounds.Unbounded, times: 10);
            Step(new SeparationSystem(), run2, new SpatialHash(2f), ArenaBounds.Unbounded, times: 10);

            for (int i = 0; i < run1.Count; i++)
            {
                Assert.That(run2[i].Position.x, Is.EqualTo(run1[i].Position.x), $"unit {i} x детерминирован");
                Assert.That(run2[i].Position.y, Is.EqualTo(run1[i].Position.y), $"unit {i} y детерминирован");
            }
        }

        [Test]
        public void StaysWithinArenaBounds()
        {
            var bounds = new ArenaBounds(Vector2.zero, new Vector2(2f, 2f)); // x,y ∈ [-1, 1]
            var a = MakeUnit(0, 0.95f, 0f);  // у правой стены
            var b = MakeUnit(1, 0.75f, 0f);  // перекрывает — толкнёт a в стену
            var units = new List<RuntimeUnit> { a, b };
            var sys = new SeparationSystem();
            var hash = new SpatialHash(2f);

            Step(sys, units, hash, bounds, times: 30);

            foreach (var u in units)
            {
                Assert.That(u.Position.x, Is.LessThanOrEqualTo(1f + 1e-4f).And.GreaterThanOrEqualTo(-1f - 1e-4f));
                Assert.That(u.Position.y, Is.LessThanOrEqualTo(1f + 1e-4f).And.GreaterThanOrEqualTo(-1f - 1e-4f));
            }
        }

        [Test]
        public void DisplacedUnit_ExcludedFromSeparation()
        {
            // Летящий (в полёте) исключён из сепарации целиком: им владеет DisplacementSystem, а удар
            // телом по толпе — забота логики броска, не расталкивания. Значит перекрытие с летящим НЕ
            // расталкивается — ни он, ни сосед не двигаются.
            var flying = MakeUnit(0, 0f, 0f, displaced: 5);
            var neighbor = MakeUnit(1, 0.2f, 0f); // перекрывает летящего
            var units = new List<RuntimeUnit> { flying, neighbor };
            var sys = new SeparationSystem();
            var hash = new SpatialHash(2f);

            Step(sys, units, hash, ArenaBounds.Unbounded, times: 5);

            Assert.AreEqual(0f,   flying.Position.x,   1e-6f, "летящий не двигается");
            Assert.AreEqual(0f,   flying.Position.y,   1e-6f);
            Assert.AreEqual(0.2f, neighbor.Position.x, 1e-6f, "сосед летящего не расталкивается (пара пропущена)");
            Assert.AreEqual(0f,   neighbor.Position.y, 1e-6f);
        }

        [Test]
        public void SameTeam_PushesSofterThanCrossTeam()
        {
            // Свои расступаются мягче врагов: за одинаковое время своя пара разойдётся МЕНЬШЕ вражеской
            // (задние просачиваются к фронту, а не отскакивают). Одинаковый старт, разные команды.
            var allyA = MakeUnit(0, 0f, 0f, team: 0);
            var allyB = MakeUnit(1, 0.2f, 0f, team: 0);
            var allies = new List<RuntimeUnit> { allyA, allyB };

            var foeA = MakeUnit(0, 0f, 0f, team: 0);
            var foeB = MakeUnit(1, 0.2f, 0f, team: 1);
            var foes = new List<RuntimeUnit> { foeA, foeB };

            Step(new SeparationSystem(), allies, new SpatialHash(2f), ArenaBounds.Unbounded, times: 3);
            Step(new SeparationSystem(), foes,   new SpatialHash(2f), ArenaBounds.Unbounded, times: 3);

            float allyDist = (allyA.Position - allyB.Position).magnitude;
            float foeDist  = (foeA.Position - foeB.Position).magnitude;
            Assert.Less(allyDist, foeDist, "своя пара должна расходиться мягче (медленнее) вражеской");
        }
    }
}
