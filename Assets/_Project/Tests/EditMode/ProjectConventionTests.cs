using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode
{
    /// <summary>
    /// Конвенции проекта, объявленные в <c>CLAUDE.md</c> как HARD. Каждая из них нарушается СНАРУЖИ —
    /// в чужом файле, чужой сессией — и потому не может держаться комментарием: тот виден только
    /// одной стороне шва.
    /// </summary>
    public sealed class ProjectConventionTests
    {
        /// <summary>
        /// Весь наш редакторный тулинг живёт под корнем «Alebardium/».
        /// </summary>
        /// <remarks>
        /// Меню Unity общее с вендорными пакетами: пункт, заведённый мимо корня, оседает среди чужих
        /// и перестаёт находиться. Проверяем только НАШИ сборки — вендорные вольны класть свои пункты
        /// куда угодно.
        /// </remarks>
        [Test]
        public void EveryEditorMenuItem_LivesUnderAlebardium()
        {
            const string Root = "Alebardium/";

            var offenders = new List<string>();
            foreach (Assembly asm in OurAssemblies())
            {
                foreach (Type type in TypesOf(asm))
                {
                    MethodInfo[] methods = type.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);

                    foreach (MethodInfo m in methods)
                    {
                        foreach (MenuItem attr in m.GetCustomAttributes<MenuItem>())
                        {
                            if (attr.menuItem != null && !attr.menuItem.StartsWith(Root, StringComparison.Ordinal))
                            {
                                offenders.Add($"{attr.menuItem}  ({type.FullName}.{m.Name})");
                            }
                        }
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Пункты редакторного меню обязаны начинаться с «Alebardium/» — иначе теряются среди "
                + "вендорных. Мимо корня: " + string.Join("; ", offenders));
        }

        /// <summary>
        /// В исходниках нет невидимых символов — неразрывного пробела и пробела нулевой ширины.
        /// </summary>
        /// <remarks>
        /// Такой символ даёт строки, которые выглядят одинаково и при этом не равны, а любая
        /// нормализация пробелов молча превращает его в обычный. Нужен в значении — пишется
        /// escape-последовательностью, и тогда он виден в дифе. Так уже случилось с константой
        /// неразрывного пробела в <c>StatFormat</c>: докстринг обещал escape, в файле лежал символ.
        /// </remarks>
        [Test]
        public void NoSourceFile_ContainsInvisibleCharacters()
        {
            // Они только escape: иначе тест сам содержал бы то, что ловит, и падал бы на себе.
            const char Nbsp = '\u00A0';
            const char ZeroWidthSpace = '\u200B';

            string root = Path.Combine(Application.dataPath, "_Project");
            Assert.That(Directory.Exists(root), "Не найден корень исходников: " + root);

            var offenders = new List<string>();
            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                int index = text.IndexOfAny(new[] { Nbsp, ZeroWidthSpace });
                if (index < 0) continue;

                int line = text.Take(index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line}");
            }

            Assert.That(offenders, Is.Empty,
                "Невидимый символ в исходнике: строки выглядят одинаково и не равны, а форматирование "
                + "молча заменит его на обычный пробел. Нужен в значении — пиши escape (\\u00A0). Найден в: "
                + string.Join(", ", offenders));
        }

        private static IEnumerable<Assembly> OurAssemblies() =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("Guildmaster.", StringComparison.Ordinal));

        /// <summary>Типы сборки, не падая на той, чьи зависимости не догрузились.</summary>
        private static IEnumerable<Type> TypesOf(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }
    }
}
