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
    /// Единая математика побега (<see cref="FleeSteering"/>, вики «13» §9.7): направление = центроид
    /// врагов (не «ближайший») + тыл по команде + избегание стен; у стены — скольжение вдоль неё, а не
    /// прижимание/загон в угол. Детерминированная векторная математика — без RNG, без сцены.
    /// </summary>
    public sealed class FleeSteeringTests
    {
        private static readonly SimTuning T = SimTuning.Default;

        private static RuntimeUnit Unit(int id, int team, Vector2 pos)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, 100f) });
            return new RuntimeUnit { Id = id, Team = team, Stats = stats, CurrentHP = 100f, Position = pos, PreviousPosition = pos };
        }

        // ===================== Направление: центроид, а не «ближайший» =====================

        // Два врага симметрично сверху и снизу от беглеца → уход строго по X (центроид на оси),
        // а НЕ «от ближайшего» под углом. Гасит дёрганье «то от A, то от B».
        [Test]
        public void Retreat_UsesEnemyCentroid_NotSingleNearest()
        {
            var fleer  = Unit(1, team: 1, new Vector2(0f, 0f)); // team 1 → тыл справа (+X), не мешает оси
            var enemyA = Unit(2, team: 0, new Vector2(-1f, 2f));
            var enemyB = Unit(3, team: 0, new Vector2(-1f, -2f));
            var units  = new List<RuntimeUnit> { fleer, enemyA, enemyB };

            Vector2 p = FleeSteering.Retreat(fleer, units, step: 0.1f, ArenaBounds.Unbounded, in T);

            Assert.Greater(p.x, 0f, "Центроид врагов слева → уход вправо по X");
            Assert.AreEqual(0f, p.y, 1e-3f, "Симметричная угроза по Y → без вертикального сноса (центроид, не ближайший)");
        }

        // ===================== Home-pull по команде =====================

        // Один враг ровно сверху (угроза чисто по Y). Team 0 подмешивает тягу к тылу (-X) → уход идёт
        // и вниз (от врага), и влево (к своему тылу), а не строго вниз.
        [Test]
        public void Retreat_Team0_PullsTowardOwnBackline_Left()
        {
            var fleer = Unit(1, team: 0, new Vector2(0f, 0f));
            var enemy = Unit(2, team: 1, new Vector2(0f, 2f));
            var units = new List<RuntimeUnit> { fleer, enemy };

            Vector2 p = FleeSteering.Retreat(fleer, units, step: 0.1f, ArenaBounds.Unbounded, in T);

            Assert.Less(p.y, 0f, "Уходит от врага (вниз)");
            Assert.Less(p.x, 0f, "Team 0 подкручивает побег к своему тылу (влево)");
        }

        [Test]
        public void Retreat_Team1_PullsTowardOwnBackline_Right()
        {
            var fleer = Unit(1, team: 1, new Vector2(0f, 0f));
            var enemy = Unit(2, team: 0, new Vector2(0f, 2f));
            var units = new List<RuntimeUnit> { fleer, enemy };

            Vector2 p = FleeSteering.Retreat(fleer, units, step: 0.1f, ArenaBounds.Unbounded, in T);

            Assert.Less(p.y, 0f, "Уходит от врага (вниз)");
            Assert.Greater(p.x, 0f, "Team 1 подкручивает побег к своему тылу (вправо)");
        }

        // Тыл лишь подкручивает и не пересиливает реальную угрозу: враг прямо по курсу к тылу
        // (team 0, враг слева) — беглец всё равно уходит ОТ врага (вправо), а не в него.
        [Test]
        public void Retreat_ThreatDominatesHome_WhenEnemyOnHomeSide()
        {
            var fleer = Unit(1, team: 0, new Vector2(0f, 0f));
            var enemy = Unit(2, team: 1, new Vector2(-2f, 0f)); // враг слева = в стороне тыла
            var units = new List<RuntimeUnit> { fleer, enemy };

            Vector2 p = FleeSteering.Retreat(fleer, units, step: 0.1f, ArenaBounds.Unbounded, in T);

            Assert.Greater(p.x, 0f, "Угроза сильнее тяги к тылу — уходит ОТ врага, а не на базу сквозь него");
        }

        // ===================== Избегание стен / углов =====================

        // Беглец у стены, но не в самом углу: превентивное избегание боковых стен уводит его вдоль
        // стены к середине (в открытое место), а не дальше в угол.
        [Test]
        public void Retreat_NearWall_DoesNotDriveIntoCorner()
        {
            var bounds = new ArenaBounds(Vector2.zero, new Vector2(10f, 10f)); // x,y ∈ [-5,5]
            // Беглец у правой стены, ближе к верхнему углу; враг ниже-слева гонит его вверх-в-угол.
            var fleer = Unit(1, team: 1, new Vector2(4.6f, 4.0f));
            var enemy = Unit(2, team: 0, new Vector2(2.0f, 1.0f));
            var units = new List<RuntimeUnit> { fleer, enemy };

            Vector2 p = FleeSteering.Retreat(fleer, units, step: 0.2f, in bounds, in T);

            Assert.LessOrEqual(p.y, 4.0f + 1e-3f, "Верхняя стена рядом — побег не гонит ГЛУБЖЕ в угол по Y");
            Assert.IsTrue(bounds.Contains(p), "Остаётся в поле");
        }

        // Уперся строго в стену (угроза перпендикулярна ей): не продавливается наружу и не замирает —
        // скользит ВДОЛЬ стены (появляется движение по касательной).
        [Test]
        public void Retreat_PinnedPerpendicular_SlidesAlongWall()
        {
            var bounds = new ArenaBounds(Vector2.zero, new Vector2(10f, 10f)); // x,y ∈ [-5,5]
            var fleer  = Unit(1, team: 0, new Vector2(-5f, 0f)); // на левой стене; тыл (-X) тоже в стену
            var enemy  = Unit(2, team: 1, new Vector2(-3f, 0f)); // строго справа → отход в стену
            var units  = new List<RuntimeUnit> { fleer, enemy };

            Vector2 p = FleeSteering.Retreat(fleer, units, step: 0.1f, in bounds, in T);

            Assert.AreEqual(-5f, p.x, 1e-3f, "За левую стену по X не продавливается");
            Assert.Greater(Mathf.Abs(p.y), 1e-4f, "Зажатый перпендикулярно — скользит вдоль стены, а не стоит");
        }

        // ===================== Вырожденные случаи =====================

        [Test]
        public void Retreat_NoEnemies_WalksTowardHome()
        {
            var fleer = Unit(1, team: 0, new Vector2(0f, 0f));
            var units = new List<RuntimeUnit> { fleer };

            Vector2 p = FleeSteering.Retreat(fleer, units, step: 0.1f, ArenaBounds.Unbounded, in T);

            Assert.Less(p.x, 0f, "Врагов нет — уходит к своему тылу (team 0 → влево)");
        }

        [Test]
        public void Retreat_ZeroStep_DoesNotMove()
        {
            var fleer = Unit(1, team: 0, new Vector2(1f, 2f));
            var enemy = Unit(2, team: 1, new Vector2(0f, 0f));
            var units = new List<RuntimeUnit> { fleer, enemy };

            Vector2 p = FleeSteering.Retreat(fleer, units, step: 0f, ArenaBounds.Unbounded, in T);

            Assert.AreEqual(new Vector2(1f, 2f), p, "Нулевой шаг — позиция не меняется");
        }
    }
}
