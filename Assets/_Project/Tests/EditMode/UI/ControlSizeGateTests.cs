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
    /// Гейт РАЗМЕРА: меру и высоту контрола задаёт шкала набора, а не правило экрана.
    /// </summary>
    /// <remarks>
    /// <b>Откуда.</b> Разбор Макса 22.08.2026: «У нас который раз проблемы с размерами кнопок. Они у
    /// нас скачут по проекту. У нас нет разве какой-то единыой базы размеров кнопок и прочих
    /// элементов?» Замер подтвердил: из двенадцати правил, задававших кнопке размер, восемь сидели в
    /// правилах экранов — 430 в меню, 260 в настройках, 220 в «Продолжить» и в ленте забега, 36 в
    /// тулбаре, 100% ещё в двух местах. Цвет, кегль и отступы шкалу имели с самого начала, габариты
    /// не имели — и каждое новое место вбивало своё число.
    ///
    /// <para><b>Тот же приём, что у гейта типографики.</b> Там роль отвечает «какой это текст»,
    /// здесь ступень отвечает «какого размера этот контрол»: <c>--sm</c>, умолчание, <c>--lg</c>,
    /// плюс <c>--block</c> как отказ от меры. Экран волен двигать контрол отступами и раскладкой —
    /// но не назначать ему собственный рост.</para>
    ///
    /// <para><b>Исключения поимённо и с причиной</b> — как в гейтах мёртвого кода и типографики.
    /// Молчаливое послабление целым файлом означало бы, что через месяц никто не вспомнит, почему
    /// там можно.</para>
    /// </remarks>
    [TestFixture]
    public sealed class ControlSizeGateTests
    {
        /// <summary>Селектор — всё до открывающей скобки правила.</summary>
        private static readonly Regex RuleHead = new(@"([^{}]+)\{([^{}]*)\}", RegexOptions.Compiled);

        /// <summary>Свойства, которыми задают размер.</summary>
        private static readonly Regex Sizing =
            new(@"(^|\s)(width|height|min-width|min-height|max-width|max-height)\s*:",
                RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>
        /// Файлы, которым размер задавать и положено.
        /// </summary>
        /// <remarks>
        /// <c>button.uss</c> — дом самой шкалы: ступени живут там и обязаны задавать меру с высотой.
        /// <c>dev.uss</c> и <c>sheet.uss</c> — тулинг, которого игрок не видит: дев-консоль и
        /// контактный лист набора, там кнопки раскладываются под витрину, а не под игру.
        /// </remarks>
        private static readonly string[] ExemptFiles = { "button.uss", "dev.uss", "sheet.uss" };

        /// <summary>Поимённые исключения: селектор → причина.</summary>
        private static readonly Dictionary<string, string> ExemptSelectors = new()
        {
            [".gm-choice-list > .gm-button"] =
                "список ОТДАЁТ кнопке всю свою ширину — это раскладка контейнера, тот же приём, что " +
                "flex-grow у ряда кнопок в подвале панели",
            [".gm-panel--menu .gm-panel__body .gm-button, .gm-panel--menu > .gm-button"] =
                "ширина колонки диалога, а не размер кнопки: у неё свой токен-владелец " +
                "--gm-menu-column-width, общий на меню, паузу и профиль",
        };

        [Test]
        public void Меру_и_высоту_контрола_задаёт_ступень_набора()
        {
            var complaints = new List<string>();

            foreach (string file in ThemeFiles())
            {
                string name = Path.GetFileName(file);
                if (ExemptFiles.Contains(name)) continue;

                string text = StripComments(File.ReadAllText(file));

                foreach (Match rule in RuleHead.Matches(text))
                {
                    string selector = Collapse(rule.Groups[1].Value);
                    string body = rule.Groups[2].Value;

                    if (selector.Length == 0 || !selector.Contains("button")) continue;
                    if (!Sizing.IsMatch(body)) continue;
                    if (ExemptSelectors.ContainsKey(selector)) continue;

                    complaints.Add($"  {name}: {selector}");
                }
            }

            Assert.IsEmpty(complaints,
                "Меру и высоту кнопки задаёт СТУПЕНЬ набора (.gm-button--sm / умолчание /\n" +
                ".gm-button--lg, плюс --block как отказ от меры), а не правило экрана. Своё число в\n" +
                "экране — это ступень, расписанная под место: именно так одна кнопка стала в проекте\n" +
                "восемью разными размерами.\n" +
                "Либо повесь ступень в разметке, либо впиши селектор в ExemptSelectors С ПРИЧИНОЙ.\n" +
                string.Join("\n", complaints));
        }

        /// <summary>
        /// Ступени объявлены токенами, а не числами внутри правил ступеней.
        /// </summary>
        /// <remarks>
        /// Без этого шкала переехала бы в <c>button.uss</c> целиком и осталась бы такой же россыпью
        /// чисел, только в одном файле. Токен нужен ещё и затем, что мерой пользуется не только
        /// кнопка: значок берёт у неё высоту, чтобы стоять с ней в одном ряду.
        /// </remarks>
        [Test]
        public void Ступени_шкалы_объявлены_токенами()
        {
            string tokens = File.ReadAllText(
                Path.Combine(Application.dataPath, "_Project/UI/Theme/tokens.primitives.uss"));

            foreach (string token in new[]
                     {
                         "--gm-control-w-sm", "--gm-control-w-md", "--gm-control-w-lg",
                         "--gm-control-h-sm", "--gm-control-h-md", "--gm-control-h-lg",
                     })
            {
                StringAssert.Contains(token, tokens,
                    $"Ступень {token} исчезла из примитивов — шкала снова стала россыпью чисел.");
            }
        }

        private static IEnumerable<string> ThemeFiles()
            => Directory.GetFiles(Path.Combine(Application.dataPath, "_Project/UI"), "*.uss",
                                  SearchOption.AllDirectories);

        /// <summary>Селектор в одну строку и без лишних пробелов — чтобы сверять со списком исключений.</summary>
        private static string Collapse(string selector)
            => Regex.Replace(selector.Trim(), @"\s+", " ");

        private static string StripComments(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool inComment = false;

            for (int i = 0; i < text.Length; i++)
            {
                if (!inComment && i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
                {
                    inComment = true; i++; continue;
                }

                if (inComment && i + 1 < text.Length && text[i] == '*' && text[i + 1] == '/')
                {
                    inComment = false; i++; continue;
                }

                if (!inComment) sb.Append(text[i]);
            }

            return sb.ToString();
        }
    }
}
