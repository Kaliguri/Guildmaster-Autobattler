using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>Вид принудительного смещения (§9.9). MVP реализует <see cref="Knockback"/>.</summary>
    public enum DisplaceKind
    {
        /// <summary>Отбрасывание от источника на фиксированную дистанцию.</summary>
        Knockback = 0,

        /// <summary>Притягивание к источнику (задел, не в MVP).</summary>
        Pull = 1,

        /// <summary>Телепорт в точку (задел, не в MVP).</summary>
        Teleport = 2,
    }

    /// <summary>
    /// Параметры принудительного смещения цели (§9.9, «Шквальный толчок»). Передаётся в
    /// <see cref="ICombatContext.Displace"/>. Дистанция фиксирована (не «до столкновения»); на время
    /// полёта цель оглушена (жёсткое состояние, сопротивление контролю не применяется). При
    /// <see cref="Cannonball"/> летящая цель бьёт врагов источника на пройденной линии — «ядро».
    /// </summary>
    public readonly struct DisplaceRequest
    {
        public readonly DisplaceKind Kind;
        public readonly RuntimeUnit  Target;
        public readonly RuntimeUnit  Source;
        public readonly Vector2      Direction;
        public readonly float        Distance;
        public readonly int          Ticks;
        public readonly bool         Cannonball;
        public readonly float        Damage;
        public readonly DamageType   DamageType;
        public readonly float        Width;

        public DisplaceRequest(
            RuntimeUnit target,
            RuntimeUnit source,
            Vector2     direction,
            float       distance,
            int         ticks,
            bool        cannonball,
            float       damage,
            DamageType  damageType,
            float       width,
            DisplaceKind kind = DisplaceKind.Knockback)
        {
            Kind       = kind;
            Target     = target;
            Source     = source;
            Direction  = direction;
            Distance   = distance;
            Ticks      = ticks;
            Cannonball = cannonball;
            Damage     = damage;
            DamageType = damageType;
            Width      = width;
        }
    }
}
