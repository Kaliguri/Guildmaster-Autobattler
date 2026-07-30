using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Скрытность (§9.6, §10.5): скрытый юнит невидим для ВРАЖЕСКОГО таргетинга — мозг противника
    /// не может выбрать его целью (враги не бегут к нему и не бьют), пока стелс не снят.
    /// </summary>
    public sealed class ProfileBrainStealthTests
    {
        private sealed class FakeView : IBattleView
        {
            public IReadOnlyList<RuntimeUnit> Units { get; set; }
            public int CurrentTick => 0;
        }

        private static RuntimeUnit MakeUnit(int id, int team, float x, bool stealthed = false)
        {
            return new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = new Stats(null),
                CurrentHP        = 100f,
                Position         = new Vector2(x, 0f),
                PreviousPosition = new Vector2(x, 0f),
                EffectTagMask    = stealthed ? EffectTag.Stealth : EffectTag.None,
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
            };
        }

        [Test]
        public void EnemyTargeting_SkipsStealthedUnit_PicksNextValid()
        {
            var attacker      = MakeUnit(id: 0, team: 1, x: 0f);
            var stealthedNear = MakeUnit(id: 1, team: 0, x: 2f, stealthed: true);  // ближе, но скрыт
            var visibleFar    = MakeUnit(id: 2, team: 0, x: 5f);                    // дальше, но виден

            var view = new FakeView { Units = new[] { attacker, stealthedNear, visibleFar } };

            new ProfileBrain(null).Decide(attacker, view);

            Assert.AreSame(visibleFar, attacker.CurrentTarget,
                "Скрытого (ближнего) выбирать нельзя — цель должна быть видимый дальний враг.");
        }

        [Test]
        public void EnemyTargeting_OnlyStealthedEnemy_YieldsNoTarget()
        {
            var attacker       = MakeUnit(id: 0, team: 1, x: 0f);
            var stealthedOnly  = MakeUnit(id: 1, team: 0, x: 2f, stealthed: true);

            var view = new FakeView { Units = new[] { attacker, stealthedOnly } };

            new ProfileBrain(null).Decide(attacker, view);

            Assert.IsNull(attacker.CurrentTarget,
                "Единственный враг скрыт — цели быть не должно (враг не преследует инвиз).");
        }
    }
}
