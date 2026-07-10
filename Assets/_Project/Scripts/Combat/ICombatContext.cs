using System.Collections.Generic;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
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

        /// <summary>
        /// Заполнить <paramref name="results"/> живыми юнитами в полосе из <paramref name="origin"/>
        /// вдоль <paramref name="direction"/>: длина <paramref name="length"/>, полная ширина
        /// <paramref name="width"/>. Опора линейной авто-атаки «Размашистый выпад» (шаг 4).
        /// </summary>
        int QueryUnitsInLine(
            Vector2 origin,
            Vector2 direction,
            float length,
            float width,
            List<RuntimeUnit> results,
            TargetFilter filter,
            int requestingTeam);

        /// <summary>Применить эффект к цели. Тело — Фаза 2; в Фазе 1 — стаб (no-op).</summary>
        void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source);

        /// <summary>
        /// Сообщить презентации о сработавшей зоне удара (dev-оверлей зон, вики «13» шаг 4).
        /// Fire-and-forget — не мутирует симуляцию и не влияет на детерминизм.
        /// </summary>
        void ReportAreaHit(in AreaHit hit);

        /// <summary>Снять с цели подходящие эффекты (purge/cleanse, вики «6» §5.4).</summary>
        void Dispel(in Effects.DispelRequest req);

        /// <summary>Принудительно сместить цель (отбрасывание/«ядро», §9.9). Исполняет DisplacementSystem.</summary>
        void Displace(in DisplaceRequest req);

        /// <summary>
        /// Юнит вошёл в замах авто-атаки (вики «14»): запускает анимацию свинга во View и «вжух»-SFX.
        /// Fire-and-forget — не мутирует симуляцию.
        /// </summary>
        void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target);

        /// <summary>
        /// Замах авто-атаки прерван (стан/смерть себя): View рвёт свинг в idle, гасит звук замаха (вики «14»).
        /// Fire-and-forget — не мутирует симуляцию.
        /// </summary>
        void NotifyAttackInterrupted(RuntimeUnit unit);

        /// <summary>Генератор случайных чисел боя (детерминированный).</summary>
        IRngService Rng { get; }

        /// <summary>Текущий номер тика симуляции.</summary>
        int CurrentTick { get; }

        /// <summary>Константа K из StatsConfig для пайплайна брони.</summary>
        float ArmorK { get; }

        /// <summary>Снапшот балансного тюнинга, запечённый на старте боя (вики «13» §3.4, §4).</summary>
        SimTuning Tuning { get; }
    }
}
