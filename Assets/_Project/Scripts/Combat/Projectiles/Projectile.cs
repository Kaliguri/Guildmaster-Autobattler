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

        public float       RawDamage;
        public DamageType  DamageType;
        public float       ArmorK;
        public int         PiercesRemaining;
        public bool        IsAlive;
    }
}
