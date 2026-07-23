using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Descriptions;
using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Слой описаний (план UI-реворка §II.10.1–II.10.4): форматирование величин и сборка
    /// подробного разбора. Ключевое, что здесь проверяется — краткий и подробный вид растут
    /// из ОДНОГО значения, без второго текста описания.
    /// </summary>
    public sealed class DescriptionTests
    {
        /// <summary>Локализация-заглушка: отдаёт ключ как значение, чтобы видеть, что именно спросили.</summary>
        private sealed class EchoLocalization : ILocalizationService
        {
            private readonly Dictionary<string, string> _map;

            public EchoLocalization(Dictionary<string, string> map) => _map = map;

            public string CurrentLocale => "ru";
            public IReadOnlyList<string> AvailableLocales => new[] { "ru" };
            public event Action LocaleChanged;

            public string GetString(string key) => _map.TryGetValue(key, out string v) ? v : string.Empty;
            public string GetString(string table, string key) => GetString(key);
            public string GetString(string key, IReadOnlyDictionary<string, object> args) => GetString(key);
            public string GetString(string table, string key, IReadOnlyDictionary<string, object> args) => GetString(key);
            public void SetLocale(string localeCode) => LocaleChanged?.Invoke();
        }

        private static DescriptionService NewService(params (string key, string value)[] entries)
        {
            var map = new Dictionary<string, string>();
            foreach ((string key, string value) in entries) map[key] = value;
            return new DescriptionService(new EchoLocalization(map));
        }

        private static Stats NewStats() => new Stats(null);

        private static StatModifier Mod(StatType stat, ModifierOp op, float value)
            => new StatModifier(stat, op, value);

        private sealed class NamedSource : IModifierSource
        {
            public NamedSource(string key) => ModifierSourceLocKey = key;
            public string ModifierSourceLocKey { get; }
        }

        // --- Один источник, два вида ---

        [Test]
        public void DescribeStat_Brief_ShowsOnlyFinalValue()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new NamedSource("effect.rage.name"), new[] { Mod(StatType.MaxHP, ModifierOp.Flat, 12f) });

            Assert.AreEqual("12", NewService().DescribeStat(stats, StatType.MaxHP, false));
        }

        [Test]
        public void DescribeStat_Detailed_ShowsBaseContributionAndSourceName()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new NamedSource("relic.blade.name"), new[]
            {
                Mod(StatType.MaxHP, ModifierOp.Override, 30f),
                Mod(StatType.MaxHP, ModifierOp.Flat,     12f),
            });

            string text = NewService(("relic.blade.name", "Пылающий клинок"))
                .DescribeStat(stats, StatType.MaxHP, true);

            Assert.AreEqual("30 + 12 (Пылающий клинок) = 42", text);
        }

        [Test]
        public void DescribeStat_DetailedButUnmodified_FallsBackToBriefForm()
        {
            // Разбор нечего показывать — подробный режим не должен рисовать «30 = 30».
            Assert.AreEqual("0", NewService().DescribeStat(NewStats(), StatType.MaxHP, true));
        }

        [Test]
        public void DescribeStat_AnonymousSource_ShowsContributionWithoutName()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new object(), new[] { Mod(StatType.MaxHP, ModifierOp.Flat, 7f) });

            Assert.AreEqual("0 + 7 = 7", NewService().DescribeStat(stats, StatType.MaxHP, true));
        }

        [Test]
        public void DescribeStat_NegativeContribution_ReadsAsSubtraction()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new NamedSource("effect.curse.name"), new[]
            {
                Mod(StatType.MaxHP, ModifierOp.Override, 100f),
                Mod(StatType.MaxHP, ModifierOp.Flat,     -25f),
            });

            string text = NewService(("effect.curse.name", "Проклятие"))
                .DescribeStat(stats, StatType.MaxHP, true);

            Assert.AreEqual("100 - 25 (Проклятие) = 75", text);
        }

        // --- Защита от каши ---

        [Test]
        public void DescribeStat_TooManySources_CollapsesIntoASingleBonus()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new object(), new[] { Mod(StatType.MaxHP, ModifierOp.Override, 10f) });
            for (int i = 0; i < StatFormat.MaxDetailedTerms + 1; i++)
                stats.AddModifiersFrom(new NamedSource("effect.x.name"), new[] { Mod(StatType.MaxHP, ModifierOp.Flat, 1f) });

            string text = NewService(("effect.x.name", "Икс")).DescribeStat(stats, StatType.MaxHP, true);

            Assert.AreEqual("10 + 5 = 15", text, "разбор длиннее порога схлопывается в общую надбавку");
        }

        // --- Размерности ---

        [Test]
        public void Format_RespectsValueKind()
        {
            var units = UnitLabels.Ru;

            Assert.AreEqual("25" + StatFormat.Nbsp + "%", StatFormat.Value(Value(StatType.Lifesteal, 0.25f), units));
            Assert.AreEqual("×1.15", StatFormat.Value(Value(StatType.DamageDealtEff, 1.15f), units));
            Assert.AreEqual("1.2/с", StatFormat.Value(Value(StatType.AttackSpeed, 1.2f), units));
            Assert.AreEqual("3", StatFormat.Value(Value(StatType.ProjectilePierce, 3.4f), units));
            Assert.AreEqual("47", StatFormat.Value(Value(StatType.MaxHP, 47f), units));
        }

        [Test]
        public void Format_TrimsTrailingZeroToKeepNumbersReadable()
        {
            Assert.AreEqual("47", StatFormat.Value(Value(StatType.MaxHP, 47.04f), UnitLabels.Ru));
            Assert.AreEqual("47.5", StatFormat.Value(Value(StatType.MaxHP, 47.46f), UnitLabels.Ru));
        }

        private static StatValue Value(StatType stat, float final)
            => new StatValue(stat, final, final, null, StatKinds.KindOf(stat));

        // --- Имя и описание контента ---

        [Test]
        public void DescribeStat_UsesLocalizedUnitLabelsWhenPresent()
        {
            var stats = NewStats();
            stats.AddModifiersFrom(new object(), new[] { Mod(StatType.Lifesteal, ModifierOp.Flat, 0.25f) });

            string text = NewService(("ui.unit.percent", "проц.")).DescribeStat(stats, StatType.Lifesteal, false);

            Assert.AreEqual("25" + StatFormat.Nbsp + "проц.", text);
        }
    }
}
