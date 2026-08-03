using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Геометрия части тела: где у неё кончик и куда она смотрит. Нужна эффектам удара — форма строится по
    /// двум точкам, и первая из них это кончик оружия в момент начала взмаха.
    /// </summary>
    /// <remarks>
    /// <b>Кончик — вычисление, а не узел рига.</b> Заводить его костью значило бы требовать лишний узел от
    /// каждого предмета, который когда-либо окажется в руке, и ловить его отсутствие в рантайме. Офлайн та же
    /// точка считается так же: <c>RigSweep</c> берёт хват плюс направление предмета на его длину — здесь
    /// длина приходит из самого спрайта, поэтому меч и кинжал не требуют разной настройки.
    /// </remarks>
    public static class UnitPartGeometry
    {
        /// <summary>
        /// Мировая точка кончика части: угол спрайта, самый дальний от точки крепления.
        /// </summary>
        /// <param name="part">Часть тела (обычно предмет в руке).</param>
        /// <param name="world">Мировая позиция кончика.</param>
        /// <returns><c>false</c>, если части нечем рисоваться — тогда кончика у неё нет.</returns>
        /// <remarks>
        /// Точка крепления — локальный ноль рендерера: наш риг авторится так, что pivot спрайта сидит там,
        /// где часть держится за родителя (хват у предмета, сустав у конечности). Значит «дальний угол
        /// спрайта» и есть остриё — у клинка это лезвие, у кисти кулак, и оба ответа верные.
        /// </remarks>
        public static bool TryGetTip(in UnitPart part, out Vector3 world)
        {
            world = default;
            SpriteRenderer renderer = part.Renderer;
            if (renderer == null || renderer.sprite == null) return false;

            Bounds local = renderer.sprite.bounds;   // уже с учётом pivot: ноль = точка крепления
            Vector2 min = local.min;
            Vector2 max = local.max;

            Vector2 best = min;
            float bestSqr = -1f;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i < 2 ? min.x : max.x, (i & 1) == 0 ? min.y : max.y);
                float sqr = corner.sqrMagnitude;
                if (sqr <= bestSqr) continue;
                bestSqr = sqr;
                best = corner;
            }

            world = renderer.transform.TransformPoint(best);
            return true;
        }
    }
}
