using Guildmaster.Data.Definitions;
using Guildmaster.Game.Services;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// A1: сериализация <see cref="RunState"/> (JSON round-trip + диск через <see cref="JsonFileSaveService"/>)
    /// и правила вместимости коллекции реликов (<see cref="RunStateService"/>, план 11 §5.4).
    /// </summary>
    public sealed class RunStateSaveTests
    {
        [Test]
        public void RunState_JsonRoundTrip_PreservesFields()
        {
            var rs = new RunState
            {
                Seed = 12345, Gold = 250, RelicCapacity = 10, CurrentActIndex = 2, Difficulty = 1,
                RelicInventory = new[] { "relic.assassin", "relic.ranger" },
                PartyItemIds   = new[] { "item.banner_valor" },
                Guild = new[]
                {
                    new RosterSlot { RelicId = "relic.iron_spearman", AiPresetId = "ai_preset.spearman", SavedPosition = new Vector2(-5f, 1f) },
                },
                Map = new MapState
                {
                    CurrentNodeId = "n0",
                    Nodes = new[]
                    {
                        new MapNode { Id = "n0", Type = MapNodeType.Battle, PayloadId = "battle_preset.spearman", Edges = new[] { "n1" } },
                    },
                },
            };

            // Гоняем через НАСТОЯЩИЙ бэкенд, а не через JsonUtility: сериализатор в игре — Newtonsoft,
            // и проверять надо тот путь, которым забег реально ложится на диск.
            var svc = new JsonFileSaveService();
            const string key = "__test_runstate_roundtrip";
            RunState back;
            try
            {
                svc.Save(key, rs);
                back = svc.TryLoad<RunState>(key).Value;
            }
            finally
            {
                svc.Delete(key);
            }

            Assert.IsNotNull(back);
            Assert.AreEqual(rs.Seed, back.Seed);
            Assert.AreEqual(rs.Gold, back.Gold);
            Assert.AreEqual(rs.RelicCapacity, back.RelicCapacity);
            Assert.AreEqual(2, back.RelicInventory.Length);
            Assert.AreEqual("relic.ranger", back.RelicInventory[1]);
            Assert.AreEqual("item.banner_valor", back.PartyItemIds[0]);
            Assert.AreEqual("relic.iron_spearman", back.Guild[0].RelicId);
            Assert.AreEqual(-5f, back.Guild[0].SavedPosition.x, 1e-4f);
            Assert.AreEqual(1f, back.Guild[0].SavedPosition.y, 1e-4f);
            Assert.AreEqual("battle_preset.spearman", back.Map.Nodes[0].PayloadId);
            Assert.AreEqual(MapNodeType.Battle, back.Map.Nodes[0].Type);
            Assert.AreEqual("n1", back.Map.Nodes[0].Edges[0]);
        }

        [Test]
        public void JsonFileSaveService_DiskRoundTrip()
        {
            var svc = new JsonFileSaveService();
            const string key = "test_run_a1_roundtrip";
            try
            {
                svc.Delete(key);
                Assert.IsFalse(svc.Exists(key));

                svc.Save(key, new RunState { Gold = 99, Seed = 7 });
                Assert.IsTrue(svc.Exists(key));

                var loaded = svc.TryLoad<RunState>(key);
                Assert.IsTrue(loaded.IsOk);
                Assert.AreEqual(99, loaded.Value.Gold);
                Assert.AreEqual(7, loaded.Value.Seed);
            }
            finally
            {
                svc.Delete(key);
                Assert.IsFalse(svc.Exists(key));
            }
        }

        [Test]
        public void RelicCapacity_EnforcedAndUpgradable()
        {
            var config = GameConfig.CreateDefault(); // заготовка: вместимость 12, потолок 16
            try
            {
                var svc = new RunStateService(new JsonFileSaveService(), config);
                RunState run = svc.NewRun(1L, new[] { new RosterSlot() });

                Assert.AreEqual(config.RelicCapacityBase, run.RelicCapacity);

                for (int i = 0; i < run.RelicCapacity; i++)
                    Assert.IsTrue(svc.TryAddRelic("relic.x" + i), "должно влезать до кап");
                Assert.IsTrue(svc.RelicInventoryFull);
                Assert.IsFalse(svc.TryAddRelic("relic.overflow"), "полно → отказ");

                svc.RemoveRelic("relic.x0");
                Assert.IsFalse(svc.RelicInventoryFull);
                Assert.IsTrue(svc.TryAddRelic("relic.again"));

                while (run.RelicCapacity < config.RelicCapacityMax)
                    Assert.IsTrue(svc.IncreaseCapacity());
                Assert.IsFalse(svc.IncreaseCapacity(), "потолок → отказ");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void VesselItems_RespectPerVesselLimit()
        {
            var config = GameConfig.CreateDefault(); // заготовка: VesselItemSlots = 3
            try
            {
                var svc = new RunStateService(new JsonFileSaveService(), config);
                svc.NewRun(1L, new[] { new RosterSlot(), new RosterSlot() });

                for (int i = 0; i < config.VesselItemSlots; i++)
                    Assert.IsTrue(svc.TryAddVesselItem(0, "item.x" + i), "влезает до лимита слота");

                Assert.AreEqual(config.VesselItemSlots, svc.VesselItems(0).Count);
                Assert.IsFalse(svc.TryAddVesselItem(0, "item.overflow"), "слот сосуда полон → отказ");

                // Лимит — на КАЖДЫЙ сосуд отдельно, не общий на отряд.
                Assert.IsTrue(svc.TryAddVesselItem(1, "item.other"), "у второго сосуда свои слоты");

                svc.RemoveVesselItem(0, "item.x0");
                Assert.AreEqual(config.VesselItemSlots - 1, svc.VesselItems(0).Count);
                Assert.IsTrue(svc.TryAddVesselItem(0, "item.again"), "освободился слот → снова влезает");

                Assert.IsFalse(svc.TryAddVesselItem(5, "item.nope"), "индекс вне ростера → отказ");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void PartyBanners_RespectPartyLimit()
        {
            var config = GameConfig.CreateDefault(); // заготовка: PartyBannerSlots = 2
            try
            {
                var svc = new RunStateService(new JsonFileSaveService(), config);
                svc.NewRun(1L, new[] { new RosterSlot() });

                for (int i = 0; i < config.PartyBannerSlots; i++)
                    Assert.IsTrue(svc.TryAddBanner("item.banner" + i), "влезает до лимита отряда");

                Assert.AreEqual(config.PartyBannerSlots, svc.Banners.Count);
                Assert.IsFalse(svc.TryAddBanner("item.banner_overflow"), "лимит баннеров → отказ");

                svc.RemoveBanner("item.banner0");
                Assert.IsTrue(svc.TryAddBanner("item.banner_new"), "освободился слот баннера");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
