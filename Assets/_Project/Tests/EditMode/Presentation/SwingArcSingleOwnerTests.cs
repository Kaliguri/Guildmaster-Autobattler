using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Guildmaster.Presentation;
using Guildmaster.Presentation.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// У дуги за клинком ОДИН владелец, и обойти его нельзя.
    ///
    /// <b>Зачем этот тест существует.</b> Редакторный стенд показывал дугу СВОИМ кодом: сам решал, идёт ли
    /// взмах, сам раскладывал параметры, сам ставил порядок отрисовки. Каждый кусок по отдельности
    /// выглядел мелкой копией, а вместе они складывались во вторую правду о взмахе — и расходились с
    /// боевой молча. Требование Макса дословно (06.08.2026): «Хватит делать костыли. Еще раз. Один
    /// источник правды, и нельзя никак его обходить и случайно нарушить.»
    ///
    /// <b>Почему тест, а не договорённость.</b> «Случайно нарушить» — это ровно то, чего договорённость не
    /// ловит: обход рождается не злым умыслом, а строчкой «тут проще напрямую». Инвариант, живущий между
    /// боем и стендом, обязан падать тестом.
    /// </summary>
    public sealed class SwingArcSingleOwnerTests
    {
        /// <summary>Единственный, кому положено заводить дугу.</summary>
        const string LauncherFile = "SwingArcLaunch.cs";

        /// <summary>Где живёт сам эффект — там <c>Begin</c> объявлен, а не вызван.</summary>
        const string EffectFile = "SwingArcVfx.cs";

        [Test]
        public void ArcIsStarted_OnlyByLauncher()
        {
            var offenders = new List<string>();
            int scanned = 0;

            foreach (string file in ScriptsOf("Assets/_Project/Scripts"))
            {
                string text = File.ReadAllText(file);
                if (!text.Contains(nameof(SwingArcVfx))) continue;

                scanned++;
                string name = Path.GetFileName(file);
                if (name == LauncherFile || name == EffectFile) continue;

                string[] lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    // Ищем Begin у ЧЕГО-ТО ДРУГОГО: сам лаунчер зовут как раз правильно, и его вызовы —
                    // это ровно то, ради чего он существует.
                    if (!lines[i].Contains(".Begin(")) continue;
                    if (lines[i].Contains(nameof(SwingArcLaunch))) continue;

                    offenders.Add($"{name}:{i + 1} → {lines[i].Trim()}");
                }
            }

            Assert.That(scanned, Is.GreaterThan(1),
                "не найдено файлов, знающих про SwingArcVfx — тест проверял пустоту");
            Assert.That(offenders, Is.Empty,
                "Дугу заводят мимо SwingArcLaunch — значит её параметры (тумблер, яркость, стиль) снова " +
                "живут в двух местах:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Источник взмаха тоже один. Поддельный источник — самый удобный способ обойти всё разом:
        /// он отвечает «взмах идёт» по своему разумению, и никакой запуск через общего владельца этого уже
        /// не спасёт. Тестовым сборкам двойники разрешены: тест никому ничего не ПОКАЗЫВАЕТ, а правило
        /// ровно про показ.
        /// </summary>
        [Test]
        public void SwingSource_HasSingleImplementation()
        {
            var implementations = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("Guildmaster", StringComparison.Ordinal))
                .Where(a => !a.GetName().Name.StartsWith("Guildmaster.Tests", StringComparison.Ordinal))
                .SelectMany(SafeTypes)
                .Where(t => t != null && !t.IsInterface && !t.IsAbstract
                            && typeof(ISwingArcSource).IsAssignableFrom(t))
                .Select(t => t.FullName)
                .OrderBy(n => n)
                .ToList();

            Assert.That(implementations, Is.EquivalentTo(new[] { typeof(UnitView).FullName }),
                "Источник взмаха должен быть один — вид юнита. Лишний означает, что кто-то снова " +
                "рассказывает эффекту про взмах вместо того, чтобы спросить игру:\n  " +
                string.Join("\n  ", implementations));
        }

        static IEnumerable<Type> SafeTypes(System.Reflection.Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        static IEnumerable<string> ScriptsOf(string relative)
        {
            string root = Path.Combine(Application.dataPath, "..", relative);
            return Directory.Exists(root)
                ? Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                : Enumerable.Empty<string>();
        }
    }
}
