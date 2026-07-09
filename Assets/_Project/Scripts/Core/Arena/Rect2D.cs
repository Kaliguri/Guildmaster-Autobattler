using UnityEngine;

namespace Guildmaster.Core.Arena
{
    /// <summary>
    /// Осепараллельный прямоугольник (центр + размер) в мировых координатах боя. Единственное
    /// место геометрии прямоугольной формы — <see cref="Contains"/>/<see cref="Clamp"/>: сменить
    /// форму (круг, полигон) можно здесь, не трогая потребителей. Чистые данные, без MonoBehaviour —
    /// строится авторингом/тестами, потребляется симуляцией и расстановкой (вики «15»).
    /// </summary>
    public readonly struct Rect2D
    {
        /// <summary>Центр прямоугольника.</summary>
        public readonly Vector2 Center;

        /// <summary>Полный размер по осям (ширина, высота).</summary>
        public readonly Vector2 Size;

        public Rect2D(Vector2 center, Vector2 size)
        {
            Center = center;
            Size   = size;
        }

        /// <summary>Половина размера по осям (по модулю — отрицательный размер трактуется как положительный).</summary>
        public Vector2 HalfSize => new Vector2(Mathf.Abs(Size.x) * 0.5f, Mathf.Abs(Size.y) * 0.5f);

        /// <summary>Лежит ли точка внутри прямоугольника (границы включительно).</summary>
        public bool Contains(Vector2 p)
        {
            Vector2 h = HalfSize;
            return p.x >= Center.x - h.x && p.x <= Center.x + h.x
                && p.y >= Center.y - h.y && p.y <= Center.y + h.y;
        }

        /// <summary>Ближайшая точка внутри прямоугольника (для точки внутри — она сама).</summary>
        public Vector2 Clamp(Vector2 p)
        {
            Vector2 h = HalfSize;
            return new Vector2(
                Mathf.Clamp(p.x, Center.x - h.x, Center.x + h.x),
                Mathf.Clamp(p.y, Center.y - h.y, Center.y + h.y));
        }
    }
}
