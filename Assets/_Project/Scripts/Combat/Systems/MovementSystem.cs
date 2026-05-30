using System.Collections.Generic;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Интегрирует позиции живых юнитов к их целям. Ручная математика — без Rigidbody2D
    /// и без Time.deltaTime (dt передаётся снаружи из <see cref="CombatLoopService"/>).
    /// Фаза 1 — простое «двигайся к цели до дистанции атаки».
    /// Продвинутые режимы позиционирования (IPositioningMode) — Фаза 3.
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

                unit.PreviousPosition = unit.Position;

                // Контроль (корень/обездвиживание) — стоим на месте (вики «6» §5.3).
                if (!unit.CanMove) continue;

                if (unit.CurrentTarget == null) continue;

                float attackRange = unit.Stats.Get(StatType.AttackRange);
                Vector2 toTarget  = unit.CurrentTarget.Position - unit.Position;
                float distSq      = toTarget.sqrMagnitude;
                float rangeSq     = attackRange * attackRange;

                if (distSq <= rangeSq) continue;

                float moveSpeed = unit.Stats.Get(StatType.MoveSpeed);
                float maxMove   = moveSpeed * dt;
                float dist      = Mathf.Sqrt(distSq);

                if (dist - attackRange <= maxMove)
                {
                    unit.Position = unit.CurrentTarget.Position -
                                    toTarget / dist * attackRange;
                }
                else
                {
                    unit.Position += toTarget / dist * maxMove;
                }
            }
        }
    }
}
