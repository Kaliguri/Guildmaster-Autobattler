using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Пишет отчёты бенчей в <c>BalanceReports/</c> в корне проекта: CSV (для спредшита) и Markdown (для
    /// беглого чтения). Папка ВЕРСИОНИРУЕТСЯ — история замеров ценнее места на диске, по ней сайт отчётов
    /// считает дельты между прогонами. Инвариант-культура и «;»-разделитель — чтобы дробные значения
    /// не ломались локалью. Возвращает пути записанных файлов.
    /// </summary>
    internal static class ReportWriter
    {
        public static string OutputDir
        {
            get
            {
                string root = Directory.GetParent(Application.dataPath)!.FullName;
                string dir = Path.Combine(root, "BalanceReports");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string Cell(object o)
        {
            switch (o)
            {
                case null: return "";
                case float f: return f.ToString("0.###", CultureInfo.InvariantCulture);
                case double d: return d.ToString("0.###", CultureInfo.InvariantCulture);
                default: return Convert.ToString(o, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Записать CSV (разделитель «;»). Возвращает полный путь.</summary>
        public static string WriteCsv(string baseName, IReadOnlyList<string> headers,
            IReadOnlyList<IReadOnlyList<object>> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", headers));
            foreach (IReadOnlyList<object> row in rows)
            {
                var cells = new string[row.Count];
                for (int i = 0; i < row.Count; i++) cells[i] = Cell(row[i]).Replace(';', ',');
                sb.AppendLine(string.Join(";", cells));
            }

            string path = Path.Combine(OutputDir, Stamp(baseName) + ".csv");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        /// <summary>Записать Markdown-таблицу с заголовком и заметками. Возвращает полный путь.</summary>
        public static string WriteMarkdown(string baseName, string title,
            IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object>> rows, string notes = null)
        {
            var sb = new StringBuilder();
            sb.Append("# ").AppendLine(title);
            sb.Append("_Сгенерировано ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
              .AppendLine(" — SimBench_").AppendLine();
            if (!string.IsNullOrEmpty(notes)) sb.AppendLine(notes).AppendLine();

            sb.Append("| ").Append(string.Join(" | ", headers)).AppendLine(" |");
            var sep = new string[headers.Count];
            for (int i = 0; i < sep.Length; i++) sep[i] = "---";
            sb.Append("| ").Append(string.Join(" | ", sep)).AppendLine(" |");

            foreach (IReadOnlyList<object> row in rows)
            {
                var cells = new string[row.Count];
                for (int i = 0; i < row.Count; i++) cells[i] = Cell(row[i]);
                sb.Append("| ").Append(string.Join(" | ", cells)).AppendLine(" |");
            }

            string path = Path.Combine(OutputDir, Stamp(baseName) + ".md");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        /// <summary>
        /// Записать тот же отчёт машинно-читаемым JSON — из него собирается локальный сайт отчётов.
        /// </summary>
        /// <remarks>
        /// Третий формат рядом с CSV и Markdown, а не вместо: CSV живёт для спредшита, Markdown — для
        /// беглого чтения в редакторе, JSON — для сборщика сайта, которому нужны типы и метаданные
        /// (какой бенч, когда, с какими пояснениями). Числа пишутся числами, а не строками, чтобы на
        /// стороне сайта не разбирать локаль обратно.
        /// </remarks>
        public static string WriteJson(string baseName, string title,
            IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object>> rows, string notes = null)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"kind\":").Append(Json(baseName)).Append(',');
            sb.Append("\"title\":").Append(Json(title)).Append(',');
            sb.Append("\"generatedAt\":").Append(Json(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))).Append(',');
            sb.Append("\"notes\":").Append(Json(notes)).Append(',');

            sb.Append("\"headers\":[");
            for (int i = 0; i < headers.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Json(headers[i]));
            }
            sb.Append("],");

            sb.Append("\"rows\":[");
            for (int r = 0; r < rows.Count; r++)
            {
                if (r > 0) sb.Append(',');
                sb.Append('[');
                IReadOnlyList<object> row = rows[r];
                for (int c = 0; c < row.Count; c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append(JsonValue(row[c]));
                }
                sb.Append(']');
            }
            sb.Append("]}");

            string path = Path.Combine(OutputDir, Stamp(baseName) + ".json");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        /// <summary>Число — числом, всё прочее — строкой. Инвариантная культура: точка, а не запятая.</summary>
        private static string JsonValue(object o)
        {
            switch (o)
            {
                case null: return "null";
                case float f: return float.IsFinite(f) ? f.ToString("0.###", CultureInfo.InvariantCulture) : "null";
                case double d: return double.IsFinite(d) ? d.ToString("0.###", CultureInfo.InvariantCulture) : "null";
                case int i: return i.ToString(CultureInfo.InvariantCulture);
                case bool b: return b ? "true" : "false";
                default: return Json(Convert.ToString(o, CultureInfo.InvariantCulture));
            }
        }

        private static string Json(string s)
        {
            if (s == null) return "null";

            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string Stamp(string baseName)
            => baseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    }
}
