using Guildmaster.Combat;
using Guildmaster.Combat.Commands;
using Guildmaster.Data.Stats;
using Guildmaster.Presentation;
using QFSW.QC;
using UnityEngine;
using VContainer;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Набор отладочных команд Quantum Console для Фазы 1 (вики «10» §9).
    /// Инъектируется через VContainer — зависимости появляются только при наличии активной симуляции.
    /// </summary>
    public sealed class GuildmasterCommands : MonoBehaviour
    {
        private CombatSimulation   _simulation;
        private CombatDebugDraw    _debugDraw;

        [Inject]
        public void Construct(CombatSimulation simulation, CombatDebugDraw debugDraw)
        {
            _simulation = simulation;
            _debugDraw  = debugDraw;
        }

        /// <summary>Зафиксировать сид боя для детерминизм-отладки (только до старта).</summary>
        [Command("gm_rng_seed", "Зафиксировать сид боя (до старта симуляции)")]
        public void SetRngSeed(ulong seed)
        {
            Debug.Log($"[GuildmasterCommands] - gm_rng_seed {seed}: изменение сида поддерживается только через CombatLifetimeScope до запуска");
        }

        /// <summary>Поднять тест-бой N×M юнитов с заданными HP.</summary>
        [Command("gm_spawn_battle", "Запустить тест-бой N юнитов за каждую сторону")]
        public void SpawnBattle(int countPerTeam = 2, float hp = 300f, float damage = 50f)
        {
            if (_simulation == null) { Debug.LogWarning("[GuildmasterCommands] - Симуляция не активна"); return; }

            for (int i = 0; i < countPerTeam; i++)
            {
                _simulation.EnqueueUnitSpawn(MakeTestUnit(0, new Vector2(-5f + i, i), hp, damage));
                _simulation.EnqueueUnitSpawn(MakeTestUnit(1, new Vector2( 5f - i, i), hp, damage));
            }

            Debug.Log($"[GuildmasterCommands] - gm_spawn_battle: добавлено {countPerTeam}×2 юнитов");
        }

        /// <summary>Выставить HP юниту по ID.</summary>
        [Command("gm_set_hp", "Выставить HP юниту по ID")]
        public void SetHp(int unitId, float hp)
        {
            if (_simulation == null) return;

            for (int i = 0; i < _simulation.Units.Count; i++)
            {
                var unit = _simulation.Units[i];
                if (unit.Id == unitId)
                {
                    unit.CurrentHP = Mathf.Max(0f, hp);
                    Debug.Log($"[GuildmasterCommands] - gm_set_hp: юнит {unitId} HP = {unit.CurrentHP}");
                    return;
                }
            }

            Debug.LogWarning($"[GuildmasterCommands] - gm_set_hp: юнит {unitId} не найден");
        }

        /// <summary>Мгновенно завершить бой (убить всех из команды 1).</summary>
        [Command("gm_skip_battle", "Мгновенно завершить бой в пользу команды A")]
        public void SkipBattle()
        {
            if (_simulation == null) return;

            for (int i = 0; i < _simulation.Units.Count; i++)
            {
                var unit = _simulation.Units[i];
                if (unit.Team == 1) unit.CurrentHP = -1f;
            }

            Debug.Log("[GuildmasterCommands] - gm_skip_battle: все юниты команды B убиты");
        }

        /// <summary>Включить/выключить Shapes debug-слой.</summary>
        [Command("gm_toggle_debug_draw", "Вкл/выкл debug-отрисовку боя")]
        public void ToggleDebugDraw()
        {
            if (_debugDraw == null) { Debug.LogWarning("[GuildmasterCommands] - CombatDebugDraw не найден"); return; }
            _debugDraw.IsEnabled = !_debugDraw.IsEnabled;
            Debug.Log($"[GuildmasterCommands] - gm_toggle_debug_draw: {(_debugDraw.IsEnabled ? "ON" : "OFF")}");
        }

        private static RuntimeUnit MakeTestUnit(int team, Vector2 pos, float hp, float damage)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("test", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, hp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, damage),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 1.5f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 3f),
            });
            return new RuntimeUnit
            {
                Team             = team,
                Stats            = stats,
                CurrentHP        = hp,
                Position         = pos,
                PreviousPosition = pos,
            };
        }
    }
}
