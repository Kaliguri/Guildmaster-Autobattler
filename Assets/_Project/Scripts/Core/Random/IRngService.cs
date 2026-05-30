namespace Guildmaster.Core.Random
{
    /// <summary>
    /// Детерминированный источник случайности симуляции. Любой рандом в бою идёт
    /// ТОЛЬКО через этот интерфейс — это одно из условий детерминизма
    /// (см. вики «10. Архитектура реализации — Фаза 1» §5.1, «5. Стек и архитектура»).
    /// </summary>
    /// <remarks>
    /// Реализация — <see cref="XorShiftRng"/>. На каждый бой создаётся свой инстанс
    /// с суб-сидом (<c>runSeed + battleIndex + attempt</c>) — фабрика в боевом scope (Фаза 1, шаг 9).
    /// </remarks>
    public interface IRngService
    {
        /// <summary>Следующее 32-битное беззнаковое значение. Базовая операция генератора.</summary>
        uint NextUInt();

        /// <summary>Случайное целое в полуинтервале <c>[minInclusive, maxExclusive)</c>.</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>Случайный <see cref="float"/> в полуинтервале <c>[0, 1)</c>.</summary>
        float NextFloat();

        /// <summary>Случайный <see cref="float"/> в полуинтервале <c>[minInclusive, maxExclusive)</c>.</summary>
        float NextFloat(float minInclusive, float maxExclusive);

        /// <summary>Бросок с вероятностью <paramref name="probability"/> (клампится в <c>[0, 1]</c>).</summary>
        bool Chance(float probability);

        /// <summary>
        /// 64-битный отпечаток текущего состояния генератора. Для checksum рассинхрона
        /// (<c>SimSyncProbe</c>, Фаза 1 шаг 12) — НЕ для точного восстановления состояния.
        /// </summary>
        ulong Snapshot();
    }
}
