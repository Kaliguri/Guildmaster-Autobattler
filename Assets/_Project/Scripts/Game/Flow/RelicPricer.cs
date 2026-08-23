using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Ценообразование мементо (план [[act-map-run-loop]] §3.3): база по <see cref="KitPower"/>
    /// (Common/Cursed/Divine из <see cref="GameConfig"/>) + детерминированный разброс вокруг неё. Разброс
    /// стабилен для пары (id мементо, seed витрины) — одна витрина всегда даёт ту же цену, но между витринами
    /// цена гуляет. У <see cref="RelicData"/> нет поля Cost — цена вычисляется здесь, ассеты не размечаем.
    /// </summary>
    public sealed class RelicPricer
    {
        private readonly GameConfig _config;

        public RelicPricer(GameConfig config) => _config = config;

        /// <summary>Цена покупки мементо в витрине с сидом <paramref name="shopSeed"/>.</summary>
        public int Price(RelicData relic, int shopSeed) =>
            relic == null ? 0 : PriceFor(relic.KitPower, relic.Id, shopSeed);

        /// <summary>Цена по силе кита и id (тестируемо без конструирования RelicData).</summary>
        public int PriceFor(KitPower power, string id, int shopSeed)
        {
            int basePrice = power switch
            {
                KitPower.Divine => _config.PriceDivine,
                KitPower.Cursed => _config.PriceCursed,
                _               => _config.PriceCommon,
            };

            var rng = new XorShiftRng(DeterministicHash.Of(id) ^ unchecked((ulong)(uint)shopSeed));
            float t = rng.NextFloat(-1f, 1f);                       // -1..1
            int price = Mathf.RoundToInt(basePrice * (1f + t * _config.PriceSpread));
            return Mathf.Max(1, price);
        }

        /// <summary>Сколько игрок получит за продажу мементо с ценой покупки <paramref name="buyPrice"/>.</summary>
        public int SellValue(int buyPrice) => Mathf.Max(1, Mathf.RoundToInt(buyPrice * _config.SellPercent));

    }
}
