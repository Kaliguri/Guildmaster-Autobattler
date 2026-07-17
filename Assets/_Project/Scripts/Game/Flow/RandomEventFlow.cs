using Cysharp.Threading.Tasks;
using Guildmaster.Core.Random;
using Guildmaster.Guild;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Узел «?» (план [[act-map-run-loop]] §4 B4): тип роллится НА ВХОДЕ через <see cref="IRngService"/> и
    /// делегируется соответствующему <see cref="IEventFlow"/> (через <see cref="INodeResolver"/>). Раскладка
    /// (решения Макса 2026-07-17): текст 60% / бой 15% / сундук 12% / магазин 8% / элитка 5%.
    /// </summary>
    public sealed class RandomEventFlow : IEventFlow
    {
        private readonly INodeResolver _resolver;

        public RandomEventFlow(INodeResolver resolver) => _resolver = resolver;

        public UniTask<EventResult> Run(RunContext ctx)
        {
            MapNodeType rolled = Roll(ctx.Rng);
            var synthetic = new MapNode { Id = "unknown.rolled", Type = rolled, PayloadId = string.Empty };
            IEventFlow inner = _resolver.Resolve(synthetic, ctx);
            return inner.Run(ctx);
        }

        // Веса из плана: текст 60 / бой 15 / сундук 12 / магазин 8 / элитка 5 (сумма 100).
        internal static MapNodeType Roll(IRngService rng)
        {
            int r = rng.NextInt(0, 100);
            if (r < 60) return MapNodeType.TextEvent; //  0..59  (60%)
            if (r < 75) return MapNodeType.Battle;    // 60..74  (15%)
            if (r < 87) return MapNodeType.Chest;     // 75..86  (12%)
            if (r < 95) return MapNodeType.Shop;      // 87..94  ( 8%)
            return MapNodeType.Elite;                 // 95..99  ( 5%)
        }
    }
}
