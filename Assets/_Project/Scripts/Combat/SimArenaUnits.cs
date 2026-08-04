using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Арена глазами хозяина сеанса: правду о том, кто где стоит, держит живая симуляция.
    /// </summary>
    /// <remarks>
    /// <b>Почему не общий для обоих кадр ленты.</b> У хозяина перетаскивание правит симуляцию в тот
    /// же кадр, а кадр покоя уходит в ленту десять раз в секунду — читая его, круг-опора отставал бы
    /// от собственной мыши на сотню миллисекунд. Правду держит тот, кто её меняет; для гостя это
    /// лента, и у него своя реализация шва.
    /// <para><b>Список пересобирается на каждое обращение</b> в свой же буфер. Бойцов на арене
    /// единицы, а любой кэш здесь пришлось бы инвалидировать спавном, смертью и пересборкой превью —
    /// то есть заводить второго владельца факта ради экономии, которой не видно.</para>
    /// </remarks>
    public sealed class SimArenaUnits : IArenaUnits
    {
        private readonly CombatSimulation _sim;
        private readonly List<ArenaUnit>  _buffer = new List<ArenaUnit>(16);

        public SimArenaUnits(CombatSimulation sim) => _sim = sim;

        public IReadOnlyList<ArenaUnit> Units
        {
            get
            {
                _buffer.Clear();
                IReadOnlyList<RuntimeUnit> units = _sim.Units;
                for (int i = 0; i < units.Count; i++) _buffer.Add(Project(units[i]));
                return _buffer;
            }
        }

        public bool TryGet(int id, out ArenaUnit unit)
        {
            IReadOnlyList<RuntimeUnit> units = _sim.Units;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].Id != id) continue;
                unit = Project(units[i]);
                return true;
            }

            unit = default;
            return false;
        }

        private static ArenaUnit Project(RuntimeUnit u) =>
            new ArenaUnit(u.Id, u.Team, u.Position, u.Stats.Get(StatType.Size), u.IsDead);
    }
}
