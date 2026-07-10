using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Arena;
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
        private const float MinDist = 0.5f; // r(0.25) + r(0.25) при Size 1

        private static RuntimeUnit MakeUnit(int id, float x, float y, int displaced = 0) =>
            new RuntimeUnit
            {
                Id       = id,
                Stats    = new Stats(null), // Size = 1 → радиус 0.25
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
            var a = MakeUnit(0, 0f, 0f);
            var b = MakeUnit(1, 0.2f, 0f); // перекрытие: dist 0.2 < 0.5
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
            var a = MakeUnit(0, 0f, 0f);
            var b = MakeUnit(1, 0.1f, 0f);
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
        public void DisplacedUnit_PushesButIsNotPushed()
        {
            var flying = MakeUnit(0, 0f, 0f, displaced: 5); // в полёте — неподвижный толкатель
            var mover  = MakeUnit(1, 0.2f, 0f);
            var units = new List<RuntimeUnit> { flying, mover };
            var sys = new SeparationSystem();
            var hash = new SpatialHash(2f);

            Vector2 flyingStart = flying.Position;
            Step(sys, units, hash, ArenaBounds.Unbounded, times: 5);

            Assert.AreEqual(flyingStart.x, flying.Position.x, 1e-6f, "летящий не сдвигается");
            Assert.AreEqual(flyingStart.y, flying.Position.y, 1e-6f);
            Assert.Greater(mover.Position.x, 0.2f, "подвижный забирает всё расталкивание");
        }
    }
}
