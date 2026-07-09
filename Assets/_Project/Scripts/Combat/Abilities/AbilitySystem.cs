using System.Collections.Generic;
using Guildmaster.Combat.Abilities;
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

            // Круговой удар вокруг себя цели не требует (центр = кастующий); иначе нужна валидная цель.
            RuntimeUnit target = (panicSelf && data.IsHeal)
                ? caster
                : ResolveTarget(caster, data.TargetMode, units);
            if (data.AreaShape != AreaShape.Circle && target == null) return false;

            // Гейт условия каста (блок D): дешёвое решение «кастовать ли» — здесь, не в мозге.
            // Паника (блок E) кастует независимо от условия.
            if (!panicSelf && !CastConditionMet(caster, target, data, ctx)) return false;

            caster.CurrentResource -= data.ResourceCost;
            ability.CooldownRemaining = data.BaseCooldown * caster.Stats.Get(StatType.CooldownEff);

            if (data.AreaShape == AreaShape.Circle)
                ApplyCircle(caster, data, ctx);
            else
                ApplyToTarget(caster, target, data, ctx);

            return true;
        }

        /// <summary>Условие каста (блок D). Отмена по своему HP% (блок E) решается в <see cref="TryCast"/> до вызова.</summary>
        private bool CastConditionMet(RuntimeUnit caster, RuntimeUnit target, AbilityData data, ICombatContext ctx)
        {
            switch (data.CastCondition)
            {
                case CastCondition.EnemiesInRadius:
                    ctx.QueryUnitsInRadius(caster.Position, data.CastConditionRadius, _targets, TargetFilter.Enemies, caster.Team);
                    return _targets.Count >= data.CastConditionCount;

                case CastCondition.AllyTargetHpBelowPct:
                    // Спасаем раненого союзника: кастуем, только если выбранная цель просела до порога.
                    return target != null && HpPct(target) <= data.CastConditionHpPct;

                case CastCondition.Immediately:
                default:
                    return true;
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
