using UnityEngine;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Снаряд на конец одного тика. Нужен по той же причине, что и снимок юнита: летящий по живому симу
    /// снаряд стартовал бы на окно опережения раньше выстрела и прилетал бы задолго до цифры урона.
    /// <para>Тип снаряда, цвет и след здесь не лежат — они выводятся из ИСТОЧНИКА
    /// (<see cref="SourceId"/>), чьё определение показ и так держит.</para>
    /// </summary>
    public readonly struct ProjectileSnapshot
    {
        public readonly int     Id;
        public readonly int     SourceId;
        public readonly Vector2 Position;
        public readonly Vector2 PreviousPosition;
        public readonly Vector2 Velocity;

        /// <summary>Цель, если снаряд её ведёт, иначе <c>-1</c> — по ней показ помнит направление удара.</summary>
        public readonly int TargetId;

        /// <summary>Лечащий снаряд (Пастырь) — показ красит его иначе.</summary>
        public readonly bool IsHeal;

        public ProjectileSnapshot(
            int id, int sourceId, Vector2 position, Vector2 previousPosition, Vector2 velocity,
            int targetId, bool isHeal)
        {
            Id               = id;
            SourceId         = sourceId;
            Position         = position;
            PreviousPosition = previousPosition;
            Velocity         = velocity;
            TargetId         = targetId;
            IsHeal           = isHeal;
        }

        public static ProjectileSnapshot From(Projectile projectile) => new ProjectileSnapshot(
            projectile.Id,
            projectile.Source != null ? projectile.Source.Id : -1,
            projectile.Position,
            projectile.PreviousPosition,
            projectile.Velocity,
            projectile.TargetUnit != null ? projectile.TargetUnit.Id : -1,
            projectile.IsHeal);
    }
}
