using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Параметры принудительного смещения цели (§9.9, «Шквальный толчок»). Передаётся в
    /// <see cref="ICombatContext.Displace"/>. Дистанция фиксирована (не «до столкновения»); на время
    /// полёта цель оглушена (жёсткое состояние, сопротивление контролю не применяется). При
    /// <see cref="Cannonball"/> летящая цель бьёт врагов источника на пройденной линии — «ядро».
    /// </summary>
    public readonly struct DisplaceRequest
    {
        // Вида смещения (Knockback/Pull/Teleport) здесь нет: система смещения его не читала ни разу,
        // то есть все три «вида» вели себя одинаково — отбрасыванием (аудит 2026-07-26, волна 2).
        public readonly RuntimeUnit  Target;
        public readonly RuntimeUnit  Source;
        public readonly Vector2      Direction;
        public readonly float        Distance;
        public readonly int          Ticks;
        public readonly bool         Cannonball;
        public readonly float        Damage;
        public readonly DamageSchool School;
        public readonly DamageAffinity Affinity;
        public readonly float        Width;

        /// <summary>
        /// Слабое «цепное» отбрасывание врагов, задетых «ядром» на линии полёта (§10.6): &gt;0 — каждый задетый
        /// не только получает урон, но и сам чуть отбрасывается (что тоже триггерит «Вихревой заход» монаха).
        /// Держим слабым, чтобы цепные полёты кончались раньше главного и финальный телепорт сел на исходную цель.
        /// 0 = без цепи (обычное «ядро» — только урон).
        /// </summary>
        public readonly float        ChainDistance;
        public readonly int          ChainTicks;

        public DisplaceRequest(
            RuntimeUnit target,
            RuntimeUnit source,
            Vector2     direction,
            float       distance,
            int         ticks,
            bool        cannonball,
            float       damage,
            DamageSchool school,
            float       width,
            float       chainDistance = 0f,
            int         chainTicks = 0,
            DamageAffinity affinity = DamageAffinity.None)
        {
            Target        = target;
            Source        = source;
            Direction     = direction;
            Distance      = distance;
            Ticks         = ticks;
            Cannonball    = cannonball;
            Damage        = damage;
            School        = school;
            Affinity      = affinity;
            Width         = width;
            ChainDistance = chainDistance;
            ChainTicks    = chainTicks;
        }
    }
}
