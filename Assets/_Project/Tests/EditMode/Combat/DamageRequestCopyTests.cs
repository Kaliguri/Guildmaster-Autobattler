using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Копия <see cref="DamageRequest"/> обязана донести ВСЕ свойства удара — уязвимость и оба
    /// пробивания.
    /// </summary>
    /// <remarks>
    /// Инвариант держится тестом, потому что нарушается он молча: конструктор длинный, хвостовые
    /// аргументы необязательные, и пропущенный подставляется дефолтом, а не ошибкой компиляции.
    /// Так и случилось трижды — овертайм, усиление источника и расщепление удара теряли разовое
    /// пробивание, и удар считался по полной броне. Из кода это не видно ниоткуда: разница только
    /// в числе урона, то есть ловится замером, а не чтением.
    /// <para>Сверка идёт РЕФЛЕКСИЕЙ по полям, а не перечислением: новое поле структуры само попадёт
    /// под проверку, и следующая копия не сможет потерять его тихо.</para>
    /// </remarks>
    public sealed class DamageRequestCopyTests
    {
        private static DamageRequest Sample() => new DamageRequest(
            source: null,
            target: null,
            rawDamage: 100f,
            type: DamageType.Slash,
            armorK: 60f,
            sourceKind: DamageSourceKind.AutoAttack,
            vulnerability: 1.35f,
            bonusFlatPen: 20f,
            bonusPctPen: 0.5f);

        [Test]
        public void WithRawDamage_KeepsEveryOtherField()
        {
            DamageRequest original = Sample();
            DamageRequest copy = original.WithRawDamage(42f);

            Assert.AreEqual(42f, copy.RawDamage, "новый урон не применился");
            AssertFieldsMatch(original, copy, except: nameof(DamageRequest.RawDamage));
        }

        [Test]
        public void WithRawDamageAndType_KeepsEveryOtherField()
        {
            DamageRequest original = Sample();
            DamageRequest copy = original.WithRawDamage(42f, DamageType.Fire);

            Assert.AreEqual(DamageType.Fire, copy.Type, "школа отщеплённой половины не применилась");
            AssertFieldsMatch(original, copy,
                nameof(DamageRequest.RawDamage), nameof(DamageRequest.Type));
        }

        [Test]
        public void ScaledForTarget_KeepsPenetrationAndKind()
        {
            DamageRequest original = Sample();
            DamageRequest copy = original.ScaledForTarget(null, 2f, 1.35f);

            Assert.AreEqual(200f, copy.RawDamage, "множитель цели не применился");
            AssertFieldsMatch(original, copy,
                nameof(DamageRequest.RawDamage),
                nameof(DamageRequest.Target),
                nameof(DamageRequest.Vulnerability));
        }

        /// <summary>Все поля структуры, кроме названных, совпадают у оригинала и копии.</summary>
        private static void AssertFieldsMatch(DamageRequest original, DamageRequest copy, params string[] except)
        {
            var skip = new HashSet<string>(except);
            FieldInfo[] fields = typeof(DamageRequest)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !skip.Contains(f.Name))
                .ToArray();

            Assert.That(fields, Is.Not.Empty, "рефлексия не нашла полей — проверка выродилась в пустую");

            foreach (FieldInfo f in fields)
            {
                Assert.AreEqual(f.GetValue(original), f.GetValue(copy),
                    $"копия потеряла поле «{f.Name}» — это свойство УДАРА, оно обязано переехать");
            }
        }
    }
}
