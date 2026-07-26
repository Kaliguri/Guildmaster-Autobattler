using System.Collections.Generic;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Data.Descriptions
{
    /// <summary>
    /// Реализация <see cref="IDescriptionService"/> поверх локализации и разбора статов.
    /// </summary>
    /// <remarks>
    /// Ничего не считает сама — числа приходят из <see cref="IStatExplainer"/>, то есть из того
    /// же места, откуда их берёт симуляция. Задача сервиса — резолвить ключи и собирать строки
    /// (план UI-реворка §II.10.1).
    /// </remarks>
    public sealed class DescriptionService : IDescriptionService
    {
        private const string UiTable = "UI";
        private const string PercentKey = "ui.unit.percent";
        private const string SecondsKey = "ui.unit.seconds";
        private const string PerSecondKey = "ui.unit.per_second";

        private readonly ILocalizationService _loc;

        // Подписи единиц дёргаются на каждое число — кешируем на локаль, а не ходим в таблицу
        // по разу на стат: панель юнита рисует их десятками, тултип обновляется дважды в секунду.
        private UnitLabels _units;
        private bool _unitsReady;

        public DescriptionService(ILocalizationService loc)
        {
            _loc = loc;
            if (_loc != null) _loc.LocaleChanged += OnLocaleChanged;
        }

        public string Name(ContentDefinition def)
        {
            string key = ContentKeys.NameKey(def);
            return key == null ? string.Empty : Localized(key, null);
        }

        public string Describe(ContentDefinition def, IReadOnlyDictionary<string, object> args)
        {
            string key = ContentKeys.DescKey(def);
            return key == null ? string.Empty : KeywordMarkup.Render(Localized(key, args), KeywordForm);
        }

        public string DescribeFull(ContentDefinition def, IReadOnlyDictionary<string, object> args)
        {
            string key = ContentKeys.FullDescKey(def);
            if (key == null) return string.Empty;
            string full = Localized(key, args);
            // Полного текста может не быть (термин объяснён одной строкой) — тогда честнее показать
            // краткий, чем пустую статью.
            return string.IsNullOrEmpty(full) ? Describe(def, args) : KeywordMarkup.Render(full, KeywordForm);
        }

        public string DescribePlain(ContentDefinition def, IReadOnlyDictionary<string, object> args)
        {
            string key = ContentKeys.DescKey(def);
            return key == null ? string.Empty : KeywordMarkup.Strip(Localized(key, args), KeywordForm);
        }

        public IReadOnlyList<string> MentionedKeywords(ContentDefinition def)
        {
            string key = ContentKeys.DescKey(def);
            if (key == null || _loc == null) return System.Array.Empty<string>();

            // Берём СЫРУЮ строку: в отрендеренной разметки уже нет, и вытаскивать id обратно из
            // <link=…> значило бы парсить собственный вывод.
            string[] ids = KeywordMarkup.Mentioned(_loc.GetString(key));
            if (ids.Length <= 1) return ids;

            var unique = new List<string>(ids.Length);
            foreach (string id in ids)
                if (!unique.Contains(id)) unique.Add(id); // порядок появления важнее скорости: их единицы
            return unique;
        }

        public string KeywordForm(string keywordId, string caseTag)
        {
            if (_loc == null || string.IsNullOrEmpty(keywordId)) return null;
            string form = _loc.GetString(ContentKeys.FormKey(keywordId, caseTag));
            // Падеж может быть не заведён — откатываемся на именительный: фраза звучит хуже,
            // но остаётся читаемой, а дырка чинится одной строкой в таблице.
            if (string.IsNullOrEmpty(form) && !string.IsNullOrEmpty(caseTag))
                form = _loc.GetString(ContentKeys.FormKey(keywordId, null));
            return form;
        }

        public string DescribeStat(IStatExplainer stats, StatType stat, bool detailed)
            => StatFormat.Describe(Explain(stats, stat, detailed));

        public FormattedStat Explain(IStatExplainer stats, StatType stat, bool detailed)
        {
            if (stats == null) return default;

            StatValue value = stats.Explain(stat);
            if (!detailed || !value.IsModified) return new FormattedStat(value, null, detailed, Units);

            var names = new string[value.Terms.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string key = value.Terms[i].SourceLocKey;
                // Безымянный источник — не ошибка: системные эффекты игроку не показываются
                // поимённо, их вклад просто виден без подписи.
                names[i] = string.IsNullOrEmpty(key) ? null : _loc?.GetString(key);
            }

            return new FormattedStat(value, names, true, Units);
        }

        private UnitLabels Units
        {
            get
            {
                if (_unitsReady) return _units;

                UnitLabels fallback = UnitLabels.Ru;
                _units = new UnitLabels(
                    Or(_loc?.GetString(UiTable, PercentKey), fallback.Percent),
                    Or(_loc?.GetString(UiTable, SecondsKey), fallback.Seconds),
                    Or(_loc?.GetString(UiTable, PerSecondKey), fallback.PerSecond));
                _unitsReady = true;
                return _units;
            }
        }

        private string Localized(string key, IReadOnlyDictionary<string, object> args)
        {
            if (_loc == null) return string.Empty;
            return args == null ? _loc.GetString(key) : _loc.GetString(key, args);
        }

        private static string Or(string value, string fallback)
            => string.IsNullOrEmpty(value) ? fallback : value;

        private void OnLocaleChanged() => _unitsReady = false;
    }
}
