using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Детерминированный конвейер урона. Все методы статические и чистые.
    /// Порядок: raw → DamageDealtEff → броня/пробивание (школа) → сродство × тип существа →
    /// DamageTakenEff (вики «10» §5.4, «6» §6; ГДД «8» §«Школа vs сродство»).
    /// </summary>
    /// <remarks>
    /// Пайплайн ТОЛЬКО СЧИТАЕТ и ничего не применяет: щит и HP правит <c>TickLedger</c> на коммите
    /// тика. Разделение держится на законе видимости эффектов — статы источника и цели заморожены
    /// на весь тик, поэтому расчёт не зависит от того, в каком порядке удары дошли до пайплайна,
    /// и его можно выполнить сразу, а применение отложить (см. <c>tick-resolution</c>).
    /// </remarks>
    public static class DamagePipeline
    {
        /// <summary>
        /// Посчитать урон, который дойдёт до цели, ДО поглощения щитом. Ничего не мутирует.
        /// </summary>
        /// <param name="req">Запрос урона с источником, целью и параметрами.</param>
        /// <param name="mitigated">Сколько срезали броня и эффективности — то, чего не случилось.</param>
        /// <returns>Эффективный урон (≥ 0) после эффективностей, брони и пробивания.</returns>
        public static float Resolve(in DamageRequest req, out float mitigated)
        {
            float damage = req.RawDamage;

            // 1. Множитель эффективности урона источника
            damage *= req.Source.Stats.Get(StatType.DamageDealtEff);

            // 2. Броня по школе урона (пропускается для True damage)
            if (req.School != DamageSchool.True)
            {
                float armor, pen, penPct;

                if (req.School == DamageSchool.Physical)
                {
                    armor  = req.Target.Stats.Get(StatType.PhysArmor);
                    pen    = req.Source.Stats.Get(StatType.PhysPen);
                    penPct = req.Source.Stats.Get(StatType.PhysPenPct);
                }
                else
                {
                    armor  = req.Target.Stats.Get(StatType.MagicArmor);
                    pen    = req.Source.Stats.Get(StatType.MagicPen);
                    penPct = req.Source.Stats.Get(StatType.MagicPenPct);
                }

                // Пробивание: сначала %, потом плоское (стат источника + разовое пробивание этого удара);
                // эффективная броня не уходит в минус.
                // Проценты стата и удара умножаются ОСТАТКАМИ, а не складываются: 60% от стата и 50% от
                // удара дают 80% пробивания, а не 110% — сумма позволила бы обнулить любую броню двумя
                // умеренными источниками, и «броня вдвое меньше» перестало бы что-либо значить.
                float pctLeft = (1f - penPct) * (1f - req.BonusPctPen);
                if (pctLeft < 0f) pctLeft = 0f;

                float effArmor = Mathf.Max(0f, armor * pctLeft - pen - req.BonusFlatPen);
                damage *= req.ArmorK / (req.ArmorK + effArmor);
            }

            // Сродство урона (Яд/Свет/Тьма) НЕ участвует в расчёте: оно несёт идентичность механикой —
            // глаголом (яд травит, свет очищает и лечит, тьма бьёт голой мощью), а не коэффициентом по
            // типу цели (решение 2026-07-15/35, подтверждено 2026-07-26). Матрица «сродство × существо»
            // здесь стояла и снята: см. guard-тест Affinity_NeverScalesDamage_ByCreatureType.

            // 3. Множитель эффективности получаемого урона
            damage *= req.Target.Stats.Get(StatType.DamageTakenEff);

            damage = Mathf.Max(0f, damage);

            // Срезанное = замах минус дошедшее. Считается от СЫРОГО урона запроса (уязвимости в него
            // уже вложены вызывающим), поэтому число отвечает ровно на «сколько защита не пустила».
            mitigated = Mathf.Max(0f, req.RawDamage - damage);

            // Щит и HP здесь НЕ трогаются: их правит TickLedger, когда сложит все удары тика.
            return damage;
        }
    }
}
