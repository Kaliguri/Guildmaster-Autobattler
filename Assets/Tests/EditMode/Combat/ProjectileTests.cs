using Guildmaster.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// ProjectileSystem.SweptCircleHit: быстрый снаряд не протыкает цель между тиками.
    /// </summary>
    public sealed class ProjectileTests
    {
        // --- SweptCircleHit ---

        [Test]
        public void SweptHit_SegmentCrossesCircle_ReturnsTrue()
        {
            var from   = new Vector2(-5f, 0f);
            var to     = new Vector2( 5f, 0f);
            var center = new Vector2( 0f, 0f);

            Assert.IsTrue(ProjectileSystem.SweptCircleHit(from, to, center, 1f));
        }

        [Test]
        public void SweptHit_SegmentMissesCircle_ReturnsFalse()
        {
            var from   = new Vector2(-5f, 5f);
            var to     = new Vector2( 5f, 5f);
            var center = new Vector2( 0f, 0f);

            Assert.IsFalse(ProjectileSystem.SweptCircleHit(from, to, center, 1f));
        }

        [Test]
        public void SweptHit_FastProjectile_DetectsHitThroughTarget()
        {
            // Снаряд перемещается с -10 до +10 за один тик, цель в центре
            var from   = new Vector2(-10f, 0f);
            var to     = new Vector2( 10f, 0f);
            var center = new Vector2(  0f, 0f);

            Assert.IsTrue(ProjectileSystem.SweptCircleHit(from, to, center, 0.5f),
                "Быстрый снаряд, пролетающий сквозь цель в одном тике, должен регистрировать попадание");
        }

        [Test]
        public void SweptHit_TangentContact_Included()
        {
            // Отрезок проходит точно по краю круга
            var from   = new Vector2(-5f, 1f);
            var to     = new Vector2( 5f, 1f);
            var center = new Vector2( 0f, 0f);

            Assert.IsTrue(ProjectileSystem.SweptCircleHit(from, to, center, 1f),
                "Касание по краю должно считаться попаданием");
        }

        [Test]
        public void SweptHit_ZeroLengthSegment_ChecksPointVsCircle()
        {
            var point  = new Vector2(0.5f, 0f);
            var center = new Vector2(0f, 0f);

            Assert.IsTrue(ProjectileSystem.SweptCircleHit(point, point, center, 1f));
            Assert.IsFalse(ProjectileSystem.SweptCircleHit(point, point, center, 0.4f));
        }

        [Test]
        public void SweptHit_EndpointInsideCircle_ReturnsTrue()
        {
            var from   = new Vector2(-5f, 0f);
            var to     = new Vector2( 0.5f, 0f); // конечная точка внутри круга
            var center = new Vector2( 0f,  0f);

            Assert.IsTrue(ProjectileSystem.SweptCircleHit(from, to, center, 1f));
        }
    }
}
