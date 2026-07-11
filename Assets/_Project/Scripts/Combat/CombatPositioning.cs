using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Общие позиционные хелперы боя — детерминированная векторная математика без RNG.
    /// </summary>
    public static class CombatPositioning
    {
        /// <summary>
        /// Телепорт атакующего ЗА спину цели: на ДАЛЬНЮЮ от атакующего сторону (по направлению
        /// «атакующий → цель»), на расстоянии половины радиуса атаки — чтобы встать вплотную и в радиусе.
        /// Снимает интерполяцию (<see cref="RuntimeUnit.PreviousPosition"/> = позиция) — вид снапает, не «едет».
        /// Общая точка для монаха (§10.6) и убийцы (§10.5).
        /// </summary>
        public static void TeleportBehind(RuntimeUnit attacker, RuntimeUnit target)
        {
            if (attacker == null || target == null) return;

            Vector2 fromAttacker = target.Position - attacker.Position; // атакующий → цель = направление «в спину»
            Vector2 behindDir = fromAttacker.sqrMagnitude > 1e-4f ? fromAttacker.normalized : Vector2.right;
            float offset = attacker.Stats.Get(StatType.AttackRange) * 0.5f;

            attacker.PreviousPosition = attacker.Position;
            attacker.Position = target.Position + behindDir * offset;
        }
    }
}
