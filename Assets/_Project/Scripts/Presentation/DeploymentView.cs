using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Shapes;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Рантайм-оверлей фазы расстановки (шаг 4) на Shapes + спрайт-призрак: рамки зон (свои зелёные,
    /// вражьи красные — QA #10), тень-эллипс под ногами наведённого/перетаскиваемого юнита (QA #8/#9,
    /// вместо кольца по всей фигуре) и ghost-призрак — полупрозрачная копия силуэта юнита, следующая за
    /// курсором при drag (QA #9, вместо диска). Компонентный режим Shapes (как <see cref="CombatAreaFlash"/>):
    /// по одному <c>ShapeRenderer</c> на GameObject, слой сортировки <c>DevOverlay</c>, z=-1 (над спрайтами).
    /// Создаётся и управляется из <c>DeploymentController</c> (Guildmaster.Game) — в сцене заранее не нужен.
    /// </summary>
    public sealed class DeploymentView : MonoBehaviour
    {
        private const float OverlayZ      = -1f;
        private const float ZoneThickness = 0.06f;
        private const float ShadowFlatten = 0.42f; // сплющивание тени по Y (эллипс у ног, не круг)

        private static readonly Color ZonePlayerCol    = new Color(0.40f, 0.90f, 0.50f, 0.55f);
        private static readonly Color ZoneExtendedCol  = new Color(0.50f, 0.72f, 1.00f, 0.65f);
        private static readonly Color ZoneEnemyCol     = new Color(0.95f, 0.42f, 0.42f, 0.50f); // QA #10: зона врага
        private static readonly Color ShadowCol        = new Color(0.02f, 0.02f, 0.04f, 0.45f); // нейтральная тёмная тень
        private static readonly Color GhostValidTint   = new Color(0.55f, 1.00f, 0.65f, 0.55f); // валидный drop (зеленца)
        private static readonly Color GhostInvalidTint = new Color(1.00f, 0.45f, 0.45f, 0.55f); // reject (краснота)
        private static readonly Color RingNormalCol    = new Color(0.75f, 0.85f, 1.00f, 0.28f); // QA #20: круг-размер (покой)
        private static readonly Color RingHoverCol     = new Color(1.00f, 0.92f, 0.55f, 0.85f); // QA #20: наведён (реакция)

        private const float RingNormalThickness = 0.035f;
        private const float RingHoverThickness  = 0.07f;

        /// <summary>Состояние круга-размера под юнитом (QA #20): покой / наведён.</summary>
        public enum RingState { Normal, Hover }

        private int _sortingLayerId;
        private readonly List<(Line line, DeploymentZone zone)> _zoneLines = new();
        private readonly List<Disc> _rings = new(); // пул кругов-размеров под team-0 юнитами (QA #20)
        private Disc _shadow;              // тень-эллипс под ногами (drop-цель под призраком)
        private SpriteRenderer _ghost;     // призрак-силуэт при drag (копия кадра спрайта юнита)
        private bool _extendedHighlight;

        /// <summary>Собрать оверлей из данных арены (рамки зон + тень + спрайт-призрак). Зовётся один раз.</summary>
        public void Init(ArenaLayoutData layout)
        {
            _sortingLayerId = ResolveOverlayLayer();

            if (layout?.Zones != null)
                foreach (DeploymentZone zone in layout.Zones)
                    BuildZoneBorder(zone); // QA #10: рисуем обе стороны (свои + вражьи)

            _shadow = MakeShadow();
            _ghost  = MakeGhost();
            _shadow.gameObject.SetActive(false);
            _ghost.gameObject.SetActive(false);

            gameObject.SetActive(false);
        }

        /// <summary>Показать/скрыть весь оверлей (вход/выход из фазы расстановки).</summary>
        public void SetActive(bool active) => gameObject.SetActive(active);

        /// <summary>Подсветить свои Extended-зоны (перетаскивается юнит с правом на Extended).</summary>
        public void SetExtendedHighlight(bool on)
        {
            if (_extendedHighlight == on) return;
            _extendedHighlight = on;
            for (int i = 0; i < _zoneLines.Count; i++)
            {
                var (line, zone) = _zoneLines[i];
                if (zone.Side == DeploymentSide.Player && zone.Tier == DeploymentTier.Extended)
                    line.Color = on ? ZoneExtendedCol : DimExtended();
            }
        }

        /// <summary>Тень-эллипс под ногами (наведённый/выбранный/перетаскиваемый юнит). Нейтральная тёмная (QA #8/#9).</summary>
        public void SetShadow(bool active, Vector2 feet, float radius)
        {
            if (_shadow == null) return;
            _shadow.gameObject.SetActive(active);
            if (!active) return;
            _shadow.transform.position   = new Vector3(feet.x, feet.y, OverlayZ);
            _shadow.transform.localScale = new Vector3(1f, ShadowFlatten, 1f); // сплющить в эллипс
            _shadow.Radius = Mathf.Max(0.05f, radius);
        }

        /// <summary>
        /// Ghost-призрак при drag (QA #9): полупрозрачная копия силуэта юнита в целевой точке ног
        /// <paramref name="feet"/> со смещением арта <paramref name="spriteOffset"/> (спрайт рисуется выше ног).
        /// Тинт по валидности drop (зелёный/красный). active=false — прячет призрак.
        /// </summary>
        public void SetGhost(bool active, Vector2 feet, Vector2 spriteOffset, Sprite sprite, bool flipX, Vector3 scale, bool valid)
        {
            if (_ghost == null) return;
            _ghost.gameObject.SetActive(active && sprite != null);
            if (!active || sprite == null) return;
            _ghost.sprite               = sprite;
            _ghost.flipX                = flipX;
            _ghost.color                = valid ? GhostValidTint : GhostInvalidTint;
            _ghost.transform.position   = new Vector3(feet.x + spriteOffset.x, feet.y + spriteOffset.y, OverlayZ - 0.01f);
            _ghost.transform.localScale = scale;
        }

        /// <summary>
        /// Круги-размеры под team-0 юнитами (QA #20): показывают «размер» юнита на поле, видны ВСЕГДА
        /// (читаемость расстановки), наведённый — ярче/толще (реакция на наведение). Пул переиспользуется
        /// покадрово, лишние круги гасятся. Перетаскиваемый юнит в список НЕ кладётся (он «в руке» — ghost).
        /// </summary>
        public void SetUnitRings(IReadOnlyList<(Vector2 center, float radius, RingState state)> rings)
        {
            int n = rings?.Count ?? 0;
            while (_rings.Count < n) _rings.Add(MakeRing());
            for (int i = 0; i < _rings.Count; i++)
            {
                if (i < n)
                {
                    (Vector2 center, float radius, RingState state) = rings[i];
                    Disc d = _rings[i];
                    d.gameObject.SetActive(true);
                    d.transform.position   = new Vector3(center.x, center.y, OverlayZ);
                    d.transform.localScale = new Vector3(1f, ShadowFlatten, 1f); // эллипс «на полу»
                    d.Radius    = Mathf.Max(0.05f, radius);
                    bool hover  = state == RingState.Hover;
                    d.Color     = hover ? RingHoverCol : RingNormalCol;
                    d.Thickness = hover ? RingHoverThickness : RingNormalThickness;
                }
                else if (_rings[i].gameObject.activeSelf)
                {
                    _rings[i].gameObject.SetActive(false);
                }
            }
        }

        private void BuildZoneBorder(DeploymentZone zone)
        {
            Vector2 c = zone.Area.Center, h = zone.Area.HalfSize;
            Vector2 bl = new Vector2(c.x - h.x, c.y - h.y);
            Vector2 br = new Vector2(c.x + h.x, c.y - h.y);
            Vector2 tl = new Vector2(c.x - h.x, c.y + h.y);
            Vector2 tr = new Vector2(c.x + h.x, c.y + h.y);
            Color col = ZoneColor(zone);

            MakeZoneLine(bl, br, col, zone);
            MakeZoneLine(br, tr, col, zone);
            MakeZoneLine(tr, tl, col, zone);
            MakeZoneLine(tl, bl, col, zone);
        }

        // Цвет рамки по стороне/тиру: свои зелёные (Extended голубой, притушен пока не тащим), вражьи красные.
        private Color ZoneColor(DeploymentZone zone)
        {
            if (zone.Side == DeploymentSide.Enemy)
                return zone.Tier == DeploymentTier.Extended
                    ? new Color(ZoneEnemyCol.r, ZoneEnemyCol.g, ZoneEnemyCol.b, 0.22f)
                    : ZoneEnemyCol;
            return zone.Tier == DeploymentTier.Extended ? DimExtended() : ZonePlayerCol;
        }

        private static Color DimExtended() =>
            new Color(ZoneExtendedCol.r, ZoneExtendedCol.g, ZoneExtendedCol.b, 0.18f);

        private void MakeZoneLine(Vector2 a, Vector2 b, Color col, DeploymentZone zone)
        {
            var go = new GameObject("ZoneLine");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<Line>();
            line.Geometry       = LineGeometry.Flat2D;
            line.ThicknessSpace = ThicknessSpace.Meters;
            line.Thickness      = ZoneThickness;
            line.Start          = new Vector3(a.x, a.y, OverlayZ);
            line.End            = new Vector3(b.x, b.y, OverlayZ);
            line.Color          = col;
            line.SortingLayerID = _sortingLayerId;
            _zoneLines.Add((line, zone));
        }

        private Disc MakeShadow()
        {
            var go = new GameObject("Shadow");
            go.transform.SetParent(transform, false);
            var disc = go.AddComponent<Disc>();
            disc.Geometry       = DiscGeometry.Flat2D;
            disc.Type           = DiscType.Disc;
            disc.Radius         = 0.5f;
            disc.Color          = ShadowCol;
            disc.SortingLayerID = _sortingLayerId;
            disc.SortingOrder   = 0; // под призраком
            return disc;
        }

        private Disc MakeRing()
        {
            var go = new GameObject("UnitRing");
            go.transform.SetParent(transform, false);
            var disc = go.AddComponent<Disc>();
            disc.Geometry       = DiscGeometry.Flat2D;
            disc.Type           = DiscType.Ring;
            disc.Radius         = 0.5f;
            disc.Thickness      = RingNormalThickness;
            disc.Color          = RingNormalCol;
            disc.SortingLayerID = _sortingLayerId;
            disc.SortingOrder   = 0;
            return disc;
        }

        private SpriteRenderer MakeGhost()
        {
            var go = new GameObject("Ghost");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = _sortingLayerId;
            sr.sortingOrder   = 1; // над тенью
            return sr;
        }

        // Слой сортировки DevOverlay (над спрайтами); если его нет в проекте — Default (0).
        private static int ResolveOverlayLayer()
        {
            foreach (SortingLayer l in SortingLayer.layers)
                if (l.name == "DevOverlay") return SortingLayer.NameToID("DevOverlay");
            return 0;
        }
    }
}
