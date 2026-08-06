using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Гейт МЁРТВОГО: класс без стиля и стиль без класса — оба дефекты, и оба тихие.
    /// </summary>
    /// <remarks>
    /// <b>Зачем.</b> Отменённое решение выглядит в дереве точно так же, как живое, и агент, читающий
    /// «как надо», достаёт оттуда мёртвое наравне с настоящим. На 06.08.2026 в теме было двадцать
    /// классов, которых не заказывает никто, и двенадцать классов, которые код вешает, а тема не
    /// рисует, — то есть код думал, что показал состояние, а на экране не менялось ничего.
    ///
    /// <para><b>Две стороны, обе нужны.</b> Класс в USS без потребителя — это правило, которое
    /// нельзя проверить: его не видно ни на одном экране. Класс в коде без правила — это намерение,
    /// которое не исполнилось. Первое чистится сносом, второе — дописыванием стиля, и путать их
    /// нельзя.</para>
    ///
    /// <para><b>Исключения перечислены поимённо и с причиной.</b> Молчаливый пропуск превратил бы
    /// гейт в решето: имя, собранное в рантайме (<c>gm-cursor--p1</c>), выглядит для текстового
    /// разбора точно так же, как забытый класс.</para>
    /// </remarks>
    [TestFixture]
    public sealed class UssDeadCodeTests
    {
        /// <summary>Класс вешается кодом из строки, собранной на лету, — текстом его не поймать.</summary>
        private static readonly string[] BuiltAtRuntime =
        {
            "gm-cursor--p",            // gm-cursor--p{i}, номер игрока
            "gm-profile__swatch--p",   // тот же приём для образцов цвета
            "gm-runbar__tab--",        // модификатор режима подставляется по id
            "gm-filter-tab--",         // фильтр инвентаря подставляет вид контента
        };

        /// <summary>Класс-маркер: его читает код, а рисовать его не должен никто.</summary>
        private static readonly string[] MarkerOnly =
        {
            "gm-device--desktop", "gm-device--deck", "gm-device--ultrawide", // читает UiRootBootstrap
            "gm-pause-root",           // якорь слоя паузы для роутера
            "gm-screen--transparent",  // отметка «экран без подложки» для UiScreen
            "gm-edge-veil",            // контрол рисует вуаль мешем; класс — адрес для разметки
            "gm-tooltip__card--wide",  // читает TooltipSystem, чтобы выбрать раскладку подсказки
            "gm-tooltip__body",        // адрес контейнера, вид задают его дети
            "gm-console__line--info",  // уровень лога по умолчанию: цвет базовый, отличать нечем
            "gm-dev-screens",          // ИМЯ элемента дев-панели, не класс
            "gm-dev-screen-content",   // то же
        };

        [Test]
        public void Класс_из_кода_имеет_правило_в_теме()
        {
            HashSet<string> styled = ClassesInTheme();
            var complaints = new List<string>();

            foreach ((string cls, string where) in ClassesInCodeAndMarkup())
            {
                if (styled.Contains(cls)) continue;
                if (MarkerOnly.Contains(cls)) continue;
                if (BuiltAtRuntime.Any(prefix => cls.StartsWith(prefix))) continue;

                complaints.Add($"  {cls} — вешается в {where}, но тема его не рисует");
            }

            Assert.IsEmpty(complaints,
                "Класс, который код вешает, а тема не рисует, — это намерение, которое не исполнилось:\n" +
                "код считает, что показал состояние, а на экране не меняется ничего. Либо допиши правило,\n" +
                "либо убери класс. Если он нужен как МАРКЕР для кода — впиши его в MarkerOnly с причиной.\n" +
                string.Join("\n", complaints.Distinct()));
        }

        [Test]
        public void Правило_в_теме_имеет_потребителя()
        {
            HashSet<string> used = new(ClassesInCodeAndMarkup().Select(x => x.Class));
            var complaints = new List<string>();

            foreach (string cls in ClassesInTheme())
            {
                if (used.Contains(cls)) continue;
                if (BuiltAtRuntime.Any(prefix => cls.StartsWith(prefix))) continue;

                complaints.Add($"  {cls}");
            }

            Assert.IsEmpty(complaints,
                "Правило, которого не заказывает никто, нельзя ни увидеть, ни проверить — а прочитать\n" +
                "как живое можно. Именно так отменённые решения доживают до следующего захода.\n" +
                "Класс собирается в рантайме — впиши префикс в BuiltAtRuntime.\n" +
                string.Join("\n", complaints));
        }

        /// <summary>Все классы <c>gm-*</c>, встречающиеся в селекторах темы.</summary>
        private static HashSet<string> ClassesInTheme()
        {
            var classes = new HashSet<string>();
            string themeRoot = Path.Combine(Application.dataPath, "_Project/UI");

            foreach (string file in Directory.GetFiles(themeRoot, "*.uss", SearchOption.AllDirectories))
            {
                string text = StripComments(File.ReadAllText(file));

                foreach (Match rule in Regex.Matches(text, @"([^{}]+)\{"))
                {
                    foreach (Match cls in Regex.Matches(rule.Groups[1].Value, @"\.(gm-[a-zA-Z0-9_-]+)"))
                    {
                        classes.Add(cls.Groups[1].Value);
                    }
                }
            }

            return classes;
        }

        /// <summary>Все классы <c>gm-*</c>, которые заказывают разметка и код.</summary>
        private static IEnumerable<(string Class, string Where)> ClassesInCodeAndMarkup()
        {
            string root = Path.Combine(Application.dataPath, "_Project");

            foreach (string file in Directory.GetFiles(root, "*.uxml", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                foreach (Match attr in Regex.Matches(File.ReadAllText(file), @"class\s*=\s*""([^""]*)"""))
                {
                    foreach (string cls in attr.Groups[1].Value.Split(' '))
                    {
                        if (cls.StartsWith("gm-")) yield return (cls, name);
                    }
                }
            }

            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                string text = File.ReadAllText(file);

                // ЛЮБОЙ литерал `gm-*`, а не только вызовы AddToClassList. Классы вешаются слишком
                // многими способами, чтобы перечислить их регуляркой: через константу
                // (`ScrimlessClass`), через свой хелпер (`Line("gm-tooltip__tags")`), через switch,
                // возвращающий имя (`gm-kw--status`), через параметр метода. Узкий разбор пробовался
                // и назвал сиротами четырнадцать живых классов.
                //
                // Цена широкого разбора — имена элементов (`name = "gm-dev-screens"`) выглядят так же,
                // как классы. Их немного, и они перечислены в NamesNotClasses.
                foreach (Match literal in Regex.Matches(text, @"""(gm-[a-zA-Z0-9_-]+)"""))
                {
                    yield return (literal.Groups[1].Value, name);
                }
            }
        }

        /// <summary>Комментарии вырезаются: в них лежат примеры классов, которых в дереве нет.</summary>
        private static string StripComments(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool inComment = false;

            for (int i = 0; i < text.Length; i++)
            {
                if (!inComment && i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*') { inComment = true; i++; continue; }
                if (inComment && i + 1 < text.Length && text[i] == '*' && text[i + 1] == '/') { inComment = false; i++; continue; }
                if (!inComment) sb.Append(text[i]);
            }

            return sb.ToString();
        }
    }
}
