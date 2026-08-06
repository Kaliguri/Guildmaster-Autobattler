using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Guildmaster.UI.Components;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Гейт ВТОРОЙ ОСИ текста: метки, регистр и цветовые токены.
    /// </summary>
    /// <remarks>
    /// <b>Зачем отдельно от гейта состояний.</b> Тот проверяет, отвечает ли элемент на указатель;
    /// здесь — три вещи, которые указателя не касаются вовсе: объявлена ли метка, известно ли
    /// значение регистра и не объявлен ли цвет впустую.
    ///
    /// <para><b>Откуда взялось.</b> На 06.08.2026 витрина показывала восемь текстовых ролей в покое и
    /// молчала о том, что на них накладывается: красный текст жил в семи местах дерева и не был виден
    /// нигде. Тогда же выяснилось, что цветовых токенов текста объявлено четырнадцать, из них
    /// <c>--gm-color-text-action</c> не занят НИ ОДНИМ правилом. Мёртвый токен опаснее мёртвого
    /// правила: он выглядит частью системы и зовёт занять себя вместо верного соседа.</para>
    /// </remarks>
    [TestFixture]
    public sealed class UiTextToneGateTests
    {
        /// <summary>Селектор — всё до открывающей скобки правила.</summary>
        private static readonly Regex SelectorRule = new(@"([^{}]+)\{", RegexOptions.Compiled);

        /// <summary>Объявление токена: <c>--gm-color-x: …;</c> в блоке <c>:root</c>.</summary>
        private static readonly Regex Declaration = new(@"(--gm-color-[a-z0-9-]+)\s*:", RegexOptions.Compiled);

        /// <summary>Чтение токена: <c>var(--gm-color-x)</c>.</summary>
        private static readonly Regex Usage = new(@"var\(\s*(--gm-color-[a-z0-9-]+)", RegexOptions.Compiled);

        /// <summary>Объявление регистра: <c>--gm-text-case: upper;</c>.</summary>
        private static readonly Regex TextCase = new(@"--gm-text-case\s*:\s*([a-z]+)", RegexOptions.Compiled);

        [Test]
        public void Каждая_метка_текста_объявлена_в_теме()
        {
            List<string> selectors = AllSelectors();
            Assert.That(selectors, Is.Not.Empty, "в теме не нашлось ни одного селектора — сломан разбор, а не тема");

            var complaints = new List<string>();

            foreach (UiTextTone tone in Enum.GetValues(typeof(UiTextTone)))
            {
                if (tone == UiTextTone.None) continue;

                string cls = ".gm-text--" + tone.ToString().ToLowerInvariant();
                if (selectors.Any(s => s.Contains(cls, StringComparison.Ordinal))) continue;

                complaints.Add($"метка {tone} объявлена в UiTextTone, но правила {cls} в теме нет — " +
                               "витрина покажет её неотличимой от покоя");
            }

            Assert.That(complaints, Is.Empty, string.Join("\n", complaints));
        }

        /// <summary>
        /// Метки перечисляются только у текстовых ролей: на кнопке или карточке цвет несёт не метка,
        /// а её собственное состояние, и смешивать эти две оси мы уже пробовали.
        /// </summary>
        [Test]
        public void Метки_объявлены_только_у_текстовых_ролей()
        {
            var complaints = UiComponentRegistry.All
                .Where(e => e.Tones != UiTextTone.None && e.Group != UiComponentGroup.Typography)
                .Select(e => $"{e.Block}: метки {e.Tones} у элемента группы {e.Group}")
                .ToList();

            Assert.That(complaints, Is.Empty, string.Join("\n", complaints));
        }

        /// <summary>
        /// Регистр — своё свойство, и движок его НЕ ПРОВЕРЯЕТ: неизвестное значение он молча отдаст
        /// коду, а тот так же молча прочтёт как «как написано». Опечатка вида <c>uppercase</c> вместо
        /// <c>upper</c> выглядела бы «капс не работает» без единой жалобы где-либо.
        /// </summary>
        [Test]
        public void Регистр_объявлен_только_известным_значением()
        {
            var known = new HashSet<string> { "upper", "none" };
            var complaints = new List<string>();

            foreach (string file in ThemeFiles())
            {
                string text = StripComments(File.ReadAllText(file));
                foreach (Match match in TextCase.Matches(text))
                {
                    string value = match.Groups[1].Value;
                    if (known.Contains(value)) continue;

                    complaints.Add($"{Path.GetFileName(file)}: --gm-text-case: {value} — " +
                                   "UiTextCase.Parse такого не знает и молча оставит текст как есть");
                }
            }

            Assert.That(complaints, Is.Empty, string.Join("\n", complaints));
        }

        /// <summary>
        /// Цветовой токен ТЕКСТА, объявленный и никем не читаемый, — дефект.
        /// </summary>
        /// <remarks>
        /// Проверяются только цвета текста, а не все токены палитры: часть остальных читает не USS, а
        /// код через <c>CustomStyleProperty</c> (<c>--gm-plate-fill</c>, <c>--gm-veil-color</c>), и
        /// разбор одних лишь стилей объявил бы их мёртвыми. Здесь же весь оборот — внутри USS.
        /// </remarks>
        [Test]
        public void Цветовой_токен_текста_не_объявлен_впустую()
        {
            var declared = new Dictionary<string, string>();
            var used = new HashSet<string>();

            foreach (string file in ThemeFiles())
            {
                string text = StripComments(File.ReadAllText(file));

                foreach (Match match in Declaration.Matches(text))
                {
                    string token = match.Groups[1].Value;
                    if (IsTextColor(token)) declared[token] = Path.GetFileName(file);
                }

                foreach (Match match in Usage.Matches(text)) used.Add(match.Groups[1].Value);
            }

            Assert.That(declared, Is.Not.Empty, "не разобралось ни одного объявления — сломан разбор, а не тема");

            var complaints = declared
                .Where(pair => !used.Contains(pair.Key))
                .Select(pair => $"{pair.Key} объявлен в {pair.Value} и не прочитан ни одним правилом — " +
                                "либо занять, либо снять (прецедент: --gm-color-text-action, 06.08.2026)")
                .ToList();

            Assert.That(complaints, Is.Empty, string.Join("\n", complaints));
        }

        /// <summary>
        /// Токен красит текст: либо назван текстовым, либо перечислен как сигнальный.
        /// </summary>
        /// <remarks>
        /// Сигнальные вынесены списком, а не угаданы по имени: <c>--gm-color-accent</c> красит и
        /// текст, и каймы, и полосы прокрутки — судить о нём по одному лишь обороту в правилах
        /// нельзя, и в этот гейт он не входит.
        /// </remarks>
        private static bool IsTextColor(string token)
        {
            if (token.StartsWith("--gm-color-text", StringComparison.Ordinal)) return true;

            switch (token)
            {
                case "--gm-color-danger":
                case "--gm-color-negative":
                case "--gm-color-positive":
                case "--gm-color-warning":
                case "--gm-color-info":
                case "--gm-color-arcane":
                case "--gm-color-disabled-text":
                    return true;
                default:
                    return false;
            }
        }

        private static IEnumerable<string> ThemeFiles()
            => Directory.GetFiles(Path.Combine(Application.dataPath, "_Project/UI"), "*.uss",
                                  SearchOption.AllDirectories);

        private static List<string> AllSelectors()
        {
            var selectors = new List<string>();

            foreach (string file in ThemeFiles())
            {
                string text = StripComments(File.ReadAllText(file));

                foreach (Match match in SelectorRule.Matches(text))
                {
                    string selector = match.Groups[1].Value.Trim();
                    if (selector.Length > 0) selectors.Add(selector);
                }
            }

            return selectors;
        }

        /// <summary>Комментарии, вырезанные из текста: имена токенов внутри них — не объявления.</summary>
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
