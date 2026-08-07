using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Abilities;
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
    /// Механики китов врагов, которых нет ни у одного героя: удар в тыл гоблина-убийцы
    /// (<see cref="RearDamageBonusComponent"/>), инстинкт стаи волка
    /// (<see cref="AllyProximityDamageBonusComponent"/>) и проводка шкалы антихила
    /// (30/50/75/100% — <see cref="StatType.HealShieldTakenEff"/>, кит разбойника с отравленным арбалетом).
    /// </summary>
    /// <remarks>
    /// Числа здесь — не копия ассетов, а те, что делают инвариант читаемым; за контентными значениями
    /// ходят в сами ассеты. Проверяются именно инварианты между файлами: «носитель в счёт стаи не идёт»,
    /// «бонус тыла не достаётся фронтальному удару», «−50% антихила режет хил ровно вдвое» — каждый живёт
    /// на шве двух систем и комментарием в одном файле не удерживается.
    /// </remarks>
    public sealed class EnemyKitsSliceTests
    {
        // ===================== Удар в тыл (гоблин-убийца) =====================

        [Test]
        public void RearDamageBonus_AppliesFromRear_NotFromFront()
        {
            // Тыл цели — сторона её «дома» (FleeSteering.HomeDir): у команды 1 это +X.
            Assert.AreEqual(150f, DamageWithBackstab(attackerAt: new Vector2(2f, 0f)), 1e-3f,
                "Удар со спины: 100 × (1 + 0.5)");
            Assert.AreEqual(100f, DamageWithBackstab(attackerAt: new Vector2(-2f, 0f)), 1e-3f,
                "Удар в лицо: бонуса нет");
        }

        /// <summary>Урон, нанесённый носителем «удара в тыл» цели в (0,0), из позиции <paramref name="attackerAt"/>.</summary>
        private static float DamageWithBackstab(Vector2 attackerAt)
        {
            var sim = BuildSim();
            var victim   = MakeUnit(0, team: 1, pos: Vector2.zero, maxHp: 1000f);
            var attacker = MakeUnit(1, team: 0, pos: attackerAt);

            sim.ApplyEffect(attacker, Backstab(0.5f), attacker);
            EffectSystem.CommitPending(attacker);

            sim.DealDamage(new DamageRequest(attacker, victim, 100f, DamageType.Pure, sim.ArmorK,
                sourceKind: DamageSourceKind.AutoAttack));
            sim.Tick(SimConstants.TickDelta);

            return 1000f - victim.CurrentHP;
        }

        // ===================== Инстинкт стаи (волк) =====================

        [Test]
        public void PackInstinct_ScalesWithAlliesNearby_AndDoesNotCountSelf()
        {
            Assert.AreEqual(100f, PackDamage(alliesNear: 0), 1e-3f,
                "Одинокий волк бонуса не получает — носитель себя в стаю не считает");
            Assert.AreEqual(115f, PackDamage(alliesNear: 1), 1e-3f, "Один союзник рядом: +15%");
            Assert.AreEqual(145f, PackDamage(alliesNear: 3), 1e-3f, "Три союзника рядом: +45%");
        }

        [Test]
        public void PackInstinct_IgnoresAlliesBeyondRadius()
        {
            Assert.AreEqual(100f, PackDamage(alliesNear: 0, alliesFar: 3), 1e-3f,
                "Союзники за радиусом стаи в счёт не идут");
        }

        [Test]
        public void PackInstinct_RespectsCap()
        {
            Assert.AreEqual(130f, PackDamage(alliesNear: 5, maxAllies: 2), 1e-3f,
                "Потолок обрезает счёт союзников: +15% × 2");
        }

        /// <summary>
        /// Урон волка-носителя по врагу при заданном составе стаи. Союзники «рядом» стоят в 1 единице,
        /// «далеко» — в 12 (радиус инстинкта 3).
        /// </summary>
        private static float PackDamage(int alliesNear, int alliesFar = 0, int maxAllies = 0)
        {
            var sim = BuildSim();
            var wolf   = MakeUnit(0, team: 0, pos: Vector2.zero);
            var victim = MakeUnit(1, team: 1, pos: new Vector2(1.5f, 0f), maxHp: 1000f);
            sim.EnqueueUnitSpawn(wolf);
            sim.EnqueueUnitSpawn(victim);

            for (int i = 0; i < alliesNear; i++)
                sim.EnqueueUnitSpawn(MakeUnit(10 + i, team: 0, pos: new Vector2(0f, 1f + i * 0.1f)));
            for (int i = 0; i < alliesFar; i++)
                sim.EnqueueUnitSpawn(MakeUnit(50 + i, team: 0, pos: new Vector2(0f, 12f + i)));

            // Первый тик заводит заспавненных в пространственный хеш — без него запрос по радиусу пуст.
            sim.Tick(SimConstants.TickDelta);

            sim.ApplyEffect(wolf, PackInstinct(0.15f, radius: 3f, maxAllies: maxAllies), wolf);
            EffectSystem.CommitPending(wolf);

            float before = victim.CurrentHP;
            sim.DealDamage(new DamageRequest(wolf, victim, 100f, DamageType.Pure, sim.ArmorK,
                sourceKind: DamageSourceKind.AutoAttack));
            sim.Tick(SimConstants.TickDelta);

            return before - victim.CurrentHP;
        }

        // ===================== Шкала антихила (отравленный арбалет) =====================

        [Test]
        public void AntihealScale_CutsIncomingHeal_ByItsTier()
        {
            Assert.AreEqual(70f, HealedUnder(-0.30f), 1e-3f, "Слабый антихил: 100 → 70");
            Assert.AreEqual(50f, HealedUnder(-0.50f), 1e-3f, "Средний (кит арбалетчика): 100 → 50");
            Assert.AreEqual(25f, HealedUnder(-0.75f), 1e-3f, "Сильный: 100 → 25");
            Assert.AreEqual(0f,  HealedUnder(-1.00f), 1e-3f, "Абсолютный: хил не проходит вовсе");
        }

        /// <summary>Сколько HP реально вернул хил на 100 юниту под антихилом заданной силы.</summary>
        private static float HealedUnder(float pct)
        {
            var sim = BuildSim();
            var wounded = MakeUnit(0, team: 0, pos: Vector2.zero, maxHp: 1000f);
            wounded.CurrentHP = 500f;
            var healer = MakeUnit(1, team: 0, pos: new Vector2(1f, 0f));

            sim.ApplyEffect(wounded, Antiheal(pct), healer);
            EffectSystem.CommitPending(wounded);

            sim.Heal(wounded, 100f, healer);
            sim.Tick(SimConstants.TickDelta);

            return wounded.CurrentHP - 500f;
        }

        // ===================== Фабрики контента =====================

        /// <summary>«Удар в тыл» гоблина-убийцы (ассет GoblinBackstab).</summary>
        private static EffectData Backstab(float bonus)
        {
            var comp = new RearDamageBonusComponent()
                .With("_bonus", bonus)
                .With("_rearConeCos", 0.5f)
                .With("_autoAttackOnly", true);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: comp);
        }

        /// <summary>«Инстинкт стаи» волка (ассет PackInstinct).</summary>
        private static EffectData PackInstinct(float perAlly, float radius, int maxAllies)
        {
            var comp = new AllyProximityDamageBonusComponent()
                .With("_bonusPerAlly", perAlly)
                .With("_radius", radius)
                .With("_maxAllies", maxAllies)
                .With("_autoAttackOnly", true);
            return TestEffect.Make(baseDuration: -1f, polarity: EffectPolarity.Neutral, components: comp);
        }

        /// <summary>Ступень шкалы антихила (ассеты Antiheal*): множитель получаемого хила.</summary>
        private static EffectData Antiheal(float pct)
        {
            var comp = new StatModifierComponent()
                .With("_modifiers", new[]
                {
                    new StatModifier(StatType.HealShieldTakenEff, ModifierOp.PercentMult, pct),
                });
            return TestEffect.Make(baseDuration: 4f, polarity: EffectPolarity.Debuff,
                components: comp);
        }

        private static CombatSimulation BuildSim() =>
            new CombatSimulation(
                new Guildmaster.Core.Random.XorShiftRng(1UL), CombatTestValues.ArmorK,
                new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(),
                new AutoAttackSystem(), new ProjectileSystem(), new DeathSystem(),
                new EffectSystem(), new RegenSystem());

        private static RuntimeUnit MakeUnit(int id, int team, Vector2 pos, float maxHp = 500f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, maxHp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, 10f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 5f),
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
                AutoAttackDamageType = DamageType.Slash,
            };
        }
    }
}
