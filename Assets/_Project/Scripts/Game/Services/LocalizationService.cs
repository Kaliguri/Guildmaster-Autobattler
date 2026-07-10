using System;
using System.Collections.Generic;
using System.Linq;
using Guildmaster.Core.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Реализация <see cref="ILocalizationService"/> поверх Unity String Tables (вики «13» §5).
    /// Пакет инициализируется асинхронно — сервис форсит синхронность (<c>WaitForCompletion</c>),
    /// чтобы потребители получали строку сразу, не через async-контракт.
    /// </summary>
    public sealed class LocalizationService : ILocalizationService, IDisposable
    {
        private const string ContentTable = "Content";

        public event Action LocaleChanged;

        public LocalizationService()
        {
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        public string CurrentLocale
        {
            get
            {
                EnsureInitialized();
                Locale locale = LocalizationSettings.SelectedLocale;
                return locale != null ? locale.Identifier.Code : string.Empty;
            }
        }

        public IReadOnlyList<string> AvailableLocales
        {
            get
            {
                EnsureInitialized();
                return LocalizationSettings.AvailableLocales.Locales
                    .Select(l => l.Identifier.Code)
                    .ToList();
            }
        }

        public string GetString(string key) => GetString(ContentTable, key);

        public string GetString(string table, string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            EnsureInitialized();
            try
            {
                AsyncOperationHandle<string> op =
                    LocalizationSettings.StringDatabase.GetLocalizedStringAsync(table, key);
                string value = op.WaitForCompletion();
                // Отсутствующий ключ виден как сам ключ, а не пустая строка (проще ловить пробелы в контенте).
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch (Exception)
            {
                return key;
            }
        }

        public void SetLocale(string localeCode)
        {
            if (string.IsNullOrEmpty(localeCode)) return;
            EnsureInitialized();
            Locale target = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(localeCode));
            if (target != null) LocalizationSettings.SelectedLocale = target;
        }

        public void Dispose()
        {
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        private void OnSelectedLocaleChanged(Locale locale) => LocaleChanged?.Invoke();

        private static void EnsureInitialized()
        {
            AsyncOperationHandle init = LocalizationSettings.InitializationOperation;
            if (!init.IsDone) init.WaitForCompletion();
        }
    }
}
