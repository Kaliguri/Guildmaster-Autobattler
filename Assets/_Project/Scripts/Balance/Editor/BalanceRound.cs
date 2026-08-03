using System;
using System.Collections.Generic;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Полный круг бенчей — что и в каком порядке гоняется, когда сказано «прогони баланс».
    /// </summary>
    /// <remarks>
    /// Единственный владелец состава и порядка круга: его читают и пункт меню, и вход из командной строки
    /// (<see cref="BalanceCli"/>). Порядок сравнения важнее удобства — прогоны честно сравниваются только
    /// когда линзы одни и те же, поэтому «выборочный круг» существует лишь как явный список ключей.
    /// <para>Спутники (нормы и карточки) стоят В НАЧАЛЕ круга, а не после каждого бенча: линейка коридоров
    /// обязана быть той же версии, что замеры, и снять её один раз перед прогоном достаточно.</para>
    /// </remarks>
    internal static class BalanceRound
    {
        /// <summary>Один шаг круга: ключ для командной строки, человеческое имя, сам бенч.</summary>
        internal readonly struct Step
        {
            public readonly string Key;
            public readonly string Title;
            public readonly Func<(string csv, string md)> Run;

            public Step(string key, string title, Func<(string csv, string md)> run)
            {
                Key = key;
                Title = title;
                Run = run;
            }
        }

        public static readonly Step[] Steps =
        {
            new Step("norms", "Классовые нормы", BalanceNorms.Run),
            new Step("cards", "Карточки контента", ContentCards.Run),
            new Step("audit", "Аудит контента", ContentAuditor.Run),
            new Step("encounters", "Энкаунтеры (PvE)", EncounterBench.Run),
            new Step("dps", "DPS-бенч", DpsBench.Run),
            new Step("survivability", "Бенч выживаемости", SurvivabilityBench.Run),
            new Step("duel", "Дуэли 1v1", DuelMatrixBench.Run),
            new Step("trio", "Тройки 3v3", DuelMatrixBench.RunTrio),
            new Step("squad", "Отряды 4v4", DuelMatrixBench.RunSquad),
            new Step("swap", "Замена в живом отряде", SquadSwapBench.Run),
            new Step("synergy", "Синергия пар", PairSynergyBench.Run),
        };

        /// <summary>Все ключи через запятую — для подсказок в логе и в помощи скрипта.</summary>
        public static string Keys
        {
            get
            {
                var keys = new List<string>(Steps.Length);
                foreach (Step s in Steps) keys.Add(s.Key);
                return string.Join(", ", keys);
            }
        }

        /// <summary>
        /// Отобрать шаги по списку ключей. <c>null</c>/пусто/«all» — весь круг. Неизвестный ключ —
        /// исключение: молча прогнать не то, что заказали, хуже, чем не прогнать вовсе.
        /// </summary>
        public static IReadOnlyList<Step> Select(string keysCsv)
        {
            if (string.IsNullOrWhiteSpace(keysCsv) || keysCsv.Trim() == "all") return Steps;

            // Пробел — такой же разделитель, как запятая: список из шелла приходит склеенным то так, то
            // так (PowerShell склеивает массив пробелами), и падать из-за формы разделителя — глупость.
            var selected = new List<Step>();
            foreach (string raw in keysCsv.Split(',', ' ', ';'))
            {
                string key = raw.Trim();
                if (key.Length == 0) continue;

                bool found = false;
                foreach (Step s in Steps)
                {
                    if (!string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                    selected.Add(s);
                    found = true;
                    break;
                }

                if (!found)
                    throw new ArgumentException($"Неизвестный бенч «{key}». Известные: {Keys}.");
            }

            return selected;
        }
    }
}
