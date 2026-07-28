using Guildmaster.Data.Descriptions;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Разметка ключевых слов (Трек Т, план §II.10.3/§II.10.5 п.4): развёртка в rich text, падежи,
    /// короткая запись id и поведение при незаведённой строке.
    /// </summary>
    public sealed class KeywordMarkupTests
    {
        // Форма-заглушка: именительный «Горение», родительный «Горения»; прочие термины не заведены.
        private static string Form(string id, string caseTag)
        {
            if (id != "kw.burn") return null;
            return caseTag == "gen" ? "Горения" : "Горение";
        }

        [Test]
        public void Render_WrapsKeyword_InLinkAndBrackets()
        {
            string result = KeywordMarkup.Render("Накладывает [kw:burn].", Form);

            Assert.AreEqual("Накладывает <link=kw.burn>[Горение]</link>.", result);
        }

        [Test]
        public void Render_UsesRequestedCase()
        {
            string result = KeywordMarkup.Render("Снимает стак [kw:burn:gen].", Form);

            StringAssert.Contains("Горения", result);
            StringAssert.DoesNotContain("[Горение]", result, "родительный падеж не должен подменяться именительным");
        }

        [Test]
        public void Render_FallsBackToId_WhenStringIsMissing()
        {
            // Дырка в локализации должна быть ВИДНА: пустое место в описании не заметит никто,
            // а «kw.poison» в тексте сразу называет незаведённый ключ.
            string result = KeywordMarkup.Render("Накладывает [kw:poison].", Form);

            StringAssert.Contains("kw.poison", result);
        }

        [Test]
        public void Render_KeepsText_WhenThereIsNoMarkup()
        {
            const string text = "Просто описание без терминов.";

            Assert.AreSame(text, KeywordMarkup.Render(text, Form), "текст без разметки не должен пересобираться");
        }

        [Test]
        public void Strip_LeavesBracketedTerms()
        {
            string result = KeywordMarkup.Strip("Накладывает [kw:burn:gen] и [kw:burn].", Form);

            Assert.AreEqual("Накладывает [Горения] и [Горение].", result);
        }

        [Test]
        public void Mentioned_ReturnsFullIds()
        {
            string[] ids = KeywordMarkup.Mentioned("[kw:burn] и [kw:kw.poison:acc]");

            CollectionAssert.AreEqual(new[] { "kw.burn", "kw.poison" }, ids);
        }

        [Test]
        public void FullId_AddsDomain_OnlyToShortForm()
        {
            Assert.AreEqual("kw.burn", KeywordMarkup.FullId("burn"));
            Assert.AreEqual("kw.burn", KeywordMarkup.FullId("kw.burn"));
        }

        [Test]
        public void Mark_BuildsMarkupBothWays()
        {
            Assert.AreEqual("[kw:burn]", KeywordMarkup.Mark("burn"));
            Assert.AreEqual("[kw:burn:gen]", KeywordMarkup.Mark("burn", "gen"));
        }
    }
}
