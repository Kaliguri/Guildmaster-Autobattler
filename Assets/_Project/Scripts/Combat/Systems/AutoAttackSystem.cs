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
        // Переиспользуемый буфер целей линейной авто-атаки — без аллокаций на горячем пути.
        private readonly List<RuntimeUnit> _lineTargets = new List<RuntimeUnit>();

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

                // Прирост ресурса за удар (мана-реликвии, шаг 4): начисляется при выстреле авто-атаки.
                GainResourceOnHit(unit);

                AttackType attackType = unit.Relic != null ? unit.Relic.AttackType : AttackType.Melee;
                float raw = unit.Stats.Get(StatType.AutoAttackDamage);
                DamageType dmgType = unit.Relic != null ? unit.Relic.DamageType : DamageType.Physical;
                AreaShape shape = unit.Relic != null ? unit.Relic.AutoAttackShape : AreaShape.None;

                if (attackType == AttackType.Melee)
                {
                    if (shape == AreaShape.Line)
                        DealLineDamage(unit, range, raw, dmgType, ctx);
                    else
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

        /// <summary>Линейная авто-атака «Размашистый выпад»: полоса к цели, урон по всем врагам в ней.</summary>
        private void DealLineDamage(RuntimeUnit unit, float length, float raw, DamageType dmgType, ICombatContext ctx)
        {
            float width = unit.Relic.AutoAttackWidth;
            Vector2 dir = unit.CurrentTarget.Position - unit.Position;

            // Dev-оверлей зоны (показываем полосу даже если никого не задели).
            ctx.ReportAreaHit(AreaHit.Line(unit.Position, dir, length, width, unit.Team));

            ctx.QueryUnitsInLine(unit.Position, dir, length, width, _lineTargets, TargetFilter.Enemies, unit.Team);

            // Урон по целям независим (коммутативен) — порядок из spatial hash не влияет на итоговое состояние.
            for (int t = 0; t < _lineTargets.Count; t++)
                ctx.DealDamage(new DamageRequest(unit, _lineTargets[t], raw, dmgType, ctx.ArmorK));
        }

        /// <summary>Начислить ресурс за удар (× ResourceGainEff), клампить к MaxResource.</summary>
        private static void GainResourceOnHit(RuntimeUnit unit)
        {
            float onHit = unit.Relic != null ? unit.Relic.ResourceOnHit : 0f;
            if (onHit <= 0f) return;

            float gain = onHit * unit.Stats.Get(StatType.ResourceGainEff);
            unit.CurrentResource += gain;

            float maxRes = unit.Stats.Get(StatType.MaxResource);
            if (maxRes > 0f && unit.CurrentResource > maxRes) unit.CurrentResource = maxRes;
        }
    }
}
