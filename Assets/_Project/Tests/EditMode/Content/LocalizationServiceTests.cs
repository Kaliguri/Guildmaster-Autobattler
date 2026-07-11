using System.Linq;
using Guildmaster.Game.Services;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Acceptance §13.6: смена локали через <see cref="LocalizationService"/> меняет резолвнутую строку.
    /// Тест о механизме (en≠ru, обе непусты, не равны ключу), а не о конкретном тексте — правки контента
    /// его не ломают. Оригинальная локаль восстанавливается, чтобы не течь в другие тесты.
    /// </summary>
    public sealed class LocalizationServiceTests
    {
        private const string SampleKey = "relic.defender.name";

        [Test]
        public void SwitchingLocale_ChangesResolvedString()
        {
            var service = new LocalizationService();
            string original = service.CurrentLocale;
            try
            {
                if (!service.AvailableLocales.Contains("en") || !service.AvailableLocales.Contains("ru"))
                    Assert.Ignore("Locales en/ru not available in this environment.");

                service.SetLocale("en");
                Assert.AreEqual("en", service.CurrentLocale);
                string en = service.GetString(SampleKey);

                service.SetLocale("ru");
                Assert.AreEqual("ru", service.CurrentLocale);
                string ru = service.GetString(SampleKey);

                Assert.IsFalse(string.IsNullOrEmpty(en), "EN string empty — entry not populated?");
                Assert.IsFalse(string.IsNullOrEmpty(ru), "RU string empty — entry not populated?");
                Assert.AreNotEqual(SampleKey, en, "EN resolved to the key itself — missing entry.");
                Assert.AreNotEqual(en, ru, "Locale switch must change the resolved string.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(original)) service.SetLocale(original);
                service.Dispose();
            }
        }
    }
}
