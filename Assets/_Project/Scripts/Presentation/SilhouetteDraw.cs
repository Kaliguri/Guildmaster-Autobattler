using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Раскладка <see cref="UnitSilhouette"/> по набору спрайт-рендереров — ОДНА на всех, кто рисует копию
    /// тела: drag-призрак расстановки (<see cref="DeploymentView"/>) и боевые призрачные копии
    /// (<see cref="Effects.GhostImage"/>).
    /// </summary>
    /// <remarks>
    /// Владелец здесь один намеренно. Поза части приходит матрицей относительно ног, и в ней сидят две
    /// ловушки: зеркало отражённой команды (теряется, если раскладывать матрицу через <c>lossyScale</c>) и
    /// ВНУТРЕННИЙ порядок частей (потеряешь — рука уедет за спину). Обе уже решены один раз, и вторая копия
    /// этого кода разошлась бы с первой молча: призрак остался бы нарисованным, просто неправильно.
    /// </remarks>
    public static class SilhouetteDraw
    {
        /// <summary>
        /// Разложить силуэт по рендерерам <paramref name="parts"/>, дополняя список фабрикой
        /// <paramref name="make"/> и гася лишние. Позы — локальные, поэтому список живёт под общим корнем,
        /// который и ставится в точку ног.
        /// </summary>
        /// <param name="tint">Цвет всех частей копии (альфа несёт прозрачность).</param>
        /// <param name="baseOrder">Порядок отрисовки самой нижней части; выше неё части идут вверх.</param>
        public static void Apply(List<SpriteRenderer> parts, in UnitSilhouette silhouette,
                                 Color tint, int baseOrder, System.Func<SpriteRenderer> make)
        {
            if (parts == null || !silhouette.Valid) return;

            SilhouettePart[] src = silhouette.Parts;
            while (parts.Count < src.Length && make != null) parts.Add(make());

            for (int i = 0; i < parts.Count; i++)
            {
                SpriteRenderer sr = parts[i];
                if (sr == null) continue;

                if (i >= src.Length)
                {
                    if (sr.gameObject.activeSelf) sr.gameObject.SetActive(false);
                    continue;
                }

                SilhouettePart part = src[i];
                if (!sr.gameObject.activeSelf) sr.gameObject.SetActive(true);
                sr.sprite = part.Sprite;
                sr.flipX  = part.FlipX;
                sr.color  = tint;

                // Поза части приходит матрицей относительно ног: в ней и поворот от клипа, и зеркало
                // отражённого тела. Раскладываем её в локальный трансформ — копия живёт под общим корнем.
                part.Decompose(out Vector3 pos, out Quaternion rot, out Vector3 scale);
                sr.transform.localPosition = pos;
                sr.transform.localRotation = rot;
                sr.transform.localScale    = scale;

                // Внутренний порядок частей копия обязана сохранять, иначе рука уезжает за спину.
                sr.sortingOrder = baseOrder + (src.Length - 1 - part.Order);
            }
        }
    }
}
