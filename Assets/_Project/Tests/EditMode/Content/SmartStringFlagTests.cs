using System.Collections.Generic;
using System.Text.RegularExpressions;
using Guildmaster.Data.Editor;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Строка с плейсхолдером обязана быть помечена Smart (Трек Д-о, план §II.10.2 п.6).
    /// </summary>
    /// <remarks>
    /// Это НЕ формальность: без флага аргументы уходят в <c>string.Format</c>, тот падает на именованном
    /// слоте, а сервис локализации гасит исключение и отдаёт ПУСТУЮ строку. То есть описание просто
    /// исчезает — молча, в одной строке из двухсот. Проверено на живом ключе 2026-07-26.
    /// </remarks>
    public sealed class SmartStringFlagTests
    {
        private const string Ru = "ru";

        // Плейсхолдер Smart Format: одиночная { … }, не экранированная удвоением ({{ — литеральная скобка).
        private static readonly Regex Placeholder = new Regex(@"(?<!\{)\{[A-Za-z_][A-Za-z0-9_.:]*\}", RegexOptions.Compiled);

        [TestCase("Content")]
        [TestCase("UI")]
        public void EveryStringWithPlaceholder_IsMarkedSmart(string tableName)
        {
            var broken = new List<string>();

            foreach ((string key, string value, bool isSmart) in ContentLocalization.AllEntries(tableName, Ru))
            {
                if (string.IsNullOrEmpty(value) || isSmart) continue;
                if (Placeholder.IsMatch(value)) broken.Add($"{tableName}/{key}: «{value}»");
            }

            CollectionAssert.IsEmpty(broken,
                "Есть строки с плейсхолдерами без флага Smart — в игре они резолвятся в ПУСТО:\n" +
                string.Join("\n", broken));
        }
    }
}
