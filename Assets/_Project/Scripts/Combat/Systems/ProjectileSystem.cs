using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Продвигает снаряды и проверяет столкновения методом swept circle-segment,
    /// чтобы быстрые снаряды не «протыкали» цель между тиками (вики «10» §5.3).
    /// </summary>
    public sealed class ProjectileSystem
    {
        /// <summary>Обновить все живые снаряды за один тик. <paramref name="arena"/> — границы поля для деспавна (§4.2 п.4).</summary>
        public void Tick(List<Projectile> projectiles, List<RuntimeUnit> units, ICombatContext ctx, float dt, in ArenaBounds arena)
        {
            for (int i = 0; i < projectiles.Count; i++)
            {
                Projectile p = projectiles[i];
                if (!p.IsAlive) continue;

                p.PreviousPosition = p.Position;

                if (p.TargetUnit != null)
                {
                    AdvanceTracking(p, dt, units, ctx);
                }
                else
                {
                    AdvanceFreeFlying(p, dt, units, ctx, in arena);
                }
            }

            RemoveDead(projectiles);
        }

        private static void AdvanceTracking(Projectile p, float dt, List<RuntimeUnit> units, ICombatContext ctx)
        {
            if (p.TargetUnit.IsDead) { p.IsAlive = false; return; }

            Vector2 toTarget = p.TargetUnit.Position - p.Position;
            float   dist     = toTarget.magnitude;

            float speed  = p.Velocity.magnitude;
            float travel = speed * dt;

            if (dist <= travel + p.CollisionRadius)
            {
                p.Position = p.TargetUnit.Position;
                ApplyHit(p, p.TargetUnit, ctx);
            }
            else
            {
                p.Velocity  = toTarget.normalized * speed;
                p.Position += p.Velocity * dt;
            }
        }

        private static void AdvanceFreeFlying(Projectile p, float dt, List<RuntimeUnit> units, ICombatContext ctx, in ArenaBounds arena)
        {
            Vector2 newPos = p.Position + p.Velocity * dt;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead || unit.Team == p.Source.Team) continue;

                if (SweptCircleHit(p.PreviousPosition, newPos, unit.Position, p.CollisionRadius))
                {
                    ApplyHit(p, unit, ctx);
                    if (p.PiercesRemaining <= 0) { p.Position = newPos; return; }
                }
            }

            p.Position = newPos;

            if (IsOutOfBounds(p.Position, in arena)) p.IsAlive = false;
        }

        private static void ApplyHit(Projectile p, RuntimeUnit target, ICombatContext ctx)
        {
            // Хил-снаряд лечит цель (эффективности/кламп — внутри ctx.Heal), а не бьёт. Хилы всегда
            // tracking (по TargetUnit-союзнику), поэтому пробивание к ним не применяется. §9.2.
            if (p.IsHeal)
            {
                ctx.Heal(target, p.RawDamage, p.Source);
                p.IsAlive = false;
                return;
            }

            ctx.DealDamage(new DamageRequest(
                p.Source, target, p.RawDamage, p.DamageType, ctx.ArmorK));

            // On-hit эффекты (§9.1): «Заморозка» Криоманта вешается при попадании на каждую задетую цель.
            ApplyOnHitEffects(p, target, ctx);

            if (p.PiercesRemaining > 0) p.PiercesRemaining--;
            else                        p.IsAlive = false;
        }

        /// <summary>Наложить on-hit эффекты снаряда на задетую цель (§9.1). Источник — владелец снаряда.</summary>
        private static void ApplyOnHitEffects(Projectile p, RuntimeUnit target, ICombatContext ctx)
        {
            EffectData[] effects = p.OnHitEffects;
            if (effects == null) return;
            for (int i = 0; i < effects.Length; i++)
                if (effects[i] != null) ctx.ApplyEffect(target, effects[i], p.Source);
        }

        /// <summary>
        /// Проверка swept circle-segment: отрезок [from, to] vs круг (center, radius).
        /// Возвращает true, если минимальное расстояние от центра до отрезка ≤ radius.
        /// </summary>
        public static bool SweptCircleHit(Vector2 from, Vector2 to, Vector2 center, float radius)
        {
            Vector2 ab    = to - from;
            float   lenSq = ab.sqrMagnitude;

            float distSq;
            if (lenSq < 1e-8f)
            {
                distSq = (center - from).sqrMagnitude;
            }
            else
            {
                float t   = Vector2.Dot(center - from, ab) / lenSq;
                t         = Mathf.Clamp01(t);
                Vector2 closest = from + t * ab;
                distSq   = (center - closest).sqrMagnitude;
            }

            return distSq <= radius * radius;
        }

        // Снаряд гаснет, выйдя за границы арены на ProjectileDespawnMargin (§4.2 п.4). Бесконечное поле
        // (headless-тесты, бой без арены) → Size = +∞ → снаряд не деспавнится по границам (как и стены).
        private static bool IsOutOfBounds(Vector2 pos, in ArenaBounds arena)
        {
            Vector2 center = arena.Center;
            Vector2 size   = arena.Size;
            float halfX = size.x * 0.5f + SimConstants.ProjectileDespawnMargin;
            float halfY = size.y * 0.5f + SimConstants.ProjectileDespawnMargin;
            return Mathf.Abs(pos.x - center.x) > halfX || Mathf.Abs(pos.y - center.y) > halfY;
        }

        private static void RemoveDead(List<Projectile> projectiles)
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                if (!projectiles[i].IsAlive) projectiles.RemoveAt(i);
            }
        }
    }
}
