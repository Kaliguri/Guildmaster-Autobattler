using UnityEngine;

namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// Куда поставить окно тултипа относительно якоря (план §II.10.5 п.1: кламп и флип у краёв).
    /// </summary>
    /// <remarks>
    /// Вынесено отдельной чистой функцией намеренно: это единственная часть тултипов, которую можно
    /// проверить без панели и курсора, а ошибка здесь видна не в коде, а глазами на краю экрана —
    /// то есть находится позже всех. Все величины — в координатах панели.
    /// </remarks>
    public static class TooltipPlacement
    {
        /// <summary>Зазор между якорем и окном, панельные единицы.</summary>
        public const float Gap = 8f;

        /// <summary>
        /// Позиция левого-верхнего угла окна. Предпочтение — справа от якоря по его верхнему краю;
        /// не влезло справа — зеркалим влево; не влезло и там — прижимаем к краю панели.
        /// </summary>
        public static Vector2 Place(Rect anchor, Vector2 size, Rect panel, float gap = Gap)
        {
            float x = anchor.xMax + gap;
            if (x + size.x > panel.xMax)
            {
                float mirrored = anchor.xMin - gap - size.x;
                // Зеркалим только если слева ДЕЙСТВИТЕЛЬНО просторнее: у широкого окна рядом с
                // элементом посреди экрана оба варианта плохи, и прыжок влево ничего не улучшает.
                x = mirrored >= panel.xMin ? mirrored : Mathf.Max(panel.xMin, panel.xMax - size.x);
            }

            float y = anchor.yMin;
            if (y + size.y > panel.yMax) y = panel.yMax - size.y; // не свисать снизу
            if (y < panel.yMin) y = panel.yMin;                   // но и не уезжать выше панели

            return new Vector2(x, y);
        }
    }
}
