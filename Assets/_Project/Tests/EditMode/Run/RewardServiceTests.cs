using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Генерация витрины наград (план 11 §4 A3): количество, уникальность, фильтр базового релика,
    /// детерминизм по сиду. Взятие/сброс живёт в RunStateService (тестируется в RunStateSaveTests).
    /// </summary>
    public sealed class RewardServiceTests
    {
        [Test]
        public void RollChoices_ReturnsRequestedCount_AllDistinct_FromPool()
        {
            var pool    = MakePool("relic.a", "relic.b", "relic.c", "relic.d", "relic.e");
            var service = new RewardService(new FakeContent(pool), new XorShiftRng(1));

            IReadOnlyList<RelicData> choices = service.RollChoices(RewardTier.Battle, 3);

            Assert.AreEqual(3, choices.Count);
            Assert.AreEqual(3, choices.Distinct().Count(), "витрина не должна содержать дубли");
            Assert.IsTrue(choices.All(pool.Contains), "все варианты — из пула");
        }

        [Test]
        public void RollChoices_ExcludesBaseRelic()
        {
            var pool    = MakePool("relic.base", "relic.a", "relic.b");
            var service = new RewardService(new FakeContent(pool), new XorShiftRng(1));

            IReadOnlyList<RelicData> choices = service.RollChoices(RewardTier.Battle, 3);

            Assert.IsFalse(choices.Any(r => r.Id == "relic.base"), "базовый релик не выпадает в награде");
            Assert.AreEqual(2, choices.Count, "из пула из base+2 остаётся 2 кандидата");
        }

        [Test]
        public void RollChoices_PoolSmallerThanCount_ReturnsWholePool()
        {
            var pool    = MakePool("relic.a", "relic.b");
            var service = new RewardService(new FakeContent(pool), new XorShiftRng(1));

            IReadOnlyList<RelicData> choices = service.RollChoices(RewardTier.Battle, 3);

            Assert.AreEqual(2, choices.Count);
        }

        [Test]
        public void RollChoices_EmptyPool_ReturnsEmpty()
        {
            var service = new RewardService(new FakeContent(new List<RelicData>()), new XorShiftRng(1));

            IReadOnlyList<RelicData> choices = service.RollChoices(RewardTier.Battle, 3);

            Assert.AreEqual(0, choices.Count);
        }

        [Test]
        public void RollChoices_SameSeed_SameShowcase()
        {
            var pool = MakePool("relic.a", "relic.b", "relic.c", "relic.d", "relic.e");
            var a = new RewardService(new FakeContent(pool), new XorShiftRng(42)).RollChoices(RewardTier.Battle, 3);
            var b = new RewardService(new FakeContent(pool), new XorShiftRng(42)).RollChoices(RewardTier.Battle, 3);

            CollectionAssert.AreEqual(a.Select(r => r.Id).ToList(), b.Select(r => r.Id).ToList(),
                "тот же сид → та же витрина (детерминизм для реплея/коопа)");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static List<RelicData> MakePool(params string[] ids)
        {
            var list = new List<RelicData>(ids.Length);
            foreach (string id in ids)
            {
                var relic = ScriptableObject.CreateInstance<RelicData>();
                typeof(ContentDefinition)
                    .GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(relic, id);
                list.Add(relic);
            }
            return list;
        }

        private sealed class FakeContent : IContentDatabase
        {
            private readonly List<RelicData> _relics;
            public FakeContent(List<RelicData> relics) => _relics = relics;

            public IReadOnlyList<T> All<T>() where T : ContentDefinition
                => _relics as IReadOnlyList<T> ?? new List<T>();

            public T Get<T>(string id) where T : ContentDefinition
                => TryGet(id, out T def) ? def : null;

            public bool TryGet<T>(string id, out T def) where T : ContentDefinition
            {
                foreach (RelicData r in _relics)
                    if (r.Id == id && r is T typed) { def = typed; return true; }
                def = null;
                return false;
            }
        }
    }
}
