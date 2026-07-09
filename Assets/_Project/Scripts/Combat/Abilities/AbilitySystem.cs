using System.Collections.Generic;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Тик способностей: убывание кулдаунов и детерминированный плейсхолдер-каст готовых активок
    /// (полный выбор «когда/что кастовать» — AI Фазы 3, вики «12» §1, §9). Каст списывает ресурс,
    /// ставит кулдаун (× CooldownEff) и накладывает эффекты способности на цель.
    /// </summary>
    public sealed class AbilitySystem
    {
        // Переиспользуемый буфер для радиус-запросов (условие каста / AOE-цели) — без аллокаций.
        private readonly List<RuntimeUnit> _targets = new List<RuntimeUnit>();

        public void Tick(IReadOnlyList<RuntimeUnit> units, ICombatContext ctx, float dt)
        {
            for (int u = 0; u < units.Count; u++)
            {
                RuntimeUnit unit = units[u];
                if (unit.IsDead || unit.Abilities.Count == 0) continue;

                for (int a = 0; a < unit.Abilities.Count; a++)
                {
                    AbilityRuntime ability = unit.Abilities[a];
                    if (ability.CooldownRemaining > 0f) ability.CooldownRemaining -= dt;
                }

                // Плейсхолдер-триггер: кастуем первую готовую активку, если можем.
                if (unit.CanAct && unit.CanCast)
                {
                    for (int a = 0; a < unit.Abilities.Count; a++)
                    {
                        if (TryCast(unit, a, units, ctx)) break; // одна способность за тик
                    }
                }
            }
        }

        /// <summary>
        /// Попытаться скастовать способность <paramref name="abilityIndex"/>. Возвращает false, если
        /// не готова / не хватает ресурса / нет валидной цели.
        /// </summary>
        public bool TryCast(RuntimeUnit caster, int abilityIndex, IReadOnlyList<RuntimeUnit> units, ICombatContext ctx)
        {
            if (caster == null || abilityIndex < 0 || abilityIndex >= caster.Abilities.Count) return false;

            AbilityRuntime ability = caster.Abilities[abilityIndex];
            AbilityData data = ability.Data;
            if (data == null || !ability.IsReady) return false;
            if (caster.CurrentResource < data.ResourceCost) return false;

            // Блок E (паника): при своём низком HP лечащая способность разворачивается на самого
            // кастующего (лечит себя); урон-способность просто кастуется независимо от условия.
            bool panicSelf = data.CastOverrideSelfHpPct > 0f && HpPct(caster) <= data.CastOverrideSelfHpPct;

            bool isMassTag = data.TargetMode == AbilityTargetMode.AllEnemiesWithTag;

            // Круговой удар и масс-по-тегу одиночной цели не требуют (центр = кастующий / список).
            RuntimeUnit target = (panicSelf && data.IsHeal) ? caster
                               : isMassTag                  ? null
                               : ResolveTarget(caster, data.TargetMode, units);

            // Требование валидной цели: Circle — центр = кастующий; масс-по-тегу — нужен хотя бы один
            // тегнутый враг (даже под панику масс-стан в пустоту не жжёт КД/ману); иначе — одиночная цель.
            if (isMassTag)
            {
                if (CountEnemiesWithTag(caster, data.TriggerTag, units) == 0) return false;
            }
            else if (data.AreaShape != AreaShape.Circle && target == null)
            {
                return false;
            }

            // Гейт условия каста (блок D): дешёвое решение «кастовать ли» — здесь, не в мозге.
            // Паника (блок E) кастует независимо от условия.
            if (!panicSelf && !CastConditionMet(caster, target, data, ctx, units)) return false;

            caster.CurrentResource -= data.ResourceCost;
            ability.CooldownRemaining = data.BaseCooldown * caster.Stats.Get(StatType.CooldownEff);

            if (isMassTag)
                ApplyAllWithTag(caster, data, units, ctx);
            else if (data.AreaShape == AreaShape.Circle)
                ApplyCircle(caster, data, ctx);
            else
                ApplyToTarget(caster, target, data, ctx);

            return true;
        }

        /// <summary>Условие каста (блок D). Отмена по своему HP% (блок E) решается в <see cref="TryCast"/> до вызова.</summary>
        private bool CastConditionMet(RuntimeUnit caster, RuntimeUnit target, AbilityData data, ICombatContext ctx, IReadOnlyList<RuntimeUnit> units)
        {
            switch (data.CastCondition)
            {
                case CastCondition.EnemiesInRadius:
                    ctx.QueryUnitsInRadius(caster.Position, data.CastConditionRadius, _targets, TargetFilter.Enemies, caster.Team);
                    return _targets.Count >= data.CastConditionCount;

                case CastCondition.AllyTargetHpBelowPct:
                    // Спасаем раненого союзника: кастуем, только если выбранная цель просела до порога.
                    return target != null && HpPct(target) <= data.CastConditionHpPct;

                case CastCondition.EnemiesWithTagCount:
                    // Криомант: кастуем масс-стан, когда замороженных врагов накопилось ≥ X (глобально).
                    return CountEnemiesWithTag(caster, data.TriggerTag, units) >= data.CastConditionCount;

                case CastCondition.Immediately:
                default:
                    return true;
            }
        }

        /// <summary>Число живых врагов кастующего, несущих <paramref name="tag"/> (по маске активных эффектов). Глобально, без дальности (§9.10).</summary>
        private static int CountEnemiesWithTag(RuntimeUnit caster, EffectTag tag, IReadOnlyList<RuntimeUnit> units)
        {
            if (tag == EffectTag.None) return 0;
            int count = 0;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.IsDead || u.Team == caster.Team) continue;
                if ((u.EffectTagMask & tag) != 0) count++;
            }
            return count;
        }

        /// <summary>
        /// Масс-каст «Ледяные оковы» (§9.10): наложить эффекты активки на всех живых врагов с
        /// <see cref="AbilityData.TriggerTag"/> (глобально), затем — при <see cref="AbilityData.ConsumesTriggerTag"/> —
        /// снять этот тег (конверсия «Заморозки» в стан). Обход по индексу списка — детерминизм.
        /// </summary>
        private static void ApplyAllWithTag(RuntimeUnit caster, AbilityData data, IReadOnlyList<RuntimeUnit> units, ICombatContext ctx)
        {
            EffectTag tag = data.TriggerTag;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.IsDead || u.Team == caster.Team) continue;
                if ((u.EffectTagMask & tag) == 0) continue;

                ApplyEffects(u, data, caster, ctx);

                // Конверсия: снять тег-триггер (напр. Frozen) после наложения стана — «Заморозка» превращается в стан.
                if (data.ConsumesTriggerTag)
                    ctx.Dispel(new DispelRequest(u, DispelTargetPolarity.Any, tag, dispelPower: int.MaxValue, maxCount: 0));
            }
        }

        /// <summary>Круговой AOE-удар вокруг кастующего («Стальной вихрь»): урон + эффекты по всем врагам в радиусе.</summary>
        private void ApplyCircle(RuntimeUnit caster, AbilityData data, ICombatContext ctx)
        {
            // Dev-оверлей зоны круга.
            ctx.ReportAreaHit(AreaHit.Circle(caster.Position, data.AreaRadius, caster.Team));

            ctx.QueryUnitsInRadius(caster.Position, data.AreaRadius, _targets, TargetFilter.Enemies, caster.Team);

            float dmg = AbilityDamage(caster, data);
            DamageType dmgType = caster.Relic != null ? caster.Relic.DamageType : DamageType.Physical;

            // Урон по целям независим (коммутативен) — порядок из spatial hash не влияет на итог.
            for (int i = 0; i < _targets.Count; i++)
            {
                RuntimeUnit t = _targets[i];
                if (dmg > 0f) ctx.DealDamage(new DamageRequest(caster, t, dmg, dmgType, ctx.ArmorK));
                ApplyEffects(t, data, caster, ctx);
            }
        }

        /// <summary>Одиночный каст: хил-нагрузка (Пастырь) ИЛИ прямой урон ×AutoAttackDamage (поведение Ф2) + эффекты.</summary>
        private static void ApplyToTarget(RuntimeUnit caster, RuntimeUnit target, AbilityData data, ICombatContext ctx)
        {
            if (data.IsHeal)
            {
                // Сырое лечение (dealt/taken eff и кламп к MaxHP применяет ctx.Heal). «Длань жизни» = X + недостающее HP.
                ctx.Heal(target, HealAmount(target, data), caster);
            }
            else
            {
                float dmg = AbilityDamage(caster, data);
                if (dmg > 0f)
                {
                    DamageType dmgType = caster.Relic != null ? caster.Relic.DamageType : DamageType.Physical;
                    ctx.DealDamage(new DamageRequest(caster, target, dmg, dmgType, ctx.ArmorK));
                }
            }
            ApplyEffects(target, data, caster, ctx);
        }

        /// <summary>Сырое лечение способности = HealFlat + HealPctTargetMissingHp × недостающее HP цели.</summary>
        private static float HealAmount(RuntimeUnit target, AbilityData data)
        {
            float missing = target.Stats.Get(StatType.MaxHP) - target.CurrentHP;
            if (missing < 0f) missing = 0f;
            return data.HealFlat + data.HealPctTargetMissingHp * missing;
        }

        /// <summary>Прямой урон способности = DamageMultiplier × AutoAttackDamage кастующего (0 = только эффекты).</summary>
        private static float AbilityDamage(RuntimeUnit caster, AbilityData data)
        {
            return data.DamageMultiplier > 0f
                ? data.DamageMultiplier * caster.Stats.Get(StatType.AutoAttackDamage)
                : 0f;
        }

        private static void ApplyEffects(RuntimeUnit target, AbilityData data, RuntimeUnit caster, ICombatContext ctx)
        {
            EffectData[] effects = data.Effects;
            if (effects == null) return;
            for (int i = 0; i < effects.Length; i++)
                ctx.ApplyEffect(target, effects[i], caster);
        }

        private static float HpPct(RuntimeUnit u)
        {
            float maxHp = u.Stats.Get(StatType.MaxHP);
            return maxHp > 0f ? u.CurrentHP / maxHp : u.CurrentHP;
        }

        private static RuntimeUnit ResolveTarget(RuntimeUnit caster, AbilityTargetMode mode, IReadOnlyList<RuntimeUnit> units)
        {
            switch (mode)
            {
                case AbilityTargetMode.Self:
                    return caster;

                case AbilityTargetMode.NearestEnemy:
                    return caster.CurrentTarget != null && !caster.CurrentTarget.IsDead ? caster.CurrentTarget : null;

                case AbilityTargetMode.NearestAlly:
                    return NearestAlly(caster, units);

                case AbilityTargetMode.LowestHpAlly:
                    return LowestHpAlly(caster, units);

                default:
                    return null;
            }
        }

        private static RuntimeUnit NearestAlly(RuntimeUnit caster, IReadOnlyList<RuntimeUnit> units)
        {
            RuntimeUnit best = null;
            float bestSq = float.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit other = units[i];
                if (other == caster || other.IsDead || other.Team != caster.Team) continue;

                float sq = (other.Position - caster.Position).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = other;
                }
            }

            return best;
        }

        /// <summary>
        /// Союзник с наименьшим HP% — глобально, без ограничения дальности (хилер-ульта «Длань жизни»).
        /// Себя исключаем: свой критический HP покрывает блок E. Тай-брейк — дистанция, затем Id (детерминизм).
        /// </summary>
        private static RuntimeUnit LowestHpAlly(RuntimeUnit caster, IReadOnlyList<RuntimeUnit> units)
        {
            RuntimeUnit best      = null;
            float       bestPct   = float.MaxValue;
            float       bestDistSq = float.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit other = units[i];
                if (other == caster || other.IsDead || other.Team != caster.Team) continue;

                float pct    = HpPct(other);
                float distSq = (other.Position - caster.Position).sqrMagnitude;

                bool better =
                    best == null
                    || pct < bestPct
                    || (pct == bestPct && distSq < bestDistSq)
                    || (pct == bestPct && distSq == bestDistSq && other.Id < best.Id);

                if (better) { best = other; bestPct = pct; bestDistSq = distSq; }
            }

            return best;
        }
    }
}
