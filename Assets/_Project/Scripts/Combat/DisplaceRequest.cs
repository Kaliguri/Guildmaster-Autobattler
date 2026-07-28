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
        // Длительности полёта здесь нет: она считается из Distance при фиксированной скорости
        // (SimTuning.DisplaceTicks) — дальний толчок держит цель в оглушении дольше, и у этого
        // свойства ровно один владелец (решение 2026-07-28).
        public readonly RuntimeUnit  Target;
        public readonly RuntimeUnit  Source;
        public readonly Vector2      Direction;
        public readonly float        Distance;
        public readonly bool         Cannonball;
        public readonly float        Damage;
        public readonly DamageSchool School;
        public readonly DamageAffinity Affinity;
        public readonly float        Width;

        /// <summary>
        /// Слабое «цепное» отбрасывание врагов, задетых «ядром» на линии полёта (§10.6): &gt;0 — каждый задетый
        /// не только получает урон, но и сам чуть отбрасывается (что тоже триггерит «Вихревой заход» монаха).
        /// Держим КОРОЧЕ главного толчка: тогда цепные полёты кончаются раньше и финальный телепорт садится на
        /// исходную цель. Длительность цепи считается из этой дистанции, поэтому короче = быстрее, автоматически.
        /// 0 = без цепи (обычное «ядро» — только урон).
        /// </summary>
        public readonly float        ChainDistance;

        public DisplaceRequest(
            RuntimeUnit target,
            RuntimeUnit source,
            Vector2     direction,
            float       distance,
            bool        cannonball,
            float       damage,
            DamageSchool school,
            float       width,
            float       chainDistance = 0f,
            DamageAffinity affinity = DamageAffinity.None)
        {
            Target        = target;
            Source        = source;
            Direction     = direction;
            Distance      = distance;
            Cannonball    = cannonball;
            Damage        = damage;
            School        = school;
            Affinity      = affinity;
            Width         = width;
            ChainDistance = chainDistance;
        }
    }
}
