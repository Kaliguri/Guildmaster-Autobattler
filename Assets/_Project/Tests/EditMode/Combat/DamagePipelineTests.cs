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
        private const float ArmorFull = 100f; // броня 100 → −50% урона

        private static RuntimeUnit MakeUnit(int team = 0, float maxHp = 1000f, float armorK = CombatTestValues.ArmorK)
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
                AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash,
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
            return new RuntimeUnit { Team = 1, Stats = stats, CurrentHP = maxHp, AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash };
        }

        private static DamageRequest Req(
            RuntimeUnit source,
            RuntimeUnit target,
            float raw,
            DamageType type = DamageType.Slash,
            float armorK = CombatTestValues.ArmorK)
            => new DamageRequest(source, target, raw, type, armorK);

        /// <summary>
        /// Весь путь урона: заявка + счёт пайплайном на коммите + применение реестром. Пайплайн сам щит и
        /// HP не трогает, поэтому тест проходит обе половины — иначе он проверял бы формулу, но не то, что
        /// она доходит до бойца.
        /// </summary>
        private static DamageResult Execute(in DamageRequest req)
        {
            var ledger = new TickLedger();
            ledger.AddDamage(req.Target, in req);

            var sink = new CapturingSink();
            ledger.Commit(sink);
            return sink.Damage;
        }

        /// <summary>
        /// Ловушка исходов реестра: тесту нужен результат, а не события наружу. Считает голым пайплайном —
        /// pre-damage цели здесь не при чём, его проверяют срезы китов.
        /// </summary>
        private sealed class CapturingSink : ITickLedgerSink
        {
            public DamageResult Damage;
            public float Healed;

            public DamageResolution ResolveIncoming(RuntimeUnit target, in DamageRequest req)
            {
                float dealt = DamagePipeline.Resolve(in req, out float mitigated);
                return new DamageResolution(dealt, mitigated, req.Vulnerability);
            }

            public void OnDamageResolved(RuntimeUnit source, RuntimeUnit target, in DamageResult result)
                => Damage = result;

            public void OnHealResolved(RuntimeUnit source, RuntimeUnit target, float applied)
                => Healed += applied;

            /// <summary>Раскладка поглощённого по авторам щитов — работа сима; здесь только сумма.</summary>
            public void OnShieldAbsorbed(RuntimeUnit target, float absorbed)
                => ShieldAbsorbed += absorbed;

            public float ShieldAbsorbed;
        }

        // --- True damage ---

        [Test]
        public void TrueDamage_BypassesArmor()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(physArmor: ArmorFull);
            float hpBefore = tgt.CurrentHP;

            var result = Execute(Req(src, tgt, 100f, DamageType.Pure));

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

            var result = Execute(Req(src, tgt, 100f, DamageType.Slash));

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
            var src = new RuntimeUnit { Team = 0, Stats = stats, CurrentHP = 1000f, AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash };
            var tgt = MakeUnitWithArmor(physArmor: ArmorFull);

            var result = Execute(Req(src, tgt, 100f, DamageType.Slash));

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
            var src = new RuntimeUnit { Team = 0, Stats = stats, CurrentHP = 1000f, AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash };
            var tgt = MakeUnitWithArmor(physArmor: ArmorFull);

            var result = Execute(Req(src, tgt, 100f, DamageType.Slash));

            float expectedMult = CombatTestValues.ArmorK / (CombatTestValues.ArmorK + 50f);
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
            var src = new RuntimeUnit { Team = 0, Stats = stats, CurrentHP = 1000f, AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash };
            var tgt = MakeUnitWithArmor(physArmor: 10f);

            var result = Execute(Req(src, tgt, 100f, DamageType.Slash));
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
            var tgt = new RuntimeUnit { Team = 1, Stats = stats, CurrentHP = 1000f, AutoAttackDamageType = Guildmaster.Data.Definitions.DamageType.Slash };

            var result = Execute(Req(src, tgt, 100f, DamageType.Pure));
            Assert.AreEqual(150f, result.TotalDamage, 0.01f);
        }

        // --- Щит ---

        [Test]
        public void Shield_AbsorbsBeforeHP()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(maxHp: 1000f);
            tgt.CurrentShield = 40f;

            var result = Execute(Req(src, tgt, 100f, DamageType.Pure));

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

            var result = Execute(Req(src, tgt, 100f, DamageType.Pure));

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

            var result = Execute(Req(src, tgt, 100f, DamageType.Pure));

            Assert.IsTrue(result.KilledTarget);
            Assert.LessOrEqual(tgt.CurrentHP, 0f);
        }

        [Test]
        public void Result_NotKilled_WhenHpRemains()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(maxHp: 200f);

            var result = Execute(Req(src, tgt, 100f, DamageType.Pure));

            Assert.IsFalse(result.KilledTarget);
            Assert.Greater(tgt.CurrentHP, 0f);
        }

        // --- Школа: стихийная броня (ГДД «8»: Огонь/Лёд/Молния под ОДНОЙ бронёй) ---

        [Test]
        public void MagicalDamage_MitigatedByMagicArmor_NotPhysArmor()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(physArmor: ArmorFull, magicArmor: 0f);

            var result = Execute(Req(src, tgt, 100f, DamageType.Arcane));

            // Физ. броня не гасит стихию — урон проходит полностью.
            Assert.AreEqual(100f, result.HpDamage, 0.01f);
        }

        [Test]
        public void MagicalDamage_MagicArmorHalfsMitigation()
        {
            var src = MakeUnit();
            var tgt = MakeUnitWithArmor(magicArmor: ArmorFull);

            var result = Execute(Req(src, tgt, 100f, DamageType.Arcane));

            Assert.AreEqual(50f, result.HpDamage, 0.01f);
        }

        // --- Тип существа: идентичность, а не множитель ---

        private static RuntimeUnit MakeCreature(CreatureType type, float maxHp = 1000f)
        {
            var unit = MakeUnitWithArmor(maxHp: maxHp);
            unit.Unit = TestRelic.Make(creatureType: type);
            return unit;
        }

        [Test]
        public void DamageType_NeverScalesDamage_ByCreatureType()
        {
            // Guard против возврата матрицы «тип урона x тип существа» (снята 2026-07-26, решение
            // 2026-07-15/35). Яд травит, свет лечит частью урона, тьма бьёт мощью — идентичность
            // работает ГЛАГОЛОМ и обязана быть одинаковой против любого врага. Прежняя таблица тихо
            // давала Тьме +30% почти всегда, потому что весь ростер Living.
            // Эталон берётся по Living, а не константой: физика и магия режутся бронёй, и сравнивать
            // надо цели между собой, а не с сырым числом.
            var src = MakeUnit();
            var types = new[]
            {
                CreatureType.Living, CreatureType.Undead, CreatureType.Construct,
                CreatureType.Demon, CreatureType.Beast,
            };

            foreach (DamageType damageType in DamageTypes.All)
            {
                float baseline = Execute(Req(src, MakeCreature(CreatureType.Living), 100f, damageType)).HpDamage;

                foreach (CreatureType type in types)
                {
                    var result = Execute(Req(src, MakeCreature(type), 100f, damageType));
                    Assert.AreEqual(baseline, result.HpDamage, 0.01f,
                        $"Тип урона {damageType} дал по цели {type} другое число — матрица вернулась.");
                }
            }
        }
    }
}
