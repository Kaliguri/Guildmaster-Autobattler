namespace Guildmaster.Core.Random
{
    /// <summary>
    /// Детерминированный PRNG (xorshift128). Одинаковый сид → одинаковая
    /// последовательность на любой платформе (только целочисленные операции).
    /// </summary>
    /// <remarks>
    /// Начальное состояние выводится из одного <see cref="ulong"/>-сида через SplitMix64,
    /// чтобы даже «слабые» сиды (0, 1, …) давали хорошо размешанное состояние.
    /// </remarks>
    public sealed class XorShiftRng : IRngService
    {
        private uint _x, _y, _z, _w;

        public XorShiftRng(ulong seed)
        {
            _x = NextSplitMix(ref seed);
            _y = NextSplitMix(ref seed);
            _z = NextSplitMix(ref seed);
            _w = NextSplitMix(ref seed);

            // Состояние xorshift не должно быть полностью нулевым.
            if ((_x | _y | _z | _w) == 0u)
            {
                _w = 1u;
            }
        }

        /// <inheritdoc/>
        public uint NextUInt()
        {
            uint t = _x ^ (_x << 11);
            _x = _y;
            _y = _z;
            _z = _w;
            _w = _w ^ (_w >> 19) ^ (t ^ (t >> 8));
            return _w;
        }

        /// <inheritdoc/>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        /// <inheritdoc/>
        public float NextFloat()
        {
            // Верхние 24 бита → мантисса float в [0, 1).
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <inheritdoc/>
        public float NextFloat(float minInclusive, float maxExclusive)
        {
            return minInclusive + ((maxExclusive - minInclusive) * NextFloat());
        }

        /// <inheritdoc/>
        public bool Chance(float probability)
        {
            if (probability <= 0f)
            {
                return false;
            }

            if (probability >= 1f)
            {
                return true;
            }

            return NextFloat() < probability;
        }

        /// <inheritdoc/>
        public ulong Snapshot()
        {
            // FNV-1a поверх четырёх слов состояния — отпечаток для checksum.
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong h = offset;
            h = (h ^ _x) * prime;
            h = (h ^ _y) * prime;
            h = (h ^ _z) * prime;
            h = (h ^ _w) * prime;
            return h;
        }

        /// <summary>SplitMix64 — размешивает один сид в последовательность 32-битных слов состояния.</summary>
        private static uint NextSplitMix(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (uint)(z >> 32);
        }
    }
}
