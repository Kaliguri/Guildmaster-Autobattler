using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Маскировка: скрытый юнит невидим для ВРАЖЕСКОГО таргетинга — мозг противника не может выбрать
    /// его целью (враги не бегут к нему и не бьют), пока его не заметили или он себя не выдал.
    /// </summary>
    /// <remarks>
    /// С 2026-07-31 «скрыт» — это состояние (<c>ConcealTier</c> плюс необнаруженность), а не тег
    /// эффекта: у Маскировки четыре ступени, и висящий тег сам по себе больше не значит «не видно».
    /// Кто именно скрыт, решает <c>ConcealmentSystem</c> по расстоянию — здесь оно задано напрямую.
    /// </remarks>
    public sealed class ProfileBrainStealthTests
    {
        private sealed class FakeView : IBattleView
        {
            public IReadOnlyList<RuntimeUnit> Units { get; set; }
            public int CurrentTick => 0;
            public Guildmaster.Core.Simulation.SimTuning Tuning => Guildmaster.Core.Simulation.SimTuning.Default;
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
                ConcealTier      = stealthed ? ConcealmentTier.Invisible : ConcealmentTier.None,
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
