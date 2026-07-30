using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Единая ось типа урона (реформа 2026-07-30): тотальность правила «тип → школа», сопоставление
    /// фильтров и то, что тип задаётся каждым источником отдельно.
    /// </summary>
    /// <remarks>
    /// Прежняя редакция этого класса проверяла нормализацию четырёх осей и <c>Inherit</c>-резолверы —
    /// ровно ту механику, которую реформа сняла: «просто физический» урон был выразим, и пропуск
    /// подтипа не ловился ничем. Наследование типа от юнита тоже проверять больше нечего, его нет.
    /// </remarks>
    public sealed class DamageTypeResolverTests
    {
        // --- Тотальность: у каждого типа есть школа, и она не «физика по умолчанию» ---

        [Test]
        public void EveryDamageType_IsListedInAll()
        {
            // DamageTypes.All — источник для редакторных выпадашек и тестов. Новый тип, забытый в нём,
            // выпал бы из всех проверок покрытия и вернул бы нас к «просто физическому» урону.
            foreach (DamageType type in System.Enum.GetValues(typeof(DamageType)))
            {
                if (type == DamageType.Undefined) continue;
                Assert.Contains(type, DamageTypes.All,
                    $"Тип {type} не добавлен в DamageTypes.All — проверки покрытия его не увидят.");
            }
        }

        [Test]
        public void SchoolOf_IsExplicit_ForEveryType()
        {
            // Каждый валидный тип обязан быть НАЗВАН в switch школы. Без этого теста забытый тип
            // молча уехал бы в default и стал физическим — тем самым тихим дефектом, из-за которого
            // взрыв костей полгода не попадал в хрупкость статуи.
            foreach (DamageType type in DamageTypes.All)
            {
                DamageSchool school = DamageTypes.SchoolOf(type);
                bool exactlyOne = (DamageTypes.IsPhysical(type) ? 1 : 0)
                                + (DamageTypes.IsMagical(type) ? 1 : 0)
                                + (DamageTypes.IsTrue(type) ? 1 : 0) == 1;
                Assert.IsTrue(exactlyOne, $"Тип {type} попал в школу {school} неоднозначно.");
            }
        }

        [Test]
        public void Poison_TakesBothSchools_ButNeverTrue()
        {
            // Канон (ГДД «Статы» §Школа vs сродство, решение 2026-07-25/4): DoT яда идёт по школе
            // конкретного яда — Физической или Магической, — и НЕ бывает чистым. Исключение живёт
            // двумя значениями списка, поэтому здесь оно и проверяется.
            Assert.AreEqual(DamageSchool.Physical, DamageTypes.SchoolOf(DamageType.PoisonPhysical));
            Assert.AreEqual(DamageSchool.Magical, DamageTypes.SchoolOf(DamageType.PoisonMagical));

            Assert.IsTrue(DamageTypes.IsPoison(DamageType.PoisonPhysical));
            Assert.IsTrue(DamageTypes.IsPoison(DamageType.PoisonMagical));

            foreach (DamageType type in DamageTypes.All)
                if (DamageTypes.IsPoison(type))
                    Assert.AreNotEqual(DamageSchool.True, DamageTypes.SchoolOf(type),
                        $"Яд {type} стал чистым — запрещено решением 2026-07-25/4.");
        }

        [Test]
        public void LightAndDark_GoPastArmor()
        {
            // Канон: Свет и Тьма — оба Чистый, мимо брони, потому и редкие. Они НЕ магия.
            Assert.AreEqual(DamageSchool.True, DamageTypes.SchoolOf(DamageType.Light));
            Assert.AreEqual(DamageSchool.True, DamageTypes.SchoolOf(DamageType.Dark));
        }

        [Test]
        public void Bleed_IsPhysical_ButNotPierce()
        {
            // Кровотечение — своя природа с фиксированной физической школой. Как Pierce оно попадало бы
            // под «уязвимость к колющему», а «нежить не кровоточит» стало бы невыразимо.
            Assert.AreEqual(DamageSchool.Physical, DamageTypes.SchoolOf(DamageType.Bleed));
            Assert.AreNotEqual(DamageType.Pierce, DamageType.Bleed);
        }

        // --- Фильтры защитных эффектов ---

        [Test]
        public void Matches_NarrowFilter_TakesOnlyItsOwnType()
        {
            // «Огненный вард» держит Огонь и не держит прочую магию.
            Assert.IsTrue(DamageTypes.Matches(DamageType.Fire, false, DamageType.Fire));
            Assert.IsFalse(DamageTypes.Matches(DamageType.Fire, false, DamageType.Ice));
        }

        [Test]
        public void Matches_WholeSchoolFilter_TakesAnyTypeOfThatSchool()
        {
            // «Аркановый щит» держит любую магию, но не физику и не чистый урон.
            Assert.IsTrue(DamageTypes.Matches(DamageType.Arcane, true, DamageType.Ice));
            Assert.IsTrue(DamageTypes.Matches(DamageType.Arcane, true, DamageType.PoisonMagical));
            Assert.IsFalse(DamageTypes.Matches(DamageType.Arcane, true, DamageType.Slash));
            Assert.IsFalse(DamageTypes.Matches(DamageType.Arcane, true, DamageType.Dark));
        }

        // --- Тип задаётся каждым источником отдельно ---

        [Test]
        public void AutoAttackAndAbility_CarryTheirOwnTypes()
        {
            // Копейщик: автоатака Колющая, ульта Режущая. Раньше это выражалось override-ом поверх
            // юнита; теперь каждый источник объявляет тип сам, и наследования нет вовсе.
            var relic = ScriptableObject.CreateInstance<RelicData>()
                .With("_autoAttackDamageType", DamageType.Pierce);
            var ult = new AbilityData().With("_damageType", DamageType.Slash);

            Assert.AreEqual(DamageType.Pierce, relic.AutoAttackDamageType);
            Assert.AreEqual(DamageType.Slash, ult.DamageType,
                "тип способности не зависит от типа автоатаки её носителя");

            Object.DestroyImmediate(relic);
        }

        [Test]
        public void AbilityWithoutDirectDamage_MayLeaveTypeUndefined()
        {
            // Хилящей способности тип урона не нужен: Undefined здесь — не пропуск, а «урона нет».
            // Спрашивают тип только у тех, у кого множитель больше нуля (тест покрытия контента).
            var heal = new AbilityData().With("_damageMultiplier", 0f);
            Assert.AreEqual(DamageType.Undefined, heal.DamageType);
        }
    }
}
