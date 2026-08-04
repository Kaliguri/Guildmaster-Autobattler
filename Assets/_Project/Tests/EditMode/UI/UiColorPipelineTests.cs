using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Guildmaster.UI.Components;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Гейт КОНВЕЙЕРА ЦВЕТА: что задано в токене, то и рисуется. Ручные преобразования цветового
    /// пространства и собственные цвета внутри контролов — дефекты.
    /// </summary>
    /// <remarks>
    /// <b>Почему тест, а не правило в документе.</b> Правило «не конвертируй цвет руками» жило
    /// докстрингом ровно сутки — и сам этот докстринг предписывал обратное. Шейдер UI Toolkit
    /// переводит вершинный tint в линейное пространство сам, как Canvas у uGUI; наш дополнительный
    /// <c>c.linear</c> был второй конверсией подряд и душил каждую заливку втрое:
    /// <c>rgb(44,140,146)</c> приходило на экран как <c>rgb(6,62,69)</c>. Замечено это было не
    /// глазами, а замером пикселей кадра, и стоило сессии подбора палитры, которая лечила симптом.
    ///
    /// <para>Комментарий не падает — падает тест. Отсюда три проверки ниже: конверсия отсутствует
    /// как таковая, контрол не хранит собственного цвета, и оба правила видны с обеих сторон шва.</para>
    ///
    /// <para><b>Отказаться от ручного меша нельзя:</b> градиентов в USS Unity не даёт, а градиент на
    /// кнопке нужен. Значит вторая дорога цвета (USS → custom property → поле C# → вершина) остаётся
    /// навсегда, и единственное, что делает её безопасной, — невозможность молча изменить значение
    /// по пути.</para>
    /// </remarks>
    [TestFixture]
    public sealed class UiColorPipelineTests
    {
        /// <summary>Каталог UI-кода: гейт смотрит на все компоненты, а не на список известных.</summary>
        private const string UiScriptsRel = "_Project/Scripts/UI";

        /// <summary>
        /// Ручной перевод цветового пространства в любом виде. <c>Color.linear</c> / <c>Color.gamma</c>
        /// и обе функции <see cref="Mathf"/> — всё это делает работу, которую уже сделал шейдер.
        /// </summary>
        private static readonly Regex ManualConversion =
            new(@"\.(linear|gamma)\b|GammaToLinearSpace|LinearToGammaSpace");

        /// <summary>Строка, целиком закомментированная: в ней ключевые слова — рассказ, а не код.</summary>
        private static readonly Regex CommentLine = new(@"^\s*(//|///|\*|/\*)");

        private static string ScriptsRoot => Path.Combine(Application.dataPath, UiScriptsRel);

        /// <summary>
        /// Единственная функция перевода цвета в проекте обязана быть ТОЖДЕСТВОМ. Красный тест здесь
        /// значит, что кто-то вернул конверсию — и все меши стали втрое темнее заданного.
        /// </summary>
        [Test]
        public void VertexColor_IsIdentity_ForEveryChannel()
        {
            var samples = new[]
            {
                new Color(0.024f, 0.255f, 0.278f, 1f),   // поле кнопки: как раз тот случай
                new Color(1f, 0f, 0f, 1f),
                new Color(0f, 0f, 0f, 0f),
                new Color(0.5f, 0.5f, 0.5f, 0.42f),
                new Color(1f, 1f, 1f, 1f),
            };

            foreach (Color c in samples)
            {
                Color got = PlateButton.VertexColor(c);
                Assert.That(got.r, Is.EqualTo(c.r).Within(1e-6f), $"канал R изменён для {c}");
                Assert.That(got.g, Is.EqualTo(c.g).Within(1e-6f), $"канал G изменён для {c}");
                Assert.That(got.b, Is.EqualTo(c.b).Within(1e-6f), $"канал B изменён для {c}");
                Assert.That(got.a, Is.EqualTo(c.a).Within(1e-6f), $"альфа изменена для {c}");
            }
        }

        /// <summary>
        /// В UI-коде не должно быть ручных преобразований цветового пространства НИГДЕ. Проверка
        /// текстовая намеренно: она ловит попытку до того, как та доедет до экрана, и не зависит от
        /// того, через какой из контролов её внесли.
        /// </summary>
        [Test]
        public void UiScripts_ContainNoManualColorSpaceConversion()
        {
            var offenders = new List<string>();

            foreach (string file in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (CommentLine.IsMatch(lines[i])) continue;
                    if (!ManualConversion.IsMatch(lines[i])) continue;
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {lines[i].Trim()}");
                }
            }

            Assert.That(offenders, Is.Empty,
                "Ручной перевод цветового пространства в UI-коде. Шейдер UI Toolkit делает это сам — "
                + "вторая конверсия душит цвет примерно втрое, и заметно это только замером пикселей:\n"
                + string.Join("\n", offenders));
        }

        /// <summary>
        /// У контрола, рисующего мешем, нет СВОЕГО цвета: поля-дефолты прозрачны, значение приходит
        /// только из USS. Иначе у токена появляется второй владелец, который однажды разойдётся с
        /// ним молча — так у вуали хардкод 0.92 жил рядом с токеном 0.88, и играл код.
        /// </summary>
        [Test]
        public void MeshControls_DeclareNoColorOfTheirOwn()
        {
            var offenders = new List<string>();
            var fieldWithColor = new Regex(@"private\s+Color\s+(_\w+)\s*=\s*(?!Color\.clear)(\S.*?);");

            foreach (string file in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (CommentLine.IsMatch(lines[i])) continue;
                    Match m = fieldWithColor.Match(lines[i]);
                    if (!m.Success) continue;
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {m.Groups[1].Value} = {m.Groups[2].Value}");
                }
            }

            Assert.That(offenders, Is.Empty,
                "Контрол держит собственный цвет вместо того, чтобы взять его из USS. Дефолт обязан "
                + "быть Color.clear — «цвет не пришёл» должно быть ВИДНО, а не подменяться правдоподобным:\n"
                + string.Join("\n", offenders));
        }

        /// <summary>
        /// Роли кнопки обязаны существовать и ссылаться на примитивы, а не на литералы: подбор цвета
        /// идёт ступенями рампы, и правило это держит уже <see cref="TokenTierTests"/> — здесь
        /// проверяется, что сами роли на месте после всех переименований.
        /// </summary>
        [Test]
        public void ButtonRoles_ResolveToPrimitives()
        {
            string semantic = File.ReadAllText(
                Path.Combine(Application.dataPath, "_Project/UI/Theme/tokens.semantic.uss"));

            string[] required =
            {
                "--gm-color-surface-accent",
                "--gm-color-surface-accent-far",
                "--gm-color-surface-accent-hover",
                "--gm-color-surface-accent-hover-far",
                "--gm-color-border-action",
                "--gm-color-border-action-strong",
            };

            foreach (string role in required)
            {
                var decl = new Regex(Regex.Escape(role) + @"\s*:\s*([^;]+);");
                Match m = decl.Match(semantic);
                Assert.That(m.Success, Is.True, $"роль {role} пропала из семантики");
                Assert.That(m.Groups[1].Value.Contains("var(--gm-"), Is.True,
                    $"роль {role} задана литералом, а не ступенью примитива: {m.Groups[1].Value.Trim()}");
            }
        }
    }
}
