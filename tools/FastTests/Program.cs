using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Guildmaster.Tools.FastTests
{
    /// <summary>
    /// Прогон EditMode-тестов мимо редактора: сборки уже собраны, здесь их только грузят и зовут
    /// методы с <c>[Test]</c>. Своего компилятора нет намеренно — команды компиляции принадлежат
    /// <c>compile-check.ps1</c>, и второй их владелец разъехался бы с ним дефайнами.
    /// </summary>
    internal static class Program
    {
        /// <summary>Итог одного теста. «Нужен редактор» — третий исход, не провал и не успех.</summary>
        private enum Outcome { Passed, Failed, NeedsEditor, Skipped }

        private static readonly List<string> Failures = new List<string>();
        private static readonly List<string> EditorOnly = new List<string>();

        /// <summary>
        /// Тесты, которые вне редактора НЕДОСТОВЕРНЫ, хотя формально запускаются: их результат здесь
        /// ничего не говорит об игре. Список задаётся снаружи и печатается поимённо — молча
        /// выкинутый тест ничем не отличается от забытого.
        /// </summary>
        private static readonly List<string> EditorOnlyByName = new List<string>();

        private static readonly List<string> SkippedByName = new List<string>();

        private static int Main(string[] args)
        {
            string binDir = Arg(args, "--bin");
            string[] assemblies = (Arg(args, "--assemblies") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            string[] probeDirs = (Arg(args, "--probe") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
            string filter = Arg(args, "--filter");
            EditorOnlyByName.AddRange((Arg(args, "--editor-only") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries));

            if (string.IsNullOrEmpty(binDir) || assemblies.Length == 0)
            {
                Console.Error.WriteLine("usage: FastTests --bin <dir> --assemblies A,B [--probe dir;dir] [--filter substring]");
                return 2;
            }

            SetupResolver(binDir, probeDirs);
            MuteUnityLogger();

            var sw = Stopwatch.StartNew();
            int passed = 0, failed = 0, needsEditor = 0, skipped = 0;

            foreach (string name in assemblies)
            {
                string path = Path.Combine(binDir, name + ".dll");
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"НЕТ СБОРКИ: {path} — сначала compile-check.ps1");
                    return 2;
                }

                Assembly asm;
                try
                {
                    asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"НЕ ЗАГРУЗИЛАСЬ {name}: {e.GetType().Name}: {e.Message}");
                    return 2;
                }

                foreach (Type type in SafeTypes(asm))
                {
                    if (type.IsAbstract || type.ContainsGenericParameters) continue;

                    MethodInfo[] tests = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => HasAttr(m, "TestAttribute") || HasAttr(m, "TestCaseAttribute"))
                        .ToArray();
                    if (tests.Length == 0) continue;

                    foreach (MethodInfo test in tests)
                    {
                        string id = $"{type.FullName}.{test.Name}";
                        if (!string.IsNullOrEmpty(filter) && id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        switch (RunOne(type, test, id))
                        {
                            case Outcome.Passed: passed++; break;
                            case Outcome.Failed: failed++; break;
                            case Outcome.NeedsEditor: needsEditor++; break;
                            case Outcome.Skipped: skipped++; break;
                        }
                    }
                }
            }

            sw.Stop();
            Report(passed, failed, needsEditor, skipped, sw.Elapsed.TotalSeconds);

            // Ноль запущенных — это НЕ успех: так выглядит опечатка в фильтре, и молчаливое «зелено»
            // здесь стоило бы дороже любого падения (та же защита, что в run-tests.ps1).
            if (passed + failed == 0)
            {
                // Отдельный код, а не общая ошибка: при фильтре по нескольким сборкам пустота в одной
                // из них нормальна, и решить, беда это или нет, может только вызывающий, который
                // видит все наборы разом.
                Console.WriteLine("Ни одного теста не запущено (фильтр? сборка?).");
                return 3;
            }

            return failed > 0 ? 1 : 0;
        }

        private static Outcome RunOne(Type type, MethodInfo test, string id)
        {
            // [TestCase] с аргументами и параметризованные фикстуры не поддерживаем: их разбор — это
            // половина NUnit, а нам нужен быстрый предпросмотр, а не второй раннер. Они идут в
            // «пропущено» и остаются за редактором.
            if (test.GetParameters().Length > 0 || HasAttr(test, "IgnoreAttribute") || HasAttr(test, "ExplicitAttribute"))
                return Outcome.Skipped;

            // [UnityTest] — корутина, её крутит игровой цикл; вне редактора запускать нечем.
            if (HasAttr(test, "UnityTestAttribute")) return Outcome.Skipped;

            foreach (string mask in EditorOnlyByName)
            {
                if (id.IndexOf(mask, StringComparison.OrdinalIgnoreCase) < 0) continue;
                SkippedByName.Add(id);
                return Outcome.NeedsEditor;
            }

            object instance;
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                return Classify(id, Unwrap(e), "конструктор фикстуры");
            }

            try
            {
                InvokeAll(type, instance, "SetUpAttribute");
                test.Invoke(instance, null);
                InvokeAll(type, instance, "TearDownAttribute");
                return Outcome.Passed;
            }
            catch (Exception e)
            {
                return Classify(id, Unwrap(e), null);
            }
        }

        private static void InvokeAll(Type type, object instance, string attrName)
        {
            foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                if (HasAttr(m, attrName) && m.GetParameters().Length == 0)
                    m.Invoke(instance, null);
        }

        /// <summary>
        /// Провал теста или отсутствие движка. Различать обязательно: вне редактора нативные вызовы
        /// Unity падают сами по себе, и записать их в провалы значило бы утопить прогон в красном,
        /// а записать в успехи — соврать, что механика проверена.
        /// </summary>
        private static Outcome Classify(string id, Exception e, string where)
        {
            if (IsEngineMissing(e))
            {
                EditorOnly.Add(id);
                return Outcome.NeedsEditor;
            }

            string prefix = where == null ? "" : where + ": ";
            string message = e.Message;
            if (message.Length > 300) message = message.Substring(0, 300) + "…";
            Failures.Add($"{id}\n    {prefix}{e.GetType().Name}: {message}");
            return Outcome.Failed;
        }

        /// <summary>Признаки того, что упало не наше, а отсутствующий рантайм движка.</summary>
        private static bool IsEngineMissing(Exception e)
        {
            for (Exception cur = e; cur != null; cur = cur.InnerException)
            {
                if (cur is DllNotFoundException || cur is EntryPointNotFoundException ||
                    cur is BadImageFormatException || cur is MissingMethodException ||
                    cur is TypeLoadException || cur is FileNotFoundException)
                    return true;

                string m = cur.Message ?? "";

                // Главный признак: сборки движка полны internal call'ов, которые линкует только
                // рантайм Unity. Обычный .NET отказывается загружать такой ТИП целиком, и падает не
                // тест, а первое же прикосновение к движковому классу.
                if (cur is System.Security.SecurityException && m.Contains("ECall", StringComparison.Ordinal))
                    return true;

                // LogAssert из Unity Test Framework: он ведёт свой перехват лога, и без редактора
                // области лога просто нет. Тест при этом про поведение, а не про логи.
                if (m.Contains("No log scope is available", StringComparison.Ordinal)) return true;

                // Часть заглушек кидает обычный экземпляр с внятным текстом — ловим по нему.
                if (m.Contains("UnityEngine", StringComparison.Ordinal) &&
                    (m.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                     m.Contains("player loop", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        private static Exception Unwrap(Exception e)
            => e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;

        private static bool HasAttr(MemberInfo m, string attributeTypeName)
        {
            foreach (CustomAttributeData a in m.GetCustomAttributesData())
                if (a.AttributeType.Name == attributeTypeName) return true;
            return false;
        }

        /// <summary>Типы сборки без падения на тех, чьи зависимости не подтянулись.</summary>
        private static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        /// <summary>
        /// Резолвер сборок: сначала папка компилятора, потом переданные каталоги (Managed редактора и
        /// кэш пакетов, откуда берётся nunit). Без него рантайм ищет только рядом с FastTests.dll и
        /// падает на первой же ссылке на UnityEngine.
        /// </summary>
        private static void SetupResolver(string binDir, string[] probeDirs)
        {
            var dirs = new List<string> { binDir };
            dirs.AddRange(probeDirs);

            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                foreach (string dir in dirs)
                {
                    if (!Directory.Exists(dir)) continue;

                    string direct = Path.Combine(dir, name.Name + ".dll");
                    if (File.Exists(direct)) return ctx.LoadFromAssemblyPath(direct);

                    // Managed редактора разложен по подпапкам (UnityEngine/*.dll) — ищем и там.
                    string[] nested = Directory.GetFiles(dir, name.Name + ".dll", SearchOption.AllDirectories);
                    if (nested.Length > 0) return ctx.LoadFromAssemblyPath(nested[0]);
                }
                return null;
            };
        }

        /// <summary>
        /// Гасит логгер движка. Не косметика, а условие живучести прогона: логирование внутри
        /// UnityEngine уходит в internal call, которого вне редактора нет, а зовут его в том числе
        /// ФИНАЛИЗАТОРЫ движковых объектов (`VisualElement.Finalize` → `Debug.LogError`). Исключение
        /// из финализатора летит в потоке сборщика мусора — поймать его нельзя, и оно убивает весь
        /// процесс в случайный момент, унося отчёт по всем остальным тестам.
        /// </summary>
        private static void MuteUnityLogger()
        {
            try
            {
                Type debugType = Type.GetType("UnityEngine.Debug, UnityEngine.CoreModule");
                object logger = debugType?.GetProperty("unityLogger", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                logger?.GetType().GetProperty("logEnabled")?.SetValue(logger, false);
            }
            catch
            {
                // Не вышло — не беда: значит движковых типов в этом наборе может и не оказаться.
                // Падать здесь нельзя, иначе прогон не состоится из-за подстраховки.
            }
        }

        private static void Report(int passed, int failed, int needsEditor, int skipped, double seconds)
        {
            Console.WriteLine();
            if (Failures.Count > 0)
            {
                Console.WriteLine("ПРОВАЛЫ:");
                foreach (string f in Failures) Console.WriteLine("  " + f);
                Console.WriteLine();
            }

            Console.WriteLine($"Прошло: {passed} · Провалов: {failed} · Нужен редактор: {needsEditor} · Пропущено: {skipped} · {seconds:0.0} с");

            if (needsEditor > 0)
                Console.WriteLine($"«Нужен редактор» — тесты, зовущие движок; их проверяет run-tests.ps1 и CI, здесь они НЕ зачтены.");

            // Исключённые по списку называются поимённо: молча выкинутый тест неотличим от забытого.
            foreach (string id in SkippedByName)
                Console.WriteLine($"  вне редактора недостоверен, отдан редактору: {id}");
        }

        private static string Arg(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return null;
        }
    }
}
