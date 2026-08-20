using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Щит: кому засчитывается поглощённое и что остаётся в пуле после истечения одного из щитов.
    /// </summary>
    /// <remarks>
    /// Инвариант живёт между тремя файлами и потому обязан жить в тесте: пул щита один
    /// (<c>RuntimeUnit.CurrentShield</c>), расходует его реестр тика (<c>TickLedger</c>), а держат его
    /// отдельные <c>RuntimeEffect</c> разных авторов. Комментарий в любом из троих виден только своей
    /// стороне шва.
    /// </remarks>
    public sealed class ShieldAttributionTests
    {
        /// <summary>
        /// Поглощённое делится между авторами пропорционально удерживаемым долям — а не достаётся
        /// целиком тому, чей щит наложили первым.
        /// </summary>
        [Test]
        public void Absorbed_SplitsBetweenAuthors_ByHeldShare()
        {
            var effects = new EffectSystem();
            CombatSimulation sim = BuildSim(1UL, effects);

            RuntimeUnit target   = MakeUnit(0, team: 0);
            RuntimeUnit authorA  = MakeUnit(1, team: 0);
            RuntimeUnit authorB  = MakeUnit(2, team: 0);
            RuntimeUnit attacker = MakeUnit(3, team: 1);
            foreach (RuntimeUnit u in new[] { target, authorA, authorB, attacker }) sim.EnqueueUnitSpawn(u);
            sim.Tick(SimConstants.TickDelta);

            effects.Apply(target, MakeShield(100f, duration: 10f), authorA, sim);
            effects.Apply(target, MakeShield(300f, duration: 10f), authorB, sim);
            Assert.AreEqual(400f, target.CurrentShield, 1e-3f, "Оба щита в общем пуле");

            float creditedA = 0f, creditedB = 0f;
            sim.OnShieldAbsorbed += (author, _, amount) =>
            {
                if (ReferenceEquals(author, authorA)) creditedA += amount;
                if (ReferenceEquals(author, authorB)) creditedB += amount;
            };

            sim.DealDamage(new DamageRequest(attacker, target, 200f, DamageType.Pure, CombatTestValues.ArmorK));
            sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(200f, target.CurrentShield, 1e-3f, "Поглощено 200 из 400");
            Assert.AreEqual(50f, creditedA, 1e-3f, "Автору четверти пула — четверть поглощённого");
            Assert.AreEqual(150f, creditedB, 1e-3f, "Автору трёх четвертей — три четверти");
        }

        /// <summary>
        /// Истечение щита снимает его СОБСТВЕННЫЙ остаток, а не выданную когда-то величину: иначе
        /// закончившийся щит уносил бы с собой чужой.
        /// </summary>
        [Test]
        public void ExpiringShield_TakesOnlyItsRemainder_NotTheWholeGrant()
        {
            var effects = new EffectSystem();
            CombatSimulation sim = BuildSim(2UL, effects);

            RuntimeUnit target   = MakeUnit(0, team: 0);
            RuntimeUnit shortLived = MakeUnit(1, team: 0);
            RuntimeUnit longLived  = MakeUnit(2, team: 0);
            RuntimeUnit attacker   = MakeUnit(3, team: 1);
            foreach (RuntimeUnit u in new[] { target, shortLived, longLived, attacker }) sim.EnqueueUnitSpawn(u);
            sim.Tick(SimConstants.TickDelta);

            effects.Apply(target, MakeShield(200f, duration: 1f), shortLived, sim);
            effects.Apply(target, MakeShield(200f, duration: 30f), longLived, sim);

            sim.DealDamage(new DamageRequest(attacker, target, 200f, DamageType.Pure, CombatTestValues.ArmorK));
            sim.Tick(SimConstants.TickDelta);
            Assert.AreEqual(200f, target.CurrentShield, 1e-3f, "Половина пула съедена — по 100 с каждого щита");

            // Тридцать тиков — секунда: короткий щит истекает, длинный держится дальше.
            for (int t = 0; t < 40; t++) sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(100f, target.CurrentShield, 1e-3f,
                "Истёкший щит уносит свои 100, а не выданные 200 — остаток соседа цел");
        }

        private static EffectData MakeShield(float amount, float duration)
        {
            var shield = new ShieldComponent().With("_amount", new ScalableValue(amount));
            return TestEffect.Make(baseDuration: duration, tags: EffectTag.Shield, components: shield);
        }

        private static CombatSimulation BuildSim(ulong seed, EffectSystem effects) =>
            new CombatSimulation(
                new XorShiftRng(seed), CombatTestValues.ArmorK, new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                effects, new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 10000f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 0f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 0f),
            });
            return new RuntimeUnit
            {
                Id                   = id,
                Team                 = team,
                Stats                = stats,
                CurrentHP            = 10000f,
                Position             = new Vector2(id * 20f, 0f),
                PreviousPosition     = new Vector2(id * 20f, 0f),
                AutoAttackDamageType = DamageType.Slash,
            };
        }
    }
}
