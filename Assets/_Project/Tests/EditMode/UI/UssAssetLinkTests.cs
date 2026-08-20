using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Гейт ссылок темы на ассеты: каждый <c>url("…")</c> обязан вести на существующий файл.
    /// </summary>
    /// <remarks>
    /// <b>Откуда взялось.</b> 07.08.2026 в теме нашлось СЕМНАДЦАТЬ битых ссылок на картинки — фон и
    /// все три логотипа бут-экрана, восемь иконок ленты забега, иконки фильтров, сепаратор, замок.
    /// Игрок видел вместо них пустые места, и заметил это тоже он, а не прогон.
    ///
    /// <para><b>Почему сломалось.</b> Тема переехала «файл на блок, файл на экран», и правила,
    /// лежавшие в <c>UI/Theme/</c>, оказались на уровень глубже — в <c>Theme/screens/</c>. Путь
    /// <c>../../Art/…</c> при этом остался прежним и стал указывать в несуществующую
    /// <c>UI/Art/</c>. Ни одна сторона об этом не узнала: относительная ссылка живёт в USS,
    /// картинка — на диске, и связь между ними до сих пор не проверял никто.</para>
    ///
    /// <para><b>Почему тестом, а не внимательностью.</b> Инвариант кросс-файловый: он ломается от
    /// ПЕРЕМЕЩЕНИЯ файла, в диффе которого не меняется ни один символ ссылки. Ревью такого не
    /// ловит — в глаза бросается содержимое правил, а сломался их адрес.</para>
    /// </remarks>
    [TestFixture]
    public sealed class UssAssetLinkTests
    {
        private const string UiRoot = "_Project/UI";

        /// <summary>Ссылка на ассет в USS: <c>url("путь")</c> или <c>url('путь')</c>.</summary>
        private static readonly Regex UrlRef = new(@"url\(\s*[""']([^""')]+)[""']\s*\)", RegexOptions.Compiled);

        /// <summary>
        /// Схемы, за которыми файла нет и не должно быть.
        /// </summary>
        /// <remarks>
        /// <c>unity-theme://</c> — встроенная тема движка, <c>project://</c> — путь от корня
        /// проекта (его резолвит сам Unity, и относительным он не является). Проверять их на
        /// существование файла значило бы получить вечно красный гейт на верной записи.
        /// </remarks>
        private static readonly string[] IgnoredSchemes = { "unity-theme://", "project://", "resource://" };

        [Test]
        public void Каждая_ссылка_темы_ведёт_на_существующий_файл()
        {
            var broken = new List<string>();

            foreach (string file in UssFiles())
            {
                string dir = Path.GetDirectoryName(file);

                foreach (Match match in UrlRef.Matches(File.ReadAllText(file)))
                {
                    string url = match.Groups[1].Value.Trim();
                    if (IsIgnored(url)) continue;

                    // Путь в USS — от самого USS-файла, а не от корня проекта: ровно это правило и
                    // сломалось переездом темы.
                    string resolved = Path.GetFullPath(Path.Combine(dir, url));
                    if (File.Exists(resolved)) continue;

                    broken.Add($"{Rel(file)} → {url}");
                }
            }

            if (broken.Count == 0) return;

            var report = new StringBuilder();
            report.AppendLine($"Битых ссылок на ассеты в теме: {broken.Count}.");
            report.AppendLine("Путь в url(\"…\") считается ОТ ФАЙЛА USS. Переезд файла на уровень глубже");
            report.AppendLine("ломает такие ссылки молча — допиши уровень или поправь адрес.");
            report.AppendLine();
            foreach (string line in broken) report.AppendLine("  " + line);

            Assert.Fail(report.ToString());
        }

        private static bool IsIgnored(string url)
        {
            for (int i = 0; i < IgnoredSchemes.Length; i++)
            {
                if (url.StartsWith(IgnoredSchemes[i], System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Все таблицы стилей интерфейса: и тема, и разметочные. Расширения два — <c>.uss</c> и
        /// <c>.tss</c>: тематический файл несёт такие же <c>url</c> и ломается так же.
        /// </summary>
        private static IEnumerable<string> UssFiles()
        {
            string root = Path.Combine(Application.dataPath, UiRoot);

            foreach (string file in Directory.GetFiles(root, "*.uss", SearchOption.AllDirectories))
                yield return file;

            foreach (string file in Directory.GetFiles(root, "*.tss", SearchOption.AllDirectories))
                yield return file;
        }

        private static string Rel(string absolute)
            => absolute.Substring(Application.dataPath.Length - "Assets".Length).Replace('\\', '/');
    }
}
