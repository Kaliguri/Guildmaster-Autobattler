using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Управляет кулдаунами автоатак и инициирует нанесение урона или создание снарядов
    /// в зависимости от <see cref="AttackType"/> реликвии юнита (вики «10» §5.3).
    /// </summary>
    public sealed class AutoAttackSystem
    {
        /// <summary>Обработать автоатаки всех живых юнитов за один тик.</summary>
        public void Tick(List<RuntimeUnit> units, ICombatContext ctx, float dt)
        {
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead || unit.CurrentTarget == null || unit.CurrentTarget.IsDead)
                    continue;

                // Контроль (оглушение/сон) — не атакуем и не тикаем кулдаун (вики «6» §5.3).
                if (!unit.CanAct) continue;

                unit.AttackCooldown -= dt;
                if (unit.AttackCooldown > 0f) continue;

                float range = unit.Stats.Get(StatType.AttackRange);
                if ((unit.CurrentTarget.Position - unit.Position).sqrMagnitude > range * range)
                    continue;

                float attackSpeed = unit.Stats.Get(StatType.AttackSpeed);
                unit.AttackCooldown = attackSpeed > 0f ? 1f / attackSpeed : float.MaxValue;

                AttackType attackType = unit.Relic != null ? unit.Relic.AttackType : AttackType.Melee;
                float raw = unit.Stats.Get(StatType.AutoAttackDamage);
                DamageType dmgType = unit.Relic != null ? unit.Relic.DamageType : DamageType.Physical;

                if (attackType == AttackType.Melee)
                {
                    ctx.DealDamage(new DamageRequest(unit, unit.CurrentTarget, raw, dmgType, ctx.ArmorK));
                }
                else
                {
                    float speed = unit.Stats.Get(StatType.ProjectileSpeed);
                    int   pierces = (int)unit.Stats.Get(StatType.ProjectilePierce);
                    float collRadius = unit.Stats.Get(StatType.Size) * 0.25f;

                    ctx.SpawnProjectile(new ProjectileSpawn(
                        unit, unit.Position, unit.CurrentTarget,
                        speed, collRadius, raw, dmgType, ctx.ArmorK, pierces));
                }
            }
        }
    }
}
