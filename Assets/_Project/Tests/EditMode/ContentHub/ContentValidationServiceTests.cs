using Guildmaster.ContentHub.Editor;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;
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
        /// <para>
        /// Находка ОДНА, а не две: с 06.08.2026 вид приходит из архетипа, своего поля под префаб у юнита
        /// нет. Пустой юнит не может «не иметь префаба» отдельно от «не иметь архетипа» — вторая жалоба
        /// была бы пересказом первой и отправила бы автора искать поле, которого не существует.
        /// </para>
        /// </summary>
        [Test]
        public void UnitWithoutArchetype_IsOneIssue()
        {
            var unit = ScriptableObject.CreateInstance<RelicData>();
            try
            {
                var issues = ContentValidationService.ValidateUnitVisual(unit);
                Assert.AreEqual(1, issues.Count,
                    "Пустой юнит обязан дать РОВНО одну находку — нет AnimationArchetypeData. " +
                    "Вид берётся из него же, поэтому отдельной жалобы на ViewPrefab быть не должно: " +
                    "фактически найдено — " + string.Join(" | ", issues));
            }
            finally
            {
                Object.DestroyImmediate(unit);
            }
        }

        /// <summary>
        /// А вот архетип БЕЗ префаба — настоящая вторая находка, и жаловаться надо на архетип по имени:
        /// поле пустует у него, а не у юнита, и без имени автор пойдёт искать его не там.
        /// </summary>
        [Test]
        public void ArchetypeWithoutViewPrefab_ComplainsAboutTheArchetype()
        {
            var unit = ScriptableObject.CreateInstance<RelicData>();
            var archetype = ScriptableObject.CreateInstance<AnimationArchetypeData>();
            archetype.name = "ArchetypeUnderTest";
            try
            {
                var so = new SerializedObject(unit);
                so.FindProperty("_archetype").objectReferenceValue = archetype;
                so.ApplyModifiedPropertiesWithoutUndo();

                var issues = ContentValidationService.ValidateUnitVisual(unit);
                Assert.AreEqual(1, issues.Count, "Ожидалась ровно одна находка — про пустой ViewPrefab.");
                Assert.That(issues[0], Does.Contain("ArchetypeUnderTest"),
                    "Жалоба обязана назвать АРХЕТИП: поле пустует у него.");
            }
            finally
            {
                Object.DestroyImmediate(archetype);
                Object.DestroyImmediate(unit);
            }
        }
    }
}
