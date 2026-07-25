using System.Collections.Generic;
using System.Globalization;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Реализация шва <see cref="IUnitStatPreview"/>: считает статы кита ТЕМ ЖЕ путём, что и бой
    /// (контракт «таблица не врёт»), и отдаёт их UI готовыми строками.
    /// <para>
    /// Порядок сборки повторяет <see cref="RuntimeUnitFactory.Create"/>: дефолты
    /// <see cref="StatsConfig"/> → классовая база (<see cref="ClassBaseline"/>) → видовые скейлы
    /// врага (<see cref="EnemyScalers"/>) → стат-блок персоны. Совпадение — по построению: обе
    /// стороны зовут один и тот же <see cref="Stats"/>, формула нигде не переписана.
    /// </para>
    /// <para>
    /// ЧЕГО ЗДЕСЬ НЕТ (осознанно): пассивок из <c>GrantedEffects</c> и перков сосуда — они
    /// накладываются через EffectSystem внутри боя, и поднимать боевой контекст ради панели
    /// инвентаря дороже, чем стоит. У кита с пассивным «+X к HP» число в панели будет ниже
    /// боевого. Это тот же охват, что показывает таблица Content Hub.
    /// </para>
    /// </summary>
    public sealed class UnitStatPreview : IUnitStatPreview
    {
        private readonly StatsConfig _config;
        private readonly ClassBalanceConfig _classBalance;

        public UnitStatPreview(StatsConfig config, ClassBalanceConfig classBalance)
        {
            _config       = config;
            _classBalance = classBalance;
        }

        /// <summary>
        /// Базовая семёрка «быстрого чтения» (реш. Макса 2026-07-25), сгруппирована по смыслу:
        /// выживаемость (HP / брони) → атака (урон / скорость / дальность) → подвижность.
        /// DPS сюда НЕ входит: он выводится из урона и скорости, что стоят рядом, и врёт у китов,
        /// чей основной урон идёт со способностей, а не с автоатаки. Size убран после play-QA:
        /// у всех китов 1.0, клетка кормила глаз нулевой информацией.
        /// </summary>
        public IReadOnlyList<UnitStatLine> Basic(UnitData data)
        {
            var lines = new List<UnitStatLine>(7);
            if (data == null) return lines;

            Stats stats = Build(data);

            lines.Add(Line("ui.stat.hp",     "HP",      Num(stats.Get(StatType.MaxHP))));
            lines.Add(Line("ui.stat.parmor", "Ф.броня", Num(stats.Get(StatType.PhysArmor))));
            lines.Add(Line("ui.stat.marmor", "М.броня", Num(stats.Get(StatType.MagicArmor))));
            lines.Add(Line("ui.stat.dmg",    "Урон",    Num(stats.Get(StatType.AutoAttackDamage))));
            // Атак/сек — с тиковой квантизацией сима (как считает бой), а не сырой AttackSpeed.
            lines.Add(Line("ui.stat.aspd",   "Ск.атк",  Num(AttacksPerSecond(stats.Get(StatType.AttackSpeed)))));
            lines.Add(Line("ui.stat.range",  "Дальн",   Num(stats.Get(StatType.AttackRange))));
            lines.Add(Line("ui.stat.move",   "Скор",    Num(stats.Get(StatType.MoveSpeed))));
            return lines;
        }

        private Stats Build(UnitData data)
        {
            var stats = new Stats(_config);
            ClassBaseline.Apply(stats, data, _classBalance);
            EnemyScalers.Apply(stats, data);
            if (data.Stats != null && data.Stats.Length > 0)
                stats.AddModifiersFrom(data, data.Stats);
            return stats;
        }

        private static float AttacksPerSecond(float attackSpeed)
        {
            int interval = AttackTiming.IntervalTicks(attackSpeed);
            if (interval <= 0 || interval == int.MaxValue) return 0f;
            return (float)Core.Simulation.SimConstants.TickRate / interval;
        }

        private static UnitStatLine Line(string key, string ru, string value) => new UnitStatLine(key, ru, value);

        /// <summary>Целое — без хвоста, дробное — с одним знаком (панель узкая, второй знак не читается).</summary>
        private static string Num(float v) => v % 1f == 0f
            ? ((int)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
