using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Гейт ЯРУСОВ: компонент берёт цвет только из роли. Ступень палитры в компонентном USS и сырой
    /// литерал цвета — дефекты, а не сокращения.
    /// </summary>
    /// <remarks>
    /// <b>Почему тест, а не правило в документе.</b> Правило существовало комментарием в шапке
    /// <c>tokens.primitives.uss</c> с 2026-07-27 и к 04.08.2026 было нарушено в семнадцати местах —
    /// девять из них в базовых кнопках. Последнее добавила Никси, перенеся цвет титула из старого
    /// кода не глядя. Дисциплина без проверки размывается за месяцы, и заметить это можно только
    /// когда палитру пробуют сменить целиком: половина интерфейса не едет.
    ///
    /// <para><b>Что именно запрещено.</b> Ссылки на ЦВЕТОВЫЕ примитивы — те, чьё значение в
    /// <c>tokens.primitives.uss</c> является цветом. Список собирается из самого файла, а не пишется
    /// руками: новая рампа попадает под гейт в тот же день, когда появилась. Шкалы
    /// (<c>--gm-space-*</c>, <c>--gm-font-*</c>, <c>--gm-radius-*</c>) разрешены намеренно — ступень
    /// шкалы осмысленна сама по себе, ступень рампы нет
    /// (см. <c>docs/wiki/tech/10-reference/ui-design-system.md</c>).</para>
    ///
    /// <para><b>Три законных исключения:</b> <c>rgba(0, 0, 0, 0)</c> — это «нет заливки», а не цвет;
    /// альфа поверх роли в САМОЙ семантике (USS не умеет <c>rgba(var(--токен), 0.42)</c>); файлы
    /// ярусов токенов, которым положено называть примитивы по имени.</para>
    /// </remarks>
    [TestFixture]
    public sealed class TokenTierTests
    {
        private const string ThemeDir      = "_Project/UI/Theme";
        private const string PrimitivesRel = ThemeDir + "/tokens.primitives.uss";
        private const string SemanticRel   = ThemeDir + "/tokens.semantic.uss";

        /// <summary>Объявление токена: имя и значение до точки с запятой.</summary>
        private static readonly Regex Declaration = new(@"(--gm-[a-z0-9-]+)\s*:\s*([^;]+);");

        /// <summary>Значение-цвет. Ступени шкал (px, числа) под него не подходят и остаются разрешёнными.</summary>
        private static readonly Regex ColorValue = new(@"^\s*(rgba?\(|#)", RegexOptions.IgnoreCase);

        /// <summary>Прозрачное «нет заливки» — единственный литерал, законный где угодно.</summary>
        private static readonly Regex Transparent = new(@"rgba\(\s*0\s*,\s*0\s*,\s*0\s*,\s*0(\.0+)?\s*\)");

        private static readonly Regex AnyLiteralColor = new(@"(rgba?\([^)]*\)|#[0-9a-f]{3,8}\b)", RegexOptions.IgnoreCase);

        [Test]
        public void Компонент_не_берёт_цвет_из_палитры()
        {
            HashSet<string> colorPrimitives = CollectColorPrimitives();
            Assert.That(colorPrimitives, Is.Not.Empty, "в примитивах не нашлось ни одного цвета — сломан разбор, а не палитра");

            var complaints = new List<string>();

            foreach (string file in ComponentSheets())
            {
                string[] lines = StripComments(File.ReadAllText(file));
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in Regex.Matches(lines[i], @"var\(\s*(--gm-[a-z0-9-]+)\s*\)"))
                    {
                        string token = m.Groups[1].Value;
                        if (!colorPrimitives.Contains(token)) continue;

                        complaints.Add($"  {Rel(file)}:{i + 1} — {token}: это ступень палитры. " +
                                       "Возьми роль из tokens.semantic.uss, а если подходящей роли нет — заведи её.");
                    }
                }
            }

            Assert.IsEmpty(complaints,
                "Цвет в компонентах берётся ТОЛЬКО через семантическую роль (правило 2026-07-27, гейт 2026-08-04).\n" +
                "Ступень палитры отвечает на вопрос «какой это цвет», но не на вопрос «что это значит», и\n" +
                "пока хоть один потребитель висит на ступени, палитру нельзя сменить целиком.\n" +
                string.Join("\n", complaints));
        }

        [Test]
        public void Компонент_не_держит_сырой_цвет()
        {
            var complaints = new List<string>();

            foreach (string file in ComponentSheets())
            {
                string[] lines = StripComments(File.ReadAllText(file));
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    foreach (Match m in AnyLiteralColor.Matches(line))
                    {
                        if (Transparent.IsMatch(m.Value)) continue;   // «нет заливки», а не цвет
                        complaints.Add($"  {Rel(file)}:{i + 1} — {m.Value}");
                    }
                }
            }

            Assert.IsEmpty(complaints,
                "Сырой цвет в компонентах запрещён: у него нет ни имени, ни смысла, и он не поедет при смене палитры.\n" +
                "Прозрачное rgba(0,0,0,0) не считается — это отсутствие заливки.\n" +
                string.Join("\n", complaints));
        }

        /// <summary>Имена примитивов, чьё значение — цвет. Шкалы сюда не попадают по значению.</summary>
        private static HashSet<string> CollectColorPrimitives()
        {
            var set = new HashSet<string>();
            string text = File.ReadAllText(Path.Combine(Application.dataPath, PrimitivesRel));

            foreach (Match m in Declaration.Matches(text))
            {
                if (ColorValue.IsMatch(m.Groups[2].Value)) set.Add(m.Groups[1].Value);
            }

            return set;
        }

        /// <summary>
        /// Комментарии, заменённые пробелами. Именно заменённые, а не вырезанные: номера строк в
        /// претензии обязаны совпадать с файлом, иначе гейт отправляет чинить не туда.
        /// </summary>
        /// <remarks>
        /// Построчной обрезки по <c>/*</c> НЕ хватает, и это ловилось живьём: в комментарии к правилу
        /// фокуса записан замер <c>RGBA(0, 0.416, 0.651)</c> — синяя рамка темы Unity, — и стоит он на
        /// строке ВНУТРИ многострочного комментария, а не после его начала.
        /// </remarks>
        private static string[] StripComments(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool inComment = false;

            for (int i = 0; i < text.Length; i++)
            {
                if (!inComment && i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
                {
                    inComment = true; sb.Append("  "); i++; continue;
                }

                if (inComment && i + 1 < text.Length && text[i] == '*' && text[i + 1] == '/')
                {
                    inComment = false; sb.Append("  "); i++; continue;
                }

                sb.Append(inComment && text[i] != '\n' && text[i] != '\r' ? ' ' : text[i]);
            }

            return sb.ToString().Split('\n');
        }

        /// <summary>
        /// Таблицы стилей ИГРОВОГО интерфейса. Из обхода выходят два яруса токенов (им как раз
        /// положено называть примитивы по имени — в этом их работа) и редакторный тулинг.
        /// </summary>
        /// <remarks>
        /// <b>Почему редакторные окна не под гейтом.</b> Content Hub и родня рисуются внутри редактора
        /// и обязаны попадать в ЕГО тёмную тему, а не в тему игры: игрок их не видит никогда, а
        /// разработчик видит рядом с инспектором Unity. Своя палитра там — не дубликат нашей, а другой
        /// адресат. Игровая тема при этом остаётся единственной для всего, что видит игрок.
        /// </remarks>
        private static IEnumerable<string> ComponentSheets()
        {
            string root = Path.Combine(Application.dataPath, "_Project");
            string primitives = Path.GetFullPath(Path.Combine(Application.dataPath, PrimitivesRel));
            string semantic   = Path.GetFullPath(Path.Combine(Application.dataPath, SemanticRel));
            string editorTools = Path.GetFullPath(Path.Combine(Application.dataPath, "_Project/Scripts/EditorTools"));

            foreach (string file in Directory.GetFiles(root, "*.uss", SearchOption.AllDirectories))
            {
                string full = Path.GetFullPath(file);
                if (full == primitives || full == semantic) continue;
                if (full.StartsWith(editorTools)) continue;
                yield return file;
            }
        }

        private static string Rel(string full)
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            string norm = full.Replace('\\', '/');
            return norm.StartsWith(dataPath) ? "Assets" + norm.Substring(dataPath.Length) : norm;
        }
    }
}
