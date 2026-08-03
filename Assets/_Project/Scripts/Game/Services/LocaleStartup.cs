using System;
using System.Globalization;
using Guildmaster.Core.Localization;
using Guildmaster.Core.Settings;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Выбирает язык на старте сессии: сохранённый — применить, невыбранный — взять у СИСТЕМЫ, а не
    /// дефолт проекта. До этого сервиса локаль никто не трогал, и русский игрок при первом запуске
    /// получал язык, стоящий в настройках проекта.
    /// <para>Определив язык системы, сервис его ЗАПИСЫВАЕТ (<see cref="ISettingsService.SetLanguage"/>):
    /// подбор происходит ровно один раз, и последующая смена языка ОС не переключает игру под человеком,
    /// который уже привык к её языку.</para>
    /// <para>Применяет по <see cref="ISettingsService.Changed"/> вдобавок к <see cref="Start"/>, и это не
    /// перестраховка: порядок <c>IStartable</c> в VContainer — порядок регистрации, поэтому одного Start
    /// хватило бы лишь до первой перестановки строк в скоупе. Через событие сервис ещё и подхватывает
    /// будущий переключатель языка в настройках, не зная о нём.</para>
    /// </summary>
    public sealed class LocaleStartup : IStartable, IDisposable
    {
        private const string FallbackLocale = "en";

        private readonly ISettingsService _settings;
        private readonly ILocalizationService _loc;

        private string _applied = string.Empty;

        public LocaleStartup(ISettingsService settings, ILocalizationService loc)
        {
            _settings = settings;
            _loc = loc;
        }

        // Публичный, а не явная реализация IStartable: так тест поднимает сервис, не притаскивая
        // VContainer в тестовую сборку ради одного вызова.
        public void Start()
        {
            if (_settings != null) _settings.Changed += Sync;
            Sync();
        }

        public void Dispose()
        {
            if (_settings != null) _settings.Changed -= Sync;
        }

        private void Sync()
        {
            if (_settings == null || _loc == null) return;

            string wanted = _settings.LanguageCode;
            if (string.IsNullOrEmpty(wanted))
            {
                wanted = PickForSystem();
                // Запись поднимет Changed и вернёт нас сюда — но уже с непустым кодом, поэтому
                // ветка «выбора не было» больше не сработает и рекурсия обрывается на втором заходе.
                _settings.SetLanguage(wanted);
                return;
            }

            if (wanted == _applied) return;

            _loc.SetLocale(wanted);
            _applied = wanted;
        }

        /// <summary>
        /// Язык системы, суженный до того, что игра реально умеет. Берём двухбуквенный код UI-культуры
        /// (он отвечает на «на каком языке человеку читать интерфейс», в отличие от культуры форматов
        /// чисел), сверяем со списком локалей проекта и при промахе уходим в <see cref="FallbackLocale"/>:
        /// английский понятнее случайно выбранного русского для того, кто не знает ни того, ни другого.
        /// </summary>
        private string PickForSystem()
        {
            string code;
            try
            {
                code = CultureInfo.CurrentUICulture?.TwoLetterISOLanguageName ?? string.Empty;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Locale] - язык системы не прочитан ({e.Message}) → беру {FallbackLocale}");
                code = string.Empty;
            }

            var available = _loc.AvailableLocales;
            if (!string.IsNullOrEmpty(code) && available != null)
            {
                for (int i = 0; i < available.Count; i++)
                {
                    string candidate = available[i];
                    if (string.IsNullOrEmpty(candidate)) continue;

                    // Сравниваем ПО ЯЗЫКУ, а не по полному коду: в проекте локаль может быть заведена
                    // как "en", а система сообщить "en-GB" — и наоборот.
                    if (candidate.StartsWith(code, StringComparison.OrdinalIgnoreCase) ||
                        code.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }

            return FallbackLocale;
        }
    }
}
