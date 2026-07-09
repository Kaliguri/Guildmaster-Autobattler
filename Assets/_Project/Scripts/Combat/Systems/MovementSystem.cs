using System.Collections.Generic;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Интегрирует позиции живых юнитов. Ручная математика — без Rigidbody2D и без Time.deltaTime
    /// (dt передаётся снаружи из <see cref="CombatLoopService"/>). Ветвится по
    /// <see cref="RuntimeUnit.Positioning"/> (§9.7): Approach (Ф1), Kite (полоса дистанции),
    /// Retreat (от ближайшего врага). Стрельба на ходу (§9.8) снимает рут замаха.
    /// </summary>
    public sealed class MovementSystem
    {
        /// <summary>Продвинуть позиции всех живых юнитов на один тик.</summary>
        /// <param name="units">Список всех юнитов в бою.</param>
        /// <param name="dt">Длительность тика (всегда <see cref="SimConstants.TickDelta"/>).</param>
        public void Tick(List<RuntimeUnit> units, float dt)
        {
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead) continue;

                // В полёте (§9.9) юнита двигает DisplacementSystem — сам он не перемещается.
                if (unit.DisplacedTicksRemaining > 0) continue;

                unit.PreviousPosition = unit.Position;

                // Контроль (корень/обездвиживание) — стоим на месте (вики «6» §5.3).
                if (!unit.CanMove) continue;

                bool windingUp        = unit.IsWindingUp;
                bool attackWhileMoving = unit.Relic != null && unit.Relic.CanAttackWhileMoving;

                // Замах авто-атаки рутит юнита (свинг на месте, вики «14») — КРОМЕ реликвий со
                // «стрельбой на ходу» (§9.8): те продолжают движение со штрафом скорости.
                if (windingUp && !attackWhileMoving) continue;

                RuntimeUnit target = unit.CurrentTarget;
                if (target == null) continue;

                float moveSpeed = unit.Stats.Get(StatType.MoveSpeed);
                if (windingUp && attackWhileMoving)
                    moveSpeed *= Mathf.Max(0f, 1f - unit.Relic.MovingAttackSpeedPenaltyPct); // §9.8

                float maxMove = moveSpeed * dt;
                if (maxMove <= 0f) continue;

                switch (unit.Positioning)
                {
                    case PositioningIntent.Kite:    MoveKite(unit, target, maxMove); break;
                    case PositioningIntent.Retreat: MoveRetreat(unit, units, maxMove); break;
                    default:                        MoveApproach(unit, target, maxMove); break;
                }
            }
        }

        /// <summary>Сближение до дистанции атаки (поведение Ф1).</summary>
        private static void MoveApproach(RuntimeUnit unit, RuntimeUnit target, float maxMove)
        {
            float attackRange = unit.Stats.Get(StatType.AttackRange);
            Vector2 toTarget  = target.Position - unit.Position;
            float distSq      = toTarget.sqrMagnitude;
            float rangeSq     = attackRange * attackRange;

            if (distSq <= rangeSq) return;

            float dist = Mathf.Sqrt(distSq);
            if (dist - attackRange <= maxMove)
                unit.Position = target.Position - toTarget / dist * attackRange;
            else
                unit.Position += toTarget / dist * maxMove;
        }

        /// <summary>
        /// Кайт (§9.7): держим дистанцию в полосе [AttackRange×0.6, AttackRange] от цели —
        /// отходим, если ближе нижней границы; подходим, если дальше радиуса; иначе стоим и стреляем.
        /// </summary>
        private static void MoveKite(RuntimeUnit unit, RuntimeUnit target, float maxMove)
        {
            float range = unit.Stats.Get(StatType.AttackRange);
            float near   = range * 0.6f;

            Vector2 toTarget = target.Position - unit.Position;
            float dist = toTarget.magnitude;
            if (dist < 1e-4f) return;

            Vector2 dir = toTarget / dist;
            if (dist < near)
                unit.Position -= dir * Mathf.Min(maxMove, near - dist);  // слишком близко — отходим
            else if (dist > range)
                unit.Position += dir * Mathf.Min(maxMove, dist - range); // вне радиуса — подходим
            // иначе — в полосе, стоим (атакуем на ходу)
        }

        /// <summary>Отступление (§9.7): движемся прочь от ближайшего врага (тай-брейк по Id — детерминизм).</summary>
        private static void MoveRetreat(RuntimeUnit unit, List<RuntimeUnit> units, float maxMove)
        {
            RuntimeUnit nearest = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit o = units[i];
                if (o.IsDead || o.Team == unit.Team) continue;
                float sq = (o.Position - unit.Position).sqrMagnitude;
                if (sq < bestSq || (sq == bestSq && (nearest == null || o.Id < nearest.Id)))
                {
                    bestSq = sq;
                    nearest = o;
                }
            }
            if (nearest == null) return;

            Vector2 away = unit.Position - nearest.Position;
            if (away.sqrMagnitude < 1e-4f) return;
            unit.Position += away.normalized * maxMove;
        }
    }
}
