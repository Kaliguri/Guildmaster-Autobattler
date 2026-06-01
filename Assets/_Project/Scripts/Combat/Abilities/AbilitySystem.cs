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

            // Гейт условия каста (блоки D/E): дешёвое решение «кастовать ли» — здесь, не в мозге.
            if (!CastConditionMet(caster, data, ctx)) return false;

            // Круговой удар вокруг себя цели не требует (центр = кастующий); иначе нужна валидная цель.
            RuntimeUnit target = ResolveTarget(caster, data.TargetMode, units);
            if (data.AreaShape != AreaShape.Circle && target == null) return false;

            caster.CurrentResource -= data.ResourceCost;
            ability.CooldownRemaining = data.BaseCooldown * caster.Stats.Get(StatType.CooldownEff);

            if (data.AreaShape == AreaShape.Circle)
                ApplyCircle(caster, data, ctx);
            else
                ApplyToTarget(caster, target, data, ctx);

            return true;
        }

        /// <summary>Условие каста (блок D) + отмена по своему HP% (блок E). Immediately = всегда.</summary>
        private bool CastConditionMet(RuntimeUnit caster, AbilityData data, ICombatContext ctx)
        {
            // Блок E: при падении HP кастующего ≤ порога кастуем независимо от условия (паника/выживание).
            if (data.CastOverrideSelfHpPct > 0f && HpPct(caster) <= data.CastOverrideSelfHpPct)
                return true;

            switch (data.CastCondition)
            {
                case CastCondition.EnemiesInRadius:
                    ctx.QueryUnitsInRadius(caster.Position, data.CastConditionRadius, _targets, TargetFilter.Enemies, caster.Team);
                    return _targets.Count >= data.CastConditionCount;

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

        /// <summary>Одиночный каст (поведение Ф2 + опциональный прямой урон ×AutoAttackDamage).</summary>
        private static void ApplyToTarget(RuntimeUnit caster, RuntimeUnit target, AbilityData data, ICombatContext ctx)
        {
            float dmg = AbilityDamage(caster, data);
            if (dmg > 0f)
            {
                DamageType dmgType = caster.Relic != null ? caster.Relic.DamageType : DamageType.Physical;
                ctx.DealDamage(new DamageRequest(caster, target, dmg, dmgType, ctx.ArmorK));
            }
            ApplyEffects(target, data, caster, ctx);
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
    }
}
