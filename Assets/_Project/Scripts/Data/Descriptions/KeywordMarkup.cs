using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Guildmaster.Data.Descriptions
{
    /// <summary>
    /// Разметка ключевых слов в текстах описаний (план §II.10.3, §II.10.5 п.4): автор пишет
    /// <c>[kw:burn]</c> или <c>[kw:burn:gen]</c>, читатель получает готовый rich text со ссылкой,
    /// по которой открывается вложенная подсказка.
    /// </summary>
    /// <remarks>
    /// <para><b>Почему квадратные скобки, а не <c>{kw:burn:gen}</c> из спеки.</b> Фигурные скобки
    /// принадлежат Smart Format: он разбирает их ДО нас и прочтёт <c>burn</c> как имя форматтера.
    /// Занять их своим синтаксисом можно только расширением Smart Format, а расширения создаются
    /// сериализацией ассета настроек и зависимостей (база контента, локализация) получить не могут —
    /// пришлось бы заводить статический доступ к сервисам, чего в проекте нет. Квадратные скобки
    /// оставляют разбор здесь, где зависимости приходят через конструктор. Для автора разница
    /// в одном символе, для архитектуры — принципиальная.</para>
    /// <para>Ручного rich text в текстах НЕ требуем (§II.10.5 п.4): и ссылку, и подчёркивание
    /// расставляет эта разметка.</para>
    /// </remarks>
    public static class KeywordMarkup
    {
        /// <summary>Домен id ключевых слов: короткая запись <c>[kw:burn]</c> разворачивается в <c>kw.burn</c>.</summary>
        public const string Domain = "kw";

        // Ленивый id (+?) — чтобы «burn:gen» не был съеден целиком как id при отсутствии второй группы.
        private static readonly Regex Pattern =
            new Regex(@"\[kw:([A-Za-z0-9_.]+?)(?::([A-Za-z]+))?\]", RegexOptions.Compiled);

        /// <summary>
        /// Развернуть разметку в rich text. <paramref name="form"/> отдаёт форму слова по (id, падеж);
        /// вернул пусто — на месте термина останется его id, и дырка в локализации будет видна сразу.
        /// </summary>
        public static string Render(string text, Func<string, string, string> form)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("[kw:", StringComparison.Ordinal) < 0) return text;

            return Pattern.Replace(text, m =>
            {
                string id = FullId(m.Groups[1].Value);
                string caseTag = m.Groups[2].Success ? m.Groups[2].Value : null;
                string word = Word(form, id, caseTag);
                // <link> даёт событие с нашим id; вид термина — КВАДРАТНЫЕ СКОБКИ и заглавная буква
                // (решение Макса 2026-07-26): «[Скрытность]» читается как термин даже в сыром тексте —
                // в логе, в поиске, в редакторе таблиц. Подчёркивание работало только на экране и
                // терялось всюду, где rich text не рисуется.
                return "<link=" + id + ">[" + word + "]</link>";
            });
        }

        /// <summary>
        /// Убрать ссылки, оставив термин в скобках: «[Скрытность]». Нужен там, где ссылка не должна
        /// работать — во вложенном определении (§II.10.5: глоссарий плоский, второй уровень не кликается),
        /// в логах и в тестах.
        /// </summary>
        public static string Strip(string text, Func<string, string, string> form)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("[kw:", StringComparison.Ordinal) < 0) return text;

            return Pattern.Replace(text, m =>
            {
                string id = FullId(m.Groups[1].Value);
                return "[" + Word(form, id, m.Groups[2].Success ? m.Groups[2].Value : null) + "]";
            });
        }

        /// <summary>
        /// Слово термина в нужной форме. Пусто — показываем id: молчащая дырка в локализации не видна
        /// никому, а «kw.poison» в тексте сразу называет незаведённый ключ.
        /// </summary>
        private static string Word(Func<string, string, string> form, string id, string caseTag)
        {
            string word = form?.Invoke(id, caseTag);
            if (string.IsNullOrEmpty(word)) return id;
            // Заглавная буква — часть формата термина: формы в таблице могут прийти строчными
            // (переводчик пишет «горения»), но в тексте термин обязан выглядеть одинаково всегда.
            return char.IsLower(word[0]) ? char.ToUpperInvariant(word[0]) + word.Substring(1) : word;
        }

        /// <summary>Все id ключевых слов, упомянутых в тексте (валидация контента, сбор глоссария).</summary>
        public static string[] Mentioned(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            MatchCollection matches = Pattern.Matches(text);
            if (matches.Count == 0) return Array.Empty<string>();

            var ids = new string[matches.Count];
            for (int i = 0; i < matches.Count; i++) ids[i] = FullId(matches[i].Groups[1].Value);
            return ids;
        }

        /// <summary>Короткая запись <c>burn</c> → полный id <c>kw.burn</c>; полный id остаётся как есть.</summary>
        public static string FullId(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            return id.IndexOf('.') >= 0 ? id : Domain + "." + id;
        }

        /// <summary>Собрать разметку программно (редакторные тулы, тесты).</summary>
        public static string Mark(string id, string caseTag = null)
        {
            var sb = new StringBuilder("[kw:");
            sb.Append(id);
            if (!string.IsNullOrEmpty(caseTag)) sb.Append(':').Append(caseTag);
            sb.Append(']');
            return sb.ToString();
        }
    }
}
