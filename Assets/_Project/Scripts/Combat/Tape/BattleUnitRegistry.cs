using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Паспорта юнитов боя: id → определение и команда. То, что за бой не меняется, и потому не лежит
    /// в снимках.
    /// <para><b>Зачем отдельный реестр:</b> показ отстаёт от симуляции на окно опережения, поэтому к
    /// моменту, когда до юнита доходит картинка, живого <see cref="RuntimeUnit"/> может уже не быть под
    /// рукой — а спросить «кто это, какой у него арт и палитра» надо. Заполняется по событию спавна:
    /// это регистрация, а не показ, и приходить заранее ей не мешает.</para>
    /// </summary>
    public sealed class BattleUnitRegistry : IDisposable
    {
        /// <summary>Неизменная за бой часть юнита.</summary>
        public readonly struct Entry
        {
            public readonly UnitData Definition;
            public readonly int      Team;
            public readonly int      Id;

            public Entry(UnitData definition, int team, int id)
            {
                Definition = definition;
                Team       = team;
                Id         = id;
            }

            /// <summary>Строковый id контента — ключ звука и локализации. Пусто у болванчиков без данных.</summary>
            public string ContentId => Definition != null ? Definition.Id : null;
        }

        private readonly CombatSimulation      _simulation;
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        public BattleUnitRegistry(CombatSimulation simulation)
        {
            _simulation = simulation;
            _simulation.OnUnitSpawned += Register;
            _simulation.OnBattleReset += Clear;
        }

        public void Dispose()
        {
            _simulation.OnUnitSpawned -= Register;
            _simulation.OnBattleReset -= Clear;
        }

        public bool TryGet(int unitId, out Entry entry) => _entries.TryGetValue(unitId, out entry);

        public UnitData DefinitionOf(int unitId) =>
            _entries.TryGetValue(unitId, out Entry e) ? e.Definition : null;

        private void Register(RuntimeUnit unit) =>
            _entries[unit.Id] = new Entry(unit.Unit, unit.Team, unit.Id);

        private void Clear() => _entries.Clear();
    }
}
