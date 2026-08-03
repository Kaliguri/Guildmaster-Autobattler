using Guildmaster.Combat;
using Guildmaster.Combat.Effects;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Разложение стата для показа игроку (план UI-реворка §II.10.1). Главное свойство,
    /// ради которого шов существует: разбор НЕ является второй копией формулы —
    /// <c>Explain(x).Final</c> обязан совпадать с <c>Get(x)</c> на любом наборе модификаторов.
    /// Тесты headless — без StatsConfig SO.
    /// </summary>
    public sealed class StatsExplainTests
    {
        private const StatType ZeroStat = StatType.MaxHP;          // NaturalDefault = 0
        private const StatType OneStat  = StatType.DamageDealtEff; // NaturalDefault = 1

        private static Stats NewStats() => new Stats(null);

        private static StatModifier Mod(StatType stat, ModifierOp op, float value)
            => new StatModifier(stat, op, value);

        /// <summary>Источник, умеющий назвать себя, — так его увидит тултип.</summary>
        private sealed class NamedSource : IModifierSource
        {
            public NamedSource(string key) => ModifierSourceLocKey = key;
            public string ModifierSourceLocKey { get; }
        }

        // --- Главный инвариант: разбор сходится с симуляцией ---

        [Test]
        public void Explain_NoModifiers_FinalMatchesGet()
        {
            var stats = NewStats();
            Assert.AreEqual(stats.Get(ZeroStat), stats.Explain(ZeroStat).Final, 0.0001f);
            Assert.AreEqual(stats.Get(OneStat), stats.Explain(OneStat).Final, 0.0001f);
        }

        [Test]
        public void Explain_AllOpsMixed_FinalMatchesGet()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new NamedSource("relic.blade.name"), new[]
            {
                Mod(ZeroStat, ModifierOp.Override,    30f),
                Mod(ZeroStat, ModifierOp.Flat,        12f),
            });
            stats.AddModifiersFrom(new NamedSource("effect.rage.name"), new[]
            {
                Mod(ZeroStat, ModifierOp.PercentAdd,  0.08f),
                Mod(ZeroStat, ModifierOp.PercentMult, 0.25f),
            });

            Assert.AreEqual(stats.Get(ZeroStat), stats.Explain(ZeroStat).Final, 0.0001f);
        }

        [Test]
        public void Explain_AfterSourceRemoved_FinalMatchesGet()
        {
            var stats = NewStats();
            var keep = new NamedSource("relic.blade.name");
            var drop = new NamedSource("effect.rage.name");
            stats.AddModifiersFrom(keep, new[] { Mod(ZeroStat, ModifierOp.Flat, 10f) });
            stats.AddModifiersFrom(drop, new[] { Mod(ZeroStat, ModifierOp.Flat, 90f) });

            stats.RemoveModifiersFrom(drop);

            StatValue value = stats.Explain(ZeroStat);
            Assert.AreEqual(stats.Get(ZeroStat), value.Final, 0.0001f);
            Assert.AreEqual(1, value.Terms.Length);
        }

        // --- База и Override ---

        [Test]
        public void Explain_Override_BecomesBaseAndIsNotATerm()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new NamedSource("relic.blade.name"), new[]
            {
                Mod(ZeroStat, ModifierOp.Override, 30f),
                Mod(ZeroStat, ModifierOp.Flat,     12f),
            });

            StatValue value = stats.Explain(ZeroStat);

            Assert.AreEqual(30f, value.Base, 0.0001f, "Override задаёт базу");
            Assert.AreEqual(1, value.Terms.Length, "Override не показывается как бонус");
            Assert.AreEqual(ModifierOp.Flat, value.Terms[0].Op);
        }

        [Test]
        public void Explain_LastOverrideWins_SameAsGet()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new object(), new[] { Mod(ZeroStat, ModifierOp.Override, 30f) });
            stats.AddModifiersFrom(new object(), new[] { Mod(ZeroStat, ModifierOp.Override, 50f) });

            StatValue value = stats.Explain(ZeroStat);

            Assert.AreEqual(50f, value.Base, 0.0001f);
            Assert.AreEqual(stats.Get(ZeroStat), value.Final, 0.0001f);
        }

        [Test]
        public void Explain_NoModifiers_NotModifiedAndNoBonus()
        {
            StatValue value = NewStats().Explain(ZeroStat);

            Assert.IsFalse(value.IsModified);
            Assert.AreEqual(0, value.Terms.Length);
            Assert.AreEqual(0f, value.Bonus, 0.0001f);
        }

        // --- Вклады ---

        [Test]
        public void Explain_FlatOnly_ContributionsEqualValuesAndSumToBonus()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new NamedSource("relic.blade.name"), new[] { Mod(ZeroStat, ModifierOp.Flat, 12f) });
            stats.AddModifiersFrom(new NamedSource("effect.rage.name"), new[] { Mod(ZeroStat, ModifierOp.Flat,  8f) });

            StatValue value = stats.Explain(ZeroStat);

            Assert.AreEqual(2, value.Terms.Length);
            Assert.AreEqual(12f, value.Terms[0].Contribution, 0.0001f);
            Assert.AreEqual(8f, value.Terms[1].Contribution, 0.0001f);
            Assert.AreEqual(value.Bonus, value.Terms[0].Contribution + value.Terms[1].Contribution, 0.0001f);
        }

        [Test]
        public void Explain_PercentAdd_ContributionIsInStatUnitsNotRawValue()
        {
            var stats = NewStats();
            // База 100, затем +25 % — вклад должен читаться как «+25», а не как «0.25».
            stats.AddModifiersFrom(new NamedSource("relic.blade.name"), new[] { Mod(ZeroStat, ModifierOp.Override, 100f) });
            stats.AddModifiersFrom(new NamedSource("effect.rage.name"), new[] { Mod(ZeroStat, ModifierOp.PercentAdd, 0.25f) });

            StatValue value = stats.Explain(ZeroStat);

            Assert.AreEqual(1, value.Terms.Length);
            Assert.AreEqual(0.25f, value.Terms[0].Value, 0.0001f, "сырое значение сохраняется как есть");
            Assert.AreEqual(25f, value.Terms[0].Contribution, 0.0001f, "вклад — в единицах стата");
        }

        // --- Атрибуция источника ---

        [Test]
        public void Explain_NamedSource_ExposesLocKey()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new NamedSource("relic.blade.name"), new[] { Mod(ZeroStat, ModifierOp.Flat, 12f) });

            Assert.AreEqual("relic.blade.name", stats.Explain(ZeroStat).Terms[0].SourceLocKey);
        }

        [Test]
        public void Explain_AnonymousSource_HasNullLocKeyButStillContributes()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new object(), new[] { Mod(ZeroStat, ModifierOp.Flat, 12f) });

            StatTerm term = stats.Explain(ZeroStat).Terms[0];

            Assert.IsNull(term.SourceLocKey);
            Assert.AreEqual(12f, term.Contribution, 0.0001f);
        }

        // --- Боевой источник называет себя ---

        [Test]
        public void Explain_RuntimeEffectSource_NamesItselfByContentId()
        {
            EffectData def = EffectData.CreateRuntime(
                "effect.rage", EffectPolarity.Buff, EffectTag.None, 5f, false);
            var effect = new RuntimeEffect { Def = def };

            var stats = NewStats();
            stats.AddModifiersFrom(effect, new[] { Mod(ZeroStat, ModifierOp.Flat, 12f) });

            Assert.AreEqual("effect.rage.name", stats.Explain(ZeroStat).Terms[0].SourceLocKey);

            Object.DestroyImmediate(def);
        }

        [Test]
        public void Explain_RuntimeEffectWithoutDefinition_HasNoName()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new RuntimeEffect(), new[] { Mod(ZeroStat, ModifierOp.Flat, 12f) });

            Assert.IsNull(stats.Explain(ZeroStat).Terms[0].SourceLocKey);
        }

        // --- Размерность ---

        [Test]
        public void Explain_CarriesValueKindOfStat()
        {
            var stats = NewStats();

            Assert.AreEqual(ValueKind.Flat, stats.Explain(StatType.MaxHP).Kind);
            Assert.AreEqual(ValueKind.Percent, stats.Explain(StatType.Lifesteal).Kind);
            Assert.AreEqual(ValueKind.Multiplier, stats.Explain(StatType.DamageDealtEff).Kind);
            Assert.AreEqual(ValueKind.PerSecond, stats.Explain(StatType.AttackSpeed).Kind);
            Assert.AreEqual(ValueKind.Distance, stats.Explain(StatType.AttackRange).Kind);
            Assert.AreEqual(ValueKind.Count, stats.Explain(StatType.ProjectilePierce).Kind);
        }
    }
}
