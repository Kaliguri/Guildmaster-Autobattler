using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Core.Arena;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Шов «кто на арене» глазами гостя: руки игрока обязаны видеть бойцов там, где их показывает
    /// присланный кадр, и не зависеть от симуляции — у гостя её нет вовсе.
    /// </summary>
    /// <remarks>
    /// Инвариант живёт в тесте, потому что нарушается он с ДРУГОЙ стороны шва и молча: стоит рукам
    /// снова спросить <c>CombatSimulation</c> напрямую, и у хозяина всё продолжит работать, а у гостя
    /// пропадут круги-опоры и перетаскивание — ровно то, что три прогона вдвоём находили поштучно.
    /// </remarks>
    [TestFixture]
    public class ArenaUnitsSeamTests
    {
        /// <summary>Кадр, который «прислал хост»: столько, сколько нужно шву, и ни полем больше.</summary>
        private sealed class FakeFrames : IStageFrameSource
        {
            private readonly List<UnitSnapshot> _units = new List<UnitSnapshot>();
            public bool HasFrame = true;

            public void Add(int id, int team, Vector2 pos, float size, bool dead = false) =>
                _units.Add(new UnitSnapshot(
                    id, team, pos, pos,
                    currentHp: 10f, maxHp: 10f, currentShield: 0f, currentResource: 0f, maxResource: 0f,
                    size: size, phase: AttackPhase.Idle, windupTicks: 0, windupRemaining: 0,
                    attackCooldownTicks: 0, targetId: -1, effectTagMask: default, isDead: dead));

            public void Advance(float deltaTime) { }
            public float Alpha => 0f;

            public bool TryGetFrame(out IReadOnlyList<UnitSnapshot> units,
                                    out IReadOnlyList<ProjectileSnapshot> projectiles)
            {
                units       = _units;
                projectiles = System.Array.Empty<ProjectileSnapshot>();
                return HasFrame;
            }
        }

        [Test]
        public void Гость_видит_на_арене_тех_кого_показывает_кадр()
        {
            var frames = new FakeFrames();
            frames.Add(7, team: 0, pos: new Vector2(1f, 2f), size: 1.5f);
            frames.Add(9, team: 1, pos: new Vector2(-3f, 0f), size: 2f, dead: true);

            var arena = new TapeArenaUnits(frames);

            Assert.AreEqual(2, arena.Units.Count, "шов потерял бойцов кадра");

            Assert.IsTrue(arena.TryGet(7, out ArenaUnit mine), "бойца из кадра не нашли по id");
            Assert.AreEqual(0, mine.Team);
            Assert.AreEqual(new Vector2(1f, 2f), mine.Position);
            Assert.AreEqual(1.5f, mine.Size, 0.0001f, "габарит нужен для радиуса круга-опоры");
            Assert.IsFalse(mine.IsDead);
        }

        [Test]
        public void Мёртвых_шов_не_прячет_решает_потребитель()
        {
            var frames = new FakeFrames();
            frames.Add(9, team: 1, pos: Vector2.zero, size: 1f, dead: true);

            var arena = new TapeArenaUnits(frames);

            // Расстановка мёртвых пропускает сама, а вот проверка перекрытия обязана знать о теле,
            // которое ещё лежит на арене: спрячь их здесь — и бойца можно будет поставить в труп.
            Assert.IsTrue(arena.TryGet(9, out ArenaUnit dead));
            Assert.IsTrue(dead.IsDead);
        }

        [Test]
        public void Пустой_кадр_это_пустая_арена_а_не_отказ()
        {
            var frames = new FakeFrames { HasFrame = false };
            frames.Add(1, team: 0, pos: Vector2.zero, size: 1f);

            var arena = new TapeArenaUnits(frames);

            // Лента ещё не пришла — показывать нечего. Это нормальный ответ, а не ошибка: руки просто
            // не найдут, кого хватать, и ничего не нарисуют.
            Assert.IsEmpty(arena.Units);
            Assert.IsFalse(arena.TryGet(1, out _));
        }
    }
}
