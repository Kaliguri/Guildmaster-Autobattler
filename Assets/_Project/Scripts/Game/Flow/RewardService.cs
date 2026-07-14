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
    /// Генератор витрины наград (план 11 §4 A3, «сердце рогалика»): катит N (обычно 3) РАЗНЫХ реликов из
    /// контент-БД. Детерминирован через <see cref="IRngService"/> (тот же seed → та же витрина; для реплея/коопа).
    /// Взятие/сброс живёт в <see cref="Guildmaster.Guild.RunStateService"/> (единая точка вместимости, §5.4) —
    /// сервис только формирует выбор. Наклон по <see cref="RewardTier"/> (шанс уникального 10/20/100% бой/элита/босс,
    /// [[meta-progression-economy]]) заложен швом-параметром, но НЕ реализован: зависит от миграции рарности
    /// выпадения (RelicRarity сейчас = нагрузка Common/Cursed/Divine, а не Trash/Common/Unique). TODO — при миграции.
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

        /// <summary>
        /// Скатать витрину: до <paramref name="count"/> РАЗНЫХ реликов (без базового). Меньше пула — вернёт сколько
        /// есть. Порядок детерминирован RNG. Дубли с уже собранными допустимы (дубль = прогресс, экономика §5.4).
        /// </summary>
        public IReadOnlyList<RelicData> RollChoices(RewardTier tier, int count = DefaultChoiceCount)
        {
            var pool = new List<RelicData>();
            IReadOnlyList<RelicData> all = _content.All<RelicData>();
            for (int i = 0; i < all.Count; i++)
            {
                RelicData r = all[i];
                if (r != null && r.Id != BaseRelicId) pool.Add(r);
            }

            // TODO(ramp): наклонить веса по tier (шанс уникального) после миграции рарности выпадения.
            int take = count < pool.Count ? count : pool.Count;
            PartialShuffleFront(pool, take);

            var choices = new List<RelicData>(take);
            for (int i = 0; i < take; i++) choices.Add(pool[i]);
            return choices;
        }

        // Fisher-Yates для первых take позиций: тянет уникальные элементы в начало без полной перетасовки.
        private void PartialShuffleFront(List<RelicData> list, int take)
        {
            int n = list.Count;
            for (int i = 0; i < take; i++)
            {
                int j = _rng.NextInt(i, n); // [i, n)
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
