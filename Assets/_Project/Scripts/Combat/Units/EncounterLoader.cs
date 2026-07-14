using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Один player-юнит для старта боя: кит (реликвия/база) + сосуд + позиция (team 0). Player-сторону
    /// энкаунтер НЕ хранит — её подаёт вызывающий: в dev-срезе (шаг 1) это временный селектор реликвии,
    /// в шаге 3 — ростер <c>BattlePresetData</c>. Так форма <see cref="EncounterData"/> финальна.
    /// </summary>
    public readonly struct PlayerSpawn
    {
        public readonly UnitData   Unit;
        public readonly VesselData Vessel;
        public readonly Vector2    Position;

        public PlayerSpawn(UnitData unit, VesselData vessel, Vector2 position)
        {
            Unit     = unit;
            Vessel   = vessel;
            Position = position;
        }
    }

    /// <summary>
    /// Data-driven строитель боя из <see cref="EncounterData"/>: сбрасывает текущий бой на месте
    /// (<see cref="CombatSimulation.ResetBattle"/> + <see cref="RuntimeUnitFactory.ResetIds"/>) и спавнит
    /// вражескую сторону из энкаунтера (team 1) + переданную player-сторону (team 0). Приходит на смену
    /// заготовке <c>BattleSetupBuilder</c> (вики «13» §3.1). Реюз движка переключения без перезагрузки сцены.
    /// </summary>
    /// <remarks>
    /// Хранит последний загруженный бой для рестарта на месте (<see cref="Reload"/>, dev-R). Сервис живёт
    /// в боевом скоупе — после F5 (релоад сцены) создаётся заново, поэтому «последний бой» не течёт между
    /// боями через статику (в отличие от QC-команд).
    /// </remarks>
    public sealed class EncounterLoader
    {
        private readonly RuntimeUnitFactory _factory;
        private readonly CombatSimulation   _simulation;
        private readonly IContentDatabase   _content;

        // Последний загруженный бой — для рестарта на месте (dev-R). Player-сторона копируется в свой список.
        private EncounterData     _lastEncounter;
        private List<PlayerSpawn> _lastPlayerSide;

        public EncounterLoader(RuntimeUnitFactory factory, CombatSimulation simulation, IContentDatabase content)
        {
            _factory    = factory;
            _simulation = simulation;
            _content    = content;
        }

        /// <summary>Есть ли что перезапустить по <see cref="Reload"/>.</summary>
        public bool HasLast => _lastEncounter != null;

        /// <summary>Content id последнего загруженного энкаунтера (для UI-подсветки), или null.</summary>
        public string LastEncounterId => _lastEncounter != null ? _lastEncounter.Id : null;

        /// <summary>
        /// Загрузить бой: прерывает текущий, спавнит врагов энкаунтера (team 1) + player-сторону (team 0),
        /// запоминает как «последний» для <see cref="Reload"/>.
        /// </summary>
        /// <param name="encounter">Состав врагов. null — no-op с предупреждением.</param>
        /// <param name="playerSide">Player-юниты (team 0). null/пусто — бой без союзников (превью врагов).</param>
        public void Load(EncounterData encounter, IReadOnlyList<PlayerSpawn> playerSide = null)
        {
            if (encounter == null)
            {
                Debug.LogWarning("[EncounterLoader] - Load: encounter == null");
                return;
            }

            _lastEncounter  = encounter;
            _lastPlayerSide = CopyPlayerSide(playerSide);

            Build(encounter, _lastPlayerSide);
        }

        /// <summary>Перезапустить последний загруженный бой на месте (dev-R). No-op, если ничего не грузили.</summary>
        public void Reload()
        {
            if (_lastEncounter == null)
            {
                Debug.LogWarning("[EncounterLoader] - Reload: последний бой не задан (сначала загрузи энкаунтер)");
                return;
            }
            Build(_lastEncounter, _lastPlayerSide);
        }

        private void Build(EncounterData encounter, List<PlayerSpawn> playerSide)
        {
            // Сброс текущего боя на месте: OnBattleReset снимет виды/цифры и slowmo/тряску (как в QC-пути).
            _simulation.ResetBattle();
            _factory.ResetIds();

            // Player-сторона (team 0) — сначала, чтобы Id союзников шли раньше врагов (стабильнее для отладки).
            if (playerSide != null)
            {
                for (int i = 0; i < playerSide.Count; i++)
                {
                    PlayerSpawn p = playerSide[i];
                    if (p.Unit == null) continue;
                    _simulation.EnqueueUnitSpawn(_factory.Create(p.Unit, p.Vessel, team: 0, p.Position));
                }
            }

            // Вражеская сторона (team 1) — из энкаунтера, по строковым id через реестр.
            SpawnEnemies(encounter);
        }

        private void SpawnEnemies(EncounterData encounter)
        {
            IReadOnlyList<EncounterUnit> units = encounter.Units;
            if (units == null) return;

            for (int i = 0; i < units.Count; i++)
            {
                EncounterUnit u = units[i];

                if (string.IsNullOrEmpty(u.EnemyId) || !_content.TryGet(u.EnemyId, out EnemyData enemy))
                {
                    Debug.LogWarning($"[EncounterLoader] - энкаунтер '{encounter.Id}': враг '{u.EnemyId}' " +
                                     "не найден в контент-БД (пропущен). Синхронизирован ли ContentDatabase?");
                    continue;
                }

                int count = u.Count;
                for (int c = 0; c < count; c++)
                {
                    // Count>1 → вертикальный кластер вокруг якоря (как компактные ряды в хардкод-боях).
                    float dy = (c - (count - 1) * 0.5f) * u.Spacing;
                    var pos = new Vector2(u.Position.x, u.Position.y + dy);
                    _simulation.EnqueueUnitSpawn(_factory.Create(enemy, null, team: 1, pos));
                }
            }
        }

        private static List<PlayerSpawn> CopyPlayerSide(IReadOnlyList<PlayerSpawn> src)
        {
            if (src == null || src.Count == 0) return null;
            var copy = new List<PlayerSpawn>(src.Count);
            for (int i = 0; i < src.Count; i++) copy.Add(src[i]);
            return copy;
        }
    }
}
