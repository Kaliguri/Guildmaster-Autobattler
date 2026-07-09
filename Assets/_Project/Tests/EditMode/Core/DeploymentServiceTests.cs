using System.Collections.Generic;
using Guildmaster.Core.Arena;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Core
{
    /// <summary>
    /// Правила фазы расстановки (вики «15» §6): Normal доступна всем, Extended — только с правом,
    /// чужая сторона и точки вне зон отклоняются.
    /// </summary>
    public sealed class DeploymentServiceTests
    {
        // Player Normal:   x∈[-7,-3], y∈[-4,4]
        // Player Extended: x∈[-10,-8]  (не пересекается с Normal)
        // Enemy  Normal:   x∈[ 3, 7]
        private static DeploymentService Build()
        {
            var zones = new List<DeploymentZone>
            {
                new DeploymentZone(new Rect2D(new Vector2(-5f, 0f), new Vector2(4f, 8f)), DeploymentSide.Player, DeploymentTier.Normal),
                new DeploymentZone(new Rect2D(new Vector2(-9f, 0f), new Vector2(2f, 8f)), DeploymentSide.Player, DeploymentTier.Extended),
                new DeploymentZone(new Rect2D(new Vector2( 5f, 0f), new Vector2(4f, 8f)), DeploymentSide.Enemy,  DeploymentTier.Normal),
            };
            var layout = new ArenaLayoutData(new ArenaBounds(Vector2.zero, new Vector2(24f, 10f)), zones);
            return new DeploymentService(layout);
        }

        [Test]
        public void NormalZone_AllowsUnitWithoutExtendedRight()
        {
            var s = Build();
            Assert.IsTrue(s.CanPlace(new Vector2(-5f, 0f), DeploymentSide.Player, canUseExtended: false));
        }

        [Test]
        public void ExtendedZone_RequiresRight()
        {
            var s = Build();
            Vector2 p = new Vector2(-9f, 0f); // только в Extended-зоне

            Assert.IsFalse(s.CanPlace(p, DeploymentSide.Player, canUseExtended: false), "без права — нельзя");
            Assert.IsTrue (s.CanPlace(p, DeploymentSide.Player, canUseExtended: true),  "с правом — можно");
        }

        [Test]
        public void EnemyZone_RejectedForPlayer_EvenWithExtendedRight()
        {
            var s = Build();
            Assert.IsFalse(s.CanPlace(new Vector2(5f, 0f), DeploymentSide.Player, canUseExtended: true));
        }

        [Test]
        public void OutsideAllZones_Rejected()
        {
            var s = Build();
            Assert.IsFalse(s.CanPlace(new Vector2(0f, 0f), DeploymentSide.Player, canUseExtended: true));
        }

        [Test]
        public void EmptyLayout_RejectsEverything()
        {
            var s = new DeploymentService(ArenaLayoutData.Unbounded);
            Assert.IsFalse(s.CanPlace(Vector2.zero, DeploymentSide.Player, canUseExtended: true));
        }
    }
}
