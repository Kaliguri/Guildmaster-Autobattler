using Guildmaster.Core.Simulation;
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
        /// Радиус тела юнита (мировые ед.) = Size × <see cref="SimTuning.BodyRadiusPerSize"/>.
        /// Тот же расчёт, что и в <c>SeparationSystem</c> — метрики обязаны совпадать (см. ниже).
        /// </summary>
        public static float BodyRadius(RuntimeUnit u, in SimTuning tuning)
            => Mathf.Max(0.01f, u.Stats.Get(StatType.Size)) * tuning.BodyRadiusPerSize;

        /// <summary>
        /// Эффективная дистанция ЦЕНТРОВ, на которой цель «в досягаемости»: <see cref="StatType.AttackRange"/>
        /// трактуется как зазор между <b>поверхностями</b> тел, поэтому к нему прибавляются радиусы обоих тел.
        /// Так удар корректен при любых размерах (крупный враг бьётся из-за большего тела с большей дистанции
        /// центров), а <b>ключевой инвариант</b> — reach считается из того же <c>BodyRadiusPerSize</c>, что и
        /// расталкивание: точка покоя сепарации (<c>r_свой + r_чужой</c>) всегда лежит ВНУТРИ reach, поэтому
        /// расталкивание физически не может выпихнуть юнита из радиуса атаки (иначе — вечный удар «вхолостую»).
        /// </summary>
        public static float AttackReachCenter(RuntimeUnit self, RuntimeUnit target, in SimTuning tuning)
            => self.Stats.Get(StatType.AttackRange) + BodyRadius(self, tuning) + BodyRadius(target, tuning);

        /// <summary>Цель в пределах досягаемости атаки (сравнение квадратов — без sqrt). См. <see cref="AttackReachCenter"/>.</summary>
        public static bool InAttackRange(RuntimeUnit self, RuntimeUnit target, in SimTuning tuning)
        {
            float reach = AttackReachCenter(self, target, tuning);
            return (target.Position - self.Position).sqrMagnitude <= reach * reach;
        }

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
