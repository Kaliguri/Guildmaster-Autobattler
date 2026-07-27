using System;
using System.Collections.Generic;
using System.Globalization;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Фаза 0 — статический аудитор контента. БЕЗ симуляции: бейкает реальные статы каждого кита
    /// (реюз боевого <see cref="EffectiveStats"/> (полный каскад) и <see cref="AttackTiming"/>, НЕ свои формулы), считает грубые
    /// производные (raw-DPS, EHP) и флагует выбросы (z-score) и подозрительные нули. Ловит грубые ошибки
    /// данных до всякого боя. Производные — приближение: без учёта wind-up, способностей, on-hit эффектов.
    /// </summary>
    public static class ContentAuditor
    {
        private sealed class Row
        {
            public string Type;
            public string Name;
            public float MaxHP;
            public float AutoAtk;
            public float AtkSpeed;
            public float AtkRange;
            public float MoveSpeed;
            public float PhysArmor;
            public float ElemArmor;
            public float DmgTakenEff;
            public float DmgDealtEff;
            public float Lifesteal;
            public float RawDps;
            public float EhpPhys;
            public float EhpElem;
            public string Flags = "";
        }

        public static (string csv, string md) Run()
        {
            StatsConfig config = BalanceAssets.LoadStatsConfig();
            // Классовый слой каскада: без него MaxHP и MoveSpeed в таблице — не те, что в бою.
            ClassBalanceConfig classConfig = BalanceAssets.LoadClassBalanceConfig();
            float armorK = config != null ? config.ArmorConstantK : 100f;

            var rows = new List<Row>();
            foreach (RelicData r in BalanceAssets.LoadRelics()) rows.Add(BuildRow("Relic", r, config, classConfig, armorK));
            foreach (EnemyData e in BalanceAssets.LoadEnemies()) rows.Add(BuildRow("Enemy", e, config, classConfig, armorK));

            FlagOutliers(rows);

            var headers = new List<string>
            {
                "Type", "Name", "MaxHP", "AutoAtk", "Атк/сек", "AtkRange", "MoveSpeed",
                "PhysArmor", "ElemArmor", "DmgTakenEff", "DmgDealtEff", "Lifesteal",
                "RawDPS", "EHP_phys", "EHP_elem", "Flags"
            };
            var table = new List<IReadOnlyList<object>>();
            foreach (Row row in rows)
            {
                table.Add(new object[]
                {
                    row.Type, row.Name, row.MaxHP, row.AutoAtk, row.AtkSpeed, row.AtkRange, row.MoveSpeed,
                    row.PhysArmor, row.ElemArmor, row.DmgTakenEff, row.DmgDealtEff, row.Lifesteal,
                    row.RawDps, row.EhpPhys, row.EhpElem, row.Flags
                });
            }

            string notes =
                "**Что это:** статический аудит контента (без боя). Производные — приближение " +
                "(raw-DPS = AutoAtk × Атк/сек × DmgDealtEff, где Атк/сек — с тиковой квантизацией сима; " +
                "EHP = MaxHP × (K+armor)/K ÷ DmgTakenEff, " +
                $"K={armorK:0.#}). Не учтены wind-up, способности, on-hit эффекты. **Flags** — z-score>2 по " +
                "RawDPS/EHP или подозрительный ноль. Флаг ≠ баг: это повод посмотреть, а не приговор.";

            string csv = ReportWriter.WriteCsv("audit_content", headers, table);
            string md = ReportWriter.WriteMarkdown("audit_content", "SimBench — аудит контента (Фаза 0)", headers, table, notes);
            ReportWriter.WriteJson("audit_content", "SimBench — аудит контента (Фаза 0)", headers, table, notes);
            return (csv, md);
        }

        private static Row BuildRow(string type, UnitData data, StatsConfig config, ClassBalanceConfig classConfig,
            float armorK)
        {
            // ТОТ ЖЕ путь, что у боя и у панели инвентаря: класс → вид → персона (EffectiveStats).
            Stats stats = EffectiveStats.Build(data, config, classConfig);

            float maxHp = stats.Get(StatType.MaxHP);
            float autoAtk = stats.Get(StatType.AutoAttackDamage);
            // Скорострельность — квантизованная тиками, как её производит сим. Прежний кламп по
            // StatsConfig.AttackSpeedMin/Max здесь врал: сим никаких клампов не применяет (T-18).
            float atkSpeed = AttackTiming.AttacksPerSecond(stats.Get(StatType.AttackSpeed));
            float dmgDealt = stats.Get(StatType.DamageDealtEff);
            float dmgTaken = stats.Get(StatType.DamageTakenEff);
            float physArmor = stats.Get(StatType.PhysArmor);
            float elemArmor = stats.Get(StatType.MagicArmor);

            float takenEff = dmgTaken <= 0f ? 1f : dmgTaken;
            var row = new Row
            {
                Type = type,
                Name = data.name,
                MaxHP = maxHp,
                AutoAtk = autoAtk,
                AtkSpeed = atkSpeed,
                AtkRange = stats.Get(StatType.AttackRange),
                MoveSpeed = stats.Get(StatType.MoveSpeed),
                PhysArmor = physArmor,
                ElemArmor = elemArmor,
                DmgTakenEff = dmgTaken,
                DmgDealtEff = dmgDealt,
                Lifesteal = stats.Get(StatType.Lifesteal),
                RawDps = autoAtk * atkSpeed * dmgDealt,
                EhpPhys = maxHp * (armorK + physArmor) / armorK / takenEff,
                EhpElem = maxHp * (armorK + elemArmor) / armorK / takenEff,
            };
            return row;
        }

        private static void FlagOutliers(List<Row> rows)
        {
            if (rows.Count < 3) return;

            void Z(Func<Row, float> get, string label)
            {
                double mean = 0;
                for (int i = 0; i < rows.Count; i++) mean += get(rows[i]);
                mean /= rows.Count;
                double var = 0;
                for (int i = 0; i < rows.Count; i++) { double d = get(rows[i]) - mean; var += d * d; }
                double std = Math.Sqrt(var / rows.Count);
                if (std < 1e-6) return;
                for (int i = 0; i < rows.Count; i++)
                {
                    double z = (get(rows[i]) - mean) / std;
                    if (Math.Abs(z) > 2.0)
                        Append(rows[i], label + ":" + (z > 0 ? "+" : "") + z.ToString("0.0", CultureInfo.InvariantCulture) + "σ");
                }
            }

            Z(r => r.RawDps, "RawDPS");
            Z(r => r.EhpPhys, "EHP");

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].MaxHP <= 0f) Append(rows[i], "MaxHP<=0");
                if (rows[i].RawDps <= 0f) Append(rows[i], "RawDPS<=0");
            }
        }

        private static void Append(Row row, string flag)
            => row.Flags = string.IsNullOrEmpty(row.Flags) ? flag : row.Flags + ", " + flag;
    }
}
