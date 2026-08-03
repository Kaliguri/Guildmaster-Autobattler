using Guildmaster.Data.Descriptions;
using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Оформление терминов и чисел в тексте (Трек Т): термин приходит обёрнутым тем, что дал стиль,
    /// а число выделяется только там, где строку прочитает rich text.
    /// </summary>
    public sealed class KeywordStyleTests
    {
        private static string Form(string id, string caseTag) => id == "kw.burn" ? "Горение" : null;

        /// <summary>Стиль-заглушка: проверяем ПРОВОДКУ, а не конкретный цвет (цвет живёт в USS).</summary>
        private sealed class FakeStyle : IKeywordStyle
        {
            public string Open(string keywordId) => "<b><color=#ABCDEF>";
            public string Close(string keywordId) => "</color></b>";
        }

        [Test]
        public void Render_WrapsTerm_WithStyleFromOutside()
        {
            string result = KeywordMarkup.Render("Накладывает [kw:burn].", Form, new FakeStyle());

            Assert.AreEqual("Накладывает <link=kw.burn><b><color=#ABCDEF>[Горение]</color></b></link>.", result);
        }

        [Test]
        public void Render_WithoutStyle_StaysPlain()
        {
            // Тесты и места без rich text обязаны получать чистый текст — стиль опционален по замыслу.
            string result = KeywordMarkup.Render("Накладывает [kw:burn].", Form);

            Assert.AreEqual("Накладывает <link=kw.burn>[Горение]</link>.", result);
        }

        [Test]
        public void StatValue_IsBold_OnlyInRichText()
        {
            var value = new StatValue(StatType.AutoAttackDamage, 30f, 42f,
                System.Array.Empty<StatTerm>(), ValueKind.Flat);

            string plain = StatFormat.Describe(new FormattedStat(value, null, false, UnitLabels.Ru));
            string rich  = StatFormat.Describe(new FormattedStat(value, null, false, UnitLabels.Ru, rich: true));

            Assert.AreEqual("42", plain, "в поле без rich text теги вылезли бы в текст");
            Assert.AreEqual("<b>42</b>", rich);
        }

        [Test]
        public void DetailedBreakdown_EmphasizesOnlyTheTotal()
        {
            var terms = new[] { new StatTerm("relic.ember.name", ModifierOp.Flat, 12f, 12f) };
            var value = new StatValue(StatType.AutoAttackDamage, 30f, 42f, terms, ValueKind.Flat);

            string rich = StatFormat.Describe(
                new FormattedStat(value, new[] { "Пылающий клинок" }, true, UnitLabels.Ru, rich: true));

            StringAssert.EndsWith("= <b>42</b>", rich, "выделяется итог, ради которого фразу и читают");
            StringAssert.DoesNotContain("<b>30</b>", rich, "база и слагаемые остаются обычными");
        }
    }
}
