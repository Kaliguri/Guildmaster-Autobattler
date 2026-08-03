using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Дальность авто-атаки: у числа один владелец — ступень в <see cref="UnitData"/> плюс дистанция
    /// ступени в <see cref="StatsConfig"/>.
    /// </summary>
    /// <remarks>
    /// Инвариант держится тестом, а не соглашением, потому что нарушить его можно из другого файла и
    /// молча: достаточно дописать <see cref="StatType.AttackRange"/> в стат-блок юнита, и он перекроет
    /// ступень — правка ступени перестанет доезжать до этого кита, но отчёты продолжат показывать
    /// «дальник обычной дистанции». Ровно из такой тишины вырос Кровомант: дальность ему не задали
    /// вовсе, он уехал на дефолт конфига (единицу) и дрался вплотную, будучи РДД со снарядом.
    /// </remarks>
    public sealed class AttackRangeBandTests
    {
        private static List<UnitData> AllUnits()
        {
            var units = new List<UnitData>();
            foreach (string guid in AssetDatabase.FindAssets("t:UnitData"))
            {
                var unit = AssetDatabase.LoadAssetAtPath<UnitData>(AssetDatabase.GUIDToAssetPath(guid));
                if (unit != null) units.Add(unit);
            }
            return units;
        }

        [Test]
        public void NoUnit_AuthorsAttackRange_InItsStatBlock()
        {
            foreach (UnitData unit in AllUnits())
            {
                if (unit.Stats == null) continue;
                foreach (StatModifier mod in unit.Stats)
                {
                    Assert.AreNotEqual(StatType.AttackRange, mod.Stat,
                        $"'{unit.name}' задаёт дальность в стат-блоке ({AssetDatabase.GetAssetPath(unit)}). " +
                        "Дистанция назначается ступенью Range Band, а личное отличие — полем Range Adjust " +
                        "в долях от неё.");
                }
            }
        }

        [Test]
        public void EveryBand_HasDistance_InStatsConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:StatsConfig");
            Assert.IsNotEmpty(guids, "StatsConfig не найден — ступеням дальности негде жить");

            var config = AssetDatabase.LoadAssetAtPath<StatsConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            float previous = 0f;
            foreach (AttackRangeBand band in System.Enum.GetValues(typeof(AttackRangeBand)))
            {
                float distance = config.RangeOf(band);
                Assert.Greater(distance, previous,
                    $"Ступень '{band}' обязана быть дальше предыдущей: порядок ступеней — это и есть их смысл");
                previous = distance;
            }
        }

        /// <summary>
        /// Стрелок не может стоять в ближнем бою по построению: если его ступень «обычный мили», это не
        /// балансное решение, а незаполненное поле — то же, с чего началась история Кровоманта.
        /// </summary>
        [Test]
        public void RangedUnits_DoNotStandInMeleeBand()
        {
            foreach (UnitData unit in AllUnits())
            {
                if (unit.AttackType == AttackType.Melee) continue;
                Assert.AreNotEqual(AttackRangeBand.Melee, unit.RangeBand,
                    $"'{unit.name}' стреляет снарядом, но стоит на ступени ближнего боя " +
                    $"({AssetDatabase.GetAssetPath(unit)})");
            }
        }
    }
}
