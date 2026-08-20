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
    /// Гейт СОСТОЯНИЙ: элемент, принимающий указатель, обязан отвечать на все состояния из своей
    /// записи в <see cref="UiComponentRegistry"/>.
    /// </summary>
    /// <remarks>
    /// <b>Почему тест, а не дисциплина.</b> На 06.08.2026 <c>:checked</c> не был объявлен ни разу,
    /// <c>:active</c> отсутствовал у двенадцати кликабельных классов, <c>:focus</c> у тринадцати. Ни
    /// одно из этих упущений не было замечено при написании — набор состояний держался в голове, и
    /// каждый раз забывалось разное. Контактный лист показывает дыру глазу, этот гейт — сборке.
    ///
    /// <para><b>Что считается покрытием.</b> Селектор, который СОДЕРЖИТ класс блока и псевдокласс
    /// состояния. Мягко намеренно: половина наших правил — двухклассовые
    /// (<c>.unity-button.gm-button:hover</c>), потому что одноклассовый селектор проигрывает теме
    /// Unity, а <c>:checked</c> вообще садится на потомка (<c>.gm-toggle-row .unity-toggle:checked</c>).
    /// Требовать точного вида селектора значило бы гейтить приём вместо результата.</para>
    ///
    /// <para><b>Выключенность считается по двум приметам</b> — <c>:disabled</c> и класс
    /// <c>.unity-disabled</c>. Движок при <c>SetEnabled(false)</c> поднимает оба, и у нас в ходу оба;
    /// признавать только первый значило бы объявить дефектом работающий код.</para>
    ///
    /// <para><b>Наследование.</b> Состояние засчитывается, если объявлено у блока ИЛИ выше по цепочке
    /// <see cref="UiComponentEntry.Base"/>. Без этого гейт требовал бы у <c>gm-runbar__tab</c> своих
    /// правил, тогда как он живёт на <c>gm:Chip</c> и отвечает правилами <c>.gm-chip</c>.</para>
    /// </remarks>
    [TestFixture]
    public sealed class UiStateGateTests
    {
        private const string ThemeDir = "_Project/UI/Theme";

        /// <summary>Селектор — всё до открывающей скобки правила.</summary>
        private static readonly Regex SelectorRule = new(@"([^{}]+)\{", RegexOptions.Compiled);

        [Test]
        public void Интерактивный_элемент_отвечает_на_все_свои_состояния()
        {
            List<string> selectors = AllSelectors();
            Assert.That(selectors, Is.Not.Empty, "в теме не нашлось ни одного селектора — сломан разбор, а не тема");

            var byBlock = UiComponentRegistry.All.ToDictionary(e => e.Block);
            var complaints = new List<string>();

            foreach (UiComponentEntry entry in UiComponentRegistry.All)
            {
                if (!entry.IsInteractive) continue;

                foreach (UiElementState state in StatesOf(entry.Required))
                {
                    if (Declared(selectors, byBlock, entry, state)) continue;

                    complaints.Add($"  {entry.Label} ({entry.Block}) — нет {Pseudo(state)}");
                }
            }

            Assert.IsEmpty(complaints,
                "Элемент, который принимает указатель, обязан отвечать на каждое состояние из своей записи.\n" +
                "Обязательный набор — решение Макса 06.08.2026: наведение, нажатие, выключенность и фокус.\n" +
                "Фокус нужен клавиатуре и геймпаду, а не мыши: без него интерфейс непроходим на Steam Deck,\n" +
                "и по монитору этого не увидеть. Не нужно состояние конкретному элементу — снимай требование\n" +
                "в реестре осознанно, а не молчанием в USS.\n" +
                string.Join("\n", complaints));
        }

        [Test]
        public void Порядок_ярусов_темы_не_менялся()
        {
            string themePath = Path.Combine(Application.dataPath, ThemeDir, "theme.uss");
            Assert.IsTrue(File.Exists(themePath), $"нет агрегатора темы: {themePath}");

            List<string> imports = Regex.Matches(File.ReadAllText(themePath), @"@import\s+url\(""([^""]+)""\)")
                                        .Select(m => m.Groups[1].Value)
                                        .ToList();

            Assert.That(imports.Count, Is.GreaterThanOrEqualTo(3),
                "Ярусов меньше трёх: агрегатор темы должен собирать примитивы, семантику и компоненты.");

            Assert.That(imports[0], Is.EqualTo("tokens.primitives.uss"),
                "Примитивы идут первыми — на них ссылается семантика.");
            Assert.That(imports[1], Is.EqualTo("tokens.semantic.uss"),
                "Семантика идёт второй — на неё ссылаются компоненты.");

            for (int i = 2; i < imports.Count; i++)
            {
                Assert.That(imports[i],
                    Does.StartWith("components").Or.StartWith("screens").Or.StartWith("utilities"),
                    $"После двух ярусов токенов идут только таблицы компонентов, экранов и утилит, " +
                    $"а не «{imports[i]}».\n" +
                    "Порядок здесь несущий: при равной специфичности выигрывает импортированный ПОЗЖЕ,\n" +
                    "поэтому перестановка молча меняет вид там, где никто ничего не правил.");
            }

            // Утилиты — ПОСЛЕДНИЕ, и это несущее правило, а не порядок для красоты. Метка текста
            // (.gm-text--danger) равна по специфичности правилу роли (.gm-tooltip__desc), поэтому
            // при равенстве решает порядок импорта. Первый прогон витрины 06.08.2026 показал метку
            // «опасность» неотличимой от покоя ровно по этой причине.
            int firstUtility = imports.FindIndex(i => i.StartsWith("utilities"));
            if (firstUtility >= 0)
            {
                Assert.That(imports.Skip(firstUtility).All(i => i.StartsWith("utilities")), Is.True,
                    "После яруса утилит не должно идти ничего: утилита обязана перебивать и компонент,\n" +
                    "и экран, иначе она срабатывает через раз — по алфавиту имени файла.");
            }
        }

        /// <summary>Объявлено ли состояние у самого блока или выше по цепочке наследования.</summary>
        private static bool Declared(List<string> selectors, IReadOnlyDictionary<string, UiComponentEntry> byBlock,
                                     UiComponentEntry entry, UiElementState state)
        {
            var seen = new HashSet<string>();

            for (UiComponentEntry current = entry;
                 current != null && seen.Add(current.Block);
                 current = current.Base != null && byBlock.TryGetValue(current.Base, out UiComponentEntry parent) ? parent : null)
            {
                if (DeclaredOn(selectors, current.Block, state)) return true;
            }

            return false;
        }

        private static bool DeclaredOn(List<string> selectors, string block, UiElementState state)
        {
            var hasClass = new Regex(@"\." + Regex.Escape(block) + @"(?![a-zA-Z0-9_-])");

            for (int i = 0; i < selectors.Count; i++)
            {
                string selector = selectors[i];
                if (!hasClass.IsMatch(selector)) continue;

                if (selector.Contains(Pseudo(state))) return true;

                // Выключенность движок поднимает и классом, и псевдоклассом — в ходу обе приметы.
                if (state == UiElementState.Disabled && selector.Contains(".unity-disabled")) return true;
            }

            return false;
        }

        private static string Pseudo(UiElementState state) => ":" + state.ToString().ToLowerInvariant();

        private static IEnumerable<UiElementState> StatesOf(UiElementState mask)
        {
            foreach (UiElementState state in new[]
                     {
                         UiElementState.Hover, UiElementState.Active, UiElementState.Focus,
                         UiElementState.Disabled, UiElementState.Checked,
                     })
            {
                if (mask.HasFlag(state)) yield return state;
            }
        }

        /// <summary>
        /// Все селекторы игровой темы. Редакторный тулинг из обхода выведен — он рисуется в тему
        /// редактора и игроку не показывается (та же граница, что у гейта ярусов токенов).
        /// </summary>
        private static List<string> AllSelectors()
        {
            var selectors = new List<string>();
            string themeRoot = Path.Combine(Application.dataPath, "_Project/UI");

            foreach (string file in Directory.GetFiles(themeRoot, "*.uss", SearchOption.AllDirectories))
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

        /// <summary>Комментарии, вырезанные из текста: замеры цвета внутри них — не селекторы.</summary>
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
