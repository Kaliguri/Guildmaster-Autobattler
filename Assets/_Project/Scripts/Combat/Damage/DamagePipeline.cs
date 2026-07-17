using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Детерминированный конвейер урона. Все методы статические и чистые
    /// (in-параметры, мутация только HP/Shield цели через <see cref="DamageRequest"/>).
    /// Порядок: raw → DamageDealtEff → броня/пробивание (школа) → сродство × тип существа →
    /// DamageTakenEff → щит → HP (вики «10» §5.4, «6» §6; ГДД «8» §«Школа vs сродство»).
    /// </summary>
    public static class DamagePipeline
    {
        /// <summary>
        /// Выполнить пайплайн: вычислить финальный урон и применить его к <see cref="DamageRequest.Target"/>.
        /// </summary>
        /// <param name="req">Запрос урона с источником, целью и параметрами.</param>
        /// <returns>Детализированный результат для триггеров (lifesteal, шипы — Фаза 2).</returns>
        public static DamageResult Execute(in DamageRequest req)
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
                    armor  = req.Target.Stats.Get(StatType.ElementalArmor);
                    pen    = req.Source.Stats.Get(StatType.ElementalPen);
                    penPct = req.Source.Stats.Get(StatType.ElementalPenPct);
                }

                // Пробивание: сначала %, потом плоское; эффективная броня не уходит в минус
                float effArmor = Mathf.Max(0f, armor * (1f - penPct) - pen);
                damage *= req.ArmorK / (req.ArmorK + effArmor);
            }

            // 2.5. Сродство: бронёй НЕ гасится, зависит от типа существа цели. Действует и на True-урон.
            if (req.Affinity != DamageAffinity.None)
            {
                damage *= AffinityTable.Multiplier(req.Affinity, req.Target.CreatureType);
            }

            // 3. Множитель эффективности получаемого урона
            damage *= req.Target.Stats.Get(StatType.DamageTakenEff);

            damage = Mathf.Max(0f, damage);

            // 4. Поглощение щитом
            float shieldAbsorbed = Mathf.Min(req.Target.CurrentShield, damage);
            req.Target.CurrentShield -= shieldAbsorbed;
            float hpDamage = damage - shieldAbsorbed;

            // 5. Вычет из HP
            req.Target.CurrentHP -= hpDamage;

            return new DamageResult(hpDamage, shieldAbsorbed, req.Target.CurrentHP <= 0f, req.SourceKind);
        }
    }
}
