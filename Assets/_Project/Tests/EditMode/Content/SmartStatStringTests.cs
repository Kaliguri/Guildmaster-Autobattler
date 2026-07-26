using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Stats;
using Guildmaster.Game.Services;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Связка «строка в таблице → Smart Format → наш форматтер статов» (Трек Д-о, план §II.10.2).
    /// Приёмка трека: ОДНА строка описания рисует и краткий, и подробный вид — второго текста нет.
    /// </summary>
    /// <remarks>
    /// Тест проверяет проводку, а не текст: он падает, если <c>StatValueFormatter</c> выпал из списка
    /// форматтеров в настройках локализации или потерял пустое имя (тогда безымянный <c>{dmg}</c>
    /// достаётся <c>DefaultFormatter</c>, и в строку печатается имя структуры).
    /// </remarks>
    public sealed class SmartStatStringTests
    {
        // Служебный ключ-зонд в таблице UI: «Урон: {dmg}». Живёт ради этой проверки — игроку не показывается.
        private const string ProbeKey = "ui.dev.stat_probe";
        private const string UiTable = "UI";

        private static FormattedStat Sample(bool detailed)
        {
            var terms = new[] { new StatTerm("relic.ember.name", ModifierOp.Flat, 12f, 12f) };
            var value = new StatValue(StatType.AutoAttackDamage, 30f, 42f, terms, ValueKind.Flat);
            return new FormattedStat(value, detailed ? new[] { "Пылающий клинок" } : null, detailed, UnitLabels.Ru);
        }

        private static string Resolve(LocalizationService loc, bool detailed) =>
            loc.GetString(UiTable, ProbeKey, new Dictionary<string, object> { { "dmg", Sample(detailed) } });

        [Test]
        public void SameString_RendersShortAndDetailed()
        {
            var loc = new LocalizationService();
            string original = loc.CurrentLocale;
            try
            {
                if (!loc.AvailableLocales.Contains("ru")) Assert.Ignore("Локаль ru недоступна.");
                loc.SetLocale("ru");

                string brief = Resolve(loc, false);
                string detailed = Resolve(loc, true);

                Assert.AreEqual("Урон: 42", brief, "краткий вид — только итог");
                StringAssert.Contains("30", detailed, "подробный вид показывает базу");
                StringAssert.Contains("Пылающий клинок", detailed, "и называет источник надбавки");
                StringAssert.Contains("= 42", detailed, "разбор сходится с итогом");
            }
            finally
            {
                if (!string.IsNullOrEmpty(original)) loc.SetLocale(original);
                loc.Dispose();
            }
        }

        [Test]
        public void UnnamedPlaceholder_ReachesStatFormatter()
        {
            var loc = new LocalizationService();
            string original = loc.CurrentLocale;
            try
            {
                if (!loc.AvailableLocales.Contains("ru")) Assert.Ignore("Локаль ru недоступна.");
                loc.SetLocale("ru");

                string result = Resolve(loc, false);

                StringAssert.DoesNotContain("FormattedStat", result,
                    "безымянный {dmg} ушёл в DefaultFormatter — StatValueFormatter потерял пустое имя " +
                    "или выпал из списка форматтеров в LocalizationSettings.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(original)) loc.SetLocale(original);
                loc.Dispose();
            }
        }
    }
}
