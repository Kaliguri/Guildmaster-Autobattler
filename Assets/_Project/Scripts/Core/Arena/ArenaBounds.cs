using UnityEngine;

namespace Guildmaster.Core.Arena
{
    /// <summary>
    /// Границы боевого поля — «стены», об которые упирается движение (MovementSystem) и
    /// отбрасывание (DisplacementSystem). Обёртка над прямоугольной формой (<see cref="Rect2D"/>):
    /// единственная точка расширения на непрямоугольную арену в будущем (вики «15» §2, §9).
    /// </summary>
    public readonly struct ArenaBounds
    {
        private readonly Rect2D _rect;

        public ArenaBounds(Rect2D rect) => _rect = rect;

        public ArenaBounds(Vector2 center, Vector2 size) : this(new Rect2D(center, size)) { }

        public Rect2D  Rect   => _rect;
        public Vector2 Center => _rect.Center;
        public Vector2 Size   => _rect.Size;

        /// <summary>
        /// Бесконечное поле: движение не клампится. Явный дефолт для контекстов без заданной арены
        /// (headless-тесты движения, бой без арены). ВНИМАНИЕ: это НЕ то же, что
        /// <c>default(ArenaBounds)</c> — у дефолта нулевой размер, и он схлопнул бы всё в начало
        /// координат. Всегда используйте <see cref="Unbounded"/>, а не <c>default</c>.
        /// </summary>
        public static ArenaBounds Unbounded =>
            new ArenaBounds(new Rect2D(Vector2.zero,
                new Vector2(float.PositiveInfinity, float.PositiveInfinity)));

        /// <summary>Лежит ли точка внутри поля.</summary>
        public bool Contains(Vector2 p) => _rect.Contains(p);

        /// <summary>Ближайшая точка внутри поля (клампинг движения/отбрасывания).</summary>
        public Vector2 Clamp(Vector2 p) => _rect.Clamp(p);
    }
}
