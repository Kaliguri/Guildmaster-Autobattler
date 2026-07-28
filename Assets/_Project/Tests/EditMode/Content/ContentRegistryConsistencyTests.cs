using System;
using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Editor;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// «Какие типы контента существуют» — один ответ на всю игру.
    /// <para>До аудита 2026-07-26 (T-24) их было два: <see cref="ContentDomains"/> знал 17 типов, а
    /// редакторный <see cref="ContentPaths"/> держал свой список на 13. Четыре типа — Species, Encounter,
    /// BattlePreset и TextEvent — из-за этого создавались в <c>Misc</c> вместо своих папок, а меню создания
    /// в Content Hub предлагало ровно те типы, что помечены к удалению, и прятало живые.</para>
    /// </summary>
    public sealed class ContentRegistryConsistencyTests
    {
        [Test]
        public void EveryRegisteredContentType_HasItsOwnFolder()
        {
            var homeless = new List<string>();

            foreach (Type type in ContentDomains.RegisteredTypes)
            {
                string folder = ContentPaths.FolderFor(type);
                if (folder.EndsWith("/Misc"))
                    homeless.Add($"{type.Name} (домен {ContentDomains.GetDomain(type)})");
            }

            Assert.IsEmpty(homeless,
                "Эти типы создадутся в Misc, потому что для их домена не задана папка в ContentPaths: " +
                string.Join(", ", homeless));
        }

        [Test]
        public void CreateMenu_OffersEveryRegisteredType()
        {
            var offered = ContentPaths.CreatableTypes.ToList();

            CollectionAssert.AreEquivalent(ContentDomains.RegisteredTypes.ToList(), offered,
                "Меню создания расходится с реестром доменов — значит список типов снова живёт в двух местах");
        }

        [Test]
        public void EveryRegisteredType_HasADomain()
        {
            foreach (Type type in ContentDomains.RegisteredTypes)
                Assert.DoesNotThrow(() => ContentDomains.GetDomain(type), $"{type.Name} без домена");
        }
    }
}
