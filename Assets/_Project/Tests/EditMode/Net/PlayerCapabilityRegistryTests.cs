using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Guildmaster.Guild.Commands;
using Guildmaster.Net;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Полнота реестра возможностей игрока (<c>docs/player-capability-registry.md</c>): всё, что
    /// игрок может инициировать и всё, чем состояние едет по сети, обязано иметь там строку.
    ///
    /// <para><b>Почему тестом, а не правилом в докстринге.</b> Реестр — второй берег шва: код на
    /// своей стороне ничего не теряет, если строки нет, и автор правки об этом не узнает. Ровно
    /// так родился разбор 04.08.2026, когда несинхронизированное находилось поштучно и в случайном
    /// порядке — пропавший тинт, пропавшая мана, неподсвеченные узлы. Это были не отдельные баги, а
    /// пустые клетки одной таблицы, которую никто не завёл.</para>
    ///
    /// <para><b>Держится только то, что выводится из кода.</b> Виды команд и каналы — перечисления,
    /// их полнота проверяема. Прозу про подачу (какая анимация обязана совпасть у обоих) автомат не
    /// проверит: она держится правилом ведения в самом реестре и ревью. Тест намеренно не делает
    /// вид, что закрывает больше, чем закрывает.</para>
    /// </summary>
    /// <remarks>
    /// Корень репозитория ищется от файла ЭТОГО теста вверх, а не через <c>Application.dataPath</c>:
    /// так тест исполняется и в быстром прогоне мимо редактора, где движка нет. Гейт, который виден
    /// только после тридцатисекундного прогона, срабатывает позже, чем правка уходит в коммит.
    /// </remarks>
    [TestFixture]
    public class PlayerCapabilityRegistryTests
    {
        private const string RegistryRelativePath = "docs/player-capability-registry.md";

        private static string RegistryPath([CallerFilePath] string callerFile = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(callerFile) ?? ".");
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, RegistryRelativePath);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        private static string ReadRegistry()
        {
            string path = RegistryPath();
            Assert.IsNotNull(path,
                "не нашла " + RegistryRelativePath + " ни в одной родительской папке от теста. " +
                "Реестр переехал или удалён — верни его или поправь путь здесь.");

            return File.ReadAllText(path);
        }

        [Test]
        public void Каждый_вид_команды_забега_описан_в_реестре()
        {
            string registry = ReadRegistry();

            List<string> missing = Enum.GetValues(typeof(RunCommandKind))
                .Cast<RunCommandKind>()
                .Where(kind => kind != RunCommandKind.None)
                .Select(kind => kind.ToString())
                .Where(name => !registry.Contains(name))
                .ToList();

            Assert.IsEmpty(missing,
                "эти виды команд игрок может инициировать, но в реестре про них не сказано ничего: " +
                string.Join(", ", missing) +
                ". Заведи строку в разделе «Право» — кто может, работает ли у гостя. " +
                "Ответ «только хост» допустим, но названный вслух.");
        }

        [Test]
        public void Каждый_сетевой_канал_описан_в_реестре()
        {
            string registry = ReadRegistry();

            List<string> missing = Enum.GetValues(typeof(NetChannel))
                .Cast<NetChannel>()
                .Select(channel => channel.ToString())
                .Where(name => !registry.Contains(name))
                .ToList();

            Assert.IsEmpty(missing,
                "эти каналы что-то везут между машинами, но в реестре их нет: " +
                string.Join(", ", missing) +
                ". Допиши в таблицу каналов — что несёт и в какую сторону.");
        }

        [Test]
        public void Реестр_не_потерял_свои_разделы()
        {
            string registry = ReadRegistry();

            // Три категории — несущая конструкция реестра, а не оглавление: строка, не попавшая ни в
            // одну, теряется молча. Именно так пропала анимация входа в узел — она не право и не
            // состояние, и потому не имела дома.
            string[] required =
            {
                "## Право", "## Состояние", "## Подача", "## Правило ведения",
            };

            List<string> lost = required.Where(section => !registry.Contains(section)).ToList();

            Assert.IsEmpty(lost,
                "реестр потерял разделы: " + string.Join(", ", lost) +
                ". Категории — несущая конструкция: возможность, не попавшая ни в одну, теряется молча.");
        }
    }
}
