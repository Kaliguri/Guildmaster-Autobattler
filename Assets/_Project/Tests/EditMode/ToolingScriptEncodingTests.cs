using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode
{
    /// <summary>
    /// Кодировка скриптов тулинга: каждый <c>scripts/*.ps1</c> обязан начинаться с UTF-8 BOM.
    ///
    /// Инвариант держится тестом, а не комментарием в скрипте, потому что нарушается он снаружи и
    /// молча: любой редактор или агент, сохранивший файл «просто в UTF-8», ломает его для консоли
    /// Макса, а в pwsh 7 всё продолжает работать — то есть автор правки ничего не замечает.
    ///
    /// Почему это ломает. Windows PowerShell 5.1 (а он и стоит у Макса по умолчанию) без сигнатуры
    /// читает файл в ANSI-кодировке. Наши комментарии и сообщения по-русски превращаются в мусор,
    /// в мусоре разъезжаются кавычки — и падает <b>парсер</b>, ещё до первой строки логики. Ошибка
    /// при этом показывает не кодировку, а «незакрытую скобку» в случайном месте, поэтому чинится
    /// она долго и не туда.
    ///
    /// Замер 2026-08-02: девять скриптов из одиннадцати не парсились в 5.1 — включая
    /// <c>compile-check.ps1</c>, которым по HARD-правилу проверяется каждая правка <c>.cs</c>.
    /// Ровно те два, что имели BOM, работали.
    /// </summary>
    [TestFixture]
    public class ToolingScriptEncodingTests
    {
        private static string ScriptsDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "scripts"));

        private static bool IsInsideHiddenFolder(string fullPath)
        {
            string relative = fullPath.Substring(ScriptsDir.Length).TrimStart(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return relative
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment.StartsWith("."));
        }

        [Test]
        public void Каждый_скрипт_тулинга_начинается_с_BOM()
        {
            Assert.IsTrue(Directory.Exists(ScriptsDir), $"не нашла папку скриптов: {ScriptsDir}");

            // Скрытые папки — не наши: там лежат Python-venv аудио-пайплайна со своим Activate.ps1.
            // Они вендорные, в .gitignore, и их кодировка нас не касается.
            string[] scripts = Directory.GetFiles(ScriptsDir, "*.ps1", SearchOption.AllDirectories)
                .Where(path => !IsInsideHiddenFolder(path))
                .ToArray();
            Assert.IsNotEmpty(scripts, "в scripts/ не осталось ни одного .ps1 — тест потерял предмет");

            List<string> without = new List<string>();
            foreach (string path in scripts)
            {
                byte[] head = new byte[3];
                using (FileStream stream = File.OpenRead(path))
                {
                    if (stream.Read(head, 0, 3) < 3) { without.Add(Path.GetFileName(path)); continue; }
                }

                bool hasBom = head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
                if (!hasBom) without.Add(Path.GetFileName(path));
            }

            Assert.IsEmpty(without,
                "эти скрипты сохранены без UTF-8 BOM и не запустятся в Windows PowerShell 5.1: " +
                string.Join(", ", without.OrderBy(name => name)) +
                ". Пересохрани их в «UTF-8 с сигнатурой» — правило см. в докстринге теста.");
        }
    }
}
