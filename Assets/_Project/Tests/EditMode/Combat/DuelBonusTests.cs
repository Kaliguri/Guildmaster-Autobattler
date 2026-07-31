using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Дуэль разбойника-дуэлянта (<see cref="DuelDamageBonusComponent"/>): прибавка к урону живёт, пока
    /// рядом с носителем не больше одного противника, и гаснет в свалке.
    /// </summary>
    /// <remarks>
    /// Инвариант между файлами: порог считается по врагам вокруг <b>носителя</b>, а цель входит в счёт.
    /// Ошибись в любом из двух — и кит останется играбельным, просто станет другим: при счёте вокруг цели
    /// он получал бы бонус, вклинившись в чужой строй, а без цели в счёте «только один враг» означало бы
    /// «ноль врагов», то есть бонус не включался бы никогда. Ни то, ни другое из боя не видно.
    /// </remarks>
    public sealed class DuelBonusTests
    {
        [Test]
        public void Duel_BonusApplies_WhenOnlyOneEnemyIsNear()
        {
            float damage = FirstHitAgainst(enemies: 1);

            Assert.AreEqual(150f, damage, 1e-2f, "Один противник рядом: 100 урона + 50%");
        }

        [Test]
        public void Duel_BonusGone_InACrowd()
        {
            float damage = FirstHitAgainst(enemies: 3);

            Assert.AreEqual(100f, damage, 1e-2f, "Трое рядом — размен кончился, прибавки нет");
        }

        [Test]
        public void Duel_TargetItselfCounts_SoTheThresholdIsOne()
        {
            // Порог 1 обязан включать бонус против одиночной цели: она и есть тот единственный «рядом».
            // Если бы цель не входила в счёт, работающим порогом было бы 0 — и карточка врала бы.
            var duel = new DuelDamageBonusComponent()
                .With("_bonus", 0.5f)
                .With("_radius", 3f)
                .With("_maxEnemies", 0);   // Min(1) в инспекторе, но в тесте проверяем именно смысл нуля

            Assert.AreEqual(0f, BonusOf(duel, enemies: 1), 1e-4f,
                "Порог ноль не включает бонус ни при одном враге — значит цель в счёте, и порог кита это 1");
        }

        // ===================== Обвязка =====================

        /// <summary>Урон первого удара дуэлянта, когда вокруг него стоит <paramref name="enemies"/> врагов.</summary>
        private static float FirstHitAgainst(int enemies)
        {
            var sim = BuildSim();
            var duelist = MakeUnit(0, team: 0, pos: Vector2.zero);
            sim.EnqueueUnitSpawn(duelist);

            for (int i = 0; i < enemies; i++)
                sim.EnqueueUnitSpawn(MakeUnit(i + 1, team: 1, pos: new Vector2(1f + i * 0.5f, 0f),
                                              maxHp: 1_000_000f, damage: 0f));

            sim.Tick(SimConstants.TickDelta);

            var hits = new List<float>();
            sim.OnDamageDealt += (src, tgt, res) => { if (src == duelist) hits.Add(res.HpDamage); };

            sim.ApplyEffect(duelist, DuelBonus(), duelist);
            EffectSystem.CommitPending(duelist);

            for (int t = 0; t < SimConstants.TickRate * 5 && hits.Count == 0; t++)
                sim.Tick(SimConstants.TickDelta);

            Assert.AreEqual(1, hits.Count, "Предусловие: дуэлянт успел ударить");
            return hits[0];
        }

        /// <summary>Прибавка, которую компонент выдаёт напрямую — без боя, чтобы проверить сам порог.</summary>
        private static float BonusOf(DuelDamageBonusComponent comp, int enemies)
        {
            var sim = BuildSim();
            var carrier = MakeUnit(0, team: 0, pos: Vector2.zero);
            sim.EnqueueUnitSpawn(carrier);
            for (int i = 0; i < enemies; i++)
                sim.EnqueueUnitSpawn(MakeUnit(i + 1, team: 1, pos: new Vector2(1f, 0f), damage: 0f));
            sim.Tick(SimConstants.TickDelta);

            EffectData def = TestEffect.Make(baseDuration: -1f, components: comp);
            sim.ApplyEffect(carrier, def, carrier);
            EffectSystem.CommitPending(carrier);

            RuntimeEffect runtime = carrier.ActiveEffects[0];
            var ctx = new EffectContext(carrier, carrier, sim, runtime, potency: 0f, dt: SimConstants.TickDelta);
            return comp.BonusAgainst(carrier, carrier.CurrentTarget, isAutoAttack: true, in ctx);
        }

        private static EffectData DuelBonus()
        {
            var comp = new DuelDamageBonusComponent()
                .With("_bonus", 0.5f)
                .With("_radius", 3f)
                .With("_maxEnemies", 1)
                .With("_autoAttackOnly", true);
            return TestEffect.Make(baseDuration: -1f, components: comp);
        }

        private static CombatSimulation BuildSim() =>
            new CombatSimulation(
                new Guildmaster.Core.Random.XorShiftRng(31UL), CombatTestValues.ArmorK,
                new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new Guildmaster.Combat.AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp = 500f,
                                            float damage = 100f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, damage),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 3f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 0f),
            });
            return new RuntimeUnit
            {
                Id                   = id,
                Team                 = team,
                Stats                = stats,
                CurrentHP            = maxHp,
                Position             = pos,
                PreviousPosition     = pos,
                AutoAttackDamageType = DamageType.Pure,
            };
        }
    }
}
