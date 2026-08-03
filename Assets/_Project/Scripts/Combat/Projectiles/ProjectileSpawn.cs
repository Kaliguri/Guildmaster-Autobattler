using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Параметры создания снаряда. Передаётся в <see cref="ICombatContext.SpawnProjectile"/>.
    /// </summary>
    public readonly struct ProjectileSpawn
    {
        public readonly RuntimeUnit Source;
        public readonly Vector2     StartPosition;
        public readonly RuntimeUnit TargetUnit;
        public readonly float       Speed;
        public readonly float       CollisionRadius;
        public readonly float       RawDamage;

        /// <summary>
        /// Тип урона снаряда — тот же, что у источника, выпустившего его. Не читается у хил-снаряда
        /// (<see cref="IsHeal"/>): лечение типа урона не имеет.
        /// </summary>
        public readonly DamageType  DamageType;
        public readonly float       ArmorK;
        public readonly int         MaxPierces;

        /// <summary>Хил-снаряд: лечит цель при попадании (<see cref="RawDamage"/> = сырое лечение), а не бьёт. §9.2.</summary>
        public readonly bool        IsHeal;

        /// <summary>On-hit эффекты (§9.1): накладываются на каждую задетую цель при попадании урон-снаряда (Криомант — «Заморозка»). null = нет.</summary>
        public readonly EffectData[] OnHitEffects;

        /// <summary>Снаряд-автоатака (для тега <see cref="DamageRequest.IsAutoAttack"/> при попадании). Хил-снаряды/будущие снаряды-способности = false.</summary>
        public readonly bool         IsAutoAttack;

        public ProjectileSpawn(
            RuntimeUnit  source,
            Vector2      startPosition,
            RuntimeUnit  targetUnit,
            float        speed,
            float        collisionRadius,
            float        rawDamage,
            DamageType   damageType,
            float        armorK,
            int          maxPierces   = 0,
            bool         isHeal       = false,
            EffectData[] onHitEffects = null,
            bool         isAutoAttack = false)
        {
            Source          = source;
            StartPosition   = startPosition;
            TargetUnit      = targetUnit;
            Speed           = speed;
            CollisionRadius = collisionRadius;
            RawDamage       = rawDamage;
            DamageType      = damageType;
            ArmorK          = armorK;
            MaxPierces      = maxPierces;
            IsHeal          = isHeal;
            OnHitEffects    = onHitEffects;
            IsAutoAttack    = isAutoAttack;
        }
    }
}
