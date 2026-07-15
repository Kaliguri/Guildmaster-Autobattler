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
                var op = LocalizationSettings.StringDatabase.GetTableEntryAsync(table, key);
                var res = op.WaitForCompletion();
                // Отсутствующий ключ → пустая строка, чтобы вызывающий применил свой RU-фолбэк
                // (а не показывал Unity-плейсхолдер «No translation found …» или сам ключ). Это делает
                // code-фолбэки экранов (L(key, "RU")) реальной страховкой на случай незаведённого ключа.
                if (res.Entry == null) return string.Empty;
                return res.Entry.GetLocalizedString() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
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
