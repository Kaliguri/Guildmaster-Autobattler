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

            // ОБЪЯВЛЕННЫЙ размер важнее замера: длина оружия — величина данных, а не картинки. Пока её
            // не объявили, работает замер по мешу (переходный режим, см. UnitHeldItem.DeclaredLength).
            if (part.IsReach && renderer.transform.parent != null
                && renderer.transform.parent.TryGetComponent(out UnitHeldItem held)
                && held.TryGetDeclaredTip(out world))
                return true;

            if (!TryMeasureTipFromMesh(renderer, out Vector3 local)) return false;
            world = renderer.transform.TransformPoint(local);
            return true;
        }

        /// <summary>
        /// ЧИСТЫЙ замер кончика по мешу спрайта, в локальных координатах рендерера: дальняя от точки
        /// крепления вершина. Объявленный размер здесь НЕ учитывается — это и есть смысл метода.
        /// </summary>
        /// <returns><c>false</c>, если у спрайта нет вершин.</returns>
        /// <remarks>
        /// Отдельный публичный метод нужен редакторной кнопке «Замерить вылет по мешу»: она обязана
        /// спросить именно картинку. Позови она <see cref="TryGetTip"/>, тот вернул бы уже объявленное
        /// значение, и «замер» переписал бы число им же самим — тождество вместо проверки.
        /// <para>
        /// Меряем по МЕШУ, а не по углам рамки: клинок «сторибука» нарисован по диагонали кадра, и дальний
        /// угол рамки лежит в пустоте за остриём. Меш обтягивает рисунок (28 вершин у клинка), поэтому
        /// дальняя вершина и есть остриё. Спрайт с рамочным мешом (4 вершины) деградирует к углам сам
        /// собой — это те же вершины.
        /// </para>
        /// <para>
        /// Похожий на вид <c>RigProfile.MeasureAxis</c> — НЕ дубль и сливать их нельзя: он меряет от точки
        /// ХВАТА (не от нуля рендерера) и ищет заодно пятку, потому что отвечает на другой вопрос — «куда
        /// смотрит предмет», а не «где его остриё».
        /// </para>
        /// </remarks>
        public static bool TryMeasureTipFromMesh(SpriteRenderer renderer, out Vector3 local)
        {
            local = default;
            if (renderer == null || renderer.sprite == null) return false;

            Vector2[] vertices = renderer.sprite.vertices;
            if (vertices == null || vertices.Length == 0) return false;

            Vector2 best = vertices[0];
            float bestSqr = -1f;
            for (int i = 0; i < vertices.Length; i++)
            {
                // Ноль локальных координат = точка крепления: риг авторится так, что pivot спрайта сидит
                // там, где часть держится за родителя. Значит дальняя от нуля вершина — остриё.
                float sqr = vertices[i].sqrMagnitude;
                if (sqr <= bestSqr) continue;
                bestSqr = sqr;
                best = vertices[i];
            }

            local = best;
            return true;
        }
    }
}
