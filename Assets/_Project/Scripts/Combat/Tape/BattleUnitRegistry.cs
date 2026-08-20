using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Паспорта юнитов боя: боевая сторона <see cref="IUnitDirectory"/>. То, что за бой не меняется,
    /// и потому не лежит в снимках. Вне боя ту же роль исполняет держатель тел мира.
    /// <para><b>Зачем отдельный реестр:</b> показ отстаёт от симуляции на окно опережения, поэтому к
    /// моменту, когда до юнита доходит картинка, живого <see cref="RuntimeUnit"/> может уже не быть под
    /// рукой — а спросить «кто это, какой у него арт и палитра» надо. Заполняется по событию спавна:
    /// это регистрация, а не показ, и приходить заранее ей не мешает.</para>
    /// <para><b>Симуляция необязательна.</b> Владелец и гость подают её, чтобы реестр сам заполнялся по
    /// спавну; у кого сима нет вовсе (реплей с диска), состав приходит только через
    /// <see cref="RegisterRemote"/>, и подписываться не на что. Требовать сим там, где его нет, значило
    /// бы держать в реплей-скоупе бесполезную симуляцию ради одной подписки.</para>
    /// </summary>
    public sealed class BattleUnitRegistry : IUnitDirectory, IDisposable
    {
        private readonly CombatSimulation             _simulation;
        private readonly Dictionary<int, UnitIdentity> _entries = new Dictionary<int, UnitIdentity>();

        public BattleUnitRegistry(CombatSimulation simulation)
        {
            _simulation = simulation;
            if (_simulation == null) return;   // реплей: спавнов нет, состав придёт RegisterRemote

            _simulation.OnUnitSpawned += Register;
            _simulation.OnBattleReset += Clear;
        }

        public void Dispose()
        {
            if (_simulation == null) return;

            _simulation.OnUnitSpawned -= Register;
            _simulation.OnBattleReset -= Clear;
        }

        public bool TryGet(int unitId, out UnitIdentity identity) => _entries.TryGetValue(unitId, out identity);

        public int Count => _entries.Count;

        /// <summary>
        /// Записать паспорт, пришедший извне. Нужен гостю в коопе: своей симуляции у него нет, спавнов
        /// он не видит, а показу всё равно надо знать, кто это и какой у него арт. Состав приезжает
        /// отдельным сообщением раньше кадров — в снимках этих полей нет, они за бой не меняются.
        /// </summary>
        public void RegisterRemote(int unitId, UnitData definition, int team) =>
            _entries[unitId] = new UnitIdentity(definition, team, unitId);

        public UnitData DefinitionOf(int unitId) =>
            _entries.TryGetValue(unitId, out UnitIdentity e) ? e.Definition : null;

        private void Register(RuntimeUnit unit) =>
            _entries[unit.Id] = new UnitIdentity(unit.Unit, unit.Team, unit.Id);

        private void Clear() => _entries.Clear();
    }
}
