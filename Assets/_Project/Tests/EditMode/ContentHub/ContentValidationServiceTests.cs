using Guildmaster.ContentHub.Editor;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

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

        /// <summary>
        /// Сторож наличия тела сам под сторожем: правило зовёт <c>UnitVisualPresenceTests</c> по всему
        /// ростеру, и если оно молча перестанет находить пустоту, тот прогон останется зелёным — то есть
        /// поломка спрячется ровно в гейте, который её и должен ловить.
        /// </summary>
        [Test]
        public void UnitWithoutBody_IsTwoIssues()
        {
            var unit = ScriptableObject.CreateInstance<RelicData>();
            try
            {
                var issues = ContentValidationService.ValidateUnitVisual(unit);
                Assert.AreEqual(2, issues.Count,
                    "Пустой юнит обязан дать две находки — нет AnimationArchetypeData и нет ViewPrefab.");
            }
            finally
            {
                Object.DestroyImmediate(unit);
            }
        }
    }
}
