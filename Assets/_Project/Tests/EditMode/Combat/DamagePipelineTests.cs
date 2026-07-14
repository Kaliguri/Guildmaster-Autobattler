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

        private static RuntimeUnit MakeUnitWithArmor(float physArmor = 0f, float elementalArmor = 0f, float maxHp = 1000f)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,      ModifierOp.Flat, maxHp),
                new StatModifier(StatType.PhysArmor,  ModifierOp.Flat, physArmor),
                new StatModifier(StatType.ElementalArmor, ModifierOp.Flat, elementalArmor),
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
        public void ElementalDamage_MitigatedByElementalArmor_NotPhysArmor()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(physArmor: ArmorFull, elementalArmor: 0f);

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.Elemental));

            // Физ. броня не гасит стихию — урон проходит полностью.
            Assert.AreEqual(100f, result.HpDamage, 0.01f);
        }

        [Test]
        public void ElementalDamage_ElementalArmorHalfsMitigation()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(elementalArmor: ArmorFull);

            var result = DamagePipeline.Execute(Req(src, tgt, 100f, DamageSchool.Elemental));

            Assert.AreEqual(50f, result.HpDamage, 0.01f);
        }

        // --- Сродство × тип существа (ГДД «8» §«Школа vs сродство») ---

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
        public void Poison_ImmuneAgainstUndeadAndConstruct()
        {
            var src = MakeUnit();

            var undead = MakeCreature(CreatureType.Undead);
            var construct = MakeCreature(CreatureType.Construct);

            Assert.AreEqual(0f, DamagePipeline.Execute(AffinityReq(src, undead, 100f, DamageAffinity.Poison)).HpDamage, 0.01f);
            Assert.AreEqual(0f, DamagePipeline.Execute(AffinityReq(src, construct, 100f, DamageAffinity.Poison)).HpDamage, 0.01f);
        }

        [Test]
        public void Poison_NeutralAgainstLivingAndBeast()
        {
            var src = MakeUnit();

            var living = MakeCreature(CreatureType.Living);
            var beast = MakeCreature(CreatureType.Beast);

            Assert.AreEqual(100f, DamagePipeline.Execute(AffinityReq(src, living, 100f, DamageAffinity.Poison)).HpDamage, 0.01f);
            Assert.AreEqual(100f, DamagePipeline.Execute(AffinityReq(src, beast, 100f, DamageAffinity.Poison)).HpDamage, 0.01f);
        }

        [Test]
        public void Light_VulnerableAgainstUndeadAndDemon()
        {
            var src = MakeUnit();

            var undead = MakeCreature(CreatureType.Undead);
            var demon = MakeCreature(CreatureType.Demon);
            var living = MakeCreature(CreatureType.Living);

            float expected = 100f * AffinityTable.VulnerableMult;
            Assert.AreEqual(expected, DamagePipeline.Execute(AffinityReq(src, undead, 100f, DamageAffinity.Light)).HpDamage, 0.01f);
            Assert.AreEqual(expected, DamagePipeline.Execute(AffinityReq(src, demon, 100f, DamageAffinity.Light)).HpDamage, 0.01f);
            Assert.AreEqual(100f, DamagePipeline.Execute(AffinityReq(src, living, 100f, DamageAffinity.Light)).HpDamage, 0.01f);
        }

        [Test]
        public void Dark_VulnerableAgainstLiving()
        {
            var src = MakeUnit();

            var living = MakeCreature(CreatureType.Living);
            var undead = MakeCreature(CreatureType.Undead);

            Assert.AreEqual(100f * AffinityTable.VulnerableMult,
                DamagePipeline.Execute(AffinityReq(src, living, 100f, DamageAffinity.Dark)).HpDamage, 0.01f);
            Assert.AreEqual(100f, DamagePipeline.Execute(AffinityReq(src, undead, 100f, DamageAffinity.Dark)).HpDamage, 0.01f);
        }

        [Test]
        public void Affinity_AppliesOnTopOfTrueDamage_AndIsNotMitigatedByArmor()
        {
            var src = MakeUnit();

            // Цель с полной физ. бронёй: True идёт мимо брони, сродство Тьмы всё равно множит.
            var living = MakeCreature(CreatureType.Living);
            living.Stats.AddModifiersFrom("armor", new[] { new StatModifier(StatType.PhysArmor, ModifierOp.Flat, ArmorFull) });

            var result = DamagePipeline.Execute(AffinityReq(src, living, 100f, DamageAffinity.Dark, DamageSchool.True));

            Assert.AreEqual(100f * AffinityTable.VulnerableMult, result.HpDamage, 0.01f);
        }

        [Test]
        public void Affinity_None_LeavesDamageUnchanged()
        {
            var src = MakeUnit();
            var undead = MakeCreature(CreatureType.Undead);

            var result = DamagePipeline.Execute(AffinityReq(src, undead, 100f, DamageAffinity.None, DamageSchool.True));

            Assert.AreEqual(100f, result.HpDamage, 0.01f);
        }
    }
}
