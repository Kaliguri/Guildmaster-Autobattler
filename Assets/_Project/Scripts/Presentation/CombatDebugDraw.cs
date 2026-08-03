using Guildmaster.Combat;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Debug-слой боя на базе Shapes: spatial hash сетка, радиусы атак, снаряды.
    /// Тогглится командой <c>gm_toggle_debug_draw</c> (DevTools).
    /// (вики «10» §7).
    /// <para><b>Источник состояния — по умолчанию ПОКАЗАННЫЙ кадр</b> (<see cref="DevOverlayMode"/>):
    /// круги досягаемости рисуются в мировых координатах, и в координатах живого сима они уехали бы на
    /// окно опережения вперёд юнитов. Режим общий с <see cref="CombatStatusOverlay"/> и подписан прямо
    /// в Scene view.</para>
    /// </summary>
    public sealed class CombatDebugDraw : MonoBehaviour
    {
        [SerializeField] private bool _enabled = true;
        [SerializeField] private Color _gridColor       = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        [SerializeField] private Color _attackRangeColor = new Color(0.9f, 0.7f, 0.1f, 0.6f);
        [SerializeField] private Color _unitTeamAColor   = new Color(0.2f, 0.5f, 1f, 0.8f);
        [SerializeField] private Color _unitTeamBColor   = new Color(1f, 0.3f, 0.2f, 0.8f);

        // Всё, что рисует этот слой, принадлежит бою — и приходит вместе с ним. Сам объект живёт в
        // персист-сцене и переживает бои, поэтому инъекцией эти ссылки взять нельзя: она случается
        // один раз, а боёв за сессию много.
        private CombatSimulation _simulation;
        private SpatialHash      _spatialHash;
        private Combat.Tape.BattleTapePlayback _playback;
        private DevOverlayMode   _mode;

        /// <summary>Бой начался: подать его состояние. Вне боя рисовать нечего — гизмо гаснут сами.</summary>
        public void BindBattle(CombatSimulation simulation, SpatialHash spatialHash,
                               Combat.Tape.BattleTapePlayback playback, DevOverlayMode mode)
        {
            _simulation  = simulation;
            _spatialHash = spatialHash;
            _playback    = playback;
            _mode        = mode;
        }

        /// <summary>
        /// Бой ушёл: держать ссылки на его внутренности нельзя. Отвязывает только свой бой — рестарт
        /// закрывает и открывает бой в одном кадре, и умирающий не должен гасить начавшийся.
        /// </summary>
        public void UnbindBattle(CombatSimulation battle = null)
        {
            if (battle != null && !ReferenceEquals(_simulation, battle)) return;

            _simulation  = null;
            _spatialHash = null;
            _playback    = null;
            _mode        = null;
        }

        public bool IsEnabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        private void OnDrawGizmos()
        {
            if (!_enabled) return;

            DrawGrid();
            DrawUnits();
            DrawModeLabel();
        }

        // Подпись режима в Scene view. Handles — редакторный API, поэтому под дефайном; в плеере
        // гизмо не рисуются вовсе, так что терять нечего.
        private void DrawModeLabel()
        {
#if UNITY_EDITOR
            if (_mode == null) return;
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(new Vector3(-20f, 20.5f, 0f), _mode.Describe());
#endif
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
            // Режим показа: кадр ленты — те же юниты и позиции, что на экране.
            if (_mode == null || !_mode.ReadsSimulation)
            {
                if (_playback == null) return;
                if (!_playback.TryGetFrame(
                        out System.Collections.Generic.IReadOnlyList<Combat.Tape.UnitSnapshot> frame))
                    return;

                for (int i = 0; i < frame.Count; i++)
                {
                    Combat.Tape.UnitSnapshot s = frame[i];
                    if (s.IsDead) continue;
                    DrawUnit(s.Position, s.AttackRange, s.Team);
                }
                return;
            }

            // Режим сима: правда модели впереди картинки — для отладки самой ленты.
            if (_simulation == null) return;
            var units = _simulation.Units;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead) continue;
                DrawUnit(unit.Position, unit.Stats.Get(Data.Stats.StatType.AttackRange), unit.Team);
            }
        }

        // Один рисовальщик на оба источника: режим меняет момент, а не то, ЧТО показано.
        private void DrawUnit(Vector2 position, float attackRange, int team)
        {
            var center = new Vector3(position.x, position.y, 0f);
            Gizmos.color = _attackRangeColor;
            Gizmos.DrawWireSphere(center, attackRange);
            Gizmos.color = team == 0 ? _unitTeamAColor : _unitTeamBColor;
            Gizmos.DrawSphere(center, 0.15f);
        }
    }
}
