using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Единый силуэт юнита для drag-призрака расстановки (QA #5/#9) — ОДИН источник того, «что в руке»:
    /// и при перетаскивании живого юнита по полю (из его вида), и при перетаскивании реликвии из инвентаря
    /// (из её боевого <see cref="Data.Definitions.UnitData.ViewPrefab"/>, юнита на поле ещё нет). Отрисовка —
    /// одна (<see cref="DeploymentView.SetGhost"/>); меняешь вид призрака здесь → меняется во всех местах.
    /// </summary>
    public readonly struct UnitSilhouette
    {
        public readonly Sprite  Sprite;
        public readonly bool    FlipX;
        public readonly Vector3 Scale;
        public readonly Vector2 Offset; // смещение спрайта от ног (мир): спрайт рисуется выше точки ног
        public readonly bool    Valid;

        private UnitSilhouette(Sprite sprite, bool flipX, Vector3 scale, Vector2 offset)
        {
            Sprite = sprite; FlipX = flipX; Scale = scale; Offset = offset; Valid = sprite != null;
        }

        public static readonly UnitSilhouette None = default;

        /// <summary>Силуэт из живого вида юнита (перетаскивание юнита по полю). feet — мировая точка ног.</summary>
        public static UnitSilhouette FromView(UnitView view, Vector2 feet)
        {
            if (view == null || view.BodySprite == null) return None;
            Vector3 off = view.BodyWorldPosition - new Vector3(feet.x, feet.y, 0f);
            return new UnitSilhouette(view.BodySprite, view.BodyFlipX, view.BodyLossyScale, new Vector2(off.x, off.y));
        }

        /// <summary>
        /// Силуэт из боевого ViewPrefab сущности (перетаскивание реликвии — юнита на поле ещё нет). Спрайт и
        /// масштаб читаются прямо из префаба (без инстанса); офсет — позиция спрайта относительно корня (ног).
        /// </summary>
        public static UnitSilhouette FromPrefab(GameObject viewPrefab)
        {
            if (viewPrefab == null) return None;
            var sr = viewPrefab.GetComponentInChildren<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return None;
            Vector3 off = sr.transform.position - viewPrefab.transform.position; // в ассете позиции локальны корню
            return new UnitSilhouette(sr.sprite, false, sr.transform.lossyScale, new Vector2(off.x, off.y));
        }
    }
}
