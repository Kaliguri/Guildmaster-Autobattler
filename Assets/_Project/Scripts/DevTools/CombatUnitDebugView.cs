using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Data.Stats;
using Sirenix.OdinInspector;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Инспектор состояния всех юнитов ИДУЩЕГО боя в реальном времени.
    /// </summary>
    /// <remarks>
    /// Симуляцию не запоминает: с 02.08.2026 бой рождается и умирает вместе со своим скоупом, а этот
    /// объект живёт в персист-сцене — запомненная ссылка показывала бы юнитов боя, который давно
    /// кончился. Спрашиваем текущий бой у хоста при каждом чтении; боя нет — таблица пуста, и это
    /// честный ответ, а не поломка.
    /// </remarks>
    public sealed class CombatUnitDebugView : MonoBehaviour
    {
        private Guildmaster.Game.Flow.BattleHost _host;

        private CombatSimulation _simulation => _host != null ? _host.Resolve<CombatSimulation>() : null;

        // Мир поднимается раньше сцены с дев-панелями, поэтому к Awake он уже построен. Нет мира —
        // панель просто ничего не показывает (standalone-сцена без бута).
        private void Awake()
        {
            var world = LifetimeScope.Find<Guildmaster.Game.WorldLifetimeScope>();
            if (world != null && world.Container != null) _host = world.Container.Resolve<Guildmaster.Game.Flow.BattleHost>();
        }

        // ── Состояние симуляции ──────────────────────────────────────────────

        [ShowInInspector, ReadOnly, BoxGroup("Simulation")]
        private int Tick => _simulation?.CurrentTick ?? -1;

        [ShowInInspector, ReadOnly, BoxGroup("Simulation")]
        private string Outcome => _simulation?.Outcome.ToString() ?? "—";

        [ShowInInspector, ReadOnly, BoxGroup("Simulation")]
        private bool IsPaused => _simulation?.IsPaused ?? false;

        [ShowInInspector, ReadOnly, BoxGroup("Simulation")]
        private int UnitCount => _simulation?.Units.Count ?? 0;

        // ── Таблица юнитов ───────────────────────────────────────────────────

        [ShowInInspector]
        [TableList(IsReadOnly = true, AlwaysExpanded = true, NumberOfItemsPerPage = 20)]
        [LabelText("Units")]
        private List<UnitRow> Units
        {
            get
            {
                var rows = new List<UnitRow>();
                if (_simulation == null) return rows;
                var units = _simulation.Units;
                for (int i = 0; i < units.Count; i++)
                    rows.Add(new UnitRow(units[i]));
                return rows;
            }
        }

        // ────────────────────────────────────────────────────────────────────

        [System.Serializable]
        private sealed class UnitRow
        {
            [TableColumnWidth(30,  Resizable = false)] public int    Id;
            [TableColumnWidth(40,  Resizable = false)] public int    Team;
            [TableColumnWidth(55,  Resizable = false)] public float  HP;
            [TableColumnWidth(55,  Resizable = false)] public float  MaxHP;
            [TableColumnWidth(55,  Resizable = false)] public float  Shield;
            [TableColumnWidth(45,  Resizable = false)] public bool   Dead;
            [TableColumnWidth(55,  Resizable = false)] public bool   CanMove;
            [TableColumnWidth(50,  Resizable = false)] public bool   CanAct;
            [TableColumnWidth(50,  Resizable = false)] public bool   CanCast;
            [TableColumnWidth(80,  Resizable = false)] public string Pos;
            [TableColumnWidth(55,  Resizable = false)] public string Target;
            [TableColumnWidth(55,  Resizable = false)] public int    AtkCD;
            [TableColumnWidth(70,  Resizable = false)] public string Windup;
            [TableColumnWidth(55,  Resizable = false)] public float  MoveSpd;
            [TableColumnWidth(55,  Resizable = false)] public float  AtkRange;
            [TableColumnWidth(55,  Resizable = false)] public float  AtkDmg;
            [TableColumnWidth(45,  Resizable = false)] public int    Effects;

            public UnitRow(RuntimeUnit u)
            {
                Id       = u.Id;
                Team     = u.Team;
                HP       = u.CurrentHP;
                MaxHP    = u.Stats.Get(StatType.MaxHP);
                Shield   = u.CurrentShield;
                Dead     = u.IsDead;
                CanMove  = u.CanMove;
                CanAct   = u.CanAct;
                CanCast  = u.CanCast;
                Pos      = $"({u.Position.x:F1},{u.Position.y:F1})";
                Target   = u.CurrentTarget != null ? $"#{u.CurrentTarget.Id}" : "—";
                AtkCD    = u.AttackCooldownTicks;
                Windup   = u.IsWindingUp ? $"{u.WindupRemaining}/{u.WindupTicks}" : "—";
                MoveSpd  = u.Stats.Get(StatType.MoveSpeed);
                AtkRange = u.Stats.Get(StatType.AttackRange);
                AtkDmg   = u.Stats.Get(StatType.AutoAttackDamage);
                Effects  = u.ActiveEffects.Count;
            }
        }
    }
}
