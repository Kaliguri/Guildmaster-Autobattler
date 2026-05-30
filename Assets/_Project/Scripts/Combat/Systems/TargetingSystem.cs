using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Назначает цель каждому живому юниту. Фаза 1 — «ближайший живой враг».
    /// Полный AI Filter/Score/Override — Фаза 3 (этот класс станет тонкой обёрткой).
    /// </summary>
    public sealed class TargetingSystem
    {
        /// <summary>Пересчитать цели всех живых юнитов за один тик.</summary>
        public void Tick(List<RuntimeUnit> units)
        {
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead) continue;

                if (unit.CurrentTarget != null && !unit.CurrentTarget.IsDead) continue;

                unit.CurrentTarget = FindNearestEnemy(unit, units);
            }
        }

        // TODO Фаза 3: заменить линейный O(n²) перебор на SpatialHash.QueryRadius,
        // когда число юнитов вырастет (сейчас десятки — перебор дешевле запроса).
        private static RuntimeUnit FindNearestEnemy(RuntimeUnit unit, List<RuntimeUnit> all)
        {
            RuntimeUnit nearest   = null;
            float       nearestSq = float.MaxValue;

            for (int i = 0; i < all.Count; i++)
            {
                RuntimeUnit other = all[i];
                if (other.IsDead || other.Team == unit.Team) continue;

                float distSq = (other.Position - unit.Position).sqrMagnitude;
                if (distSq < nearestSq)
                {
                    nearestSq = distSq;
                    nearest   = other;
                }
            }

            return nearest;
        }
    }
}
