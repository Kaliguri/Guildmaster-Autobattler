using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Пишет отчёты бенчей в gitignored-папку <c>BalanceReports/</c> в корне проекта: CSV (для спредшита)
    /// и Markdown (для беглого чтения). Инвариант-культура и «;»-разделитель — чтобы дробные значения
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

        private static string Stamp(string baseName)
            => baseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    }
}
