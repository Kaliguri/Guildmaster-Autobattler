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
    /// Гейт ТИПОГРАФИКИ: кегль, гарнитуру и цвет текста задаёт ярус ролей, а не класс экрана.
    /// </summary>
    /// <remarks>
    /// <b>Откуда.</b> Перепись 06.08.2026 нашла в теме 81 правило с кеглем или гарнитурой на 75
    /// классов — и пять пар «кегль + цвет» покрывали 42 из них. Роли существовали, но были расписаны
    /// под каждый экран заново: «тело текста» жило четырьмя именами (<c>gm-tooltip__desc</c>,
    /// <c>gm-detail__desc</c>, <c>gm-event-body</c>, <c>gm-loadout__narrative-text</c>). Та же
    /// болезнь, что была у кнопок с «пунктом главного меню» вместо «кнопки».
    ///
    /// <para><b>Что разрешено, кроме ролей.</b> Правило СОСТОЯНИЯ (<c>:hover</c> и прочие
    /// псевдоклассы) и правило ВАРИАНТА (<c>--active</c>, <c>--sm</c>) — они отвечают на «что с
    /// элементом сейчас», а не «какой это текст». Без этой поблажки гейт запретил бы чипу светлеть
    /// под курсором.</para>
    ///
    /// <para><b>Исключения выписаны поимённо и с причиной</b> — так же, как в гейте мёртвого
    /// (решение Макса 06.08.2026: «жёстко, со списком исключений»). Молчаливое послабление целым
    /// файлом означало бы, что через месяц никто не вспомнит, почему там можно.</para>
    /// </remarks>
    [TestFixture]
    public sealed class UiTypographyGateTests
    {
        /// <summary>Селектор — всё до открывающей скобки правила.</summary>
        private static readonly Regex RuleHead = new(@"([^{}]+)\{([^{}]*)\}", RegexOptions.Compiled);

        /// <summary>Свойства, которыми задают вид текста.</summary>
        private static readonly Regex Typography =
            new(@"(^|\s)(font-size|-unity-font-definition|color)\s*:", RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>Псевдоклассы состояний: правило про них — про состояние, а не про роль.</summary>
        private static readonly string[] Pseudo =
            { ":hover", ":active", ":focus", ":disabled", ":checked", ".unity-disabled" };

        /// <summary>
        /// Файлы, живущие вне игровой типографики.
        /// </summary>
        /// <remarks>
        /// <c>screens/boot.uss</c> — экран загрузки: атрибуции и легальная строка набраны своим
        /// нейтральным серым (<c>--gm-color-brand-attrib</c>), которого в игровых метках нет и быть
        /// не должно — это не текст интерфейса, а выходные данные.
        /// <c>screens/dev.uss</c> и <c>components/sheet.uss</c> — тулинг, которого игрок не видит
        /// никогда: консоль, пикеры и сам контактный лист. Гарнитуру там задаёт ПРЕДОК
        /// (<c>.gm-console .unity-text-element</c>), то есть роль на отдельном элементе всё равно
        /// не работала бы.
        /// </remarks>
        private static readonly string[] ExemptFiles =
            { "boot.uss", "dev.uss", "sheet.uss", "DevBattlePicker.uss" };

        /// <summary>Поимённые исключения: селектор → причина.</summary>
        private static readonly Dictionary<string, string> ExemptSelectors = new()
        {
            [".gm-tab"] =
                "44px — единственное число мимо лестницы; кегль подобран замером по рефу и " +
                "принадлежит вкладке, ступени между 34 и 48 нет",
            [".gm-shop__toast-label"] =
                "48 у ГРОТЕСКА роли не имеет: gm-text-display это 48 дисплейной гарнитуры, то есть " +
                "вывеска игры. Тост об отказе и глиф сундука — два таких случая на всё дерево",
            [".gm-chest__glyph"] =
                "тот же случай, что у тоста лавки: крупный гротеск без роли",
            [".gm-loadout__narrative-text"] =
                "своя гарнитура --gm-font-voice-italic: курсивной антиквы нет ни у одной роли, а " +
                "потребитель ровно один — заводить роль под него значит заводить роль под место",
            [".gm-arcana-card__title"] =
                "антиква на xxs: единственный потребитель, роли voice+13 нет",
            [".gm-arcana-card__num"] =
                "номер арканы латунью на xs — часть рисунка карты, а не текст интерфейса",
            [".gm-loadout__search .unity-text-field__input"] =
                "поле ввода Unity: вид задаётся его собственному потомку, роль туда не повесить",
            [".gm-loadout__toolbar > .gm-button"] =
                "кегль КНОПКИ в контексте панели, а не текстовая роль",
            [".gm-loadout__battle-center > .gm-button"] =
                "то же: размер кнопки в своём месте",
            [".gm-cursor__name"] =
                "цвет курсора игрока приходит из палитры участников и меняется в рантайме",
            [".gm-console .unity-text-element"] =
                "моноширинная гарнитура всей консоли задаётся предком — это и есть её роль",
            [".unity-text-element"] =
                "базовая гарнитура интерфейса: ярус, с которого начинается всё остальное",
            [".gm-version-stamp"] =
                "штамп версии: служебная строка вне композиции, снимается со скриншота багрепорта",
            [".gm-button"] =
                "кнопка задаёт вид СВОЕЙ подписи, и кегль у неё меняется вариантом (--display): " +
                "роль на подписи пришлось бы перебивать в каждом варианте",
            [".gm-wordmark__over"] =
                "надстрочник вывески: дисплейная гарнитура на кегле тела — часть одной вещи со " +
                "словом под ним, а не самостоятельная роль",
            [".gm-wordmark__stage"] =
                "метка стадии под вывеской: тот же случай, что у надстрочника",
            [".gm-boot__hint"] =
                "подсказка ожидания на бут-экране: сериф вразрядку — интонация, а не роль; " +
                "разрядка и гарнитура здесь работают вместе и на одну строку",
            [".gm-select-row__dropdown .unity-base-popup-field__input"] =
                "внутренность выпадающего списка Unity: свой элемент, роль туда не повесить",
            [".gm-title-reveal__line"] =
                "титр крупнее вывески намеренно: --gm-font-reveal (96) — свой ярус на один элемент, " +
                "роли под него нет и заводить её значит заводить роль под место",
            [".unity-base-text-field__input"] =
                "поле ввода Unity: вид задаётся его собственному потомку, роль туда не повесить — " +
                "тот же случай, что у поиска в лоадауте, только правило теперь общее для всех полей",
            [".gm-tooltip__glossary-term"] =
                "жирность термина — не кегль и не цвет; правило осталось ради неё",
        };

        [Test]
        public void Кегль_и_цвет_текста_задаёт_ярус_ролей()
        {
            var complaints = new List<string>();

            foreach (string file in ThemeFiles())
            {
                string name = Path.GetFileName(file);
                if (ExemptFiles.Contains(name)) continue;

                string text = StripComments(File.ReadAllText(file));

                foreach (Match rule in RuleHead.Matches(text))
                {
                    string selector = rule.Groups[1].Value.Trim();
                    string body = rule.Groups[2].Value;

                    if (selector.Length == 0 || !Typography.IsMatch(body)) continue;
                    if (OnlyFontFamily(body)) continue;
                    if (IsAllowed(selector)) continue;

                    complaints.Add($"  {name}: {selector}");
                }
            }

            Assert.IsEmpty(complaints,
                "Кегль, гарнитуру и цвет текста задаёт РОЛЬ (.gm-text-*), а цвет поверх неё — МЕТКА\n" +
                "(.gm-text--*). Правило экрана, задающее их само, — это роль, расписанная под место:\n" +
                "именно так «тело текста» разошлось на четыре имени и разъехалось по кеглю.\n" +
                "Либо повесь роль в разметке, либо впиши селектор в ExemptSelectors С ПРИЧИНОЙ.\n" +
                string.Join("\n", complaints));
        }

        /// <summary>
        /// Кегль пишется СТУПЕНЬЮ шкалы, а не числом.
        /// </summary>
        /// <remarks>
        /// Литерал не выражает ничего, кроме следа правок: <c>.gm-button--display</c> держал
        /// <c>34px</c>, тогда как это ровно <c>--gm-font-title</c>, а комментарий над ним описывал
        /// давно ушедшие 28 и 48. Число мимо лестницы — всегда решение, и оно обязано быть
        /// объявленным.
        /// </remarks>
        [Test]
        public void Кегль_пишется_ступенью_а_не_числом()
        {
            var literal = new Regex(@"font-size\s*:\s*\d+px", RegexOptions.Compiled);
            var complaints = new List<string>();

            foreach (string file in ThemeFiles())
            {
                string name = Path.GetFileName(file);
                if (ExemptFiles.Contains(name)) continue;

                string text = StripComments(File.ReadAllText(file));

                foreach (Match rule in RuleHead.Matches(text))
                {
                    string selector = rule.Groups[1].Value.Trim();
                    if (!literal.IsMatch(rule.Groups[2].Value)) continue;
                    if (ExemptSelectors.ContainsKey(selector)) continue;

                    complaints.Add($"  {name}: {selector}");
                }
            }

            Assert.IsEmpty(complaints,
                "Кегль задаётся ступенью --gm-font-*, а не числом. Ступеней семь: 13 / 17 / 22 / 26 /\n" +
                "34 / 48 / 88. Нужно число между ними — это решение, и его место в ExemptSelectors\n" +
                "с причиной, иначе оно неотличимо от опечатки.\n" +
                string.Join("\n", complaints));
        }

        /// <summary>
        /// ОБРАТНАЯ ПРОВЕРКА: роль, объявленная в теме, обязана быть в реестре.
        /// </summary>
        /// <remarks>
        /// Остальные гейты смотрят в одну сторону — заявленное реализовано. Обратного не проверял
        /// никто, и это стоило невидимого элемента набора: роль <c>gm-text-subtitle</c> была заведена
        /// в теме и повешена в пяти местах разметки, но в <see cref="UiComponentRegistry"/> не
        /// внесена — контактный лист её не показывал, а все гейты были ЗЕЛЁНЫМИ (06.08.2026).
        ///
        /// <para>Проверяются только роли текста (<c>gm-text-*</c> без двойного дефиса): у них
        /// имя само объявляет принадлежность к набору. Метки (<c>gm-text--*</c>) живут перечнем
        /// <see cref="UiTextTone"/>, и за них отвечает <c>UiTextToneGateTests</c>.</para>
        /// </remarks>
        [Test]
        public void Роль_из_темы_объявлена_в_реестре()
        {
            var role = new Regex(@"\.(gm-text-(?!-)[a-z-]+)", RegexOptions.Compiled);
            var declared = new HashSet<string>();

            foreach (string file in ThemeFiles())
            {
                foreach (Match m in role.Matches(StripComments(File.ReadAllText(file))))
                {
                    declared.Add(m.Groups[1].Value);
                }
            }

            Assert.That(declared, Is.Not.Empty, "не нашлось ни одной роли — сломан разбор, а не тема");

            var known = new HashSet<string>(UiComponentRegistry.All.Select(e => e.Block));
            var complaints = declared
                .Where(cls => !known.Contains(cls))
                .Select(cls => $"  .{cls} — правило в теме есть, записи в UiComponentRegistry нет")
                .ToList();

            Assert.IsEmpty(complaints,
                "Роль, которой нет в реестре, НЕВИДИМА: её не покажет контактный лист, не проверит\n" +
                "гейт состояний и не озвучит UiSoundSystem. При этом всё остальное зелено — потому\n" +
                "что остальные гейты смотрят в другую сторону.\n" +
                string.Join("\n", complaints));
        }

        /// <summary>Причина исключения — не формальность: пустая строка тут хуже отсутствия записи.</summary>
        [Test]
        public void У_каждого_исключения_есть_причина()
        {
            var complaints = ExemptSelectors
                .Where(pair => string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length < 20)
                .Select(pair => pair.Key)
                .ToList();

            Assert.IsEmpty(complaints,
                "Исключение без внятной причины через месяц читается как «тут можно всем»:\n" +
                string.Join("\n", complaints));
        }

        /// <summary>
        /// Правило задаёт ТОЛЬКО гарнитуру — это ярус шрифта, а не роль.
        /// </summary>
        /// <remarks>
        /// Голос интерфейса (сериф) назначается списком блоков одним правилом, и кегля с цветом оно
        /// не трогает. Запретить его значило бы требовать роль там, где решается один-единственный
        /// вопрос — «этот элемент говорит или показывает».
        /// </remarks>
        private static bool OnlyFontFamily(string body)
            => !Regex.IsMatch(body, @"(^|\s)(font-size|color)\s*:", RegexOptions.Multiline);

        /// <summary>Правило состояния или варианта: оно про «что сейчас», а не про «какой это текст».</summary>
        private static bool IsAllowed(string selector)
        {
            if (ExemptSelectors.ContainsKey(selector)) return true;

            // Роли и метки — сам ярус типографики.
            if (selector.Contains(".gm-text-", StringComparison.Ordinal)) return true;

            // Состояние.
            if (Pseudo.Any(p => selector.Contains(p, StringComparison.Ordinal))) return true;

            // Вариант: класс-модификатор BEM. `--` встречается только в них и в наших переменных,
            // а переменных в селекторе не бывает.
            if (selector.Contains("--", StringComparison.Ordinal)) return true;

            return false;
        }

        private static IEnumerable<string> ThemeFiles()
            => Directory.GetFiles(Path.Combine(Application.dataPath, "_Project/UI"), "*.uss",
                                  SearchOption.AllDirectories);

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
