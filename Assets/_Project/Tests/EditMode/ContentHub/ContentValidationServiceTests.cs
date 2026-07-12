using Guildmaster.ContentHub.Editor;
using Guildmaster.Data.Definitions;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.ContentHub
{
    /// <summary>Правила id-валидации (чистые, без индекса).</summary>
    public sealed class ContentValidationServiceTests
    {
        private static readonly string RelicDomain = ContentDomains.GetDomain(typeof(RelicData));

        [Test]
        public void EmptyId_IsIssue()
        {
            Assert.IsNotEmpty(ContentValidationService.ValidateIdString("", typeof(RelicData)));
            Assert.IsNotEmpty(ContentValidationService.ValidateIdString(null, typeof(RelicData)));
        }

        [Test]
        public void BadFormat_IsIssue()
        {
            Assert.IsNotEmpty(ContentValidationService.ValidateIdString("noDotHere", typeof(RelicData)));
            Assert.IsNotEmpty(ContentValidationService.ValidateIdString("relic.Bad Caps", typeof(RelicData)));
        }

        [Test]
        public void WrongDomain_IsIssue()
        {
            Assert.IsNotEmpty(ContentValidationService.ValidateIdString("enemy.something", typeof(RelicData)));
        }

        [Test]
        public void ValidId_NoIssues()
        {
            Assert.IsEmpty(ContentValidationService.ValidateIdString($"{RelicDomain}.fire_swordsman", typeof(RelicData)));
        }
    }
}
