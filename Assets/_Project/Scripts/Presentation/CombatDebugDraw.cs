using Guildmaster.Combat;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Debug-слой боя на базе Shapes: spatial hash сетка, радиусы атак, снаряды.
    /// Тогглится командой <c>gm_toggle_debug_draw</c> (DevTools).
    /// (вики «10» §7).
    /// </summary>
    public sealed class CombatDebugDraw : MonoBehaviour
    {
        [SerializeField] private bool _enabled = true;
        [SerializeField] private Color _gridColor       = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        [SerializeField] private Color _attackRangeColor = new Color(0.9f, 0.7f, 0.1f, 0.6f);
        [SerializeField] private Color _unitTeamAColor   = new Color(0.2f, 0.5f, 1f, 0.8f);
        [SerializeField] private Color _unitTeamBColor   = new Color(1f, 0.3f, 0.2f, 0.8f);

        private CombatSimulation _simulation;
        private SpatialHash      _spatialHash;

        [Inject]
        public void Construct(CombatSimulation simulation, SpatialHash spatialHash)
        {
            _simulation  = simulation;
            _spatialHash = spatialHash;
        }

        public bool IsEnabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        private void OnDrawGizmos()
        {
            if (!_enabled || _simulation == null) return;

            DrawGrid();
            DrawUnits();
        }

        private void DrawGrid()
        {
            if (_spatialHash == null) return;
            float cellSize = _spatialHash.CellSize;

            for (float x = -20f; x <= 20f; x += cellSize)
            {
                Gizmos.color = _gridColor;
                Gizmos.DrawLine(new Vector3(x, -20f, 0f), new Vector3(x, 20f, 0f));
            }

            for (float y = -20f; y <= 20f; y += cellSize)
            {
                Gizmos.color = _gridColor;
                Gizmos.DrawLine(new Vector3(-20f, y, 0f), new Vector3(20f, y, 0f));
            }
        }

        private void DrawUnits()
        {
            if (_simulation == null) return;
            var units = _simulation.Units;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead) continue;

                float range = unit.Stats.Get(Data.Stats.StatType.AttackRange);
                Gizmos.color = _attackRangeColor;
                Gizmos.DrawWireSphere(new Vector3(unit.Position.x, unit.Position.y, 0f), range);
                Gizmos.color = unit.Team == 0 ? _unitTeamAColor : _unitTeamBColor;
                Gizmos.DrawSphere(new Vector3(unit.Position.x, unit.Position.y, 0f), 0.15f);
            }
        }
    }
}
