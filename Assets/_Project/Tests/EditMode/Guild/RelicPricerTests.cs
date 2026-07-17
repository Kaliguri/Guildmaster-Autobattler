using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Ценообразование реликвий (план act-map-run-loop §3.3, B1): детерминизм по (id, сид витрины), база растёт
    /// с силой кита, разброс в пределах конфига, продажа = доля от покупки.
    /// </summary>
    public sealed class RelicPricerTests
    {
        private RelicPricer _pricer;
        private GameConfig  _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<GameConfig>(); // код-дефолты: 50/100/150, spread .2, sell .25
            _pricer = new RelicPricer(_config);
        }

        [Test]
        public void Price_IsDeterministic_ForSameSeedAndId()
        {
            Assert.AreEqual(
                _pricer.PriceFor(KitPower.Cursed, "relic.x", 7),
                _pricer.PriceFor(KitPower.Cursed, "relic.x", 7));
        }

        [Test]
        public void Price_GrowsWithKitPower()
        {
            // База 50/100/150, разброс ±20% → тиры не пересекаются: Divine всегда дороже Common.
            for (int seed = 0; seed < 25; seed++)
            {
                int common = _pricer.PriceFor(KitPower.Common, "relic.x", seed);
                int divine = _pricer.PriceFor(KitPower.Divine, "relic.x", seed);
                Assert.Greater(divine, common, $"Divine должен быть дороже Common (сид {seed}).");
            }
        }

        [Test]
        public void Price_StaysWithinSpread()
        {
            // Common база 50, spread 0.2 → [40, 60].
            for (int seed = 0; seed < 50; seed++)
            {
                int p = _pricer.PriceFor(KitPower.Common, "relic.y", seed);
                Assert.GreaterOrEqual(p, 40);
                Assert.LessOrEqual(p, 60);
            }
        }

        [Test]
        public void SellValue_IsQuarterOfBuyPrice()
        {
            Assert.AreEqual(25, _pricer.SellValue(100));
            Assert.AreEqual(50, _pricer.SellValue(200));
        }
    }
}
