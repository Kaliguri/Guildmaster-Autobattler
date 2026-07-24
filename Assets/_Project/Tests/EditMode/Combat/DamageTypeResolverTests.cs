using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Поисточниковая модель типа урона (рефактор 2026-07): нормализация <see cref="DamageType"/>,
    /// override-резолверы <see cref="DamageCategories"/>, сборка типа урона автоатаки/способности.
    /// </summary>
    public sealed class DamageTypeResolverTests
    {
        // --- Нормализация DamageType: конкретика живёт только при своей школе ---

        [Test]
        public void DamageType_Physical_KeepsSubtype_DropsElement()
        {
            var dt = new DamageType(DamageSchool.Physical, PhysicalSubtype.Pierce, MagicElement.Fire, DamageAffinity.None);
            Assert.AreEqual(PhysicalSubtype.Pierce, dt.PhysicalSubtype);
            Assert.AreEqual(MagicElement.None, dt.MagicElement, "элемент нерелевантен физ-школе");
            Assert.IsTrue(dt.IsPhysical);
            Assert.IsTrue(dt.HasSpecific);
        }

        [Test]
        public void DamageType_Magical_KeepsElement_DropsSubtype()
        {
            var dt = new DamageType(DamageSchool.Magical, PhysicalSubtype.Slash, MagicElement.Ice, DamageAffinity.None);
            Assert.AreEqual(MagicElement.Ice, dt.MagicElement);
            Assert.AreEqual(PhysicalSubtype.None, dt.PhysicalSubtype, "подтип нерелевантен маг-школе");
            Assert.IsTrue(dt.IsMagical);
        }

        [Test]
        public void DamageType_True_DropsBothSpecifics()
        {
            var dt = new DamageType(DamageSchool.True, PhysicalSubtype.Blunt, MagicElement.Arcane, DamageAffinity.Light);
            Assert.AreEqual(PhysicalSubtype.None, dt.PhysicalSubtype);
            Assert.AreEqual(MagicElement.None, dt.MagicElement);
            Assert.AreEqual(DamageAffinity.Light, dt.Affinity, "сродство ортогонально школе и сохраняется");
            Assert.IsFalse(dt.HasSpecific);
        }

        // --- Override-резолверы: Inherit берёт значение кастера, явное — переопределяет ---

        [Test]
        public void Resolve_SubtypeOverride_InheritTakesUnit_ExplicitWins()
        {
            Assert.AreEqual(PhysicalSubtype.Pierce,
                DamageCategories.Resolve(PhysicalSubtypeOverride.Inherit, PhysicalSubtype.Pierce));
            Assert.AreEqual(PhysicalSubtype.Slash,
                DamageCategories.Resolve(PhysicalSubtypeOverride.Slash, PhysicalSubtype.Pierce));
            Assert.AreEqual(PhysicalSubtype.None,
                DamageCategories.Resolve(PhysicalSubtypeOverride.None, PhysicalSubtype.Pierce));
        }

        [Test]
        public void Resolve_ElementOverride_InheritTakesUnit_ExplicitWins()
        {
            Assert.AreEqual(MagicElement.Fire,
                DamageCategories.Resolve(MagicElementOverride.Inherit, MagicElement.Fire));
            Assert.AreEqual(MagicElement.Arcane,
                DamageCategories.Resolve(MagicElementOverride.Arcane, MagicElement.Fire));
        }

        // --- Сборка из данных ---

        [Test]
        public void UnitData_AutoAttackDamageType_FromFlatFields()
        {
            var relic = ScriptableObject.CreateInstance<RelicData>()
                .With("_damageSchool", DamageSchool.Physical)
                .With("_physicalSubtype", PhysicalSubtype.Pierce)
                .With("_affinity", DamageAffinity.None);

            DamageType dt = relic.ResolveAutoAttackDamageType();
            Assert.AreEqual(DamageSchool.Physical, dt.School);
            Assert.AreEqual(PhysicalSubtype.Pierce, dt.PhysicalSubtype);
            Object.DestroyImmediate(relic);
        }

        [Test]
        public void AbilityData_DamageType_SpearmanCase_UltSlashOverAutoPierce()
        {
            // Копейщик: автоатака Колющая (Pierce), ульта задаёт Режущий (Slash) поверх.
            var caster = ScriptableObject.CreateInstance<RelicData>()
                .With("_damageSchool", DamageSchool.Physical)
                .With("_physicalSubtype", PhysicalSubtype.Pierce);

            var ability = new AbilityData()
                .With("_schoolOverride", DamageSchoolOverride.Inherit)
                .With("_physicalSubtypeOverride", PhysicalSubtypeOverride.Slash);

            DamageType dt = ability.ResolveDamageType(caster);
            Assert.AreEqual(DamageSchool.Physical, dt.School, "школа наследована от кастера");
            Assert.AreEqual(PhysicalSubtype.Slash, dt.PhysicalSubtype, "подтип переопределён способностью");
            Object.DestroyImmediate(caster);
        }

        [Test]
        public void AbilityData_DamageType_InheritsAllFromCaster()
        {
            var caster = ScriptableObject.CreateInstance<RelicData>()
                .With("_damageSchool", DamageSchool.Magical)
                .With("_magicElement", MagicElement.Ice)
                .With("_affinity", DamageAffinity.None);

            var ability = new AbilityData(); // всё Inherit

            DamageType dt = ability.ResolveDamageType(caster);
            Assert.AreEqual(DamageSchool.Magical, dt.School);
            Assert.AreEqual(MagicElement.Ice, dt.MagicElement);
            Object.DestroyImmediate(caster);
        }
    }
}
