using Guildmaster.UI;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Разбор content id в заголовок карточки. Раньше жил в двух копиях с разной семантикой — игра резала по
    /// последней точке, витрина превью по первой — и совпадали они лишь потому, что все id сегодня содержат
    /// ровно одну точку (аудит 2026-07-26, R1-78).
    /// </summary>
    public sealed class ContentTitleTests
    {
        [TestCase("relic.flame_swordsman", "The Flame Swordsman")]
        [TestCase("relic.base",            "The Base")]
        [TestCase("enemy.bonewright",      "The Bonewright")]
        public void Arcana_DropsTheDomainAndTitleCasesTheRest(string id, string expected)
            => Assert.AreEqual(expected, ContentTitle.Arcana(id));

        /// <summary>Расхождение двух копий было видно только здесь: домен один, режем по ПЕРВОЙ точке.</summary>
        [Test]
        public void Arcana_OnAnIdWithTwoDots_KeepsEverythingAfterTheDomain()
        {
            Assert.AreEqual("The Ice.chains", ContentTitle.Arcana("effect.ice.chains"));
            Assert.AreEqual("ice.chains",     ContentTitle.WithoutDomain("effect.ice.chains"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Arcana_WithoutAnId_IsADashRatherThanAStrayThe(string id)
            => Assert.AreEqual(ContentTitle.Missing, ContentTitle.Arcana(id));

        [Test]
        public void Arcana_OnAnIdWithoutADomain_StillReadsAsATitle()
            => Assert.AreEqual("The Loose Name", ContentTitle.Arcana("loose_name"));

        [Test]
        public void TitleCase_TreatsUnderscoresAsWordBreaks()
            => Assert.AreEqual("Flame Swordsman", ContentTitle.TitleCase("flame_swordsman"));
    }
}
