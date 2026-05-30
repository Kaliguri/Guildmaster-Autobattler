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

            RuntimeUnit target = ResolveTarget(caster, data.TargetMode, units);
            if (target == null) return false;

            caster.CurrentResource -= data.ResourceCost;
            ability.CooldownRemaining = data.BaseCooldown * caster.Stats.Get(StatType.CooldownEff);

            EffectData[] effects = data.Effects;
            if (effects != null)
            {
                for (int i = 0; i < effects.Length; i++)
                {
                    ctx.ApplyEffect(target, effects[i], caster);
                }
            }

            return true;
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
