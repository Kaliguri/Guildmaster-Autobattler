using System;
using System.Collections.Generic;
using System.Linq;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
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

        // Таблица выбирается ПО ДОМЕНУ КЛЮЧА (ContentKeys.TableFor), а не берётся одна на всё. Пока она была
        // одна ("Content"), ключи ui.* уходили не туда и не находились никогда — экраны жили на C#-фолбэках,
        // а незаведённые ключи были неотличимы от заведённых (аудит 2026-07-26, T-3).
        public string GetString(string key) => GetString(ContentKeys.TableFor(key), key);

        public string GetString(string key, IReadOnlyDictionary<string, object> args)
            => GetString(ContentKeys.TableFor(key), key, args);

        public string GetString(string table, string key) => GetString(table, key, null);

        public string GetString(string table, string key, IReadOnlyDictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(key)) return key;
            EnsureInitialized();
            try
            {
                var op = LocalizationSettings.StringDatabase.GetTableEntryAsync(table, key);
                var res = op.WaitForCompletion();

                // Ключ лежит не в своей таблице — прочитаем, но скажем об этом: молчаливый промах здесь
                // выглядел бы как «перевода нет», хотя строка есть, просто не там.
                if (res.Entry == null)
                {
                    string other = table == ContentKeys.UiTableName ? ContentKeys.TableName : ContentKeys.UiTableName;
                    var fallback = LocalizationSettings.StringDatabase.GetTableEntryAsync(other, key).WaitForCompletion();
                    if (fallback.Entry != null)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[Localization] - ключ '{key}' лежит в таблице '{other}', а по домену принадлежит '{table}'");
                        res = fallback;
                    }
                }
                // Отсутствующий ключ → пустая строка, чтобы вызывающий применил свой RU-фолбэк
                // (а не показывал Unity-плейсхолдер «No translation found …» или сам ключ). Это делает
                // code-фолбэки экранов (L(key, "RU")) реальной страховкой на случай незаведённого ключа.
                // Но страховка обязана быть ВИДНА: RU-литерал всегда правильный, и именно поэтому никто не
                // замечал, что половина ключей не заведена (аудит фолбэков 2026-07-26, п.7). Говорим один раз
                // на ключ — вызов идёт на каждую перерисовку экрана.
                if (res.Entry == null)
                {
                    if (_reportedMissingKeys.Add(key))
                        UnityEngine.Debug.LogWarning(
                            $"[Localization] - ключ '{key}' не заведён ни в '{table}', ни в парной таблице → экран покажет свой RU-литерал");
                    return string.Empty;
                }

                // Именованные слоты ({dmg}) Smart Format достаёт из ОДНОГО аргумента-словаря
                // через свой Dictionary-source; передавать пары по отдельности нельзя.
                if (args != null && args.Count > 0)
                    return res.Entry.GetLocalizedString(new object[] { args }) ?? string.Empty;

                return res.Entry.GetLocalizedString() ?? string.Empty;
            }
            catch (Exception e)
            {
                // Сама подсистема локализации отказала (таблицы не загружены, битый Smart-формат). Это не
                // «перевода нет», и молча отдавать пустоту здесь — значит потерять причину.
                if (_reportedMissingKeys.Add("!" + key))
                    UnityEngine.Debug.LogError($"[Localization] - отказ при чтении ключа '{key}' из '{table}': {e.Message}");
                return string.Empty;
            }
        }

        // Ключи, о которых уже сказали (промах и отказ — раздельно, префикс «!»). Только для дедупа лога.
        private readonly HashSet<string> _reportedMissingKeys = new HashSet<string>();

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
