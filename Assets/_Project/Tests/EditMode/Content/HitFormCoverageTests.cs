using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Definitions;
using Guildmaster.Presentation.Effects;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// У КАЖДОЙ автоатаки есть форма удара, и её архетип берётся из данных юнита — либо из
    /// <c>AttackType.Ranged</c> (у выстрела форма одна на всех, линия-всполох), либо из типа урона,
    /// который прямо называет способ доставки: рубанули, укололи, ударили тяжёлым.
    /// <para>
    /// <b>Почему тестом, а не дефолтом в коде.</b> Прямое требование Макса (01.08.2026): «Никакого
    /// дефолта. Только явное указание (или по типу из SO)». Дефолт молча назначает язык вместо автора —
    /// «ледяной удар» не говорит, посохом бьют или когтем, и подставленный серп будет догадкой показа,
    /// выданной за замысел. Поэтому невыразимый случай обязан падать ЗДЕСЬ, до боя, а не рисоваться
    /// наугад.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Тест кросс-файловый по построению: тип урона живёт в SO юнита, а язык форм — в презентации, и ни
    /// одна сторона шва не видит вторую. Комментарий в любом из двух файлов увидела бы только одна.
    /// </remarks>
    public sealed class HitFormCoverageTests
    {
        private static List<UnitData> AllUnits() =>
            AssetDatabase.FindAssets($"t:{nameof(UnitData)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<UnitData>)
                .Where(u => u != null)
                .OrderBy(u => u.name)
                .ToList();

        [Test]
        public void КаждаяАвтоатака_ИмеетВыразимыйАрхетипФормы()
        {
            var broken = new List<string>();

            foreach (UnitData unit in AllUnits())
            {
                // Undefined ловит DamageTypeCoverageTests — здесь он дал бы вторую жалобу на тот же дефект.
                if (unit.AutoAttackDamageType == DamageType.Undefined) continue;

                bool ranged = unit.AttackType == AttackType.Ranged;
                if (HitFormFactory.ResolveKind(unit.AutoAttackDamageType, ranged, out _)) continue;

                broken.Add($"{unit.name}: мили-удар типа {unit.AutoAttackDamageType} не называет способ " +
                           "доставки");
            }

            Assert.IsEmpty(broken,
                "Этим ударам нечем нарисовать форму: " + string.Join("; ", broken) +
                ". Ближний бой обязан называть СПОСОБ доставки — физическим типом урона (Slash / Pierce / " +
                "Blunt; безоружный это Blunt) либо явным архетипом в UnitData, если понадобится развести " +
                "способ и школу. Дефолт здесь запрещён: он назначил бы язык вместо автора.");
        }

        [Test]
        public void УДальнегоБоя_ФормаОднаНаВсех()
        {
            var wrong = new List<string>();

            foreach (UnitData unit in AllUnits())
            {
                if (unit.AttackType != AttackType.Ranged) continue;
                if (unit.AutoAttackDamageType == DamageType.Undefined) continue;

                HitFormFactory.ResolveKind(unit.AutoAttackDamageType, ranged: true, out HitFormKind kind);
                if (kind != HitFormKind.Bolt)
                    wrong.Add($"{unit.name} -> {kind}");
            }

            Assert.IsEmpty(wrong,
                "У выстрела форма одна — линия-всполох, и тип урона на неё не влияет: " +
                string.Join(", ", wrong) + ". Иначе режущая стрела получила бы серп, то есть взмах, " +
                "которого не было.");
        }
    }
}
