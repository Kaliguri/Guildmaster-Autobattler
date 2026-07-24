using System.Reflection;
using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Классовый каскад стат-базы (ГДД «Combat - Stats» §Классы, решение 2026-07-24): класс даёт
    /// базу HP/скорости ПЕРВОЙ группой, персона кладёт дельту поверх, персон-Override перекрывает
    /// класс (обратная совместимость), Vessel «закрывает» (Override) и «перемножает» (PercentMult)
    /// поверх всего. Тесты headless — StatsConfig SO не нужен.
    /// </summary>
    public sealed class ClassBaselineTests
    {
        private const float Anchor = 2000f; // Брузер = 100% HP
        private const float AnchorMove = 3f;

        /// <summary>Конфиг с полной проектной сеткой (эталон в инициализаторах поля).</summary>
        private static ClassBalanceConfig MakeConfig()
        {
            var cfg = ScriptableObject.CreateInstance<ClassBalanceConfig>();
            SetField(cfg, "_profiles", new[]
            {
                new ClassBalanceConfig.ClassProfile(UnitClass.Bruiser,  1.00f, 1.00f),
                new ClassBalanceConfig.ClassProfile(UnitClass.Tank,     1.50f, 0.85f),
                new ClassBalanceConfig.ClassProfile(UnitClass.Assassin, 0.75f, 1.10f),
                new ClassBalanceConfig.ClassProfile(UnitClass.Ranged,   0.65f, 0.75f),
                new ClassBalanceConfig.ClassProfile(UnitClass.Support,  0.65f, 0.75f),
                new ClassBalanceConfig.ClassProfile(UnitClass.Summoner, 0.65f, 0.75f),
            });
            return cfg;
        }

        // --- База от класса ---

        [Test]
        public void Bruiser_IsAnchor()
        {
            var stats = new Stats(null);
            ClassBaseline.Apply(stats, TestRelic.Make(combatClass: UnitClass.Bruiser), MakeConfig());
            Assert.AreEqual(Anchor, stats.Get(StatType.MaxHP), 0.001f);
            Assert.AreEqual(AnchorMove, stats.Get(StatType.MoveSpeed), 0.001f);
        }

        [Test]
        public void Tank_ScalesHpUpAndMoveDown()
        {
            var stats = new Stats(null);
            ClassBaseline.Apply(stats, TestRelic.Make(combatClass: UnitClass.Tank), MakeConfig());
            Assert.AreEqual(3000f, stats.Get(StatType.MaxHP), 0.001f);
            Assert.AreEqual(2.55f, stats.Get(StatType.MoveSpeed), 0.001f);
        }

        [Test]
        public void Backline_IsSlowestAndFrailest()
        {
            var stats = new Stats(null);
            ClassBaseline.Apply(stats, TestRelic.Make(combatClass: UnitClass.Ranged), MakeConfig());
            Assert.AreEqual(1300f, stats.Get(StatType.MaxHP), 0.001f);
            Assert.AreEqual(2.25f, stats.Get(StatType.MoveSpeed), 0.001f);
        }

        // --- Дельта персоны поверх класса ---

        [Test]
        public void PersonaFlat_StacksOverClassBase()
        {
            var stats = new Stats(null);
            RelicData relic = TestRelic.Make(
                combatClass: UnitClass.Tank,
                stats: new[] { new StatModifier(StatType.MaxHP, ModifierOp.Flat, 200f) });

            // Порядок фабрики: класс ПЕРВОЙ группой, затем стат-блок персоны.
            ClassBaseline.Apply(stats, relic, MakeConfig());
            stats.AddModifiersFrom(relic, relic.Stats);

            Assert.AreEqual(3200f, stats.Get(StatType.MaxHP), 0.001f); // 3000 + 200
        }

        [Test]
        public void PersonaOverride_BeatsClassBase()
        {
            var stats = new Stats(null);
            RelicData relic = TestRelic.Make(
                combatClass: UnitClass.Tank,
                stats: new[] { new StatModifier(StatType.MaxHP, ModifierOp.Override, 1800f) });

            ClassBaseline.Apply(stats, relic, MakeConfig());
            stats.AddModifiersFrom(relic, relic.Stats);

            // Персон-Override перекрывает классовую базу (обратная совместимость со старым авторингом).
            Assert.AreEqual(1800f, stats.Get(StatType.MaxHP), 0.001f);
        }

        // --- Отсутствие конфига ---

        [Test]
        public void NullConfig_NoOp_FallsBackToDefaults()
        {
            var stats = new Stats(null);
            ClassBaseline.Apply(stats, TestRelic.Make(combatClass: UnitClass.Tank), null);
            // Без класс-конфига база не задаётся — натуральный дефолт MaxHP = 0.
            Assert.AreEqual(0f, stats.Get(StatType.MaxHP), 0.001f);
        }

        // --- Vessel: 4-й уровень каскада (через фабрику, чтобы проверить порядок групп) ---

        [Test]
        public void VesselOverride_ClosesBase_OverClassAndPersona()
        {
            var factory = MakeFactory(MakeConfig(), out _);
            // Персона тоже диктует базу через Override (1800) — Vessel должен перекрыть и класс, и её.
            RelicData relic = TestRelic.Make(
                combatClass: UnitClass.Tank,
                stats: new[] { new StatModifier(StatType.MaxHP, ModifierOp.Override, 1800f) });
            VesselData vessel = MakeVessel(new StatModifier(StatType.MaxHP, ModifierOp.Override, 5000f));

            RuntimeUnit unit = factory.Create(relic, vessel, team: 0, spawnPosition: Vector2.zero);

            // Vessel-группа добавляется последней → её Override — последний, побеждает как база.
            // (Ось базы закрывается; Flat/Percent-дельты — отдельная ось, копятся поверх — см. соседний тест.)
            Assert.AreEqual(5000f, unit.Stats.Get(StatType.MaxHP), 0.001f);
        }

        [Test]
        public void VesselPercentMult_MultipliesOverClassBase()
        {
            var factory = MakeFactory(MakeConfig(), out _);
            RelicData relic = TestRelic.Make(combatClass: UnitClass.Tank);
            VesselData vessel = MakeVessel(new StatModifier(StatType.MaxHP, ModifierOp.PercentMult, 0.2f));

            RuntimeUnit unit = factory.Create(relic, vessel, team: 0, spawnPosition: Vector2.zero);

            // 3000 (Tank base) × 1.2 = 3600.
            Assert.AreEqual(3600f, unit.Stats.Get(StatType.MaxHP), 0.001f);
        }

        // --- helpers ---

        private static RuntimeUnitFactory MakeFactory(ClassBalanceConfig classConfig, out EffectSystem effects)
        {
            effects = new EffectSystem();
            var ctx = new MockCombatContext(effects: effects);
            return new RuntimeUnitFactory(null, classConfig, effects, ctx);
        }

        private static VesselData MakeVessel(params StatModifier[] perks)
        {
            var v = ScriptableObject.CreateInstance<VesselData>();
            SetField(v, "_perkModifiers", perks);
            return v;
        }

        private static void SetField(object target, string field, object value)
        {
            FieldInfo fi = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(fi, $"Нет поля {field} в {target.GetType().Name}");
            fi.SetValue(target, value);
        }
    }
}
