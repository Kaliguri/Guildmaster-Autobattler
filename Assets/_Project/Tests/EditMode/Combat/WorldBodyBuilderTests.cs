using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Тела на арене вне боя собираются из ростера БЕЗ симуляции — и теми же числами, что в бою.
    /// </summary>
    /// <remarks>
    /// Инвариант кросс-файловый: каскад статов живёт у <see cref="EffectiveStats"/>, боевая сборка
    /// (<see cref="RuntimeUnitFactory"/>) и мировая (<see cref="WorldBodyBuilder"/>) обязаны звать
    /// именно его. Разъезд заметен не сразу: тело просто стоит во дворе чуть другого размера, чем
    /// выйдет в бой, и на глаз это ловится только рядом.
    /// </remarks>
    public sealed class WorldBodyBuilderTests
    {
        [Test]
        public void EverySlot_BecomesABodyWithItsIdentity()
        {
            var builder = new WorldBodyBuilder(null, null);

            RelicData first  = TestRelic.Make();
            RelicData second = TestRelic.Make();
            var roster = new[]
            {
                new PlayerSlot(first,  null, new Vector2(-6f, 1f)),
                new PlayerSlot(second, null, new Vector2(-6f, -1f)),
            };

            List<WorldBody> bodies = builder.Build(roster, null);

            Assert.AreEqual(2, bodies.Count, "Каждый слот встал телом");
            Assert.AreEqual(new Vector2(-6f, 1f), bodies[0].Body.Position, "Позиция — из слота");
            Assert.AreEqual(0, bodies[0].Body.Team, "Отряд игрока всегда team 0");
            Assert.AreEqual(bodies[0].Body.Id, bodies[0].Who.Id, "Паспорт и тело говорят об одном юните");
            Assert.AreSame(first,  bodies[0].Who.Definition, "И паспорт несёт кит своего слота");
            Assert.AreSame(second, bodies[1].Who.Definition);
            Assert.AreNotEqual(bodies[0].Body.Id, bodies[1].Body.Id, "Id тел различны — по ним показ ищет вид");
        }

        [Test]
        public void StandingBody_HasNoMotionAndFullHealth()
        {
            var builder = new WorldBodyBuilder(null, null);
            var roster = new[] { new PlayerSlot(TestRelic.Make(), null, new Vector2(2f, 3f)) };

            UnitSnapshot body = builder.Build(roster, null)[0].Body;

            Assert.AreEqual(body.Position, body.PreviousPosition, "Стоящему телу нечего интерполировать");
            Assert.AreEqual(body.MaxHP, body.CurrentHP, 1e-4f, "Вне боя отряд цел");
            Assert.IsFalse(body.IsDead);
            Assert.AreEqual(AttackPhase.Idle, body.Phase, "Никто не замахивается: боя нет");
        }

        [Test]
        public void SlotWithoutKit_IsSkipped()
        {
            var builder = new WorldBodyBuilder(null, null);
            var roster = new[]
            {
                new PlayerSlot(null, null, Vector2.zero),
                new PlayerSlot(TestRelic.Make(), null, Vector2.one),
            };

            Assert.AreEqual(1, builder.Build(roster, null).Count, "Слот без кита ставить нечем");
            Assert.AreEqual(0, builder.Build(null, null).Count, "Пустая арена законна");
        }

        // Тот самый кросс-файловый инвариант: числа тела мира и боевого юнита из одного слота совпадают.
        [Test]
        public void WorldBody_ShowsTheSameNumbersAsTheBattleUnit()
        {
            var effects = new EffectSystem();
            var factory = new RuntimeUnitFactory(null, null, effects, new MockCombatContext(effects: effects));
            var builder = new WorldBodyBuilder(null, null);

            RelicData relic = TestRelic.Make(stats: new[]
            {
                new StatModifier(StatType.MaxHP, ModifierOp.Flat, 250f),
                new StatModifier(StatType.Size,  ModifierOp.Flat, 0.4f),
            });
            ItemData item = TestItem.Make(new StatModifier(StatType.MaxHP, ModifierOp.Flat, 60f));
            var slot = new PlayerSlot(relic, null, new Vector2(-4f, 0f), new[] { item });

            RuntimeUnit inBattle = factory.Create(relic, null, team: 0, slot.Position, new[] { item });
            UnitSnapshot inWorld = builder.Build(new[] { slot }, null)[0].Body;

            Assert.AreEqual(inBattle.Stats.Get(StatType.MaxHP), inWorld.MaxHP, 1e-4f,
                "Полоса здоровья во дворе и в бою — одно и то же число");
            Assert.AreEqual(inBattle.Stats.Get(StatType.Size), inWorld.Size, 1e-4f,
                "И размер тела тоже: иначе юнит меняет масштаб на входе в бой");
            Assert.AreEqual(inBattle.CurrentResource, inWorld.CurrentResource, 1e-4f);
        }

        // Баннеры команды действуют на всех — и на арене вне боя тоже, иначе полосы прыгнут в бою.
        [Test]
        public void PartyBanners_ReachEveryBody()
        {
            var builder = new WorldBodyBuilder(null, null);
            var roster  = new[] { new PlayerSlot(TestRelic.Make(), null, Vector2.zero) };

            float plain = builder.Build(roster, null)[0].Body.MaxHP;

            ItemData banner = TestItem.Make(new StatModifier(StatType.MaxHP, ModifierOp.Flat, 100f));
            float withBanner = builder.Build(roster, new[] { banner })[0].Body.MaxHP;

            Assert.AreEqual(plain + 100f, withBanner, 1e-4f, "Баннер поднял здоровье тела");
        }

        // ── помощники ────────────────────────────────────────────────────────────

        private static class TestItem
        {
            public static ItemData Make(params StatModifier[] mods)
                => ScriptableObject.CreateInstance<ItemData>().With("_mods", mods);
        }
    }
}
