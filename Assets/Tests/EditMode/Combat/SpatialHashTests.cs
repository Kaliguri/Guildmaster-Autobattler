using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// SpatialHash: QueryRadius возвращает ровно тех, кто в радиусе; граничные ячейки; мёртвые пропускаются.
    /// </summary>
    public sealed class SpatialHashTests
    {
        private const float CellSize = 2f;

        private static SpatialHash NewHash() => new SpatialHash(CellSize);

        private static RuntimeUnit MakeUnit(float x, float y, bool dead = false)
        {
            var stats = new Stats(null);
            return new RuntimeUnit
            {
                Stats    = stats,
                Position = new Vector2(x, y),
                IsDead   = dead,
            };
        }

        [Test]
        public void QueryRadius_FindsUnitInsideRadius()
        {
            var hash = NewHash();
            var unit = MakeUnit(1f, 1f);
            var units = new List<RuntimeUnit> { unit };
            hash.Rebuild(units);

            var results = new List<RuntimeUnit>();
            hash.QueryRadius(Vector2.zero, 2f, results);

            Assert.Contains(unit, results);
        }

        [Test]
        public void QueryRadius_ExcludesUnitOutsideRadius()
        {
            var hash = NewHash();
            var near = MakeUnit(1f, 0f);
            var far  = MakeUnit(5f, 0f);
            var units = new List<RuntimeUnit> { near, far };
            hash.Rebuild(units);

            var results = new List<RuntimeUnit>();
            hash.QueryRadius(Vector2.zero, 2f, results);

            Assert.Contains(near, results);
            Assert.IsFalse(results.Contains(far), "Юнит за пределами радиуса не должен входить");
        }

        [Test]
        public void QueryRadius_DeadUnitsExcluded()
        {
            var hash = NewHash();
            var alive = MakeUnit(1f, 0f);
            var dead  = MakeUnit(0.5f, 0f, dead: true);
            var units = new List<RuntimeUnit> { alive, dead };
            hash.Rebuild(units);

            var results = new List<RuntimeUnit>();
            hash.QueryRadius(Vector2.zero, 5f, results);

            Assert.Contains(alive, results);
            Assert.IsFalse(results.Contains(dead), "Мёртвый юнит не должен возвращаться");
        }

        [Test]
        public void QueryRadius_OnBoundary_Included()
        {
            var hash  = NewHash();
            var unit  = MakeUnit(2f, 0f);
            var units = new List<RuntimeUnit> { unit };
            hash.Rebuild(units);

            var results = new List<RuntimeUnit>();
            hash.QueryRadius(Vector2.zero, 2f, results);

            Assert.Contains(unit, results, "Юнит ровно на границе должен включаться");
        }

        [Test]
        public void Rebuild_ClearsPreviousState()
        {
            var hash = NewHash();
            var unit = MakeUnit(1f, 0f);
            var units = new List<RuntimeUnit> { unit };
            hash.Rebuild(units);

            unit.IsDead = true;
            hash.Rebuild(units);

            var results = new List<RuntimeUnit>();
            hash.QueryRadius(Vector2.zero, 5f, results);

            Assert.AreEqual(0, results.Count, "После Rebuild мёртвый юнит не должен быть в хэше");
        }

        [Test]
        public void QueryRadius_MultipleUnits_ReturnsAll()
        {
            var hash  = NewHash();
            var a     = MakeUnit(0.5f, 0f);
            var b     = MakeUnit(0f, 0.5f);
            var c     = MakeUnit(-0.5f, 0f);
            var units = new List<RuntimeUnit> { a, b, c };
            hash.Rebuild(units);

            var results = new List<RuntimeUnit>();
            hash.QueryRadius(Vector2.zero, 1f, results);

            Assert.AreEqual(3, results.Count);
        }
    }
}
