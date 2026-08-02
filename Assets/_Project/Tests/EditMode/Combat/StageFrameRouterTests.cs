using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Кто поставляет показу кадр сцены: бой, пока он идёт, и тела мира всё остальное время.
    /// </summary>
    /// <remarks>
    /// Инвариант кросс-файловый и потому живёт тестом: боевая симуляция теперь рождается и умирает
    /// вместе с боем, а тела на арене (двор, Ристалище, строй между забегами) обязаны стоять и тогда,
    /// когда никакой симуляции нет. Сломать это можно, вернув показу прямую ссылку на ленту — и
    /// заметно это станет только в хабе, пустой ареной.
    /// </remarks>
    public sealed class StageFrameRouterTests
    {
        [Test]
        public void WithoutBattle_TheWorldBodiesAreShown()
        {
            var world  = new WorldBodyStage();
            var router = new StageFrameRouter(world);

            world.Set(new List<WorldBody> { Standing(1), Standing(2) });

            Assert.IsFalse(router.ShowingBattle, "Боя нет");
            Assert.IsTrue(router.TryGetFrame(out IReadOnlyList<UnitSnapshot> units, out var projectiles));
            Assert.AreEqual(2, units.Count, "На арене стоят оба тела");
            Assert.AreEqual(0, projectiles.Count, "Вне боя снарядов не бывает");
        }

        [Test]
        public void EmptyWorld_ShowsNothing()
        {
            var router = new StageFrameRouter(new WorldBodyStage());

            Assert.IsFalse(router.TryGetFrame(out _, out _), "Пустая арена — не кадр из воздуха");
        }

        [Test]
        public void Battle_TakesOverTheFrame_AndGivesItBack()
        {
            var world  = new WorldBodyStage();
            var router = new StageFrameRouter(world);
            world.Set(new List<WorldBody> { Standing(1) });

            var battle = new FakeSource(Body(7), Body(8), Body(9));
            router.Bind(battle, battle);

            Assert.IsTrue(router.ShowingBattle);
            Assert.IsTrue(router.TryGetFrame(out IReadOnlyList<UnitSnapshot> inBattle, out _));
            Assert.AreEqual(3, inBattle.Count, "Пока идёт бой, кадр приходит с ленты");

            router.Unbind(battle);

            Assert.IsFalse(router.ShowingBattle);
            Assert.IsTrue(router.TryGetFrame(out IReadOnlyList<UnitSnapshot> afterBattle, out _));
            Assert.AreEqual(1, afterBattle.Count, "После боя арена возвращается к телам мира");
        }

        // Умирающий бой не должен гасить арену того, кто уже начался: между Dispose старого скоупа и
        // рождением нового порядок не гарантирован никем.
        [Test]
        public void UnbindingSomeoneElse_DoesNotClearTheArena()
        {
            var router = new StageFrameRouter(new WorldBodyStage());

            var previous = new FakeSource(Body(1));
            var current  = new FakeSource(Body(2), Body(3));

            router.Bind(previous, previous);
            router.Bind(current, current);
            router.Unbind(previous);   // опоздавший Dispose прошлого боя

            Assert.IsTrue(router.ShowingBattle, "Текущий бой остался подключён");
            Assert.IsTrue(router.TryGetFrame(out IReadOnlyList<UnitSnapshot> units, out _));
            Assert.AreEqual(2, units.Count, "И показывает свой кадр, а не чужой");
        }

        // Время двигает АКТИВНЫЙ источник: статичной сцене оно не нужно, а лента на нём и живёт.
        [Test]
        public void Advance_ReachesOnlyTheActiveSource()
        {
            var router = new StageFrameRouter(new WorldBodyStage());
            var battle = new FakeSource(Body(1));

            router.Advance(0.5f);
            Assert.AreEqual(0f, battle.Advanced, 1e-6f, "Отключённый источник времени не получает");

            router.Bind(battle, battle);
            router.Advance(0.5f);
            Assert.AreEqual(0.5f, battle.Advanced, 1e-6f);
        }

        // Паспорта переключаются ВМЕСТЕ с кадром: иначе показ рисует бой людьми из двора гильдии.
        [Test]
        public void Directory_FollowsTheFrameSource()
        {
            var world  = new WorldBodyStage();
            var router = new StageFrameRouter(world);
            world.Set(new List<WorldBody> { Standing(1) });

            Assert.IsTrue(router.TryGet(1, out UnitIdentity inWorld), "Вне боя паспорта держит мир");
            Assert.AreEqual(0, inWorld.Team);

            var battle = new FakeSource(Body(7));
            battle.Announce(new UnitIdentity(null, team: 1, id: 7));
            router.Bind(battle, battle);

            Assert.IsFalse(router.TryGet(1, out _), "Тело мира в бою не значится");
            Assert.IsTrue(router.TryGet(7, out UnitIdentity inBattle));
            Assert.AreEqual(1, inBattle.Team);

            router.Unbind(battle);
            Assert.IsTrue(router.TryGet(1, out _), "После боя паспорта снова мировые");
        }

        // ── помощники ────────────────────────────────────────────────────────────

        private static UnitSnapshot Body(int id) =>
            new UnitSnapshot(
                id, team: 0, position: Vector2.zero, previousPosition: Vector2.zero,
                currentHp: 100f, maxHp: 100f, currentShield: 0f, currentResource: 0f, maxResource: 0f,
                size: 1f, phase: AttackPhase.Idle, windupTicks: 0, windupRemaining: 0,
                attackCooldownTicks: 0, targetId: -1, effectTagMask: EffectTag.None, isDead: false,
                attackRange: 1.5f, canAct: true);

        /// <summary>Тело мира: снимок плюс паспорт, как их кладёт сборщик тел.</summary>
        private static WorldBody Standing(int id) =>
            new WorldBody(Body(id), new UnitIdentity(null, team: 0, id));

        private sealed class FakeSource : IStageFrameSource, IUnitDirectory
        {
            private readonly List<UnitSnapshot>       _units = new List<UnitSnapshot>();
            private readonly List<ProjectileSnapshot> _projectiles = new List<ProjectileSnapshot>();
            private readonly Dictionary<int, UnitIdentity> _who = new Dictionary<int, UnitIdentity>();

            public FakeSource(params UnitSnapshot[] units) => _units.AddRange(units);

            public float Advanced { get; private set; }
            public float Alpha => 0.25f;

            public void Advance(float deltaTime) => Advanced += deltaTime;

            public void Announce(in UnitIdentity identity) => _who[identity.Id] = identity;

            public bool TryGet(int unitId, out UnitIdentity identity) => _who.TryGetValue(unitId, out identity);

            public int Count => _who.Count;

            public bool TryGetFrame(out IReadOnlyList<UnitSnapshot> units,
                                    out IReadOnlyList<ProjectileSnapshot> projectiles)
            {
                units       = _units;
                projectiles = _projectiles;
                return _units.Count > 0;
            }
        }
    }
}
