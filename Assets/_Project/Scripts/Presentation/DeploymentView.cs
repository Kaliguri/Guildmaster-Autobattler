using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Shapes;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Рантайм-оверлей фазы расстановки (шаг 4) на Shapes + спрайт-призрак: рамки зон (свои зелёные,
    /// вражьи красные — QA #10), круги-опоры («footprint») под ногами КАЖДОГО юнита (QA #20/#3, всегда видны,
    /// ярче на наведении, ярко+валидность у ног призрака при drag — QA #4) и ghost-призрак — полупрозрачная
    /// копия силуэта юнита, следующая за курсором при drag (QA #9). Компонентный режим Shapes (как
    /// <see cref="CombatAreaFlash"/>): по одному <c>ShapeRenderer</c> на GameObject, слой сортировки
    /// <c>DevOverlay</c>, z=-1 (над спрайтами). Управляется из <c>DeploymentController</c> (Guildmaster.Game).
    /// </summary>
    public sealed class DeploymentView : MonoBehaviour
    {
        private const float OverlayZ      = -1f;
        private const float ZoneThickness = 0.06f;
        private const float RingFlatten   = 0.42f; // сплющивание круга по Y (эллипс «на полу», не круг анфас)

        private static readonly Color ZonePlayerCol      = new Color(0.40f, 0.90f, 0.50f, 0.55f);
        private static readonly Color ZoneExtendedCol    = new Color(0.50f, 0.72f, 1.00f, 0.65f);
        private static readonly Color ZoneEnemyCol       = new Color(0.95f, 0.42f, 0.42f, 0.50f); // QA #10: зона врага
        private static readonly Color GhostValidTint     = new Color(0.55f, 1.00f, 0.65f, 0.55f); // валидный drop (зеленца)
        private static readonly Color GhostInvalidTint   = new Color(1.00f, 0.45f, 0.45f, 0.55f); // reject (краснота)
        private static readonly Color RingNormalCol      = new Color(0.75f, 0.85f, 1.00f, 0.30f); // покой
        private static readonly Color RingHoverCol       = new Color(1.00f, 0.92f, 0.55f, 0.90f); // наведён
        private static readonly Color RingDragValidCol   = new Color(0.45f, 1.00f, 0.55f, 0.95f); // тащу, можно ставить
        private static readonly Color RingDragInvalidCol = new Color(1.00f, 0.40f, 0.40f, 0.95f); // тащу, нельзя

        private const int GhostBaseOrder = 1;   // призрак над кругами-опорами; внутри него — порядок частей тела

        private const float RingNormalThickness = 0.035f;
        private const float RingBoldThickness   = 0.075f;
        // Круг рисуется КРУПНЕЕ сим-радиуса тела (реш. Макса 2026-07-26: «в размер тела» смотрелось странно —
        // фигурка будто не стоит на подставке, а зажата ею). Радиус коллизии от этого не меняется — это визуал.
        private const float RingRadiusScale     = 1.45f;

        /// <summary>Состояние круга-опоры под юнитом: покой / наведён / тащу-можно / тащу-нельзя (QA #20/#4).</summary>
        public enum RingState { Normal, Hover, DragValid, DragInvalid }

        private int _sortingLayerId;
        private readonly List<(Line line, DeploymentZone zone)> _zoneLines = new();
        private readonly List<Disc> _rings = new(); // пул кругов-опор под юнитами (QA #20)
        private readonly List<SpriteRenderer> _ghostParts = new(); // пул частей призрака (у скелета их полтора десятка)
        private Transform _ghostRoot;      // общий родитель частей призрака: гасится/двигается одним куском
        private bool _extendedHighlight;

        /// <summary>Собрать оверлей из данных арены (рамки зон + спрайт-призрак). Зовётся один раз.</summary>
        public void Init(ArenaLayoutData layout)
        {
            _sortingLayerId = ResolveOverlayLayer();

            if (layout?.Zones != null)
                foreach (DeploymentZone zone in layout.Zones)
                    BuildZoneBorder(zone); // QA #10: рисуем обе стороны (свои + вражьи)

            var ghostRoot = new GameObject("Ghost");
            ghostRoot.transform.SetParent(transform, false);
            _ghostRoot = ghostRoot.transform;
            _ghostRoot.gameObject.SetActive(false);

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

        /// <summary>
        /// Ghost-призрак при drag (QA #9): полупрозрачная копия силуэта юнита, поставленная ногами в
        /// <paramref name="feet"/>. Части рисуются со своими позами из силуэта — у составного юнита призрак
        /// повторяет ТЕКУЩУЮ позу тела, а не один кадр торса. Тинт по валидности drop (зелёный/красный).
        /// active=false — прячет призрак.
        /// </summary>
        public void SetGhost(bool active, Vector2 feet, in UnitSilhouette silhouette, bool valid)
        {
            if (_ghostRoot == null) return;

            if (!active || !silhouette.Valid)
            {
                _ghostRoot.gameObject.SetActive(false);
                return;
            }

            _ghostRoot.gameObject.SetActive(true);
            _ghostRoot.position = new Vector3(feet.x, feet.y, OverlayZ - 0.01f);

            SilhouettePart[] parts = silhouette.Parts;
            while (_ghostParts.Count < parts.Length) _ghostParts.Add(MakeGhostPart());

            Color tint = valid ? GhostValidTint : GhostInvalidTint;
            for (int i = 0; i < _ghostParts.Count; i++)
            {
                SpriteRenderer sr = _ghostParts[i];
                if (i >= parts.Length)
                {
                    if (sr.gameObject.activeSelf) sr.gameObject.SetActive(false);
                    continue;
                }

                SilhouettePart part = parts[i];
                sr.gameObject.SetActive(true);
                sr.sprite = part.Sprite;
                sr.flipX  = part.FlipX;
                sr.color  = tint;
                // Поза части приходит матрицей относительно ног: в ней и поворот от клипа, и зеркало
                // отражённого тела. Раскладываем её в локальный трансформ — призрак живёт под общим корнем.
                part.Decompose(out Vector3 pos, out Quaternion rot, out Vector3 scale);
                sr.transform.localPosition = pos;
                sr.transform.localRotation = rot;
                sr.transform.localScale    = scale;
                // Внутренний порядок частей призрак обязан сохранять, иначе рука уезжает за спину.
                sr.sortingOrder = GhostBaseOrder + (parts.Length - 1 - part.Order);
            }
        }

        /// <summary>
        /// Круги-опоры под ногами team-0 юнитов (QA #20/#3/#4): показывают «место» юнита на поле, видны ВСЕГДА
        /// (читаемость). Наведённый — ярче/толще (реакция). Перетаскиваемый передаётся с состоянием DragValid/
        /// DragInvalid и позицией у ног ПРИЗРАКА (следует за курсором) — так видно, кого тащишь и можно ли
        /// поставить. Пул переиспользуется покадрово, лишние круги гасятся.
        /// </summary>
        /// <summary>
        /// Круги-опоры под бойцами. <c>tint</c> перекрывает цвет состояния — им красится ЧУЖОЕ
        /// наведение мейн-цветом того, кто навёл.
        /// </summary>
        /// <remarks>
        /// Цвет приходит снаружи, а не становится пятым состоянием: состояний у кольца четыре и все они
        /// про мою руку («навёл», «тащу», «сюда можно»), а мейн-цвет — про чужого человека и берётся из
        /// состава сеанса. Загони его в перечисление — и цвет игрока пришлось бы дублировать в показе.
        /// </remarks>
        public void SetUnitRings(IReadOnlyList<(Vector2 center, float radius, RingState state, Color? tint)> rings)
        {
            int n = rings?.Count ?? 0;
            while (_rings.Count < n) _rings.Add(MakeRing());
            for (int i = 0; i < _rings.Count; i++)
            {
                if (i < n)
                {
                    (Vector2 center, float radius, RingState state, Color? tint) = rings[i];
                    Disc d = _rings[i];
                    d.gameObject.SetActive(true);
                    d.transform.position   = new Vector3(center.x, center.y, OverlayZ);
                    d.transform.localScale = new Vector3(1f, RingFlatten, 1f); // эллипс «на полу»
                    d.Radius    = Mathf.Max(0.05f, radius * RingRadiusScale);
                    d.Color     = tint ?? RingColor(state);
                    d.Thickness = state == RingState.Normal ? RingNormalThickness : RingBoldThickness;
                }
                else if (_rings[i].gameObject.activeSelf)
                {
                    _rings[i].gameObject.SetActive(false);
                }
            }
        }

        private static Color RingColor(RingState state) => state switch
        {
            RingState.Hover       => RingHoverCol,
            RingState.DragValid   => RingDragValidCol,
            RingState.DragInvalid => RingDragInvalidCol,
            _                     => RingNormalCol,
        };

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

        private SpriteRenderer MakeGhostPart()
        {
            var go = new GameObject("GhostPart");
            go.transform.SetParent(_ghostRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = _sortingLayerId;
            sr.sortingOrder   = GhostBaseOrder;
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
