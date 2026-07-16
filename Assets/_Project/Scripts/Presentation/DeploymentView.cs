using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Shapes;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Рантайм-оверлей фазы расстановки (шаг 4) на Shapes: рамки player-зон, ghost-превью перетаскивания
    /// (диск, зелёный/красный по валидности) и outline наведённого/выбранного юнита (кольцо). Компонентный
    /// режим Shapes (как <see cref="CombatAreaFlash"/>): по одному <c>ShapeRenderer</c> на GameObject, слой
    /// сортировки <c>DevOverlay</c>, z=-1 (над спрайтами). Создаётся и управляется из <c>DeploymentController</c>
    /// (Guildmaster.Game) — в сцене заранее не нужен.
    /// </summary>
    public sealed class DeploymentView : MonoBehaviour
    {
        private const float OverlayZ      = -1f;
        private const float ZoneThickness = 0.06f;

        private static readonly Color ZoneNormalCol   = new Color(0.40f, 0.90f, 0.50f, 0.55f);
        private static readonly Color ZoneExtendedCol = new Color(0.50f, 0.72f, 1.00f, 0.65f);
        private static readonly Color GhostValidCol   = new Color(0.40f, 0.90f, 0.50f, 0.45f);
        private static readonly Color GhostInvalidCol = new Color(0.95f, 0.35f, 0.35f, 0.50f);
        private static readonly Color OutlineCol      = new Color(1.00f, 0.95f, 0.60f, 0.90f);

        private int _sortingLayerId;
        private readonly List<(Line line, DeploymentTier tier)> _zoneLines = new();
        private Disc _ghost;
        private Disc _outline;
        private bool _extendedHighlight;

        /// <summary>Собрать оверлей из данных арены (рамки player-зон + ghost/outline-диски). Зовётся один раз.</summary>
        public void Init(ArenaLayoutData layout)
        {
            _sortingLayerId = ResolveOverlayLayer();

            if (layout?.Zones != null)
                foreach (DeploymentZone zone in layout.Zones)
                    if (zone.Side == DeploymentSide.Player)
                        BuildZoneBorder(zone);

            _ghost   = MakeDisc(DiscType.Disc, GhostValidCol);
            _outline = MakeDisc(DiscType.Ring, OutlineCol);
            _outline.Thickness = 0.08f;
            _ghost.gameObject.SetActive(false);
            _outline.gameObject.SetActive(false);

            gameObject.SetActive(false);
        }

        /// <summary>Показать/скрыть весь оверлей (вход/выход из фазы расстановки).</summary>
        public void SetActive(bool active) => gameObject.SetActive(active);

        /// <summary>Подсветить Extended-зоны (перетаскивается юнит с правом на Extended).</summary>
        public void SetExtendedHighlight(bool on)
        {
            if (_extendedHighlight == on) return;
            _extendedHighlight = on;
            for (int i = 0; i < _zoneLines.Count; i++)
            {
                var (line, tier) = _zoneLines[i];
                if (tier == DeploymentTier.Extended)
                    line.Color = on ? ZoneExtendedCol : new Color(ZoneExtendedCol.r, ZoneExtendedCol.g, ZoneExtendedCol.b, 0.18f);
            }
        }

        /// <summary>Ghost-превью в точке drop: зелёный (валидно) / красный (нельзя ставить).</summary>
        public void SetGhost(bool active, Vector2 pos, float radius, bool valid)
        {
            if (_ghost == null) return;
            _ghost.gameObject.SetActive(active);
            if (!active) return;
            _ghost.transform.position = new Vector3(pos.x, pos.y, OverlayZ);
            _ghost.Radius = Mathf.Max(0.05f, radius);
            _ghost.Color  = valid ? GhostValidCol : GhostInvalidCol;
        }

        /// <summary>Кольцо-outline вокруг наведённого/выбранного юнита.</summary>
        public void SetOutline(bool active, Vector2 pos, float radius)
        {
            if (_outline == null) return;
            _outline.gameObject.SetActive(active);
            if (!active) return;
            _outline.transform.position = new Vector3(pos.x, pos.y, OverlayZ);
            _outline.Radius = Mathf.Max(0.05f, radius);
        }

        private void BuildZoneBorder(DeploymentZone zone)
        {
            Vector2 c = zone.Area.Center, h = zone.Area.HalfSize;
            Vector2 bl = new Vector2(c.x - h.x, c.y - h.y);
            Vector2 br = new Vector2(c.x + h.x, c.y - h.y);
            Vector2 tl = new Vector2(c.x - h.x, c.y + h.y);
            Vector2 tr = new Vector2(c.x + h.x, c.y + h.y);
            Color col = zone.Tier == DeploymentTier.Extended
                ? new Color(ZoneExtendedCol.r, ZoneExtendedCol.g, ZoneExtendedCol.b, 0.18f)
                : ZoneNormalCol;

            MakeZoneLine(bl, br, col, zone.Tier);
            MakeZoneLine(br, tr, col, zone.Tier);
            MakeZoneLine(tr, tl, col, zone.Tier);
            MakeZoneLine(tl, bl, col, zone.Tier);
        }

        private void MakeZoneLine(Vector2 a, Vector2 b, Color col, DeploymentTier tier)
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
            _zoneLines.Add((line, tier));
        }

        private Disc MakeDisc(DiscType type, Color col)
        {
            var go = new GameObject(type == DiscType.Ring ? "Outline" : "Ghost");
            go.transform.SetParent(transform, false);
            var disc = go.AddComponent<Disc>();
            disc.Geometry       = DiscGeometry.Flat2D;
            disc.Type           = type;
            disc.Radius         = 0.5f;
            disc.Color          = col;
            disc.SortingLayerID = _sortingLayerId;
            return disc;
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
