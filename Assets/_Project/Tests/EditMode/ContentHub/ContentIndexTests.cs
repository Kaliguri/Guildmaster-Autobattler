using Guildmaster.ContentHub.Editor;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.ContentHub
{
    /// <summary>
    /// Инварианты индекса на реальных ассетах проекта: непустой билд, симметричный граф ссылок,
    /// согласованные когорты. Не привязаны к конкретному числу ассетов (не хрупкие).
    /// </summary>
    public sealed class ContentIndexTests
    {
        [SetUp]
        public void Rebuild() => ContentIndex.Invalidate();

        [Test]
        public void Entries_BuildWithoutError()
        {
            Assert.IsNotNull(ContentIndex.Entries);
            Assert.Greater(ContentIndex.Entries.Count, 0, "в проекте должен быть контент");
        }

        [Test]
        public void ReferenceGraph_IsSymmetric()
        {
            foreach (var e in ContentIndex.Entries)
            {
                foreach (var used in e.Uses)
                    Assert.Contains(e, used.UsedBy, $"{e.Id} uses {used.Id}, но не в его UsedBy");
                foreach (var user in e.UsedBy)
                    Assert.Contains(e, user.Uses, $"{e.Id} usedBy {user.Id}, но не в его Uses");
            }
        }

        [Test]
        public void Units_HaveEffectiveStats()
        {
            foreach (var e in ContentIndex.Entries)
                if (e.IsUnit)
                    Assert.IsNotNull(e.EffectiveStats, $"{e.Id} — юнит без эффективных статов");
        }

        [Test]
        public void Cohort_AggregatesConsistent()
        {
            var cohort = ContentIndex.Cohort(typeof(RelicData));
            if (cohort == null || cohort.Size == 0)
                Assert.Ignore("нет реликвий в проекте — когорту не проверяем");

            var hp = cohort.For(StatType.MaxHP);
            Assert.AreEqual(cohort.Size, hp.Count);
            Assert.LessOrEqual(hp.Min, hp.Avg);
            Assert.LessOrEqual(hp.Avg, hp.Max);
            Assert.LessOrEqual(hp.Min, hp.Median);
            Assert.LessOrEqual(hp.Median, hp.Max);
        }
    }
}
