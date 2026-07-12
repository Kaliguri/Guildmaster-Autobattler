using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>Форматирование markdown-таблицы (чистое, тестируемое) — экспорт Balance в обсидиан.</summary>
    public static class MarkdownTable
    {
        public static string Format(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            var sb = new StringBuilder();
            sb.Append("| ").Append(string.Join(" | ", headers)).Append(" |\n");
            sb.Append('|').Append(string.Concat(headers.Select(_ => "---|"))).Append('\n');
            foreach (var row in rows)
                sb.Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
            return sb.ToString();
        }
    }
}
