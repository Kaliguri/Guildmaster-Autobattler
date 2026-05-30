using System.Collections.Generic;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Шов «логика эффектов/способностей ↔ боевой мир». Единственная точка входа для мутаций
    /// симуляции из систем и компонентов эффектов. Реализует <see cref="CombatSimulation"/>
    /// (вики «10» §5.5).
    /// </summary>
    public interface ICombatContext
    {
        /// <summary>Применить урон через полный пайплайн.</summary>
        void DealDamage(in DamageRequest req);

        /// <summary>Исцелить юнита. Не выходит за MaxHP.</summary>
        void Heal(RuntimeUnit target, float amount, RuntimeUnit source);

        /// <summary>Создать снаряд в симуляции.</summary>
        void SpawnProjectile(in ProjectileSpawn spawn);

        /// <summary>
        /// Заполнить <paramref name="results"/> живыми юнитами в радиусе от <paramref name="center"/>.
        /// </summary>
        int QueryUnitsInRadius(
            Vector2 center,
            float radius,
            List<RuntimeUnit> results,
            TargetFilter filter,
            int requestingTeam);

        /// <summary>Применить эффект к цели. Тело — Фаза 2; в Фазе 1 — стаб (no-op).</summary>
        void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source);

        /// <summary>Генератор случайных чисел боя (детерминированный).</summary>
        IRngService Rng { get; }

        /// <summary>Текущий номер тика симуляции.</summary>
        int CurrentTick { get; }

        /// <summary>Константа K из StatsConfig для пайплайна брони.</summary>
        float ArmorK { get; }
    }
}
