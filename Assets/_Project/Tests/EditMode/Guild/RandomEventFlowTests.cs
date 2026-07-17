using Cysharp.Threading.Tasks;
using Guildmaster.Core.Random;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Узел «?» <see cref="RandomEventFlow"/> (план act-map-run-loop §5.4, B4): ролл типа на входе по весам
    /// текст 60 / бой 15 / сундук 12 / магазин 8 / элитка 5 и делегирование резолверу.
    /// </summary>
    public sealed class RandomEventFlowTests
    {
        [TestCase(0,   MapNodeType.TextEvent)]
        [TestCase(59,  MapNodeType.TextEvent)]
        [TestCase(60,  MapNodeType.Battle)]
        [TestCase(74,  MapNodeType.Battle)]
        [TestCase(75,  MapNodeType.Chest)]
        [TestCase(86,  MapNodeType.Chest)]
        [TestCase(87,  MapNodeType.Shop)]
        [TestCase(94,  MapNodeType.Shop)]
        [TestCase(95,  MapNodeType.Elite)]
        [TestCase(99,  MapNodeType.Elite)]
        public void Roll_MapsToExpectedType(int rngValue, MapNodeType expected)
        {
            var resolver = new CapturingResolver();
            var ctx = new RunContext(new RunState(), new FixedRng(rngValue),
                                     new SoloReadyGate(), new SoloPlayerIntentSource());

            new RandomEventFlow(resolver).Run(ctx).GetAwaiter().GetResult();

            Assert.AreEqual(expected, resolver.LastType);
        }

        // Никогда не роллит сам себя — иначе бесконечная рекурсия.
        [TestCase(0)]
        [TestCase(60)]
        [TestCase(95)]
        [TestCase(99)]
        public void Roll_NeverUnknown(int rngValue)
        {
            var resolver = new CapturingResolver();
            var ctx = new RunContext(new RunState(), new FixedRng(rngValue),
                                     new SoloReadyGate(), new SoloPlayerIntentSource());
            new RandomEventFlow(resolver).Run(ctx).GetAwaiter().GetResult();
            Assert.AreNotEqual(MapNodeType.Unknown, resolver.LastType);
        }

        private sealed class CapturingResolver : INodeResolver
        {
            public MapNodeType LastType { get; private set; }
            public IEventFlow Resolve(MapNode node, RunContext ctx)
            {
                LastType = node.Type;
                return new DoneFlow();
            }
        }

        private sealed class DoneFlow : IEventFlow
        {
            public UniTask<EventResult> Run(RunContext ctx) => UniTask.FromResult(EventResult.Completed);
        }

        // Детерминированный RNG: NextInt всегда возвращает заданное значение (для роллов узла хватает).
        private sealed class FixedRng : IRngService
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public uint NextUInt() => (uint)_value;
            public int NextInt(int minInclusive, int maxExclusive) => _value;
            public float NextFloat() => 0f;
            public float NextFloat(float minInclusive, float maxExclusive) => minInclusive;
            public bool Chance(float probability) => false;
            public ulong Snapshot() => 0UL;
        }
    }
}
