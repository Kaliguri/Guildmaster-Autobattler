using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Dev-сигнал «зона удара сработала» для презентации (вики «13» шаг 4): линия авто-атаки или
    /// круг активки. Fire-and-forget — НЕ влияет на симуляцию и детерминизм; нужен только оверлею
    /// зон (Shapes ~0.25с) для оценки работы. Не сериализуется.
    /// </summary>
    public readonly struct AreaHit
    {
        public readonly AreaShape Shape;
        public readonly Vector2   Origin;
        public readonly Vector2   Direction; // нормализованное направление линии (для Line)
        public readonly float     Length;    // длина линии
        public readonly float     Width;     // ширина линии
        public readonly float     Radius;    // радиус круга
        public readonly int       Team;      // команда источника (для цвета оверлея)

        private AreaHit(AreaShape shape, Vector2 origin, Vector2 dir, float length, float width, float radius, int team)
        {
            Shape     = shape;
            Origin    = origin;
            Direction = dir;
            Length    = length;
            Width     = width;
            Radius    = radius;
            Team      = team;
        }

        public static AreaHit Line(Vector2 origin, Vector2 direction, float length, float width, int team)
        {
            Vector2 d = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
            return new AreaHit(AreaShape.Line, origin, d, length, width, 0f, team);
        }

        public static AreaHit Circle(Vector2 origin, float radius, int team)
            => new AreaHit(AreaShape.Circle, origin, Vector2.zero, 0f, 0f, radius, team);
    }
}
