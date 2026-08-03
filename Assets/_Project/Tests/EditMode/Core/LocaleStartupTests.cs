using System;
using System.Collections.Generic;
using System.Globalization;
using Guildmaster.Core.Localization;
using Guildmaster.Core.Settings;
using Guildmaster.Game.Services;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode
{
    /// <summary>
    /// Инвариант выбора языка. Живёт МЕЖДУ файлами: пустоту в prefs пишет <see cref="SettingsService"/>,
    /// а смысл «пусто = спроси систему» знает <see cref="LocaleStartup"/> — комментарий в одном из них
    /// вторая сторона нарушит и не заметит.
    /// </summary>
    [TestFixture]
    public sealed class LocaleStartupTests
    {
        private CultureInfo _uiCulture;

        [SetUp]
        public void SetUp() => _uiCulture = CultureInfo.CurrentUICulture;

        [TearDown]
        public void TearDown() => CultureInfo.CurrentUICulture = _uiCulture;

        [Test]
        public void FirstRun_TakesLanguageFromSystem()
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");
            var settings = new SettingsStub(string.Empty);
            var loc = new LocalizationStub("en", "ru");

            Start(settings, loc);

            Assert.AreEqual("ru", loc.Applied, "первый запуск обязан взять язык системы, а не дефолт проекта");
            Assert.AreEqual("ru", settings.LanguageCode, "подобранный язык обязан записаться — иначе он подбирается каждый запуск");
        }

        [Test]
        public void SavedChoice_WinsOverSystemLanguage()
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");
            var settings = new SettingsStub("en");
            var loc = new LocalizationStub("en", "ru");

            Start(settings, loc);

            Assert.AreEqual("en", loc.Applied, "выбор игрока сильнее языка системы");
        }

        [Test]
        public void SystemLanguageWeDoNotHave_FallsBackToEnglish()
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");
            var settings = new SettingsStub(string.Empty);
            var loc = new LocalizationStub("en", "ru");

            Start(settings, loc);

            Assert.AreEqual("en", loc.Applied, "японский игрок должен получить английский, а не случайный русский");
        }

        [Test]
        public void RegionalSystemLanguage_MatchesProjectLocaleByLanguage()
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-GB");
            var settings = new SettingsStub(string.Empty);
            var loc = new LocalizationStub("en", "ru");

            Start(settings, loc);

            Assert.AreEqual("en", loc.Applied, "en-GB — это английский: полный код совпадать не обязан");
        }

        [Test]
        public void LaterChoice_IsAppliedThroughChangedEvent()
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var settings = new SettingsStub(string.Empty);
            var loc = new LocalizationStub("en", "ru");
            Start(settings, loc);

            settings.SetLanguage("ru");

            Assert.AreEqual("ru", loc.Applied, "переключатель языка обязан работать без перезапуска");
            Assert.AreEqual(2, loc.Calls, "локаль применяется на смену выбора, а не на каждое событие настроек");
        }

        private static void Start(SettingsStub settings, LocalizationStub loc)
            => new LocaleStartup(settings, loc).Start();

        /// <summary>Настройки в памяти: важна только пара «код языка + Changed на его смену».</summary>
        private sealed class SettingsStub : ISettingsService
        {
            public SettingsStub(string language) => LanguageCode = language;

            public AudioVolumeSettings Audio => default;
            public GameplaySettings Gameplay => GameplaySettings.Defaults();
            public string LanguageCode { get; private set; }
            public event Action Changed;

            public void SetLanguage(string localeCode)
            {
                string code = string.IsNullOrWhiteSpace(localeCode) ? string.Empty : localeCode.Trim();
                if (code == LanguageCode) return;
                LanguageCode = code;
                Changed?.Invoke();
            }

            public void SetMasterVolume(float volume01) { }
            public void SetMusicVolume(float volume01) { }
            public void SetSfxVolume(float volume01) { }
            public void SetCardAnimations(bool enabled) { }
            public void SetCardAttackAnimation(bool enabled) { }
            public void SetAlwaysDetailedTooltips(bool enabled) { }
            public void SetFreeCombatCamera(bool free) { }
            public void Load() { }
            public void Save() { }
            public void ResetToDefaults() { }
        }

        /// <summary>Локализация без Unity Localization: список локалей проекта и последняя применённая.</summary>
        private sealed class LocalizationStub : ILocalizationService
        {
            private readonly List<string> _locales;

            public LocalizationStub(params string[] locales) => _locales = new List<string>(locales);

            public string Applied { get; private set; } = string.Empty;
            public int Calls { get; private set; }

            public string CurrentLocale => Applied;
            public IReadOnlyList<string> AvailableLocales => _locales;
            public event Action LocaleChanged;

            public void SetLocale(string localeCode)
            {
                Applied = localeCode;
                Calls++;
                LocaleChanged?.Invoke();
            }

            public string GetString(string key) => key;
            public string GetString(string key, IReadOnlyDictionary<string, object> args) => key;
            public string GetString(string table, string key) => key;
            public string GetString(string table, string key, IReadOnlyDictionary<string, object> args) => key;
        }
    }
}
