using System.Collections.Generic;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Game.Flow
{
    /// <summary>Тип узла-источника награды — задаёт наклон витрины (план 11 §5.4, [[meta-progression-economy]]).</summary>
    public enum RewardTier
    {
        Battle, // рядовой бой
        Elite,  // элита
        Boss,   // финалист акта
    }

    /// <summary>
    /// Генератор витрины наград (план 11 §4 A3, «сердце рогалика»): катит N (обычно 3) РАЗНЫХ реликвий из
    /// контент-БД. Детерминирован через <see cref="IRngService"/> (тот же seed → та же витрина; для реплея/коопа).
    /// Взятие/сброс живёт в <see cref="Guildmaster.Guild.RunStateService"/> (единая точка вместимости, §5.4) —
    /// сервис только формирует выбор.
    /// <para>Ramp по <see cref="RewardTier"/>: шанс уникальной реликвии в слоте витрины — 10% за рядовой бой,
    /// 20% за элиту, 100% за босса (экономика, [[meta-progression-economy]]). Наклон катится ПОСЛОТНО, так что
    /// у босса вся витрина уникальная, а в рядовом бою уник — редкий гость.</para>
    /// </summary>
    public sealed class RewardService
    {
        public const int DefaultChoiceCount = 3;
        private const string BaseRelicId = "relic.base";

        private readonly IContentDatabase _content;
        private readonly IRngService      _rng;

        public RewardService(IContentDatabase content, IRngService rng)
        {
            _content = content;
            _rng     = rng;
        }

        /// <summary>Шанс уникальной реликвии в одном слоте витрины.</summary>
        public static float UniqueChance(RewardTier tier) => tier switch
        {
            RewardTier.Boss  => 1f,
            RewardTier.Elite => 0.2f,
            _                => 0.1f,
        };

        /// <summary>
        /// Скатать витрину: до <paramref name="count"/> РАЗНЫХ реликвий (без базовой). Меньше пула — вернёт сколько
        /// есть. Порядок детерминирован RNG. Дубли с уже собранными допустимы (дубль = прогресс, экономика §5.4).
        /// </summary>
        public IReadOnlyList<RelicData> RollChoices(RewardTier tier, int count = DefaultChoiceCount)
        {
            var uniques = new List<RelicData>();
            var regular = new List<RelicData>();

            IReadOnlyList<RelicData> all = _content.All<RelicData>();
            for (int i = 0; i < all.Count; i++)
            {
                RelicData r = all[i];
                if (r == null || r.Id == BaseRelicId) continue;

                if (r.DropRarity == DropRarity.Unique) uniques.Add(r);
                else                                   regular.Add(r);
            }

            int total = uniques.Count + regular.Count;
            int take  = count < total ? count : total;

            float uniqueChance = UniqueChance(tier);
            var choices = new List<RelicData>(take);

            for (int i = 0; i < take; i++)
            {
                bool wantUnique = _rng.NextFloat() < uniqueChance;

                // Пул нужного вида может кончиться (уников мало) — тогда добираем из другого,
                // иначе витрина вернула бы меньше вариантов, чем может.
                List<RelicData> pool = wantUnique
                    ? (uniques.Count > 0 ? uniques : regular)
                    : (regular.Count > 0 ? regular : uniques);

                int j = _rng.NextInt(0, pool.Count);
                choices.Add(pool[j]);
                pool.RemoveAt(j); // без дублей внутри одной витрины
            }

            return choices;
        }
    }
}
