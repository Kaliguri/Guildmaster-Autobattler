using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Одна часть силуэта: спрайт и его поза ОТНОСИТЕЛЬНО ТОЧКИ НОГ юнита. У покадрового юнита часть
    /// ровно одна, у скелетного — все части тела, поэтому поза хранится матрицей: части повёрнуты и
    /// отзеркалены масштабом корня, и «офсетом плюс масштабом» это не описывается.
    /// </summary>
    public readonly struct SilhouettePart
    {
        public readonly Sprite    Sprite;
        /// <summary>Поза части в пространстве, где начало координат — ноги юнита.</summary>
        public readonly Matrix4x4 Local;
        /// <summary>Отражение спрайта (покадровый путь; у скелета отражение уже внутри <see cref="Local"/>).</summary>
        public readonly bool      FlipX;
        /// <summary>Порядок отрисовки внутри тела — призрак обязан сохранять внутренний порядок частей.</summary>
        public readonly int       Order;

        public SilhouettePart(Sprite sprite, Matrix4x4 local, bool flipX, int order)
        {
            Sprite = sprite; Local = local; FlipX = flipX; Order = order;
        }

        /// <summary>
        /// Разложить позу в трансформ (позиция / поворот вокруг Z / масштаб со знаком). Считается вручную, а
        /// НЕ через <c>Matrix4x4.rotation</c> и <c>lossyScale</c>: последний возвращает длины осей, то есть
        /// всегда положительные числа, и отражённое тело (вся команда врагов) потеряло бы зеркало —
        /// призрак смотрел бы в другую сторону, чем сам юнит. Знак берётся из определителя.
        /// </summary>
        public void Decompose(out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            Vector4 c0 = Local.GetColumn(0);
            Vector4 c1 = Local.GetColumn(1);
            position = Local.GetColumn(3);

            float sx  = new Vector2(c0.x, c0.y).magnitude;
            float det = c0.x * c1.y - c0.y * c1.x;   // <0 = поза зеркальная
            float sy  = sx > 1e-6f ? det / sx : new Vector2(c1.x, c1.y).magnitude;

            rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(c0.y, c0.x) * Mathf.Rad2Deg);
            scale    = new Vector3(sx, sy, 1f);
        }
    }

    /// <summary>
    /// Единый силуэт юнита для drag-призрака расстановки (QA #5/#9) — ОДИН источник того, «что в руке»:
    /// и при перетаскивании живого юнита по полю (из его вида), и при перетаскивании мементо из инвентаря
    /// (из её боевого <see cref="Data.Definitions.UnitData.ViewPrefab"/>, юнита на поле ещё нет). Отрисовка —
    /// одна (<see cref="DeploymentView.SetGhost"/>); меняешь вид призрака здесь → меняется во всех местах.
    /// </summary>
    public readonly struct UnitSilhouette
    {
        /// <summary>Части тела в порядке отрисовки. Пусто — силуэта нет.</summary>
        public readonly SilhouettePart[] Parts;
        public readonly bool             Valid;

        public UnitSilhouette(SilhouettePart[] parts)
        {
            Parts = parts;
            Valid = parts != null && parts.Length > 0;
        }

        public static readonly UnitSilhouette None = default;

        /// <summary>Силуэт из живого вида юнита (перетаскивание юнита по полю). feet — мировая точка ног.</summary>
        public static UnitSilhouette FromView(UnitView view, Vector2 feet)
            => view == null ? None : view.CaptureSilhouette(feet);

        /// <summary>
        /// Силуэт из боевого ViewPrefab сущности (перетаскивание мементо — юнита на поле ещё нет). Читается
        /// прямо из ассета, без инстанса: вид сам знает, как устроено его тело (один спрайт или части).
        /// </summary>
        public static UnitSilhouette FromPrefab(GameObject viewPrefab)
        {
            if (viewPrefab == null) return None;

            if (viewPrefab.TryGetComponent(out UnitView view))
            {
                // В ассете позиции локальны корню вида, а корень вида — это и есть точка ног в симе.
                UnitSilhouette sil = view.CaptureSilhouette(viewPrefab.transform.position);
                if (sil.Valid) return sil;
            }

            var sr = viewPrefab.GetComponentInChildren<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return None;
            Vector3 off = sr.transform.position - viewPrefab.transform.position;
            var local = Matrix4x4.TRS(off, sr.transform.rotation, sr.transform.lossyScale);
            // Order — индекс в порядке отрисовки силуэта, а не sortingOrder рендерера: часть здесь одна.
            return new UnitSilhouette(new[] { new SilhouettePart(sr.sprite, local, sr.flipX, 0) });
        }
    }
}
