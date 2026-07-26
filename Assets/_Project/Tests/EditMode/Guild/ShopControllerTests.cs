using System;
using System.Collections.Generic;
using System.Reflection;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Логика магазина <see cref="ShopController"/> (план act-map-run-loop §4 B2): покупка списывает золото и
    /// добавляет реликвию, полный запас → NoSpace, реролл списывает золото, продажа возвращает золото. Денежный
    /// поток идёт через реальные <see cref="RunStateService"/> + <see cref="RelicPricer"/>.
    /// </summary>
    public sealed class ShopControllerTests
    {
        private ShopController  _shop;
        private RunStateService _runStates;
        private GameConfig      _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<GameConfig>();
            var save = new MemSave();
            _runStates = new RunStateService(save, _config);
            _runStates.NewRun(1L, Array.Empty<RosterSlot>());

            var relics = new List<RelicData>
            {
                MakeRelic("relic.a", KitPower.Common),
                MakeRelic("relic.b", KitPower.Common),
                MakeRelic("relic.c", KitPower.Cursed),
                MakeRelic("relic.d", KitPower.Divine),
                MakeRelic("relic.e", KitPower.Common),
            };
            var content = new FakeContent(relics);
            var rng     = new XorShiftRng(5UL);
            var rewards = new RewardService(content, rng);
            var pricer  = new RelicPricer(_config);

            _shop = new ShopController(rewards, pricer, _runStates, content, rng, _config);
            _shop.Open();
        }

        [Test]
        public void Buy_SpendsGold_AddsRelic_MarksSold()
        {
            _runStates.Current.Gold = 1000;
            ShopItem item = FirstUnsold();
            int goldBefore = _runStates.Gold;

            Assert.AreEqual(ShopBuyOutcome.Bought, _shop.Buy(IndexOf(item)));
            Assert.AreEqual(goldBefore - item.Price, _runStates.Gold);
            Assert.Contains(item.Relic.Id, _runStates.Current.RelicInventory);
            Assert.IsTrue(item.Sold);
        }

        [Test]
        public void Buy_NoSpace_WhenInventoryFull()
        {
            _runStates.Current.Gold = 1000;
            _runStates.Current.RelicCapacity = 0; // запас «полон» сразу
            Assert.AreEqual(ShopBuyOutcome.NoSpace, _shop.Buy(IndexOf(FirstUnsold())));
        }

        [Test]
        public void Buy_NotEnoughGold()
        {
            _runStates.Current.Gold = 0;
            Assert.AreEqual(ShopBuyOutcome.NotEnoughGold, _shop.Buy(IndexOf(FirstUnsold())));
        }

        [Test]
        public void Reroll_SpendsRerollCost()
        {
            _runStates.Current.Gold = 1000;
            int before = _runStates.Gold;
            Assert.IsTrue(_shop.Reroll());
            Assert.AreEqual(before - _config.ShopRerollCost, _runStates.Gold);
        }

        [Test]
        public void Reroll_Fails_WithoutGold()
        {
            _runStates.Current.Gold = 0;
            Assert.IsFalse(_shop.Reroll());
        }

        [Test]
        public void Sell_AddsGold_RemovesRelic()
        {
            _runStates.TryAddRelic("relic.c");
            int before = _runStates.Gold;

            var relic = _shop.Stash.Count > 0 ? _shop.Stash[0].Relic : null;
            Assert.NotNull(relic, "В запасе есть реликвия для продажи.");
            int expectedGain = _shop.Stash[0].SellValue;

            Assert.IsTrue(_shop.Sell(relic));
            Assert.AreEqual(before + expectedGain, _runStates.Gold);
            Assert.AreEqual(-1, Array.IndexOf(_runStates.Current.RelicInventory, relic.Id));
        }

        // ── helpers ──────────────────────────────────────────────

        private ShopItem FirstUnsold()
        {
            foreach (var it in _shop.Shelf) if (!it.Sold && it.Relic != null) return it;
            Assert.Fail("Витрина пуста."); return null;
        }

        private int IndexOf(ShopItem item)
        {
            for (int i = 0; i < _shop.Shelf.Count; i++) if (ReferenceEquals(_shop.Shelf[i], item)) return i;
            return -1;
        }

        private static RelicData MakeRelic(string id, KitPower power)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            typeof(ContentDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(r, id);
            typeof(RelicData).GetField("_kitPower", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(r, power);
            return r;
        }

        private sealed class FakeContent : IContentDatabase
        {
            private readonly List<RelicData> _relics;
            private readonly Dictionary<string, RelicData> _byId = new();
            public FakeContent(List<RelicData> relics)
            {
                _relics = relics;
                foreach (var r in relics) _byId[r.Id] = r;
            }
            public bool TryGet<T>(string id, out T def) where T : ContentDefinition
            {
                if (_byId.TryGetValue(id, out var r) && r is T t) { def = t; return true; }
                def = null; return false;
            }
            public IReadOnlyList<T> All<T>() where T : ContentDefinition =>
                typeof(T) == typeof(RelicData) ? (IReadOnlyList<T>)_relics : Array.Empty<T>();
        }

        private sealed class MemSave : ISaveService
        {
            private readonly Dictionary<string, object> _s = new();
            public void Save<T>(string key, T value) => _s[key] = value;
            public T Load<T>(string key) => _s.TryGetValue(key, out var v) ? (T)v : default;
            public bool Exists(string key) => _s.ContainsKey(key);
            public void Delete(string key) => _s.Remove(key);
        }
    }
}
