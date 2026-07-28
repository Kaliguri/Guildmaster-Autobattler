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

        // ===================== Ramp редкости выпадения =====================

        [Test]
        public void RollChoices_Boss_ShowcaseIsAllUnique()
        {
            var pool = new List<RelicData>
            {
                MakeRelic("relic.u1", DropRarity.Unique),
                MakeRelic("relic.u2", DropRarity.Unique),
                MakeRelic("relic.u3", DropRarity.Unique),
                MakeRelic("relic.c1", DropRarity.Common),
                MakeRelic("relic.c2", DropRarity.Common),
                MakeRelic("relic.t1", DropRarity.Trash),
            };
            var service = new RewardService(new FakeContent(pool), new XorShiftRng(7));

            IReadOnlyList<RelicData> choices = service.RollChoices(RewardTier.Boss, 3);

            Assert.AreEqual(3, choices.Count);
            Assert.IsTrue(choices.All(r => r.DropRarity == DropRarity.Unique),
                "у босса шанс уника 100% → вся витрина уникальная");
        }

        [Test]
        public void RollChoices_Battle_UniqueIsRare_EliteIsTwiceAsLikely()
        {
            // Статистика по многим роллам: наклон витрины должен читаться в частоте, а не в одном броске.
            float battleRate = UniqueRate(RewardTier.Battle);
            float eliteRate  = UniqueRate(RewardTier.Elite);

            Assert.That(battleRate, Is.EqualTo(0.10f).Within(0.04f), "рядовой бой — примерно 10% уников");
            Assert.That(eliteRate,  Is.EqualTo(0.20f).Within(0.05f), "элита — примерно 20% уников");
            Assert.Less(battleRate, eliteRate, "элита щедрее рядового боя");
        }

        [Test]
        public void RollChoices_NoUniquesInPool_StillFillsShowcase()
        {
            // Уников в контенте может не быть вовсе (сейчас так и есть) — витрина не должна пустеть у босса.
            var pool = new List<RelicData>
            {
                MakeRelic("relic.c1", DropRarity.Common),
                MakeRelic("relic.c2", DropRarity.Common),
                MakeRelic("relic.c3", DropRarity.Common),
            };
            var service = new RewardService(new FakeContent(pool), new XorShiftRng(3));

            IReadOnlyList<RelicData> choices = service.RollChoices(RewardTier.Boss, 3);

            Assert.AreEqual(3, choices.Count, "нет уников — добираем из обычных, а не отдаём пустую витрину");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>Доля уников среди всех слотов витрины за много роллов — так виден наклон по тиру.</summary>
        private static float UniqueRate(RewardTier tier, int rolls = 400)
        {
            var pool = new List<RelicData>();
            for (int i = 0; i < 6; i++) pool.Add(MakeRelic($"relic.u{i}", DropRarity.Unique));
            for (int i = 0; i < 6; i++) pool.Add(MakeRelic($"relic.c{i}", DropRarity.Common));

            var service = new RewardService(new FakeContent(pool), new XorShiftRng(11));

            int uniques = 0, slots = 0;
            for (int i = 0; i < rolls; i++)
            {
                foreach (RelicData r in service.RollChoices(tier, 3))
                {
                    slots++;
                    if (r.DropRarity == DropRarity.Unique) uniques++;
                }
            }
            return (float)uniques / slots;
        }

        private static RelicData MakeRelic(string id, DropRarity rarity = DropRarity.Common)
        {
            var relic = ScriptableObject.CreateInstance<RelicData>();
            typeof(ContentDefinition)
                .GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(relic, id);
            typeof(RelicData)
                .GetField("_dropRarity", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(relic, rarity);
            return relic;
        }

        private static List<RelicData> MakePool(params string[] ids)
        {
            var list = new List<RelicData>(ids.Length);
            foreach (string id in ids) list.Add(MakeRelic(id));
            return list;
        }

        private sealed class FakeContent : IContentDatabase
        {
            private readonly List<RelicData> _relics;
            public FakeContent(List<RelicData> relics) => _relics = relics;

            public IReadOnlyList<T> All<T>() where T : ContentDefinition
                => _relics as IReadOnlyList<T> ?? new List<T>();

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
