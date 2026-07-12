using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Рантайм-данные одного снаряда. Хранится в списке <c>CombatSimulation._projectiles</c>
    /// и обновляется <see cref="ProjectileSystem"/> каждый тик.
    /// </summary>
    public sealed class Projectile
    {
        /// <summary>Уникальный идентификатор снаряда в рамках боя.</summary>
        public int Id;

        public RuntimeUnit Source;
        public Vector2     Position;
        public Vector2     PreviousPosition;
        public Vector2     Velocity;
        public float       CollisionRadius;

        /// <summary>Цель для трекинга. null = свободный полёт (пробивание/AOE).</summary>
        public RuntimeUnit TargetUnit;

        /// <summary>Полезная нагрузка снаряда. Для урона — сырой урон; для хил-снаряда (<see cref="IsHeal"/>) — сырое лечение.</summary>
        public float       RawDamage;
        public DamageType  DamageType;
        public float       ArmorK;
        public int         PiercesRemaining;

        /// <summary>Хил-снаряд: при попадании лечит цель (<see cref="RawDamage"/> = сырое лечение), а не бьёт. Всегда tracking. §9.2.</summary>
        public bool        IsHeal;

        /// <summary>On-hit эффекты урон-снаряда (§9.1): накладываются на каждую задетую цель при попадании. null = нет.</summary>
        public EffectData[] OnHitEffects;

        /// <summary>Снаряд-автоатака: попадание помечает урон как автоатаку (<see cref="DamageRequest.IsAutoAttack"/>).</summary>
        public bool        IsAutoAttack;

        /// <summary>
        /// Теги on-hit эффектов, «забронированные» на <see cref="TargetUnit"/> на время полёта
        /// (см. <see cref="RuntimeUnit.IncomingEffectTags"/>). Снимаются с цели при разрешении снаряда.
        /// </summary>
        public Data.Definitions.EffectTag ReservedTags;

        public bool        IsAlive;
    }
}
