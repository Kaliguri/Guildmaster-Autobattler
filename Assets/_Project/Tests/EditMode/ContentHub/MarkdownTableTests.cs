using System.Collections.Generic;
using Guildmaster.ContentHub.Editor;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.ContentHub
{
    public sealed class MarkdownTableTests
    {
        [Test]
        public void Format_ProducesHeaderSeparatorAndRows()
        {
            var md = MarkdownTable.Format(
                new[] { "Юнит", "HP" },
                new List<IReadOnlyList<string>>
                {
                    new[] { "Огненный", "640" },
                    new[] { "Криомант", "520" },
                });

            var expected =
                "| Юнит | HP |\n" +
                "|---|---|\n" +
                "| Огненный | 640 |\n" +
                "| Криомант | 520 |\n";
            Assert.AreEqual(expected, md);
        }
    }
}
