using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Продвигает снаряды и проверяет столкновения методом swept circle-segment,
    /// чтобы быстрые снаряды не «протыкали» цель между тиками (вики «10» §5.3).
    /// </summary>
    public sealed class ProjectileSystem
    {
        /// <summary>Обновить все живые снаряды за один тик.</summary>
        public void Tick(List<Projectile> projectiles, List<RuntimeUnit> units, ICombatContext ctx, float dt)
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
                    AdvanceFreeFlying(p, dt, units, ctx);
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

        private static void AdvanceFreeFlying(Projectile p, float dt, List<RuntimeUnit> units, ICombatContext ctx)
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

            if (IsOutOfBounds(p.Position)) p.IsAlive = false;
        }

        private static void ApplyHit(Projectile p, RuntimeUnit target, ICombatContext ctx)
        {
            ctx.DealDamage(new DamageRequest(
                p.Source, target, p.RawDamage, p.DamageType, ctx.ArmorK));

            if (p.PiercesRemaining > 0) p.PiercesRemaining--;
            else                        p.IsAlive = false;
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

        private static bool IsOutOfBounds(Vector2 pos) =>
            Mathf.Abs(pos.x) > 200f || Mathf.Abs(pos.y) > 200f;

        private static void RemoveDead(List<Projectile> projectiles)
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                if (!projectiles[i].IsAlive) projectiles.RemoveAt(i);
            }
        }
    }
}
