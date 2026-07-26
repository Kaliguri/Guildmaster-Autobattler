using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Пайплайн урона: формула броня/пробивание, эффективности, щит (вики «10» §5.4, «6» §6).
    /// Тесты headless — без StatsConfig SO, без RNG (крит — Фаза 4).
    /// </summary>
    public sealed class DamagePipelineTests
    {
        private const float ArmorK    = 100f;
        private const float ArmorFull = 100f; // броня 100 → −50% урона

        private static RuntimeUnit MakeUnit(int team = 0, float maxHp = 1000f, float armorK = ArmorK)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,         ModifierOp.Flat, maxHp),
                new StatModifier(StatType.DamageDealtEff, ModifierOp.Flat, 0f),   // base=1 from NaturalDefault
            });
            return new RuntimeUnit
            {
                Team      = team,
                Stats     = stats,
                CurrentHP = maxHp,
            };
        }

        private static RuntimeUnit MakeUnitWithArmor(float physArmor = 0f, float magicArmor = 0f, float maxHp = 1000f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,      ModifierOp.Flat, maxHp),
                new StatModifier(StatType.PhysArmor,  ModifierOp.Flat, physArmor),
                new StatModifier(StatType.MagicArmor, ModifierOp.Flat, magicArmor),
            });
            return new RuntimeUnit { Team = 1, Stats = stats, CurrentHP = maxHp };
        }

        private static DamageRequest Req(
            RuntimeUnit source,
            RuntimeUnit target,
            float raw,
            DamageSchool type = DamageSchool.Physical,
            float armorK = ArmorK)
            => new DamageRequest(source, target, raw, type, armorK);

        // --- True damage ---

        [Test]
        public void TrueDamage_BypassesArmor()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(physArmor: ArmorFull);
            float hpBefore = tgt.CurrentHP;

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.True));

            Assert.AreEqual(100f, result.TotalDamage, 0.01f, "True damage не уменьшается броней");
            Assert.AreEqual(hpBefore - 100f, tgt.CurrentHP, 0.01f);
        }

        // --- Броня ---

        [Test]
        public void PhysicalDamage_ArmorHalfsMitigation()
        {
            // Броня 100 при K=100 → mult = 100/(100+100) = 0.5
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(physArmor: ArmorFull);

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.Physical));

            Assert.AreEqual(50f, result.TotalDamage, 0.01f);
        }

        [Test]
        public void PhysPen_Flat_ReducesEffectiveArmor()
        {
            // Броня 100, плоское пробивание 100 → effArmor = max(0, 100*1 − 100) = 0 → нет митигации
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,   ModifierOp.Flat, 1000f),
                new StatModifier(StatType.PhysPen, ModifierOp.Flat, 100f),
            });
            var src = new RuntimeUnit { Team = 0, Stats = stats, CurrentHP = 1000f };
            var tgt = MakeUnitWithArmor(physArmor: ArmorFull);

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.Physical));

            Assert.AreEqual(100f, result.TotalDamage, 0.01f, "Полное пробивание → 0 брони");
        }

        [Test]
        public void PhysPenPct_ReducesEffectiveArmor()
        {
            // Броня 100, 50% процентное пробивание → effArmor = 100*0.5 = 50 → mult = 100/150 ≈ 0.667
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,      ModifierOp.Flat, 1000f),
                new StatModifier(StatType.PhysPenPct, ModifierOp.Flat, 0.5f),
            });
            var src = new RuntimeUnit { Team = 0, Stats = stats, CurrentHP = 1000f };
            var tgt = MakeUnitWithArmor(physArmor: ArmorFull);

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.Physical));

            float expectedMult = ArmorK / (ArmorK + 50f);
            Assert.AreEqual(100f * expectedMult, result.TotalDamage, 0.01f);
        }

        [Test]
        public void EffectiveArmor_ClampedAtZero()
        {
            // Пробивание превышает броню → эффективная броня = 0, не отрицательная
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,   ModifierOp.Flat, 1000f),
                new StatModifier(StatType.PhysPen, ModifierOp.Flat, 9999f),
            });
            var src = new RuntimeUnit { Team = 0, Stats = stats, CurrentHP = 1000f };
            var tgt = MakeUnitWithArmor(physArmor: 10f);

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.Physical));
            Assert.AreEqual(100f, result.TotalDamage, 0.01f);
        }

        // --- DamageTakenEff ---

        [Test]
        public void DamageTakenEff_ScalesFinalDamage()
        {
            var src = MakeUnit();
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,          ModifierOp.Flat,       1000f),
                new StatModifier(StatType.DamageTakenEff, ModifierOp.PercentMult, 0.5f), // +50%
            });
            // base=1, PercentMult 0.5 → 1*(1+0.5)=1.5
            var tgt = new RuntimeUnit { Team = 1, Stats = stats, CurrentHP = 1000f };

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.True));
            Assert.AreEqual(150f, result.TotalDamage, 0.01f);
        }

        // --- Щит ---

        [Test]
        public void Shield_AbsorbsBeforeHP()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(maxHp: 1000f);
            tgt.CurrentShield = 40f;

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.True));

            Assert.AreEqual(40f,  result.ShieldDamage, 0.01f);
            Assert.AreEqual(60f,  result.HpDamage,     0.01f);
            Assert.AreEqual(0f,   tgt.CurrentShield,   0.01f);
            Assert.AreEqual(940f, tgt.CurrentHP,       0.01f);
        }

        [Test]
        public void Shield_FullAbsorption_HpUnchanged()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(maxHp: 1000f);
            tgt.CurrentShield = 200f;

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.True));

            Assert.AreEqual(100f,  result.ShieldDamage, 0.01f);
            Assert.AreEqual(0f,    result.HpDamage,     0.01f);
            Assert.AreEqual(1000f, tgt.CurrentHP,       0.01f);
        }

        // --- Смерть ---

        [Test]
        public void Result_KilledTarget_WhenHpReachesZero()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(maxHp: 100f);

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.True));

            Assert.IsTrue(result.KilledTarget);
            Assert.LessOrEqual(tgt.CurrentHP, 0f);
        }

        [Test]
        public void Result_NotKilled_WhenHpRemains()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(maxHp: 200f);

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.True));

            Assert.IsFalse(result.KilledTarget);
            Assert.Greater(tgt.CurrentHP, 0f);
        }

        // --- Школа: стихийная броня (ГДД «8»: Огонь/Лёд/Молния под ОДНОЙ бронёй) ---

        [Test]
        public void MagicalDamage_MitigatedByMagicArmor_NotPhysArmor()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(physArmor: ArmorFull, magicArmor: 0f);

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.Magical));

            // Физ. броня не гасит стихию — урон проходит полностью.
            Assert.AreEqual(100f, result.HpDamage, 0.01f);
        }

        [Test]
        public void MagicalDamage_MagicArmorHalfsMitigation()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(magicArmor: ArmorFull);

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.Magical));

            Assert.AreEqual(50f, result.HpDamage, 0.01f);
        }

        // --- Сродство: идентичность, а не множитель ---

        private static RuntimeUnit MakeCreature(CreatureType type, float maxHp = 1000f)
        {
            var unit = MakeUnitWithArmor(maxHp: maxHp);
            unit.Unit = TestRelic.Make(creatureType: type);
            return unit;
        }

        private static DamageRequest AffinityReq(RuntimeUnit src, RuntimeUnit tgt, float raw,
            DamageAffinity affinity, DamageSchool school = DamageSchool.Physical)
            => new DamageRequest(src, tgt, raw, school, ArmorK, sourceKind: DamageSourceKind.Ability, affinity: affinity);

        [Test]
        public void Affinity_NeverScalesDamage_ByCreatureType()
        {
            // Guard против возврата матрицы «сродство × тип существа» (снята 2026-07-26, решение
            // 2026-07-15/35). Сродство несёт идентичность ГЛАГОЛОМ — яд травит, свет лечит частью
            // урона, тьма бьёт мощью, — и обязано работать против любого врага одинаково. Прежняя
            // таблица тихо давала Тьме +30% почти всегда, потому что весь ростер Living.
            var src = MakeUnit();
            var affinities = new[]
            {
                DamageAffinity.None, DamageAffinity.Poison, DamageAffinity.Light, DamageAffinity.Dark,
            };
            var types = new[]
            {
                CreatureType.Living, CreatureType.Undead, CreatureType.Construct,
                CreatureType.Demon, CreatureType.Beast,
            };

            foreach (DamageAffinity affinity in affinities)
            foreach (CreatureType type in types)
            {
                var target = MakeCreature(type);
                var result = DamagePipeline.Execute(AffinityReq(src, target, 100f, affinity, DamageSchool.True));

                Assert.AreEqual(100f, result.HpDamage, 0.01f,
                    $"Сродство {affinity} изменило урон по цели {type} — матрица вернулась.");
            }
        }
    }
}
