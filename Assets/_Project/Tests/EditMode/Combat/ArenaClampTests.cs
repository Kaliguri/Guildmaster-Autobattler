using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Клампинг движения границами арены (вики «15» §7): отступающий юнит не выходит за поле,
    /// а без границ (Unbounded) — уходит свободно (регресс-гарантия для существующего поведения).
    /// </summary>
    public sealed class ArenaClampTests
    {
        private static RuntimeUnit Unit(int id, int team, Vector2 pos, float moveSpeed = 5f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,       ModifierOp.Flat, 100f),
                new StatModifier(StatType.MoveSpeed,   ModifierOp.Flat, moveSpeed),
                new StatModifier(StatType.AttackRange, ModifierOp.Flat, 1f),
            });
            return new RuntimeUnit
            {
                Id = id, Team = team, Stats = stats, CurrentHP = 100f,
                Position = pos, PreviousPosition = pos,
            };
        }

        // Отступающий бежит прочь от ближайшего врага; CurrentTarget нужен, чтобы пройти гейт
        // движения (MovementSystem пропускает юнита без цели), но направление задаёт MoveRetreat.
        private static List<RuntimeUnit> RetreatPair(out RuntimeUnit fleer)
        {
            fleer       = Unit(1, team: 0, new Vector2(3f, 0f));
            var chaser  = Unit(2, team: 1, new Vector2(0f, 0f));
            fleer.Positioning  = PositioningIntent.Retreat;
            fleer.CurrentTarget = chaser;
            return new List<RuntimeUnit> { fleer, chaser };
        }

        [Test]
        public void Retreat_StaysWithinBounds()
        {
            var bounds = new ArenaBounds(Vector2.zero, new Vector2(10f, 10f)); // x,y ∈ [-5,5]
            var move   = new MovementSystem();
            var units  = RetreatPair(out RuntimeUnit fleer);

            for (int t = 0; t < 200; t++)
                move.Tick(units, SimConstants.TickDelta, in bounds, SimTuning.Default);

            Assert.IsTrue(bounds.Contains(fleer.Position),
                $"Отступающий юнит вышел за границы арены: {fleer.Position}");
        }

        [Test]
        public void Unbounded_AllowsEscape()
        {
            ArenaBounds unb = ArenaBounds.Unbounded;
            var move  = new MovementSystem();
            var units = RetreatPair(out RuntimeUnit fleer);

            for (int t = 0; t < 200; t++)
                move.Tick(units, SimConstants.TickDelta, in unb, SimTuning.Default);

            Assert.Greater(fleer.Position.x, 5f,
                "Без границ отступающий должен уйти далеко за пределы бывшей арены");
        }
    }
}
