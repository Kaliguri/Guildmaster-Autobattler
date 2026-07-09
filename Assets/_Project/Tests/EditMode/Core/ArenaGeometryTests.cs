using Guildmaster.Core.Arena;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Core
{
    /// <summary>
    /// Геометрия арены (вики «15» §3): Rect2D.Contains/Clamp и ArenaBounds.Unbounded.
    /// Чистый headless — без сцены и симуляции.
    /// </summary>
    public sealed class ArenaGeometryTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void Rect2D_Contains_InsideAndBoundary()
        {
            var r = new Rect2D(Vector2.zero, new Vector2(10f, 6f)); // x∈[-5,5], y∈[-3,3]

            Assert.IsTrue(r.Contains(new Vector2(0f, 0f)),   "центр — внутри");
            Assert.IsTrue(r.Contains(new Vector2(5f, 3f)),   "угол — границы включительно");
            Assert.IsFalse(r.Contains(new Vector2(5.01f, 0f)), "за правой гранью — снаружи");
            Assert.IsFalse(r.Contains(new Vector2(0f, -3.01f)), "ниже нижней грани — снаружи");
        }

        [Test]
        public void Rect2D_Clamp_PullsOutsidePointToNearestEdge()
        {
            var r = new Rect2D(Vector2.zero, new Vector2(10f, 6f));

            Vector2 hi = r.Clamp(new Vector2(99f, 99f));
            Assert.AreEqual(5f, hi.x, Eps);
            Assert.AreEqual(3f, hi.y, Eps);

            Vector2 lo = r.Clamp(new Vector2(-99f, -99f));
            Assert.AreEqual(-5f, lo.x, Eps);
            Assert.AreEqual(-3f, lo.y, Eps);

            Vector2 inside = new Vector2(1f, -2f);
            Vector2 same = r.Clamp(inside);
            Assert.AreEqual(inside.x, same.x, Eps, "точка внутри не должна двигаться");
            Assert.AreEqual(inside.y, same.y, Eps);
        }

        [Test]
        public void Rect2D_Clamp_TreatsNegativeSizeAsPositive()
        {
            var r = new Rect2D(Vector2.zero, new Vector2(-10f, -6f)); // размер по модулю
            Vector2 c = r.Clamp(new Vector2(99f, 99f));
            Assert.AreEqual(5f, c.x, Eps);
            Assert.AreEqual(3f, c.y, Eps);
        }

        [Test]
        public void ArenaBounds_Unbounded_NeverClampsOrExcludes()
        {
            ArenaBounds b = ArenaBounds.Unbounded;
            Vector2 far = new Vector2(1e6f, -1e6f);

            Assert.IsTrue(b.Contains(far), "бесконечное поле содержит любую точку");
            Vector2 c = b.Clamp(far);
            Assert.AreEqual(far.x, c.x, Eps, "бесконечное поле не клампит");
            Assert.AreEqual(far.y, c.y, Eps);
        }

        [Test]
        public void ArenaBounds_ClampsToRect()
        {
            var b = new ArenaBounds(new Vector2(2f, 0f), new Vector2(4f, 4f)); // x∈[0,4], y∈[-2,2]
            Vector2 c = b.Clamp(new Vector2(10f, 10f));
            Assert.AreEqual(4f, c.x, Eps);
            Assert.AreEqual(2f, c.y, Eps);
        }
    }
}
